// Hallow Overlay - settings (%APPDATA%\HallowOverlay\overlay.json) and the numbers it shows, read in the background once a second.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowOverlay
{
    [DataContract]
    public class OverlaySettings
    {
        [DataMember] public string Corner = "tl";        // tl, tr, bl, br
        [DataMember] public double Scale = 1;
        [DataMember] public double Opacity = 0.85;
        [DataMember] public bool Row = true;             // one line (true) or a column
        [DataMember] public bool OnlyInGame = true;      // show only while a game is running
        [DataMember] public bool Cpu = true, Gpu = true, GpuTemp = true, Ram = true, Ping = true, Clock = true, Session = true, MicWarn = true, Game = false;
        [DataMember] public bool Fps = true;             // frames per second and 1% low (when Windows allows reading them)
        [DataMember] public string PingHost = "1.1.1.1";
        [DataMember] public bool Hotkey = true;          // Ctrl+Shift+O shows / hides it
        [DataMember] public bool TrayOnClose = true;
        [DataMember] public List<string> OffGames = new List<string>();   // games the overlay stays off in (set by Hallow Hub through the link)
        [DataMember] public string LastSeenVersion = "";
        [DataMember] public bool OnlyBad = false;        // only show a stat when it's in trouble
        [DataMember] public bool LossJitter = true;      // packet loss and jitter next to ping
        [DataMember] public double HotGpu = 80, HighPing = 80, RamFull = 85;   // "in trouble" from: GPU °C, ping ms, RAM % used
        // Games with their own corner, size and stats (by game name). Everything above "PingHost" is the look; the rest is global.
        [DataMember(EmitDefaultValue = false)] public Dictionary<string, OverlaySettings> PerGame = new Dictionary<string, OverlaySettings>();
        [OnDeserializing] void D(StreamingContext c)
        {
            Corner = "tl"; Scale = 1; Opacity = 0.85; Row = true; OnlyInGame = true; Cpu = Gpu = GpuTemp = Ram = Ping = Clock = Session = MicWarn = true; Game = false; Fps = true; PingHost = "1.1.1.1"; Hotkey = true; TrayOnClose = true;
            OffGames = new List<string>(); LastSeenVersion = "1.0";   // a settings file without it was written by 1.0
            OnlyBad = false; LossJitter = true; HotGpu = 80; HighPing = 80; RamFull = 85; PerGame = null;
        }
        [OnDeserialized] void Fix(StreamingContext c) { if (OffGames == null) OffGames = new List<string>(); }

        // A copy of the look for a game's own settings (no global lists inside).
        public OverlaySettings LookCopy() { var l = (OverlaySettings)MemberwiseClone(); l.PerGame = null; l.OffGames = new List<string>(); return l; }
        // The settings a game's own look is stored under (case-insensitive), or null.
        public string PerGameKey(string game)
        {
            if (game == null || PerGame == null) return null;
            foreach (var k in PerGame.Keys) if (string.Equals(k, game, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }
        // What the panel looks like over this game: its own look if it has one (with the global "in trouble" limits), else the normal one.
        public OverlaySettings LookFor(string game)
        {
            var k = PerGameKey(game); if (k == null) return this;
            var l = PerGame[k].LookCopy(); l.HotGpu = HotGpu; l.HighPing = HighPing; l.RamFull = RamFull; return l;
        }
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowOverlay"); } }
        static string FilePath { get { return Path.Combine(Dir, "overlay.json"); } }
        public static bool Frozen;
        public static OverlaySettings Load()
        {
            try { if (File.Exists(FilePath)) using (var f = File.OpenRead(FilePath)) { var s = (OverlaySettings)new DataContractJsonSerializer(typeof(OverlaySettings)).ReadObject(f); if (s != null) { if (s.PerGame == null) s.PerGame = new Dictionary<string, OverlaySettings>(); return s; } } } catch { }
            return new OverlaySettings();
        }
        public void Save()
        {
            if (Frozen) return;
            try { Directory.CreateDirectory(Dir); using (var f = File.Create(FilePath)) new DataContractJsonSerializer(typeof(OverlaySettings)).WriteObject(f, this); } catch { }
        }
    }

    public class Sample
    {
        public double Cpu = -1, RamUsedGB = -1, RamTotalGB = -1, Gpu = -1, GpuTemp = -1, PingMs = -1, Fps = -1, FpsLow = -1, LossPct = -1, JitterMs = -1;
        public bool MicMuted, MixerOn;
        public Sample Copy() { return (Sample)MemberwiseClone(); }
    }

    // Everything is read off the game's thread and off the overlay's thread; the overlay just shows the latest Sample.
    public class Sampler
    {
        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX { public uint dwLength = 64; public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        readonly object gate = new object(); Sample last = new Sample();
        public Sample Latest { get { lock (gate) return last; } }
        public string PingHost = "1.1.1.1";
        double gpu = -1, gpuTemp = -1; Process smi; volatile bool stop;

        public void Start()
        {
            StartSmi();
            new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "overlay stats" }.Start();
        }
        public void Stop() { stop = true; try { if (smi != null && !smi.HasExited) smi.Kill(); } catch { } }

        // nvidia-smi keeps running and prints one line a second (-lms), so nothing is started over and over
        void StartSmi()
        {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"); if (!File.Exists(exe)) return;
            try
            {
                smi = new Process { StartInfo = new ProcessStartInfo(exe, "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits -lms 1000") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true } };
                smi.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return; var p = e.Data.Split(','); double u, t;
                    if (p.Length >= 2 && double.TryParse(p[0].Trim(), out u) && double.TryParse(p[1].Trim(), out t)) { gpu = u; gpuTemp = t; }
                };
                smi.Start(); smi.BeginOutputReadLine();
            }
            catch { smi = null; }
        }

        void Loop()
        {
            PerformanceCounter cpu = null; try { cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total"); cpu.NextValue(); } catch { try { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); } catch { } }
            int tick = 0; bool muted = false, mixer = false;
            new Thread(PingLoop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "overlay ping" }.Start();
            while (!stop)
            {
                Thread.Sleep(1000); tick++;
                var s = new Sample();
                try { if (cpu != null) s.Cpu = Math.Min(100, cpu.NextValue()); } catch { }
                try { var m = new MEMORYSTATUSEX(); if (GlobalMemoryStatusEx(m)) { s.RamTotalGB = m.ullTotalPhys / 1073741824.0; s.RamUsedGB = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0; } } catch { }
                if (tick % 2 == 0) Mixer(out muted, out mixer);
                s.Gpu = gpu; s.GpuTemp = gpuTemp; s.MicMuted = muted; s.MixerOn = mixer;
                lock (gate) { s.PingMs = pingMs; s.LossPct = lossPct; s.JitterMs = jitterMs; }
                lock (gate) last = s;
                if (smi != null && smi.HasExited && tick % 30 == 0) StartSmi();
            }
        }

        // One ping a second; packet loss and jitter over the last minute (jitter = average change between replies).
        double pingMs = -1, lossPct = -1, jitterMs = -1;
        void PingLoop()
        {
            var ping = new Ping(); var last = new Queue<long>();   // round trip ms, -1 = lost
            while (!stop)
            {
                var t0 = DateTime.UtcNow; long rtt = -1;
                try { var r = ping.Send(PingHost, 900); if (r.Status == IPStatus.Success) rtt = r.RoundtripTime; } catch { }
                last.Enqueue(rtt); while (last.Count > 60) last.Dequeue();
                var a = last.ToArray(); int lost = a.Count(v => v < 0);
                double jit = 0; int pairs = 0;
                for (int i = 1; i < a.Length; i++) if (a[i] >= 0 && a[i - 1] >= 0) { jit += Math.Abs(a[i] - a[i - 1]); pairs++; }
                lock (gate)
                {
                    pingMs = rtt;
                    lossPct = a.Length >= 10 ? lost * 100.0 / a.Length : -1;
                    jitterMs = pairs >= 5 ? jit / pairs : -1;
                }
                var wait = 1000 - (int)(DateTime.UtcNow - t0).TotalMilliseconds; if (wait > 0) Thread.Sleep(wait);
            }
        }

        // Hallow Mixer's local API: is the mic muted?
        static void Mixer(out bool muted, out bool on)
        {
            muted = false; on = false;
            try
            {
                var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"HallowMixer\api.json"); if (!File.Exists(f)) return;
                var js = new JavaScriptSerializer(); var a = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + a["port"] + "/state"); req.Headers["X-Hallow-Key"] = a["key"] as string; req.Timeout = 800; req.Proxy = null;
                using (var r = req.GetResponse()) using (var rd = new StreamReader(r.GetResponseStream()))
                {
                    var st = js.Deserialize<Dictionary<string, object>>(rd.ReadToEnd()); on = true;
                    object mic; if (st.TryGetValue("mic", out mic) && mic is Dictionary<string, object>) { object m; if (((Dictionary<string, object>)mic).TryGetValue("muted", out m) && m is bool) muted = (bool)m; }
                }
            }
            catch { }
        }
    }
}
