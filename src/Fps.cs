// Hallow Overlay - frames per second and 1% lows, read the way PresentMon does: Windows' own event tracing (ETW) reports every
// frame a game presents (DXGI for DirectX 10/11/12, D3D9 for older games). Nothing is injected into the game.
// Windows only allows this for admins and members of the "Performance Log Users" group; when it says no, FPS just isn't shown.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace HallowOverlay
{
    public class FpsMeter
    {
        // Microsoft-Windows-DXGI (Present_Start = 42) and Microsoft-Windows-D3D9 (Present_Start = 1)
        static readonly Guid DXGI = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"), D3D9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        const long Keep = 11 * 10000000L;   // 11 s of frames, in 100 ns ticks
        readonly object gate = new object();
        readonly Dictionary<int, Queue<long>> frames = new Dictionary<int, Queue<long>>();
        readonly EtwSession etw;
        public FpsMeter() { etw = new EtwSession("HallowOverlayFps", new[] { DXGI, D3D9 }, new[] { ulong.MaxValue, ulong.MaxValue }, OnEvent); }
        public bool Running { get { return etw.Running; } }
        public bool Denied { get { return etw.Denied; } }
        public bool Start() { return etw.Start(); }
        public void Stop() { etw.Stop(); lock (gate) frames.Clear(); }
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
