// Hallow Overlay - frames per second, 1% and 0.1% lows, read the way PresentMon does: Windows' own event tracing (ETW) reports every
// frame a game presents. DXGI (DirectX 10/11/12) and D3D9 come first; for games that use neither (Vulkan, OpenGL) the graphics
// kernel's own present events (Microsoft-Windows-DxgKrnl) count instead. Nothing is injected into the game. Windows only allows
// this for admins and members of the "Performance Log Users" group; when it says no, FPS just isn't shown.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace HallowOverlay
{
    public class FpsMeter
    {
        // Microsoft-Windows-DXGI (Present_Start = 42), Microsoft-Windows-D3D9 (Present_Start = 1),
        // Microsoft-Windows-DxgKrnl (Present = 184, PresentHistory = 214 / 215, Flip = 168, FlipMultiPlaneOverlay = 252)
        static readonly Guid DXGI = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"), D3D9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9"),
            DxgKrnl = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D");
        static readonly ushort[] KernelIds = { 184, 214, 215, 168, 252 };
        const long Keep = 11 * 10000000L;   // 11 s of frames, in 100 ns ticks
        readonly object gate = new object();
        // key: process id * 1000 + source (0 = DXGI / D3D9, otherwise the DxgKrnl event id); each source counts the same frames its own way
        readonly Dictionary<long, Queue<long>> frames = new Dictionary<long, Queue<long>>();
        Dictionary<long, List<long>> recording;   // benchmark: every frame since it started
        readonly EtwSession etw;
        public FpsMeter()
        {
            etw = new EtwSession("HallowOverlayFps", new[] { DXGI, D3D9, DxgKrnl }, new[] { ulong.MaxValue, ulong.MaxValue, 0x8000000UL }, OnEvent);
            etw.EventIds = new[] { new ushort[] { 42 }, new ushort[] { 1 }, KernelIds };   // Windows drops everything else
        }
        public bool Running { get { return etw.Running; } }
        public bool Denied { get { return etw.Denied; } }
        public bool Start() { return etw.Start(); }
        public void Stop() { etw.Stop(); lock (gate) { frames.Clear(); recording = null; } }

        // EVENT_RECORD: EventHeader.ProcessId at 12, TimeStamp at 16, ProviderId at 24, EventDescriptor.Id at 40
        void OnEvent(IntPtr rec)
        {
            try
            {
                int id = (ushort)Marshal.ReadInt16(rec, 40); uint d1 = (uint)Marshal.ReadInt32(rec, 24); int src;
                if ((id == 42 && d1 == 0xCA11C036) || (id == 1 && d1 == 0x783ACA0A)) src = 0;
                else if (d1 == 0x802EC45A && Array.IndexOf(KernelIds, (ushort)id) >= 0) src = id;
                else return;
                int pid = Marshal.ReadInt32(rec, 12); long ts = Marshal.ReadInt64(rec, 16); long key = pid * 1000L + src;
                lock (gate)
                {
                    Queue<long> q; if (!frames.TryGetValue(key, out q)) { q = new Queue<long>(); frames[key] = q; }
                    q.Enqueue(ts);
                    while (q.Count > 0 && q.Peek() < ts - Keep) q.Dequeue();
                    if (q.Count > 20000) q.Dequeue();
                    if (recording != null) { List<long> l; if (!recording.TryGetValue(key, out l)) { l = new List<long>(); recording[key] = l; } l.Add(ts); }
                }
            }
            catch { }
        }

        // The frames to use for these processes: DXGI / D3D9 when the game presents that way, else the busiest kernel source.
        // (Holds the lock: callers are inside lock (gate).)
        Queue<long> Pick(ICollection<int> pids)
        {
            Queue<long> run = null, kern = null;
            foreach (var kv in frames)
            {
                if (!pids.Contains((int)(kv.Key / 1000)) || kv.Value.Count < 2) continue;
                if (kv.Key % 1000 == 0) { if (run == null || kv.Value.Count > run.Count) run = kv.Value; }
                else if (kern == null || kv.Value.Count > kern.Count) kern = kv.Value;
            }
            if (run != null && run.Count * 4 >= (kern == null ? 0 : kern.Count)) return run;   // a launcher's few DXGI frames don't hide the game's real ones
            return kern ?? run;
        }
        public static string SourceOf(long key) { return key % 1000 == 0 ? "DirectX" : "graphics kernel"; }

        // The frame times (ms, oldest first) of the last `seconds` for `pids`, into `buf` (reused: no allocations).
        public int FrameTimes(ICollection<int> pids, double seconds, double[] buf)
        {
            lock (gate)
            {
                var best = Pick(pids);
                if (best == null) return 0;
                long last = 0; foreach (var t in best) last = t;
                long from = last - (long)(seconds * 10000000), prev = -1; int n = 0;
                foreach (var t in best)
                {
                    if (t >= from && prev >= 0 && n < buf.Length) buf[n++] = (t - prev) / 10000.0;
                    prev = t;
                }
                return n;
            }
        }

        public bool Read(ICollection<int> pids, out double fps, out double low) { double l01; return Read(pids, out fps, out low, out l01); }
        // FPS over the last 2 s; the 1% and 0.1% lows over the last 10 s (the average FPS of the slowest 1% / 0.1% of frames).
        public bool Read(ICollection<int> pids, out double fps, out double low, out double low01)
        {
            fps = -1; low = -1; low01 = -1;
            long[] t = null; var now = DateTime.UtcNow.ToFileTimeUtc();
            lock (gate)
            {
                var q = Pick(pids); if (q != null) t = q.ToArray();
                foreach (var dead in frames.Where(k => k.Value.Count == 0 || k.Value.Last() < now - Keep).Select(k => k.Key).ToList()) frames.Remove(dead);
            }
            if (t == null) return false;
            long last = t[t.Length - 1];
            if (last < now - 30000000L) return false;   // nothing for 3 s: paused or minimized
            int first = Array.FindIndex(t, x => x > last - 20000000L);
            if (first < 0 || t.Length - 1 - first < 1) return false;
            fps = (t.Length - 1 - first) * 10000000.0 / (last - t[first]);
            int from = Array.FindIndex(t, x => x > last - 100000000L);
            var gaps = new List<double>(); for (int i = Math.Max(1, from); i < t.Length; i++) gaps.Add((t[i] - t[i - 1]) / 10000.0);
            low = Lows(gaps, 0.01); low01 = Lows(gaps, 0.001);
            return true;
        }
        // average FPS of the slowest `part` of the frames (frame times in ms); -1 when there are too few frames for it
        public static double Lows(List<double> ms, double part)
        {
            if (ms.Count < Math.Max(20, (int)Math.Ceiling(1 / part))) return -1;
            var s = ms.OrderByDescending(v => v).ToList(); int n = Math.Max(1, (int)(s.Count * part));
            double avg = 0; for (int i = 0; i < n; i++) avg += s[i]; avg /= n;
            return avg > 0 ? 1000.0 / avg : -1;
        }

        // ---- benchmark: keep every frame from now on
        public void BeginRecording() { lock (gate) recording = new Dictionary<long, List<long>>(); }
        // Every frame time (ms) since BeginRecording for `pids` (picked like Pick), and where they came from; null if nothing.
        public List<double> EndRecording(ICollection<int> pids, out string source)
        {
            source = null;
            Dictionary<long, List<long>> r; lock (gate) { r = recording; recording = null; }
            if (r == null) return null;
            KeyValuePair<long, List<long>>? run = null, kern = null;
            foreach (var kv in r)
            {
                if (!pids.Contains((int)(kv.Key / 1000)) || kv.Value.Count < 2) continue;
                if (kv.Key % 1000 == 0) { if (run == null || kv.Value.Count > run.Value.Value.Count) run = kv; }
                else if (kern == null || kv.Value.Count > kern.Value.Value.Count) kern = kv;
            }
            var pick = run != null && run.Value.Value.Count * 4 >= (kern == null ? 0 : kern.Value.Value.Count) ? run : kern ?? run;
            if (pick == null) return null;
            source = SourceOf(pick.Value.Key); var ts = pick.Value.Value; var ms = new List<double>(ts.Count);
            for (int i = 1; i < ts.Count; i++) ms.Add((ts[i] - ts[i - 1]) / 10000.0);
            return ms;
        }
    }
}
