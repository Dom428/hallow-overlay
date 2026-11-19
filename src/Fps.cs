// Hallow Overlay - frames per second and 1% lows, read the way PresentMon does: Windows' own event tracing (ETW) reports every
// frame a game presents (DXGI for DirectX 10/11/12, D3D9 for older games). Nothing is injected into the game.
// Windows only allows this for admins and members of the "Performance Log Users" group; when it says no, FPS just isn't shown.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace HallowOverlay
{
    public class FpsMeter
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint StartTraceW(out ulong handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint ControlTraceW(ulong handle, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll")] static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint code, byte level, ulong any, ulong all, uint timeout, IntPtr parms);
        [DllImport("advapi32.dll")] static extern ulong OpenTraceW(IntPtr logfile);
        [DllImport("advapi32.dll")] static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] static extern uint CloseTrace(ulong handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void RecordCallback(IntPtr rec);

        const string SessionName = "HallowOverlayFps";
        static Guid DXGI = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");   // Microsoft-Windows-DXGI, Present_Start = 42
        static Guid D3D9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");   // Microsoft-Windows-D3D9, Present_Start = 1
        const int PropsSize = 120 + 2 * 1024;
        const long Keep = 11 * 10000000L;   // 11 s of frames, in 100 ns ticks

        readonly object gate = new object();
        readonly Dictionary<int, Queue<long>> frames = new Dictionary<int, Queue<long>>();
        ulong session, trace = ulong.MaxValue; IntPtr logfile = IntPtr.Zero, loggerName = IntPtr.Zero; RecordCallback cb; Thread thread;
        public bool Running { get; private set; }
        public bool Denied { get; private set; }   // Windows said no (not an admin / not in Performance Log Users)

        static IntPtr Props()
        {
            var p = Marshal.AllocHGlobal(PropsSize);
            for (int i = 0; i < PropsSize; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, PropsSize);      // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);             // Wnode.ClientContext: QPC timestamps
            Marshal.WriteInt32(p, 44, 0x20000);       // Wnode.Flags: WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 64, 0x100);         // LogFileMode: EVENT_TRACE_REAL_TIME_MODE
            Marshal.WriteInt32(p, 68, 1);             // FlushTimer: 1 s
            Marshal.WriteInt32(p, 116, 120);          // LoggerNameOffset
            return p;
        }

        // Starts the trace session. False when Windows doesn't allow it (Denied) or something else failed.
        public bool Start()
        {
            if (Running) return true;
            if (IntPtr.Size != 8) return false;
            var p = Props();
            try
            {
                uint r = StartTraceW(out session, SessionName, p);
                if (r == 183)   // left over from a crash: stop it and start again
                {
                    var q = Props(); try { ControlTraceW(0, SessionName, q, 1); } finally { Marshal.FreeHGlobal(q); }
                    for (int i = 0; i < PropsSize; i++) Marshal.WriteByte(p, i, 0);
                    Marshal.FreeHGlobal(p); p = Props();
                    r = StartTraceW(out session, SessionName, p);
                }
                if (r != 0) { Denied = r == 5; return false; }
                Denied = false;
                uint e1 = EnableTraceEx2(session, ref DXGI, 1, 5, ulong.MaxValue, 0, 0, IntPtr.Zero);
                uint e2 = EnableTraceEx2(session, ref D3D9, 1, 5, ulong.MaxValue, 0, 0, IntPtr.Zero);
                if (e1 != 0 && e2 != 0) { StopSession(); Denied = e1 == 5; return false; }
                // EVENT_TRACE_LOGFILEW (x64: 448 bytes): LoggerName at 8, ProcessTraceMode at 28, EventRecordCallback at 424
                logfile = Marshal.AllocHGlobal(448);
                for (int i = 0; i < 448; i++) Marshal.WriteByte(logfile, i, 0);
                loggerName = Marshal.StringToHGlobalUni(SessionName);
                Marshal.WriteIntPtr(logfile, 8, loggerName);
                Marshal.WriteInt32(logfile, 28, 0x100 | 0x10000000);   // PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD
                cb = OnEvent;
                Marshal.WriteIntPtr(logfile, 424, Marshal.GetFunctionPointerForDelegate(cb));
                trace = OpenTraceW(logfile);
                if (trace == ulong.MaxValue) { StopSession(); Free(); return false; }
                var h = trace;
                thread = new Thread(() => { try { ProcessTrace(new[] { h }, 1, IntPtr.Zero, IntPtr.Zero); } catch { } }) { IsBackground = true, Name = "overlay fps" };
                thread.Start();
                Running = true;
                return true;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(p); }
        }

        void StopSession()
        {
            var q = Props(); try { ControlTraceW(session, null, q, 1); } catch { } finally { Marshal.FreeHGlobal(q); }
        }
        void Free()
        {
            if (logfile != IntPtr.Zero) { Marshal.FreeHGlobal(logfile); logfile = IntPtr.Zero; }
            if (loggerName != IntPtr.Zero) { Marshal.FreeHGlobal(loggerName); loggerName = IntPtr.Zero; }
        }

        public void Stop()
        {
            if (!Running) return;
            Running = false;
            StopSession();
            try { CloseTrace(trace); } catch { }
            trace = ulong.MaxValue;
            if (thread != null) thread.Join(3000);
            thread = null; Free();
            lock (gate) frames.Clear();
        }

        // EVENT_RECORD: EventHeader.ProcessId at 12, TimeStamp at 16, ProviderId at 24, EventDescriptor.Id at 40
        void OnEvent(IntPtr rec)
        {
            try
            {
                int id = (ushort)Marshal.ReadInt16(rec, 40); uint d1 = (uint)Marshal.ReadInt32(rec, 24);
                if (!((id == 42 && d1 == 0xCA11C036) || (id == 1 && d1 == 0x783ACA0A))) return;
                int pid = Marshal.ReadInt32(rec, 12); long ts = Marshal.ReadInt64(rec, 16);
                lock (gate)
                {
                    Queue<long> q; if (!frames.TryGetValue(pid, out q)) { q = new Queue<long>(); frames[pid] = q; }
                    q.Enqueue(ts);
                    while (q.Count > 0 && q.Peek() < ts - Keep) q.Dequeue();
                    if (q.Count > 20000) q.Dequeue();
                }
            }
            catch { }
        }

        // FPS over the last 2 s and the 1% low over the last 10 s, for whichever of `pids` presents the most frames.
        public bool Read(ICollection<int> pids, out double fps, out double low)
        {
            fps = -1; low = -1;
            long[] t = null;
            lock (gate)
            {
                foreach (var kv in frames.Where(k => pids.Contains(k.Key) && k.Value.Count > 1).OrderByDescending(k => k.Value.Count).Take(1)) t = kv.Value.ToArray();
                foreach (var dead in frames.Where(k => k.Value.Count == 0 || k.Value.Last() < DateTime.UtcNow.ToFileTimeUtc() - Keep).Select(k => k.Key).ToList()) frames.Remove(dead);
            }
            if (t == null) return false;
            long last = t[t.Length - 1];
            if (last < DateTime.UtcNow.ToFileTimeUtc() - 30000000L) return false;   // nothing for 3 s: paused or minimized
            int first = Array.FindIndex(t, x => x > last - 20000000L);
            if (first < 0 || t.Length - 1 - first < 1) return false;
            fps = (t.Length - 1 - first) * 10000000.0 / (last - t[first]);
            int from = Array.FindIndex(t, x => x > last - 100000000L);
            var gaps = new List<long>(); for (int i = Math.Max(1, from); i < t.Length; i++) gaps.Add(t[i] - t[i - 1]);
            if (gaps.Count >= 20)
            {
                gaps.Sort(); int n = Math.Max(1, gaps.Count / 100);
                double avg = 0; for (int i = gaps.Count - n; i < gaps.Count; i++) avg += gaps[i]; avg /= n;
                if (avg > 0) low = 10000000.0 / avg;
            }
            return true;
        }
    }
}
