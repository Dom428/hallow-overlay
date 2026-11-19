// Hallow Overlay - a real-time Windows event tracing (ETW) session: what the FPS counter and the game server finder read.
// Windows allows this for admins and members of "Performance Log Users"; otherwise Start says Denied and the feature stays off.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HallowOverlay
{
    public class EtwSession
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint StartTraceW(out ulong handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern uint ControlTraceW(ulong handle, string name, IntPtr props, uint code);
        [DllImport("advapi32.dll")] static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint code, byte level, ulong any, ulong all, uint timeout, IntPtr parms);
        [DllImport("advapi32.dll")] static extern ulong OpenTraceW(IntPtr logfile);
        [DllImport("advapi32.dll")] static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] static extern uint CloseTrace(ulong handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void RecordCallback(IntPtr rec);

        const int PropsSize = 120 + 2 * 1024;
        readonly string name; readonly Guid[] providers; readonly ulong[] keywords; readonly Action<IntPtr> onEvent;
        ulong session, trace = ulong.MaxValue; IntPtr logfile = IntPtr.Zero, loggerName = IntPtr.Zero; RecordCallback cb; Thread thread;
        public bool Running { get; private set; }
        public bool Denied { get; private set; }   // Windows said no (not an admin / not in Performance Log Users)

        // onEvent gets the EVENT_RECORD: EventHeader.ProcessId at 12, TimeStamp (UTC file time) at 16, ProviderId at 24,
        // EventDescriptor.Id at 40, UserDataLength (ushort) at 86, UserData at 96.
        public EtwSession(string sessionName, Guid[] providerIds, ulong[] providerKeywords, Action<IntPtr> handler)
        {
            name = sessionName; providers = providerIds; keywords = providerKeywords; onEvent = handler;
        }

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

        public bool Start()
        {
            if (Running) return true;
            if (IntPtr.Size != 8) return false;
            var p = Props();
            try
            {
                uint r = StartTraceW(out session, name, p);
                if (r == 183)   // left over from a crash: stop it and start again
                {
                    var q = Props(); try { ControlTraceW(0, name, q, 1); } finally { Marshal.FreeHGlobal(q); }
                    Marshal.FreeHGlobal(p); p = Props();
                    r = StartTraceW(out session, name, p);
                }
                if (r != 0) { Denied = r == 5; return false; }
                Denied = false;
                uint ok = 0, last = 0;
                for (int i = 0; i < providers.Length; i++)
                {
                    var g = providers[i]; last = EnableTraceEx2(session, ref g, 1, 5, keywords[i], 0, 0, IntPtr.Zero);
                    if (last == 0) ok++;
                }
                if (ok == 0) { StopSession(); Denied = last == 5; return false; }
                // EVENT_TRACE_LOGFILEW (x64: 448 bytes): LoggerName at 8, ProcessTraceMode at 28, EventRecordCallback at 424
                logfile = Marshal.AllocHGlobal(448);
                for (int i = 0; i < 448; i++) Marshal.WriteByte(logfile, i, 0);
                loggerName = Marshal.StringToHGlobalUni(name);
                Marshal.WriteIntPtr(logfile, 8, loggerName);
                Marshal.WriteInt32(logfile, 28, 0x100 | 0x10000000);   // PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD
                cb = rec => { try { onEvent(rec); } catch { } };
                Marshal.WriteIntPtr(logfile, 424, Marshal.GetFunctionPointerForDelegate(cb));
                trace = OpenTraceW(logfile);
                if (trace == ulong.MaxValue) { StopSession(); Free(); return false; }
                var h = trace;
                thread = new Thread(() => { try { ProcessTrace(new[] { h }, 1, IntPtr.Zero, IntPtr.Zero); } catch { } }) { IsBackground = true, Name = "overlay " + name };
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
        }
    }
}
