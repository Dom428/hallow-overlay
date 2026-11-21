// Hallow Overlay - a small see-through panel over your game: CPU, GPU, temperature, RAM, ping, session time,
// and a warning when your mic is muted (Hallow Mixer). Its own window - nothing is injected into the game.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

[assembly: AssemblyTitle("Hallow Overlay")]
[assembly: AssemblyProduct("Hallow Overlay")]
[assembly: AssemblyDescription("A see-through stats panel over your games")]
[assembly: AssemblyVersion("1.20.0.0")]
[assembly: AssemblyFileVersion("1.20.0.0")]

namespace HallowOverlay
{
    public static class Program
    {
        public const string Version = "1.20";
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
        [STAThread]
        public static int Main(string[] args)
        {
            int ci = Array.IndexOf(args, "--cmd");
            if (ci >= 0)
            {
                // send one command to the running copy, print the reply, never open a window
                if (GetStdHandle(-11) == IntPtr.Zero) AttachConsole(-1);
                string reply; int code = Link.Send(ci + 1 < args.Length ? string.Join(" ", args.Skip(ci + 1)) : "ping", out reply);
                try { Console.Out.WriteLine(reply); Console.Out.Flush(); } catch { }
                return code;
            }
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length)
            {
                Snap.On = true; OverlaySettings.Frozen = true; Accent.Hex = Accent.Pick(OverlaySettings.Load().UseHallowAccent); Look.IsLight = Look.Resolve(OverlaySettings.Load().ColorMode, Accent.FilePath);
                var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(sa);
                new MainWindow(true).SnapAll(args[si + 1]); return 0;
            }
            bool created;
            var mutex = new Mutex(true, Link.MutexName, out created);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Link.ShowEventName);
            if (!created) { showEvent.Set(); return 0; }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Crash.Install(app);
            Accent.Hex = Accent.Pick(OverlaySettings.Load().UseHallowAccent); Look.IsLight = Look.Resolve(OverlaySettings.Load().ColorMode, Accent.FilePath); LoadTheme(app);
            var w = new MainWindow(args.Contains("--tray"));
            new Thread(() => { while (showEvent.WaitOne()) w.Dispatcher.BeginInvoke(new Action(w.ShowFromTray)); }) { IsBackground = true }.Start();
            app.Run();
            GC.KeepAlive(mutex);
            return 0;
        }
        // Theme.xaml with the accent colour in it (replaced as a whole when the accent changes)
        public static void LoadTheme(Application app)
        {
            string xaml;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowOverlay.Theme.xaml")) using (var r = new System.IO.StreamReader(s)) xaml = r.ReadToEnd();
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Pal.Theme(xaml, Accent.Hex, Look.IsLight)));
        }
    }

    // The panel itself (used by the overlay and by the preview in settings).
    public static class Panel
    {
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        public static FrameworkElement Build(OverlaySettings s, Sample x, string game, TimeSpan? session) { return Build(s, x, game, session, null, false, false); }

        // The frame-time graph: one frozen geometry per panel refresh (once a second), never per frame. Each pixel column shows the
        // slowest frame in it, so one long frame always shows; spikes (over twice the typical frame time) are red. Buffers are reused.
        static readonly double[] cols = new double[400], sorted = new double[4096];
        public static FrameworkElement Graph(double[] ms, int n, string size, double k, out int spikes)
        {
            double w = (size == "s" ? 110 : size == "l" ? 260 : 170) * k, h = (size == "s" ? 22 : size == "l" ? 48 : 32) * k;
            int c = Math.Min(cols.Length, Math.Max(10, (int)w));
            int m = Math.Min(n, sorted.Length); Array.Copy(ms, sorted, m); Array.Sort(sorted, 0, m);
            double med = sorted[m / 2], top = Math.Max(med * 3, 20), spike = Math.Max(med * 2, med + 4);
            for (int i = 0; i < c; i++) cols[i] = 0;
            for (int i = 0; i < n; i++) { int ci = (int)((long)i * c / n); if (ms[i] > cols[ci]) cols[ci] = ms[i]; }
            var line = new StreamGeometry(); var red = new StreamGeometry(); spikes = 0;
            using (var lc = line.Open()) using (var rc = red.Open())
            {
                for (int i = 0; i < c; i++)
                {
                    double px = i * w / (c - 1), py = h - Math.Min(cols[i], top) / top * h;
                    if (i == 0) lc.BeginFigure(new Point(px, py), false, false); else lc.LineTo(new Point(px, py), true, false);
                    if (cols[i] >= spike) { spikes++; rc.BeginFigure(new Point(px, h), false, false); rc.LineTo(new Point(px, py), true, false); }
                }
            }
            line.Freeze(); red.Freeze();
            var grid = new Grid { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            var mid = h - med / top * h;
            grid.Children.Add(new System.Windows.Shapes.Line { X1 = 0, X2 = w, Y1 = mid, Y2 = mid, Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), StrokeThickness = 1 });
            grid.Children.Add(new System.Windows.Shapes.Path { Data = line, Stroke = B("#A98BE0"), StrokeThickness = Math.Max(1, 1.3 * k), StrokeLineJoin = PenLineJoin.Round });
            grid.Children.Add(new System.Windows.Shapes.Path { Data = red, Stroke = B("#FF6B6B"), StrokeThickness = Math.Max(1.5, 2 * k) });
            var label = new TextBlock { Text = med.ToString("0.0") + " ms", FontSize = 10 * k, Foreground = B("#8F88A0"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6 * k, 0, 0, 0) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(grid); sp.Children.Add(label);
            return sp;
        }
        // msg: a short message from another Hallow app (flash, or a stronger remind); msgOnly: show just the message
        public static FrameworkElement Build(OverlaySettings s, Sample x, string game, TimeSpan? session, string msg, bool strong, bool msgOnly)
        {
            var k = s.Scale; var items = new List<KeyValuePair<string, FrameworkElement>>();
            if (msgOnly) { s = new OverlaySettings { Scale = s.Scale, Row = s.Row, Opacity = s.Opacity, MicWarn = false, Cpu = false, Gpu = false, GpuTemp = false, Ram = false, Ping = false, Clock = false, Session = false, Game = false, Fps = false, NowPlaying = false, Server = false, Traffic = false }; }
            bool bad = s.OnlyBad;   // only stats that are in trouble (and the MIC MUTED warning / messages)
            var styles = Stats.Ordered(s); double tf = Stats.TextFactor(s.TextSize);
            // id, icon, normal label, the value, 0 fine / 1 amber / 2 red
            Action<string, string, string, string, int> item = (id, icon, label, value, level) =>
            {
                if (bad && level == 0) return;
                var st = styles.FirstOrDefault(z => z.Id == id);
                var own = st != null && st.Color != null ? st.Color : null;
                var col = level == 2 ? "#FF6B6B" : level == 1 ? "#F0BE5A" : own ?? "#ECE8F4";
                var lbl = st != null && st.Label != null ? st.Label : label;
                if (id == "fps" && st != null && st.Label != null) value = value.Replace(" FPS", "");
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = s.Row ? new Thickness(0, 0, 14 * k, 0) : new Thickness(0, 1, 0, 1) };
                var ic = Icons.Make(icon, 12 * k, B(level == 0 ? own ?? "#A98BE0" : col)); ic.Margin = new Thickness(0, 0, 6 * k, 0); ic.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(ic);
                sp.Children.Add(new TextBlock { Text = (lbl.Length > 0 ? lbl + " " : "") + value, FontSize = 13 * k * tf, FontWeight = FontWeights.SemiBold, Foreground = B(col), VerticalAlignment = VerticalAlignment.Center });
                if (st != null && st.Graph)
                {
                    var g = StatHistory.Spark(id, 46 * k * tf, 13 * k * tf, B(level == 0 ? own ?? "#A98BE0" : col));
                    if (g != null) { g.Margin = new Thickness(6 * k, 0, 0, 0); sp.Children.Add(g); }
                }
                items.Add(new KeyValuePair<string, FrameworkElement>(id, sp));
            };
            if (x.BenchSeconds >= 0) item("bench", "chart-line", "BENCHMARK", ((int)x.BenchSeconds / 60) + ":" + ((int)x.BenchSeconds % 60).ToString("00"), 1);
            if (s.Game && game != null && !bad) item("game", "gamepad", "", game, 0);
            if (s.Session && session != null && !bad) item("session", "stopwatch", "", session.Value.TotalHours >= 1 ? ((int)session.Value.TotalHours + ":" + session.Value.Minutes.ToString("00")) : session.Value.Minutes + " min", 0);
            if (s.Fps && x.Fps >= 0) item("fps", "gauge-high", "", x.Fps.ToString("0") + " FPS" + (x.FpsLow >= 0 ? "  1% " + x.FpsLow.ToString("0") : "") + (s.Fps01 && x.FpsLow01 >= 0 ? "  0.1% " + x.FpsLow01.ToString("0") : ""), x.Fps < 30 ? 2 : x.Fps < 50 ? 1 : 0);
            if (s.FrameGraph && x.FrameMs != null && x.FrameCount >= 10)
            {
                int spikes; var g = Graph(x.FrameMs, x.FrameCount, s.GraphSize, k, out spikes);
                if (!bad || spikes > 0) { g.Margin = s.Row ? new Thickness(0, 0, 14 * k, 0) : new Thickness(0, 3, 0, 3); items.Add(new KeyValuePair<string, FrameworkElement>("graph", g)); }
            }
            if (s.Cpu && x.Cpu >= 0) item("cpu", "microchip", "CPU", x.Cpu.ToString("0") + "%", x.Cpu >= 90 ? 1 : 0);
            if (s.Gpu && x.Gpu >= 0) item("gpu", "display", "GPU", x.Gpu.ToString("0") + "%", 0);
            if (s.GpuTemp && x.GpuTemp >= 0) item("gputemp", "temperature-half", "", x.GpuTemp.ToString("0") + "\u00b0C", x.GpuTemp >= s.HotGpu + 7 ? 2 : x.GpuTemp >= s.HotGpu ? 1 : 0);
            if (s.GpuClocks && x.GpuClock >= 0) item("clocks", "wave-square", "", x.GpuClock.ToString("0") + (x.MemClock >= 0 ? " / " + x.MemClock.ToString("0") : "") + " MHz", 0);
            if (s.GpuPower && x.PowerW >= 0)
            {
                var pp = x.PowerLimitW > 0 ? x.PowerW / x.PowerLimitW * 100 : -1;
                item("power", "bolt", "", x.PowerW.ToString("0") + " W" + (pp >= 0 ? "  " + pp.ToString("0") + "%" : ""), pp >= 98 ? 1 : 0);   // at the limit: the card is power-limited
            }
            if (s.Vram && x.VramUsedMB >= 0 && x.VramTotalMB > 0)
                item("vram", "layer-group", "VRAM", (x.VramUsedMB / 1024).ToString("0.0") + " / " + (x.VramTotalMB / 1024).ToString("0") + " GB", x.VramUsedMB / x.VramTotalMB >= 0.95 ? 1 : 0);
            if (s.Ram && x.RamUsedGB >= 0)
            {
                var pct = x.RamUsedGB / x.RamTotalGB * 100;
                item("ram", "memory", "", x.RamUsedGB.ToString("0.0") + " / " + x.RamTotalGB.ToString("0") + " GB", pct > s.RamFull + 7 ? 2 : pct > s.RamFull ? 1 : 0);
            }
            if (s.Ping)
            {
                int lv = x.PingMs < 0 ? 2 : x.PingMs > s.HighPing + 70 ? 2 : x.PingMs > s.HighPing ? 1 : 0;
                var text = x.PingMs >= 0 ? x.PingMs.ToString("0") + " ms" : "no net";
                if (s.LossJitter && x.LossPct >= 0)
                {
                    if (x.JitterMs >= 0) text += "  \u00b1" + x.JitterMs.ToString("0");
                    text += "  " + x.LossPct.ToString("0.#") + "% loss";
                    lv = Math.Max(lv, x.LossPct >= 5 ? 2 : x.LossPct >= 2 ? 1 : x.JitterMs >= 30 ? 1 : 0);
                }
                item("ping", "signal", "", text, lv);
            }
            if (s.Server && x.ServerLabel != null)
            {
                var text = x.ServerLabel + "  " + (x.ServerMs >= 0 ? (x.ServerApprox ? "~" : "") + x.ServerMs.ToString("0") + " ms" : "no ping");
                if (x.ServerLoss >= 0) text += "  " + x.ServerLoss.ToString("0.#") + "% loss";
                if (x.Stalls > 0) text += "  " + x.Stalls + (x.Stalls == 1 ? " stall" : " stalls");
                int lv = x.ServerMs > s.HighPing + 70 ? 2 : x.ServerMs > s.HighPing ? 1 : 0;
                lv = Math.Max(lv, x.ServerLoss >= 5 ? 2 : x.ServerLoss >= 2 ? 1 : 0); if (x.Stalls >= 3) lv = Math.Max(lv, 2); else if (x.Stalls > 0) lv = Math.Max(lv, 1);
                item("server", "server", "", text, lv);
            }
            if (s.Traffic && x.UpKbps >= 0) item("traffic", "arrow-right-arrow-left", "", "\u2191" + x.UpKbps.ToString("0") + "  \u2193" + x.DownKbps.ToString("0") + " kbps", 0);
            if (s.NowPlaying && !string.IsNullOrEmpty(x.NowPlaying)) item("nowplaying", "music", "", x.NowPlaying.Length > 48 ? x.NowPlaying.Substring(0, 47) + "\u2026" : x.NowPlaying, 0);
            if (s.Clock) item("clock", "clock", "", DateTime.Now.ToString("HH:mm"), 0);
            // the layout's order (the benchmark marker always first)
            var order = styles.Select(z => z.Id).ToList();
            var list = new StackPanel { Orientation = s.Row ? Orientation.Horizontal : Orientation.Vertical };
            foreach (var i in items.OrderBy(z => z.Key == "bench" ? -1 : order.IndexOf(z.Key))) list.Children.Add(i.Value);            var outer = new StackPanel { Orientation = s.Row ? Orientation.Horizontal : Orientation.Vertical };
            if (!string.IsNullOrEmpty(msg))
            {
                var mb = new Border { Background = B(strong ? "#F07820" : "#7A4FD0"), CornerRadius = new CornerRadius(8 * k), Padding = new Thickness(10 * k, 4 * k, 10 * k, 4 * k), Margin = s.Row ? new Thickness(0, 0, 10 * k, 0) : new Thickness(0, 0, 0, 6 * k) };
                var mp = new StackPanel { Orientation = Orientation.Horizontal };
                var mi = Icons.Make(strong ? "bell" : "circle-check", (strong ? 14 : 13) * k, Brushes.White); mi.Margin = new Thickness(0, 0, 7 * k, 0); mi.VerticalAlignment = VerticalAlignment.Center; mp.Children.Add(mi);
                mp.Children.Add(new TextBlock { Text = msg.Length > 80 ? msg.Substring(0, 79) + "…" : msg, FontSize = (strong ? 14 : 13) * k, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
                mb.Child = mp; outer.Children.Add(mb);
            }
            if (s.MicWarn && x.MicMuted)
            {
                var warn = new Border { Background = B("#E0424C"), CornerRadius = new CornerRadius(8 * k), Padding = new Thickness(10 * k, 4 * k, 10 * k, 4 * k), Margin = s.Row ? new Thickness(0, 0, 10 * k, 0) : new Thickness(0, 0, 0, 6 * k) };
                var wp = new StackPanel { Orientation = Orientation.Horizontal };
                var mi = Icons.Make("microphone-slash", 13 * k, Brushes.White); mi.Margin = new Thickness(0, 0, 7 * k, 0); wp.Children.Add(mi);
                wp.Children.Add(new TextBlock { Text = "MIC MUTED", FontSize = 13 * k, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
                warn.Child = wp; outer.Children.Add(warn);
            }
            if (items.Count > 0) outer.Children.Add(list);
            if (outer.Children.Count == 0) return new Border();
            var a = (byte)(Math.Max(0.2, Math.Min(1, s.Opacity)) * 255);
            return new Border { Background = new SolidColorBrush(Color.FromArgb(a, 0x16, 0x12, 0x1C)), BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(a / 3), 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10 * k), Padding = new Thickness(12 * k, 7 * k, 0, 7 * k), Child = new Border { Padding = new Thickness(0, 0, s.Row ? 0 : 12 * k, 0), Child = outer } };
        }
    }

    public class MainWindow : Window
    {
        static SolidColorBrush B(string hex) { return Pal.B(hex); }   // 1.20: light mode swaps the colours

        readonly OverlaySettings S = OverlaySettings.Load();
        readonly Sampler sampler = new Sampler();
        readonly Tracker tracker = new Tracker();
        readonly FpsMeter fps = new FpsMeter(); DateTime fpsRetry; TextBlock foot;
        List<Game> games = new List<Game>();
        readonly OverlayWindow ov = new OverlayWindow();
        bool shown = true, exiting, toldTray; readonly bool startHidden;
        Border preview; TextBlock state; WF.NotifyIcon tray; WF.ToolStripMenuItem trayShow;
        DispatcherTimer tick, poll, msgTimer; readonly HubIcon hub = new HubIcon(); WF.ToolStripMenuItem trayBad;
        string msg, lastMessage; bool msgStrong; DateTime msgUntil;            // flash / remind from another Hallow app
        string lastStatus;                                       // what status.json says now (without the time)
        string exclusiveGame; readonly HashSet<string> toldExclusive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        public MainWindow(bool hidden)
        {
            startHidden = hidden;
            Title = "Hallow Overlay"; Width = 760; Height = 700; MinWidth = 640; MinHeight = 560;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = AppIcon(); } catch { }
            if (S.Hotkeys == null) S.Hotkeys = new Dictionary<string, string>();
            keys = new HotkeySet(IntPtr.Zero, S.Hotkeys);   // names only; the registered one replaces it below (never in --snap)
            Content = Build();
            Look.Track(this);   // 1.20: the title bar follows light / dark
            Closing += (s, e) =>
            {
                if (!exiting && S.TrayOnClose && !Snap.On)
                {
                    e.Cancel = true; Hide();
                    if (!toldTray && tray != null) { toldTray = true; Balloon("Hallow Overlay", "Still running - the overlay shows up when you play. " + (KeyText("toggle") != null ? KeyText("toggle") + " hides it. " : "") + "Right-click the icon to quit.", 4000); }
                }
                else Quit();
            };
            if (Snap.On) return;
            try { games = Library.Scan(new List<Game>()); } catch { }
            AddTestGame();
            sampler.PingHost = S.PingHost; sampler.Start();
            if (testCopy) { ov.Left = -32000; ov.Top = -32000; }   // a test copy: nothing on a real screen before it's placed on its made-up ones
            ov.Moved = SaveMove;
            ov.Ended = MoveEnded;
            SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke(new Action(DisplayChanged));   // screens plugged in / out, resolution or DPI changed
            if (!testCopy) SetupTray();
            // your own hotkeys (1.14) on this window's handle (it exists even while the window is hidden)
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            if (!testCopy) { keys = new HotkeySet(hwnd, S.Hotkeys); keyFails = keys.RegisterAll(); } ShowHotkeys();
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) => { if (msg == 0x0312) { var a = HotkeySet.ActionOf(wp.ToInt32()); if (a != null) { Invoke(a); handled = true; } } return IntPtr.Zero; });
            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; tick.Tick += (s, e) => Refresh(); tick.Start();
            poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; poll.Tick += (s, e) => { try { tracker.Poll(games.Concat(games.SelectMany(g => g.Alts)).ToList()); } catch { } CheckExclusive(); RunFps(); RunNet(); }; poll.Start();
            var rescan = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) }; rescan.Tick += (s, e) => { try { games = Library.Scan(new List<Game>()); } catch { } AddTestGame(); }; rescan.Start();
            msgTimer = new DispatcherTimer(); msgTimer.Tick += (s, e) => { msgTimer.Stop(); Refresh(); };
            Link.Serve(Dispatcher, OnCommand);
            Accent.Watch(Dispatcher, ApplyAccent);
            Look.WatchSystem(Dispatcher, ApplyAccent);   // 1.20: "Same as Windows" follows Windows' light / dark setting
            if (!testCopy) { DateTime lc; if (S.AutoUpdate && (!DateTime.TryParse(S.LastUpdateCheck, out lc) || (DateTime.Now - lc).TotalHours > 20)) CheckForUpdate(false); }
            var daily = new DispatcherTimer { Interval = TimeSpan.FromHours(6) }; daily.Tick += (s, e) => { DateTime lc; if (!testCopy && S.AutoUpdate && (!DateTime.TryParse(S.LastUpdateCheck, out lc) || (DateTime.Now - lc).TotalHours > 20)) CheckForUpdate(false); }; daily.Start();
            if (!testCopy) Dispatcher.BeginInvoke(new Action(CheckCrash), DispatcherPriority.ApplicationIdle);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { fps.Stop(); net.Stop(); } catch { } };   // never leave a trace session behind
            WriteStatus(true);
            IsVisibleChanged += (s, e) => { if (IsVisible) Dispatcher.BeginInvoke(new Action(() => { ShowWhatsNew(); ShowCrash(); }), DispatcherPriority.ApplicationIdle); };
            if (!startHidden) Show();
        }

        // ---- Hallow accent: re-theme the open windows when theme.json or the setting changes
        void ApplyAccent()
        {
            var hex = Accent.Pick(S.UseHallowAccent); var light = Look.Resolve(S.ColorMode, Accent.FilePath);
            if (hex == Accent.Hex && light == Look.IsLight) return;
            Accent.Hex = hex; Look.IsLight = light; Program.LoadTheme(Application.Current);
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text");
            var sv = Content as ScrollViewer; var off = sv != null ? sv.VerticalOffset : 0;
            Content = Build(); Refresh(); sv = Content as ScrollViewer; if (sv != null) sv.ScrollToVerticalOffset(off);
            if (histWin != null && histWin.IsLoaded) histWin.Rebuild();
            if (layoutWin != null && layoutWin.IsLoaded) layoutWin.Rebuild();
            Look.TitleBars();
        }

        // ---- 1.20: the look of these settings windows (the in-game panel always stays dark)
        FrameworkElement LookRow()
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
            var lbl = new TextBlock { Text = "Look", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }; DockPanel.SetDock(lbl, Dock.Left); row.Children.Add(lbl);
            var wp = new WrapPanel();
            foreach (var o in new[] { Tuple.Create(Look.Follow, "Follow Hallow"), Tuple.Create(Look.Light, "Light"), Tuple.Create(Look.Dark, "Dark") })
            {
                var key = o.Item1; var c = new ToggleButton { Content = o.Item2, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 5, 12, 5), IsChecked = Look.Normalize(S.ColorMode) == key };
                c.Click += (s, e) => { S.ColorMode = key; S.Save(); foreach (ToggleButton x in wp.Children) x.IsChecked = x == c; Dispatcher.BeginInvoke(new Action(ApplyAccent)); };
                wp.Children.Add(c);
            }
            row.Children.Add(wp);
            var sp = new StackPanel(); sp.Children.Add(row);
            sp.Children.Add(new TextBlock { Text = "For these settings windows. Follow Hallow uses the Light / Dark / Same as Windows choice in Hallow Toolkit. The in-game panel always stays dark (it's see-through over games).", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            return sp;
        }

        // ---- crash notice: once per crash report, when the window is shown (a tray message first if it starts hidden)
        string pendingCrash;
        void CheckCrash()
        {
            var f = Crash.Files().FirstOrDefault();
            if (f == null || System.IO.Path.GetFileName(f) == S.LastCrashShown) return;
            pendingCrash = f;
            if (IsVisible) ShowCrash();
            else Balloon("Hallow Overlay closed unexpectedly last time", "A crash report is kept on this PC (nothing is sent). Open Overlay's settings to see it.", 6000);
        }
        void ShowCrash()
        {
            if (pendingCrash == null || Snap.On) return;
            var f = pendingCrash; pendingCrash = null;
            S.LastCrashShown = System.IO.Path.GetFileName(f); S.Save();
            new CrashNotice(f) { Owner = this }.Show();
        }

        // ---- updates (GitHub releases; asks first, checks the SHA-256 before installing)
        Border updateRow; Button updateBtn; TextBlock updateText; GhRelease pending; string updateNote;
        void CheckForUpdate(bool manual)
        {
            if (manual) { updateNote = "Looking for an update..."; ShowUpdate(); }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = Updater.Latest();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (r == null) { updateNote = manual ? "Couldn't reach GitHub - try again later." : null; ShowUpdate(); return; }
                    S.LastUpdateCheck = DateTime.Now.ToString("o"); S.Save();
                    if (!Updater.IsNewer(r)) { pending = null; updateNote = manual ? "You have the newest version (v" + Program.Version + ")." : null; ShowUpdate(); return; }
                    pending = r; updateNote = null; ShowUpdate();
                    var v = r.Version.ToString(2);
                    if (!manual && S.ToldUpdate != v) { S.ToldUpdate = v; S.Save(); Balloon("Hallow Overlay " + v + " is available", "Open Overlay's settings to update (it asks first).", 6000); }
                }));
            });
        }
        void ShowUpdate()
        {
            if (updateRow == null) return;
            if (pending != null)
            {
                var v = pending.Version.ToString(2);
                updateText.Text = "Hallow Overlay " + v + " is available (you have " + Program.Version + "). It's checked against GitHub's checksum before it installs, then Overlay reopens.";
                updateBtn.Content = "Update to v" + v; updateBtn.Visibility = Visibility.Visible; updateBtn.IsEnabled = true;
                updateBtn.ToolTip = (pending.Notes ?? "").Length > 600 ? pending.Notes.Substring(0, 600) + "..." : pending.Notes;
                updateRow.Visibility = Visibility.Visible;
            }
            else if (updateNote != null) { updateText.Text = updateNote; updateBtn.Visibility = Visibility.Collapsed; updateRow.Visibility = Visibility.Visible; }
            else updateRow.Visibility = Visibility.Collapsed;
        }
        void InstallUpdate()
        {
            var r = pending; if (r == null) return;
            updateBtn.IsEnabled = false; updateBtn.Content = "Updating...";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var err = Updater.Install(r);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (err != null) { updateText.Text = err; updateBtn.IsEnabled = true; updateBtn.Content = "Update to v" + r.Version.ToString(2); }
                    else updateText.Text = "Installing the update - Hallow Overlay reopens in a few seconds.";
                }));
            });
        }

        // ---- benchmark (Ctrl+Shift+F9): every frame between start and stop, then a report
        bool benchOn; DateTime benchStart; string benchGame; HashSet<int> benchPids; string lastBench;
        readonly StatLog statLog = new StatLog();
        void ToggleBench() { if (benchOn) StopBench(); else StartBench(); }
        void StartBench()
        {
            var cur = Current;
            if (cur == null) { ShowMessage("Start a game first - the benchmark measures its frames", false, 4); return; }
            if (!fps.Running && !fps.Start()) { ShowMessage(fps.Denied ? "Benchmarks need FPS, which Windows doesn't allow on this account" : "Couldn't start the frame counter", false, 5); return; }
            fps.BeginRecording(); benchOn = true; benchStart = DateTime.Now; benchGame = (cur.Game.Parent ?? cur.Game).Name; benchPids = new HashSet<int>(cur.Pids);
            ShowMessage("Benchmark started - " + (KeyText("bench") ?? "the tray menu") + " stops it", false, 3);
        }
        void StopBench()
        {
            if (!benchOn) return; benchOn = false;
            var cur = Current; if (cur != null) foreach (var p in cur.Pids) benchPids.Add(p);
            string src; var ms = fps.EndRecording(benchPids, out src);
            var rep = BenchReport.Make(ms, benchGame, benchStart, src);
            if (rep == null) { ShowMessage("Benchmark too short - let it run a few seconds", false, 4); return; }
            try { lastBench = rep.SavePng(BenchReport.Folder); rep.SaveCsv(BenchReport.Folder); }
            catch (Exception e) { ShowMessage("Couldn't save the benchmark: " + e.Message, false, 5); return; }
            Dispatcher.BeginInvoke(new Action(() => ShowMessage(rep.Summary(), false, 8)));
            Balloon("Benchmark saved", rep.Summary() + ". Open it from Overlay's settings (Last benchmark).", 6000);
        }
        static void OpenFolder(string f) { try { System.IO.Directory.CreateDirectory(f); System.Diagnostics.Process.Start("explorer.exe", "\"" + f + "\""); } catch { } }
        static void OpenFile(string f) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(f) { UseShellExecute = true }); } catch { } }

        void ShowWhatsNew()
        {
            if (S.LastSeenVersion == Program.Version || !IsVisible || Snap.On) return;
            var items = WhatsNew.Since(S.LastSeenVersion);
            S.LastSeenVersion = Program.Version; S.Save();
            if (items.Count > 0) new WhatsNewWindow(items) { Owner = this }.Show();
        }

        // ================================================================ Hallow family link
        string OnCommand(string line)
        {
            line = line.Trim(); int sp = line.IndexOf(' ');
            var cmd = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant(); var arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            switch (cmd)
            {
                case "ping": return "ok " + Link.Json(new Dictionary<string, object> { { "app", "Overlay" }, { "version", Program.Version }, { "look", Look.IsLight ? "light" : "dark" } });
                case "status": return "ok " + StatusJson();
                case "show": shown = true; Refresh(); return "ok";
                case "hide": shown = false; Refresh(); return "ok";
                case "toggle": Toggle(); return "ok";
                case "flash":
                case "remind":
                    if (arg.Length == 0) return "error no text";
                    ShowMessage(arg, cmd == "remind"); return "ok";
                case "open": ShowFromTray(); return "ok";
                case "menu": return "ok " + Link.Json(new Dictionary<string, object> { { "app", "Overlay" }, { "items", MenuItems().ToArray() } });
                case "invoke": return Invoke(arg);
                case "move": return ToggleMove(arg.Equals("force", StringComparison.OrdinalIgnoreCase), null);
                case "test-display": if (!testCopy) return "error unknown command"; DisplayChanged(); return "ok";   // test copies: the screens file changed   // scripts: not over a focused game unless "move force"
                case "quit":
                    var qt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) }; qt.Tick += (s, e) => { qt.Stop(); Quit(); }; qt.Start();   // after the reply is sent
                    return "ok";
                case "game-on":
                case "game-off":
                    if (arg.Length == 0) return "error no game name";
                    S.OffGames.RemoveAll(n => string.Equals(n, arg, StringComparison.OrdinalIgnoreCase));
                    if (cmd == "game-off") S.OffGames.Add(arg);
                    S.Save(); Refresh(); return "ok";
                default: return "error unknown command";
            }
        }
        // ---- One Hallow icon: what Hub's menu shows for Overlay, and running those items
        OverlaySettings CurrentLook { get { var c = Current; return S.LookFor(c == null ? null : (c.Game.Parent ?? c.Game).Name); } }
        List<Dictionary<string, object>> MenuItems()
        {
            var l = new List<Dictionary<string, object>>();
            l.Add(new Dictionary<string, object> { { "id", "toggle" }, { "label", "Show the overlay" }, { "checked", shown } });
            l.Add(new Dictionary<string, object> { { "id", "only-bad" }, { "label", "Only when something's wrong" }, { "checked", CurrentLook.OnlyBad } });
            l.Add(new Dictionary<string, object> { { "id", "bench" }, { "label", benchOn ? "Stop benchmark" : "Start benchmark" } });
            l.Add(new Dictionary<string, object> { { "id", "history" }, { "label", "Match history" } });
            l.Add(new Dictionary<string, object> { { "id", "move" }, { "label", ov.Moving ? "Done moving the overlay" : "Move the overlay (drag to place)" }, { "checked", ov.Moving } });
            l.Add(new Dictionary<string, object> { { "id", "-" } });
            l.Add(new Dictionary<string, object> { { "id", "settings" }, { "label", "Settings" } });
            return l;
        }
        string Invoke(string id)
        {
            switch (id)
            {
                case "toggle": Toggle(); return "ok";
                case "only-bad": ToggleOnlyBad(); return "ok";
                case "settings": ShowFromTray(); return "ok";
                case "history": ShowHistory(); return "ok";
                case "bench": ToggleBench(); return "ok";
                case "move": return ToggleMove(true, null);   // a menu click or the hotkey: the user asked for it
                default: return "error unknown item";
            }
        }

        // ---- "Move it" (1.15): drag the panel to place it
        string moveGame;              // the game whose look is shown while moving (null: the usual look)
        string moveSpotGame;          // 1.19: the game whose own spot the move is saved into (null: your usual spot)
        string savedNote; DateTime savedNoteUntil;   // "Saved for Delta Force" / "Saved as your usual spot" (settings and the panel)
        OverlaySettings lastLook;     // the look the panel was last placed with
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        // A game has the focus: it's in exclusive fullscreen, or its window is the one in front.
        bool GameHasFocus()
        {
            var cur = Current; if (cur == null) return false;
            int st = 0; try { SHQueryUserNotificationState(out st); } catch { }
            if (st == 3) return true;
            int pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            return pid != 0 && cur.Pids.Contains(pid);
        }
        // Move mode never starts by itself over a game that has the focus - only when you asked for it (menu, hotkey, settings, "move force").
        public static bool MoveAllowed(bool explicitAsk, bool gameFocused) { return explicitAsk || !gameFocused; }
        string ToggleMove(bool explicitAsk, string forGame)
        {
            if (Snap.On) return "error not now";
            if (ov.Moving) { ov.EndMove("done"); return "ok done"; }
            if (!MoveAllowed(explicitAsk, GameHasFocus())) return "error a game has the focus - use the menu, the hotkey or \"move force\"";
            // which spot: a game's own one (the game picked in the settings, else the running game) if it has one, else the usual spot
            var c = Current; string g = IsVisible && editGame != null ? editGame : c == null ? null : (c.Game.Parent ?? c.Game).Name;
            // 1.19: never asks - a game with "its own spot" on gets the move, otherwise it's your usual spot
            var k = S.PerGameKey(g); OverlaySettings target = S; moveGame = null; moveSpotGame = null;
            if (k != null && S.PerGame[k].OwnSpot) { target = S.PerGame[k]; moveGame = k; moveSpotGame = k; }
            else if (k != null) moveGame = k;   // the game's own look, at the usual spot
            ov.BeginMove(target, !testCopy);   // a test copy never takes the focus
            Refresh();   // shows the panel (even if hidden) and puts it in place
            WriteStatus(true);
            return "ok moving";
        }
        // A drag, a nudge or the end of a drag: saved to overlay.json at once (1.19: atomically, and it says which spot it went to).
        void SaveMove()
        {
            bool ok = S.Save();
            var t = SavedText(); savedNote = ok ? t : "Couldn't save the position (" + OverlaySettings.FilePath + ") - it's kept until Overlay closes"; savedNoteUntil = DateTime.Now.AddSeconds(8);
            UpdatePosText(); WriteStatus(true);
        }
        // "Saved for Delta Force" or "Saved as your usual spot", and which games keep their own spot
        string SavedText()
        {
            var tgt = ov.MoveTarget; string game = moveSpotGame != null && tgt != null && tgt != S ? moveSpotGame : null;
            if (game != null) return "Saved for " + game;
            var own = S.PerGame.Where(p => p.Value != null && p.Value.OwnSpot).Select(p => p.Key).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return "Saved as your usual spot" + (own.Count == 0 ? "" : " (" + (own.Count <= 2 ? string.Join(" and ", own) : own.Count + " games") + (own.Count == 1 ? " keeps its" : " keep their") + " own spot)");
        }
        void MoveEnded(string why)
        {
            bool ok = S.Save(); var saved = SavedText(); var tgt = ov.MoveTarget ?? S;
            moveGame = null;
            if (!ok) ShowMessage("Couldn't save the position - check that " + OverlaySettings.Dir + " can be written", true);
            else if (why == "timeout") ShowMessage("Done moving (2 minutes without a drag) - " + saved.Substring(0, 1).ToLowerInvariant() + saved.Substring(1), false, 4);
            else if (why != "off") ShowMessage(saved + ": " + Placement.SpotName(tgt.Anchor) + (tgt.OffX != 0 || tgt.OffY != 0 ? " (moved)" : ""), false, 3);   // it stays in view a moment, even with no game running
            if (why != "off") { savedNote = ok ? saved : null; savedNoteUntil = DateTime.Now.AddSeconds(8); }
            moveSpotGame = null;
            Refresh(); if (lookHost != null && IsVisible) BuildLook(); WriteStatus(true);
        }
        // Screens plugged in / out, resolution or DPI changed: put it back from the saved spot (a saved screen that's missing
        // means the main screen for now - the choice stays saved, so it goes back when that screen returns).
        void DisplayChanged()
        {
            Refresh(); UpdatePosText(); WriteStatus(true);
        }
        Dictionary<string, object> PositionInfo()
        {
            var p = lastLook ?? CurrentLook; var r = ov.LastRect; var s = ov.LastScreen;
            return new Dictionary<string, object> { { "anchor", Placement.Valid(p.Anchor) }, { "spot", Placement.SpotName(p.Anchor) }, { "margin", p.Margin }, { "offsetX", p.OffX }, { "offsetY", p.OffY },
                { "screen", s == null ? p.Screen : s.Device }, { "screenName", s == null ? "" : s.Name }, { "screenFound", ov.LastScreenFound }, { "ownSpot", p.OwnSpot },
                { "x", r.IsEmpty ? 0 : r.X }, { "y", r.IsEmpty ? 0 : r.Y }, { "width", r.IsEmpty ? 0 : r.Width }, { "height", r.IsEmpty ? 0 : r.Height }, { "moving", ov.Moving }, { "clickThrough", ov.IsClickThrough } };
        }
        void ToggleOnlyBad()
        {
            var c = Current; var k = S.PerGameKey(c == null ? null : (c.Game.Parent ?? c.Game).Name);
            var l = k != null ? S.PerGame[k] : S; l.OnlyBad = !l.OnlyBad; S.Save();
            if (lookHost != null) BuildLook();
            Refresh();
        }
        // A tray message: from Hub's icon while Hub hosts it (our own icon comes back for a moment if Hub doesn't take it).
        void Balloon(string title, string text, int ms)
        {
            if (tray == null) return;
            if (testMode) { lastMessage = title + ": " + text; return; }
            if (!hub.Hosted) { tray.ShowBalloonTip(ms, title, text, WF.ToolTipIcon.None); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (HubIcon.Notify(title, text)) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    tray.Visible = true; tray.ShowBalloonTip(ms, title, text, WF.ToolTipIcon.None);
                    var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
                    back.Tick += (s, e) => { back.Stop(); if (hub.Hosted && !exiting) tray.Visible = false; }; back.Start();
                }));
            });
        }

        // ---- match summaries
        readonly List<GameMatch> matches = MatchStore.Load(); readonly MatchRecorder rec = new MatchRecorder(); string recKey; MatchHistoryWindow histWin;
        void MatchDone(GameMatch m)
        {
            MatchStore.Add(matches, m); WriteStatus(true);
            Dispatcher.BeginInvoke(new Action(() => ShowMessage(m.Summary(), false, 6)));
        }
        void ShowHistory()
        {
            if (histWin != null && histWin.IsLoaded) { histWin.Activate(); return; }
            histWin = new MatchHistoryWindow(matches, S.HideServerIp); try { histWin.Icon = Icon; } catch { }
            histWin.Show();
        }

        public void ShowMessage(string text, bool strong) { ShowMessage(text, strong, strong ? 8 : 2.5); }
        static readonly bool testCopy = OverlaySettings.TestDir != null;   // 1.19: a test copy with its own data folder (no tray, hotkeys, updates or focus)
        static readonly bool testMode = testCopy || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HALLOW_OVERLAY_TESTGAME"));   // developer tests: nothing on screen
        public void ShowMessage(string text, bool strong, double seconds)
        {
            if (testMode) { lastMessage = text; return; }
            msg = text; msgStrong = strong; msgUntil = DateTime.Now.AddSeconds(seconds);
            msgTimer.Stop(); msgTimer.Interval = msgUntil - DateTime.Now; msgTimer.Start();
            Refresh();
        }
        string StatusJson()
        {
            var d = new Dictionary<string, object> { { "app", "Overlay" }, { "version", Program.Version }, { "updated", DateTime.Now.ToString("s") },
                { "visible", ov.IsVisible }, { "offGames", S.OffGames.ToArray() }, { "hallowIcon", hub.Hosted }, { "matches", matches.Take(5).Select(m => m.Short()).ToArray() }, { "lastCrash", LastCrashIso() }, { "hotkeys", HotkeySet.Actions.ToDictionary(a => a.Id, a => (object)(keys == null ? a.Default : keys.Text(a.Id))) }, { "position", PositionInfo() } };
            if (live != null) d["live"] = live;
            if (testMode) d["testMessage"] = lastMessage;   // Hallow Remote's Live stats tab
            return Link.Json(d);
        }
        static string LastCrashIso() { var l = Crash.Last(); return l == null ? null : l.Value.ToString("s"); }
        void WriteStatus(bool force)
        {
            var now = ov.IsVisible + "|" + hub.Hosted + "|" + matches.Count + "|" + LastCrashIso() + "|" + string.Join("|", S.OffGames) + "|" + ov.LastRect + "|" + ov.Moving + "|" + (lastLook == null ? "" : lastLook.Anchor + lastLook.OffX + "," + lastLook.OffY + lastLook.Margin + lastLook.Screen);
            if (!force && now == lastStatus) return;
            lastStatus = now; Link.WriteStatus(StatusJson());
        }
        bool IsOff(string game) { return game != null && S.OffGames.Any(n => string.Equals(n, game, StringComparison.OrdinalIgnoreCase)); }

        // Windows can't draw anything over a game in exclusive fullscreen: say so once per game.
        void CheckExclusive()
        {
            var cur = Current; string game = cur == null ? null : (cur.Game.Parent ?? cur.Game).Name;
            int st = 0; try { SHQueryUserNotificationState(out st); } catch { }
            exclusiveGame = game != null && st == 3 ? game : null;   // 3 = QUNS_RUNNING_D3D_FULL_SCREEN
            if (exclusiveGame != null && toldExclusive.Add(exclusiveGame) && tray != null)
                Balloon("Hallow Overlay can't show over " + exclusiveGame, exclusiveGame + " is in exclusive fullscreen - Windows doesn't let anything draw over that. Switch it to borderless (windowed fullscreen) in the game's video settings.", 8000);
        }
        // The frame counter only runs while a game is running and FPS is on.
        // The game server finder only runs while a game is running and its Server or traffic stat is on.
        readonly NetMonitor net = new NetMonitor(); readonly ServerPing serverPing = new ServerPing(); DateTime netRetry; int netChanges; TextBlock serverInfo;
        void RunNet()
        {
            if (testCopy) return;   // the trace sessions have one name per PC: a test copy must never take the real Overlay's
            var cur = Current; var look = S.LookFor(cur == null ? null : (cur.Game.Parent ?? cur.Game).Name);
            bool want = cur != null && (look.Server || look.Traffic);
            if (want)
            {
                net.SetPids(cur.Pids);
                if (!net.Running && !net.Denied && DateTime.Now >= netRetry) { if (!net.Start()) netRetry = DateTime.Now.AddMinutes(1); netChanges = net.Finder.Changes; }
            }
            else if (net.Running) { net.Stop(); serverPing.Set(null); }
        }
        void RunFps()
        {
            if (testCopy) return;
            var cl = CurrentLook; bool want = ((cl.Fps || cl.FrameGraph) && Current != null) || benchOn;
            if (want && !fps.Running && !fps.Denied && DateTime.Now >= fpsRetry) { if (!fps.Start()) fpsRetry = DateTime.Now.AddMinutes(1); }
            else if (!want && fps.Running) fps.Stop();
        }
        public static string ExclusiveNote(string game) { return game + " is in exclusive fullscreen - Windows can't draw anything over that. Switch it to borderless (windowed fullscreen) in the game's video settings to see the overlay."; }

        static ImageSource AppIcon()
        {
            var ico = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            return Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        Tracker.Running Current { get { return tracker.Now.Values.OrderBy(r => r.Since).FirstOrDefault(); } }

        void Refresh()
        {
            if (Snap.On) return;
            var x = sampler.Latest; var cur = Current;
            var game = cur == null ? null : (cur.Game.Parent ?? cur.Game).Name;
            TimeSpan? session = cur == null ? (TimeSpan?)null : DateTime.Now - cur.Since;
            double f, low, low01; x.Fps = x.FpsLow = x.FpsLow01 = -1;
            if (cur != null && fps.Running && fps.Read(cur.Pids, out f, out low, out low01)) { x.Fps = f; x.FpsLow = low; x.FpsLow01 = low01; }
            x.FrameMs = null; x.FrameCount = 0;
            if (cur != null && fps.Running && S.LookFor(game).FrameGraph) { x.FrameMs = frameBuf; x.FrameCount = fps.FrameTimes(cur.Pids, 5, frameBuf); }
            x.ServerLabel = null; x.UpKbps = x.DownKbps = -1; x.ServerMs = x.ServerLoss = -1; x.Stalls = 0; string srvRegion = null, srvIp = null, srvHost = null;
            if (cur != null && net.Running)
            {
                var fd = net.Finder; fd.Tick(DateTime.UtcNow.ToFileTimeUtc());
                var srv = fd.Server; serverPing.Set(srv);
                x.UpKbps = fd.UpKbps; x.DownKbps = fd.DownKbps;
                if (srv != null)
                {
                    var info = ServerNames.Get(srv);
                    x.ServerLabel = info.Code ?? (S.HideServerIp ? "Server" : srv.ToString()); srvRegion = info.Code; srvIp = srv.ToString(); srvHost = info.Host;
                    x.ServerMs = serverPing.Ms; x.ServerApprox = serverPing.Approx; x.ServerLoss = serverPing.LossPct; x.Stalls = fd.Stalls;
                }
                if (fd.Changes != netChanges) { netChanges = fd.Changes; if (S.LookFor(game).Server && msg == null) { ShowMessage("Server changed", false); return; } }
            }
            // match summary: one per game session
            var recNow = cur == null ? null : game + "|" + cur.Since.Ticks;
            if (recNow != recKey)
            {
                if (benchOn) StopBench();   // the game closed: the benchmark ends with it
                recKey = recNow; var done = rec.End();
                if (cur != null) rec.Begin(game, cur.Since);
                if (done != null) MatchDone(done);
            }
            if (cur != null) rec.Add(x, srvRegion, srvIp, net.Running ? net.Finder.TotalStalls : -1, DateTime.Now);
            if (benchOn) x.BenchSeconds = (DateTime.Now - benchStart).TotalSeconds;
            // the last minute of each stat, for the layout's small graphs
            StatHistory.Add("fps", x.Fps); StatHistory.Add("cpu", x.Cpu); StatHistory.Add("gpu", x.Gpu); StatHistory.Add("gputemp", x.GpuTemp); StatHistory.Add("clocks", x.GpuClock);
            StatHistory.Add("power", x.PowerW); StatHistory.Add("vram", x.VramUsedMB); StatHistory.Add("ram", x.RamUsedGB); StatHistory.Add("ping", x.PingMs); StatHistory.Add("server", x.ServerLabel != null ? x.ServerMs : -1);
            if (S.LogCsv && cur != null) statLog.Write(game, recNow, x, srvRegion ?? (S.HideServerIp ? null : srvIp), DateTime.Now); else statLog.Close();
            bool off = IsOff(game);
            bool want = shown && !off && (!S.OnlyInGame || cur != null);
            if (msg != null && DateTime.Now >= msgUntil) msg = null;
            var look = S.LookFor(ov.Moving && moveGame != null ? moveGame : game);
            if (want || msg != null || ov.Moving)
            {
                // move mode: the whole panel (not just the stats in trouble), even while it's hidden, so you see its real size
                var shownLook = look; if (ov.Moving && look.OnlyBad) { shownLook = look.LookCopy(); Placement.CopySpot(look, shownLook); shownLook.OnlyBad = false; }
                ov.SetPanel(Panel.Build(shownLook, x, ov.Moving ? moveGame ?? game : game, session, msg, msgStrong, !want && !ov.Moving));   // messages still show briefly when the panel is hidden
                if (!ov.IsVisible) ov.Show();
                ov.Place(look);
            }
            else { if (ov.IsVisible) ov.Hide(); ov.Predict(look); }   // status.json still says where it goes
            ov.EnsureClickThrough();
            lastLook = look;
            if (IsVisible)
            {
                var px = x; if (px.Fps < 0 && !fps.Denied) { px = x.Copy(); px.Fps = 144; px.FpsLow = 97; px.FpsLow01 = 81; px.FrameMs = FakeFrames(); px.FrameCount = px.FrameMs.Length; }   // what it looks like in a game
                var pl = editGame != null ? S.LookFor(editGame) : look;
                AlignPreview(pl);
                preview.Child = Fit(Panel.Build(pl, px, editGame ?? game ?? "Delta Force", session ?? TimeSpan.FromMinutes(42)));
                foot.Text = FootText(); serverInfo.Text = ServerText(cur != null);
                state.Text = exclusiveGame != null && exclusiveGame == game ? ExclusiveNote(game) :
                    !shown ? "Hidden - " + (KeyText("toggle") != null ? "press " + KeyText("toggle") + " (or use the tray menu)" : "use the tray menu") + " to show it again." :
                    off ? "Turned off for " + game + " (in Hallow Hub's game settings)." :
                    cur != null ? "Showing over " + game + "." : S.OnlyInGame ? "Waiting for a game - it appears by itself when one starts." : "Showing on your screen now.";
                state.Foreground = exclusiveGame != null && exclusiveGame == game ? B("#F0BE5A") : (Brush)FindResource("Muted");
            }
            if (trayShow != null) trayShow.Checked = shown;
            // live numbers for Hallow Remote: every 2 s while a game runs, once more when it ends (to clear them)
            live = cur == null ? null : LiveStats.For(game, x, srvIp != null, srvRegion, srvHost, S.HideServerIp);
            if (live != null && (DateTime.Now - liveWritten).TotalSeconds >= 1.9) { liveWritten = DateTime.Now; WriteStatus(true); }
            else if (live == null && liveWas) WriteStatus(true);
            liveWas = live != null;
            WriteStatus(false);
        }

        void Toggle() { shown = !shown; Refresh(); }
        Dictionary<string, object> live; DateTime liveWritten; bool liveWas;
        // developer test: HALLOW_OVERLAY_TESTGAME = "Name|folder" adds a made-up game (any process started from that folder counts)
        void AddTestGame()
        {
            var e = Environment.GetEnvironmentVariable("HALLOW_OVERLAY_TESTGAME"); if (string.IsNullOrEmpty(e) || e.IndexOf('|') < 0) return;
            var p = e.Split('|'); games.Add(new Game { Id = "test", Name = p[0], InstallDir = p[1], Source = "custom" });
        }
        readonly double[] frameBuf = new double[4096];
        static double[] fakeFrames;
        // a made-up 144 fps game with a couple of hitches, for the preview and --snap
        public static double[] FakeFrames()
        {
            if (fakeFrames != null) return fakeFrames;
            var r = new Random(7); var l = new List<double>();
            for (int i = 0; i < 720; i++) l.Add(i == 260 ? 31 : i == 540 ? 22 : i % 97 == 0 ? 11 : 6.9 + r.NextDouble() * 0.9);
            return fakeFrames = l.ToArray();
        }
        static UIElement Fit(FrameworkElement el) { return new Viewbox { Child = el, StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform }; }   // the preview shrinks a wide panel to fit

        // ================================================================ settings window
        UIElement Build()
        {
            var sp = new StackPanel { Margin = new Thickness(26, 16, 26, 20) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            try { title.Children.Add(new Image { Source = AppIcon(), Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) }); } catch { }
            title.Children.Add(new TextBlock { Text = "HALLOW", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Accent.Ink(), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = " OVERLAY", FontSize = 22, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(title);
            // preview on a fake game background
            var stage = new Border { Height = 150, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 8), ClipToBounds = true };
            var bgb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            bgb.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#3A5A40"), 0)); bgb.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#24303F"), 0.6)); bgb.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#5A3A2A"), 1));
            stage.Background = bgb;
            preview = new Border { Margin = new Thickness(14) };
            var pg = new Grid(); pg.Children.Add(preview); stage.Child = pg;
            sp.Children.Add(stage);
            state = new TextBlock { Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(2, 0, 0, 14), TextWrapping = TextWrapping.Wrap };
            sp.Children.Add(state);
            // which look is being edited: every game, or one game's own
            var forRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            forRow.Children.Add(new TextBlock { Text = "Settings for", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }); DockPanel.SetDock(forRow.Children[0], Dock.Left);
            forBox = new ComboBox { Style = (Style)FindResource("Combo"), Width = 240, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            FillGames(); forBox.SelectedIndex = 0;
            forBox.DropDownOpened += (s, e) => FillGames();
            forBox.SelectionChanged += (s, e) => { if (filling) return; editGame = forBox.SelectedIndex <= 0 ? null : forBox.SelectedItem as string; BuildLook(); Refresh(); };
            ownBox = new CheckBox { Content = "Own look for this game (spot, size, stats)", Style = (Style)FindResource("Switch"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            ownBox.Click += (s, e) =>
            {
                if (editGame == null) return;
                var k = S.PerGameKey(editGame);
                if (ownBox.IsChecked == true) { if (k == null) S.PerGame[editGame] = S.LookCopy(); }
                else if (k != null) S.PerGame.Remove(k);
                S.Save(); BuildLook(); Refresh();
            };
            var fr = new StackPanel { Orientation = Orientation.Horizontal }; fr.Children.Add(forBox); fr.Children.Add(ownBox);
            forRow.Children.Add(fr); sp.Children.Add(forRow);
            lookHost = new StackPanel(); sp.Children.Add(lookHost);
            BuildLook();
            // when a stat counts as "in trouble" (all games)
            var lim = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            lim.Children.Add(new TextBlock { Text = "In trouble from", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }); DockPanel.SetDock(lim.Children[0], Dock.Left);
            var lp = new StackPanel { Orientation = Orientation.Horizontal };
            Action<string, string, Func<double>, Action<double>, double, double> num = (before, after, get, set, min, max) =>
            {
                lp.Children.Add(new TextBlock { Text = before, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var tb = new TextBox { Style = (Style)FindResource("Field"), Width = 56, Text = get().ToString("0"), VerticalAlignment = VerticalAlignment.Center };
                tb.TextChanged += (s, e) => { double v; if (double.TryParse(tb.Text, out v) && v >= min && v <= max) { set(v); S.Save(); Refresh(); } };
                tb.LostFocus += (s, e) => tb.Text = get().ToString("0");
                lp.Children.Add(tb);
                lp.Children.Add(new TextBlock { Text = after, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 18, 0), Foreground = (Brush)FindResource("Muted") });
            };
            num("GPU", "°C", () => S.HotGpu, v => S.HotGpu = v, 40, 110);
            num("Ping", "ms", () => S.HighPing, v => S.HighPing = v, 10, 1000);
            num("RAM", "% used", () => S.RamFull, v => S.RamFull = v, 30, 99);
            lim.Children.Add(lp); sp.Children.Add(lim);
            // the game's server (global)
            sp.Children.Add(new TextBlock { Text = "Game server", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
            serverInfo = new TextBlock { Text = ServerText(false), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 6) };
            sp.Children.Add(serverInfo);
            var hide = new CheckBox { Content = "Hide the server's IP address (for streaming) - only its region is shown", IsChecked = S.HideServerIp, Style = (Style)FindResource("Switch") };
            hide.Click += (s, e) => { S.HideServerIp = hide.IsChecked == true; S.Save(); Refresh(); };
            sp.Children.Add(hide);
            updateRow = new Border { Background = B("#2A2236"), BorderBrush = Accent.Ink(), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
            var ur = new DockPanel(); updateBtn = new Button { Style = (Style)FindResource("Accent"), MinWidth = 120 }; DockPanel.SetDock(updateBtn, Dock.Right);
            updateBtn.Click += (s, e) => InstallUpdate(); ur.Children.Add(updateBtn);
            updateText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }; ur.Children.Add(updateText);
            updateRow.Child = ur; sp.Children.Add(updateRow); ShowUpdate();
            var tools = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            Action<string, Action> tool = (label, act) => { var b = new Button { Content = label, Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 6) }; b.Click += (s, e) => act(); tools.Children.Add(b); };
            tool("Match history", ShowHistory);
            tool("Benchmarks", () => OpenFolder(BenchReport.Folder)); tool("Last benchmark", () => { if (lastBench != null && System.IO.File.Exists(lastBench)) OpenFile(lastBench); else OpenFolder(BenchReport.Folder); });
            tool("Logs", () => OpenFolder(StatLog.Folder));
            sp.Children.Add(tools);
            benchHelp = new TextBlock { Text = BenchHelp(), Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            sp.Children.Add(benchHelp);
            BuildHotkeys(sp);
            var more = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            Action<string, bool, Action<bool>> sw2 = (text, on, set) =>
            {
                var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on, Style = (Style)FindResource("Switch") };
                c.Click += (s, e) => { set(c.IsChecked == true); S.Save(); Refresh(); }; more.Children.Add(c);
            };
            sw2("Only while a game is running (Steam, Battle.net, Epic, EA, Ubisoft, GOG, Riot, Xbox)", S.OnlyInGame, v => S.OnlyInGame = v);
            sw2("Keep running in the tray when I close this window", S.TrayOnClose, v => S.TrayOnClose = v);
            sw2("Log every stat to a CSV file once a second while you play (Documents\\Hallow Overlay\\Logs)", S.LogCsv, v => S.LogCsv = v);
            sw2("Look for updates once a day (it always asks before installing)", S.AutoUpdate, v => S.AutoUpdate = v);
            sw2("Use the Hallow accent: the colour picked in Hallow Toolkit (warnings stay red and amber)", S.UseHallowAccent, v => { S.UseHallowAccent = v; Dispatcher.BeginInvoke(new Action(ApplyAccent)); });
            more.Children.Add(LookRow());
            sw2("Use the Hallow icon: when Hallow Hub is running, its one tray icon has Overlay's menu (Overlay's own icon hides)", S.UseHallowIcon, v => { S.UseHallowIcon = v; hub.CheckNow(); });
            sw2("Start with Windows (in the tray)", StartsWithWindows(), v => SetStartWithWindows(v));
            sp.Children.Add(more);
            foot = new TextBlock { Text = FootText(), Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
            sp.Children.Add(foot);
            var vr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            vr.Children.Add(new TextBlock { Text = "Hallow Overlay " + Program.Version + "  -  icons: Font Awesome Free (CC BY 4.0)", Foreground = (Brush)FindResource("Muted"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            var chk = new Button { Content = "Check for updates", Style = (Style)FindResource("Btn"), Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3), FontSize = 11 };
            chk.Click += (s, e) => CheckForUpdate(true); vr.Children.Add(chk); sp.Children.Add(vr);
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ---- per-game looks
        string editGame; bool filling; LayoutWindow layoutWin; ComboBox forBox; CheckBox ownBox; StackPanel lookHost;
        OverlaySettings EditLook { get { var k = S.PerGameKey(editGame); return k != null ? S.PerGame[k] : S; } }

        void FillGames()
        {
            filling = true;
            var names = games.Select(g => g.Name).Concat(S.PerGame.Keys).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            var cur = Current; if (cur != null) { var n = (cur.Game.Parent ?? cur.Game).Name; names.RemoveAll(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)); names.Insert(0, n); }
            var sel = editGame;
            forBox.Items.Clear(); forBox.Items.Add("All games");
            foreach (var n in names) forBox.Items.Add(n);
            forBox.SelectedIndex = sel == null ? 0 : Math.Max(0, names.FindIndex(x => string.Equals(x, sel, StringComparison.OrdinalIgnoreCase)) + 1);
            filling = false;
        }

        // Corner, layout, size, background and which stats - for every game, or for the game picked above.
        void BuildLook()
        {
            lookHost.Children.Clear();
            var T = EditLook; bool own = editGame != null && S.PerGameKey(editGame) != null;
            ownBox.Visibility = editGame == null ? Visibility.Collapsed : Visibility.Visible; ownBox.IsChecked = own;
            lookHost.IsEnabled = editGame == null || own; lookHost.Opacity = lookHost.IsEnabled ? 1 : 0.45;
            Func<string, IEnumerable<Tuple<string, string>>, Func<string>, Action<string>, FrameworkElement> chips = (label, opts, get, set) =>
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
                DockPanel.SetDock(row.Children[0], Dock.Left);
                var wp = new WrapPanel();
                foreach (var o in opts)
                {
                    var key = o.Item1; var c = new ToggleButton { Content = o.Item2, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = get() == key, Padding = new Thickness(12, 5, 12, 5) };
                    c.Click += (s, e) => { set(key); S.Save(); foreach (ToggleButton x in wp.Children) x.IsChecked = x == c; Refresh(); };
                    wp.Children.Add(c);
                }
                row.Children.Add(wp); return row;
            };
            BuildPosition(T, own);
            lookHost.Children.Add(chips("Layout", new[] { Tuple.Create("row", "One line"), Tuple.Create("col", "Column") }, () => T.Row ? "row" : "col", v => T.Row = v == "row"));
            lookHost.Children.Add(chips("Size", new[] { Tuple.Create("0.85", "Small"), Tuple.Create("1", "Medium"), Tuple.Create("1.25", "Large") },
                () => T.Scale < 0.95 ? "0.85" : T.Scale > 1.1 ? "1.25" : "1", v => T.Scale = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));
            var op = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            op.Children.Add(new TextBlock { Text = "Background", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }); DockPanel.SetDock(op.Children[0], Dock.Left);
            var sl = new Slider { Style = (Style)FindResource("HFader"), Minimum = 0.2, Maximum = 1, Value = T.Opacity, Foreground = Accent.Brush(), VerticalAlignment = VerticalAlignment.Center, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            sl.ValueChanged += (s, e) => { T.Opacity = Math.Round(sl.Value, 2); S.Save(); Refresh(); };
            op.Children.Add(sl); lookHost.Children.Add(op);
            lookHost.Children.Add(new TextBlock { Text = "Show", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) });
            var grid = new UniformGrid { Columns = 3 };
            Action<string, bool, Action<bool>> sw = (text, on, set) =>
            {
                var c = new CheckBox { Content = text, IsChecked = on, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 4, 10, 4) };
                c.Click += (s, e) => { set(c.IsChecked == true); S.Save(); Refresh(); }; grid.Children.Add(c);
            };
            sw("MIC MUTED warning", T.MicWarn, v => T.MicWarn = v); sw("CPU", T.Cpu, v => T.Cpu = v); sw("GPU load", T.Gpu, v => T.Gpu = v);
            sw("GPU temperature", T.GpuTemp, v => T.GpuTemp = v); sw("RAM", T.Ram, v => T.Ram = v); sw("Ping", T.Ping, v => T.Ping = v);
            sw("Session time", T.Session, v => T.Session = v); sw("Clock", T.Clock, v => T.Clock = v); sw("Game name", T.Game, v => T.Game = v);
            sw("FPS and 1% low", T.Fps, v => T.Fps = v); sw("0.1% low", T.Fps01, v => T.Fps01 = v); sw("Packet loss and jitter", T.LossJitter, v => T.LossJitter = v); sw("Now playing", T.NowPlaying, v => T.NowPlaying = v); sw("Game server", T.Server, v => T.Server = v); sw("Game traffic (up / down)", T.Traffic, v => T.Traffic = v);
            sw("GPU clocks", T.GpuClocks, v => T.GpuClocks = v); sw("GPU power", T.GpuPower, v => T.GpuPower = v); sw("VRAM", T.Vram, v => T.Vram = v);
            sw("Frame-time graph", T.FrameGraph, v => T.FrameGraph = v);
            lookHost.Children.Add(grid);
            var lay = new Button { Content = "Layout editor - order, labels, colours, graphs, text size", Style = (Style)FindResource("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            lay.Click += (s, e) =>
            {
                if (layoutWin != null && layoutWin.IsLoaded) layoutWin.Close();
                layoutWin = new LayoutWindow(T, editGame == null ? "all games" : editGame, () => { S.Save(); Refresh(); }) { Owner = this }; layoutWin.Show();
            };
            lookHost.Children.Add(lay);
            var gs = chips("Graph size", new[] { Tuple.Create("s", "Small"), Tuple.Create("m", "Medium"), Tuple.Create("l", "Large") }, () => T.GraphSize ?? "m", v => T.GraphSize = v);
            gs.Margin = new Thickness(0, 8, 0, 0); lookHost.Children.Add(gs);
            var bad = new CheckBox { Content = new TextBlock { Text = "Only when something's wrong: a stat shows up only while it's in trouble (hot GPU, high ping or packet loss, RAM almost full, low FPS, busy CPU)", TextWrapping = TextWrapping.Wrap },
                IsChecked = T.OnlyBad, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 8, 0, 0) };
            bad.Click += (s, e) => { T.OnlyBad = bad.IsChecked == true; S.Save(); Refresh(); };
            lookHost.Children.Add(bad);
        }

        static List<ScreenInfo> SnapScreens()
        {
            var l = new List<ScreenInfo> { new ScreenInfo { Device = @"\\.\DISPLAY1", Id = "MON-A", Model = "DELL U2720Q", Bounds = new Rect(0, 0, 3840, 2160), Scale = 1.5, Primary = true },
                new ScreenInfo { Device = @"\\.\DISPLAY2", Id = "MON-B", Model = "Generic PnP Monitor", Bounds = new Rect(-1920, 540, 1920, 1080), Scale = 1 } };
            Monitors.Name(l); return l;
        }
        // ---- position (1.15): 9 spots, the screen, the edge offset, Move it, Reset - a game's own look can have its own spot
        TextBlock posText; Func<List<ScreenInfo>> screens = Monitors.All;
        OverlaySettings PosLook(OverlaySettings T) { return editGame != null && T != S && T.OwnSpot ? T : S; }   // where the position controls write
        void UpdatePosText()
        {
            if (posText == null) return;
            var P = PosLook(EditLook); bool found; var s = Placement.Pick(screens(), P.ScreenId, P.Screen, out found);
            var note = savedNote != null && DateTime.Now < savedNoteUntil ? savedNote + ".  " : "";
            posText.Text = (ov.Moving ? "Moving - drag the panel, then Enter / Esc or Done.  " : "") + note + Placement.Describe(P, s, found) + ".";
        }
        void AlignPreview(OverlaySettings p)
        {
            var a = Placement.Valid(p.Anchor);
            preview.HorizontalAlignment = a[1] == 'r' ? HorizontalAlignment.Right : a[1] == 'c' ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            preview.VerticalAlignment = a[0] == 'b' ? VerticalAlignment.Bottom : a[0] == 'm' ? VerticalAlignment.Center : VerticalAlignment.Top;
        }
        void BuildPosition(OverlaySettings T, bool own)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var lbl = new TextBlock { Text = "Position", Width = 120, VerticalAlignment = VerticalAlignment.Top, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(lbl, Dock.Left); row.Children.Add(lbl);
            var body = new StackPanel(); row.Children.Add(body);
            bool gameSpot = editGame != null && own;
            if (gameSpot)
            {
                var ownSpot = new CheckBox { Content = "Its own spot in this game (off: use my usual spot)", IsChecked = T.OwnSpot, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 0, 0, 6) };
                ownSpot.Click += (s, e) =>
                {
                    if (ov.Moving) ov.EndMove("off");
                    T.OwnSpot = ownSpot.IsChecked == true; if (T.OwnSpot) Placement.CopySpot(S, T);   // starts from the usual spot
                    S.Save(); BuildLook(); Refresh();
                };
                body.Children.Add(ownSpot);
            }
            var P = PosLook(T); bool usual = gameSpot && !T.OwnSpot;
            var controls = new StackPanel { IsEnabled = !usual, Opacity = usual ? 0.5 : 1 }; body.Children.Add(controls);
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            // the 9 spots as a 3 x 3 grid
            var grid = new UniformGrid { Columns = 3, Rows = 3, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var a in Placement.Spots)
            {
                var key = a; var name = Placement.SpotName(a);
                var c = new ToggleButton { Content = name, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 4, 6, 4), FontSize = 11.5,
                    IsChecked = Placement.Valid(P.Anchor) == key && P.OffX == 0 && P.OffY == 0, ToolTip = name + " of the screen" };
                c.Click += (s, e) =>
                {
                    if (ov.Moving) ov.EndMove("off");
                    P.Anchor = key; P.Corner = Placement.CornerOf(key); P.OffX = P.OffY = 0; S.Save();
                    foreach (ToggleButton x in grid.Children) x.IsChecked = x == c;
                    UpdatePosText(); Refresh(); WriteStatus(true);
                };
                grid.Children.Add(c);
            }
            top.Children.Add(grid);
            // screen and edge offset
            var side = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
            side.Children.Add(new TextBlock { Text = "Screen", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 3) });
            var scr = new ComboBox { Style = (Style)FindResource("Combo"), Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            var list = screens(); bool found; var cur = Placement.Pick(list, P.ScreenId, P.Screen, out found);
            var items = list.OrderBy(x => x.Name).ToList();
            foreach (var s in items) scr.Items.Add(s.Label);
            if (!found) { scr.Items.Add((string.IsNullOrEmpty(P.ScreenName) ? "The saved screen" : P.ScreenName) + " - not connected (main screen for now)"); scr.SelectedIndex = scr.Items.Count - 1; }
            else scr.SelectedIndex = Math.Max(0, items.IndexOf(cur));
            scr.SelectionChanged += (s, e) =>
            {
                if (scr.SelectedIndex < 0 || scr.SelectedIndex >= items.Count) return;
                var m = items[scr.SelectedIndex];
                P.Screen = m.Primary ? "" : m.Device; P.ScreenId = m.Primary ? "" : m.Id; P.ScreenName = m.Primary ? "" : m.Name; S.Save();
                UpdatePosText(); Refresh(); WriteStatus(true);
            };
            side.Children.Add(scr);
            var mt = new TextBlock { Text = "Edge offset: " + P.Margin.ToString("0") + " px", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(0, 8, 0, 3) };
            side.Children.Add(mt);
            var ms = new Slider { Style = (Style)FindResource("HFader"), Minimum = 0, Maximum = 80, Value = P.Margin, Foreground = Accent.Brush(), Width = 260, HorizontalAlignment = HorizontalAlignment.Left, SmallChange = 1, LargeChange = 10, IsSnapToTickEnabled = true, TickFrequency = 1, ToolTip = "Space between the panel and the screen's edge" };
            ms.ValueChanged += (s, e) => { P.Margin = Math.Round(ms.Value); mt.Text = "Edge offset: " + P.Margin.ToString("0") + " px"; S.Save(); UpdatePosText(); Refresh(); };
            side.Children.Add(ms);
            top.Children.Add(side);
            controls.Children.Add(top);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var mv = new Button { Content = ov.Moving ? "Done moving" : "Move it (drag to place)" + KeyTail("move"), Style = (Style)FindResource("Btn"), Padding = new Thickness(12, 4, 12, 4), IsEnabled = !usual };
            bool wasMoving = ov.Moving;
            mv.Click += (s, e) => { if (wasMoving) { if (ov.Moving) ov.EndMove("done"); return; } var r = ToggleMove(true, editGame); if (r.StartsWith("error")) posText.Text = r.Substring(6); else BuildLook(); };
            btns.Children.Add(mv);
            var rs = new Button { Content = "Reset position", Style = (Style)FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4),
                ToolTip = gameSpot ? "Back to your usual spot" : "Top left, 14 px from the edge, on the main screen" };
            rs.Click += (s, e) =>
            {
                if (ov.Moving) ov.EndMove("off");
                if (gameSpot) T.OwnSpot = false; else Placement.Reset(S);
                S.Save(); BuildLook(); Refresh(); WriteStatus(true);
            };
            btns.Children.Add(rs);
            body.Children.Add(btns);
            posText = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            body.Children.Add(posText); UpdatePosText();
            lookHost.Children.Add(row);
        }

        // What the settings window says about the game's server.
        string ServerText(bool inGame)
        {
            const string relay = " Games that send their traffic through a relay network (Valve's SDR in Counter-Strike 2 or Deadlock, for example) show the relay, not the game server.";
            if (net.Denied) return "Windows doesn't let this account read network events (that needs the Performance Log Users group), so the game's server can't be found. The Ping stat goes to 1.1.1.1 (your internet) instead.";
            var srv = net.Running ? net.Finder.Server : null;
            if (srv == null) return (inGame && net.Running ? "Looking for the server the game is talking to..." : "Start a game to see the server it's talking to - found from the game's own network traffic, no online lookup.") + relay;
            return Describe(srv, ServerNames.Get(srv), serverPing.Mode, serverPing.HopsBefore);
        }
        string Describe(IPAddress srv, ServerNames.Info info, string mode, int hops)
        {
            const string relay = " Games that send their traffic through a relay network (Valve's SDR in Counter-Strike 2 or Deadlock, for example) show the relay, not the game server.";
            var where = info.City != null ? info.City + " (" + info.Code + ")" : info.Code ?? "Region unknown";
            if (!S.HideServerIp) where += "  -  " + (info.Host != null ? info.Host + "  -  " : "") + srv;
            var how = mode == "direct" ? "Ping goes straight to the server." :
                mode == "hop" ? "The server doesn't answer ping, so the nearest hop that does is pinged instead (shown with ~" + (hops > 0 ? ", " + hops + " hops short of it" : "") + "). Loss is that hop's; \"stalls\" come from the game's own traffic." :
                mode == "none" ? "Neither the server nor any hop on the way answers ping, so there's no ping number - stalls still come from the game's own traffic." : "Checking whether the server answers ping...";
            return "Server: " + where + ". " + how + relay;
        }

        // ---- your own hotkeys (1.14)
        HotkeySet keys; Dictionary<string, string> keyFails = new Dictionary<string, string>(); bool keysPaused; string capturing;
        TextBlock benchHelp, hkMsg; readonly Dictionary<string, TextBox> hkBoxes = new Dictionary<string, TextBox>(); readonly Dictionary<string, TextBlock> hkNotes = new Dictionary<string, TextBlock>();
        // the combo that works now for an action, or null (off, or another app owns it)
        string KeyText(string id) { if (keys == null) return null; var t = keys.Text(id); return t.Length == 0 || (keys.Live && !keys.Registered(id)) ? null : t; }
        string KeyTail(string id) { var t = KeyText(id); return t == null ? "" : "   (" + t + ")"; }
        string BenchHelp() { return "Benchmark: " + (KeyText("bench") != null ? KeyText("bench") + " starts it while you play and stops it" : "start and stop it from the tray menu while you play") + " - you get average FPS, 1% and 0.1% lows, frame-time percentiles and a chart (PNG + CSV in Documents\\Hallow Overlay\\Benchmarks)."; }

        void BuildHotkeys(StackPanel sp)
        {
            sp.Children.Add(new TextBlock { Text = "Hotkeys", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
            sp.Children.Add(new TextBlock { Text = "Click a box and press the keys you want (Esc cancels). They work everywhere, in games too.", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            foreach (var a in HotkeySet.Actions)
            {
                var id = a.Id;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var name = new TextBlock { Text = a.Name, Width = 260, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(name, Dock.Left); row.Children.Add(name);
                var box = new TextBox { Style = (Style)FindResource("Field"), Width = 170, IsReadOnly = true, IsReadOnlyCaretVisible = false, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Click, then press the keys" };
                DockPanel.SetDock(box, Dock.Left); row.Children.Add(box); hkBoxes[id] = box;
                var off = new Button { Content = "Off", Style = (Style)FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center, ToolTip = "No hotkey for this" };
                DockPanel.SetDock(off, Dock.Left); row.Children.Add(off);
                var note = new TextBlock { Foreground = B("#F0BE5A"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap }; row.Children.Add(note); hkNotes[id] = note;
                box.GotKeyboardFocus += (s, e) => { capturing = id; PauseKeys(); box.Text = "Press the keys..."; Say("Press the combo for \"" + a.Name + "\" - Esc cancels.", false); };
                box.LostKeyboardFocus += (s, e) => { if (capturing == id) { capturing = null; ResumeKeys(); ShowHotkeys(); } };
                box.PreviewKeyDown += (s, e) =>
                {
                    var k = e.Key == Key.System ? e.SystemKey : e.Key; var mods = Keyboard.Modifiers;
                    if (k == Key.Tab && mods == ModifierKeys.None) return;   // let Tab move on
                    e.Handled = true;
                    if (k == Key.Escape && mods == ModifierKeys.None) { EndCapture(id, null); return; }
                    var c = Combo.FromKeys(k, mods);
                    if (c == null) { box.Text = Combo.ModsText(mods) + "..."; return; }
                    EndCapture(id, c);
                };
                off.Click += (s, e) => SetHotkey(id, new Combo());
                sp.Children.Add(row);
            }
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            var reset = new Button { Content = "Reset to defaults", Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 3, 10, 3) };
            reset.Click += (s, e) =>
            {
                keyFails = keys.ResetDefaults(); S.Save();
                Say(keyFails.Count == 0 ? "Back to the defaults: " + string.Join(", ", HotkeySet.Actions.Where(x => x.Default != "").Select(x => x.Default + " " + x.Name.ToLowerInvariant())) + "." : string.Join(" ", keyFails.Values), keyFails.Count > 0);
                ShowHotkeys();
            };
            bottom.Children.Add(reset);
            hkMsg = new TextBlock { Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), MaxWidth = 520 };
            bottom.Children.Add(hkMsg);
            sp.Children.Add(bottom);
            ShowHotkeys();
        }
        void Say(string text, bool warn) { if (hkMsg == null) return; hkMsg.Text = text ?? ""; hkMsg.Foreground = warn ? B("#F0BE5A") : (Brush)FindResource("Muted"); }
        // While a capture box has focus our own hotkeys are let go, so pressing the current combo reaches the box instead of firing.
        void PauseKeys() { if (keysPaused || keys == null || !keys.Live) return; keys.UnregisterAll(); keysPaused = true; }
        void ResumeKeys() { if (!keysPaused) return; keysPaused = false; keyFails = keys.RegisterAll(); }
        void EndCapture(string id, Combo? c)
        {
            capturing = null; ResumeKeys();
            if (c != null) SetHotkey(id, c.Value); else { Say("", false); ShowHotkeys(); }
            Keyboard.ClearFocus();
        }
        void SetHotkey(string id, Combo c)
        {
            var err = keys.Change(id, c);
            var a = HotkeySet.Find(id);
            if (err == null) { keyFails.Remove(id); S.Save(); Say(c.IsOff ? "\"" + a.Name + "\" has no hotkey now." : c + " now does \"" + a.Name + "\".", false); }
            else Say(err, true);
            ShowHotkeys();
        }
        // The boxes, the notes next to them and every text that names a hotkey.
        void ShowHotkeys()
        {
            foreach (var a in HotkeySet.Actions)
            {
                TextBox box; if (hkBoxes.TryGetValue(a.Id, out box) && capturing != a.Id) { var t = keys.Text(a.Id); box.Text = t.Length == 0 ? "Off" : t; box.Foreground = t.Length == 0 ? (Brush)FindResource("Muted") : (Brush)FindResource("Text"); }
                TextBlock n; if (hkNotes.TryGetValue(a.Id, out n)) n.Text = keyFails.ContainsKey(a.Id) ? "Taken by another app - not working" : a.Default != "" && keys.Text(a.Id) != a.Default && keys.Text(a.Id) != "" ? "default " + a.Default : "";
            }
            if (hkMsg != null && hkMsg.Text.Length == 0 && keyFails.Count > 0) Say(string.Join(" ", keyFails.Values), true);
            if (benchHelp != null) benchHelp.Text = BenchHelp();
            if (foot != null) foot.Text = FootText();
            if (trayShow != null) trayShow.Text = "Show the overlay" + KeyTail("toggle");
        }
        // --snap: the hotkeys section with a combo another app owns
        public void SnapHotkeys(string dir)
        {
            S.Hotkeys["only-bad"] = "Ctrl+Alt+B";
            keyFails = new Dictionary<string, string> { { "bench", "Ctrl+Shift+F9 is already taken by another app (Windows gives each combo to one app) - pick another one or close that app." } };
            hkMsg.Text = ""; ShowHotkeys();
        }

        string FootText()
        {
            return (KeyText("toggle") != null ? KeyText("toggle") + " shows or hides it. " : "") + "Ping goes to " + S.PingHost + " (your internet, not the game server). GPU numbers need an NVIDIA card. " +
                "FPS comes from Windows' own frame events, like PresentMon (DirectX, Vulkan and OpenGL games). " +
                (fps.Denied ? "This Windows account isn't allowed to read them (that needs the Performance Log Users group), so FPS stays hidden. " : "") +
                "It's a normal see-through window - nothing touches the game, so it's safe with anti-cheat - but it can't draw over games in exclusive fullscreen: use borderless / windowed fullscreen.";
        }

        const string RunKey =@"Software\Microsoft\Windows\CurrentVersion\Run";
        static bool StartsWithWindows() { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("HallowOverlay") != null; }
        static void SetStartWithWindows(bool on)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) { if (on) k.SetValue("HallowOverlay", "\"" + Assembly.GetExecutingAssembly().Location + "\" --tray"); else k.DeleteValue("HallowOverlay", false); } } catch { }
        }

        void SetupTray()
        {
            tray = new WF.NotifyIcon { Text = "Hallow Overlay", Visible = true };
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            var menu = new WF.ContextMenuStrip();
            var open = new WF.ToolStripMenuItem("Overlay settings"); open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold); open.Click += (s, e) => ShowFromTray(); menu.Items.Add(open);
            trayShow = new WF.ToolStripMenuItem("Show the overlay" + KeyTail("toggle")) { Checked = true }; trayShow.Click += (s, e) => Dispatcher.BeginInvoke(new Action(Toggle)); menu.Items.Add(trayShow);
            trayBad = new WF.ToolStripMenuItem("Only when something's wrong"); trayBad.Click += (s, e) => Dispatcher.BeginInvoke(new Action(ToggleOnlyBad)); menu.Items.Add(trayBad);
            menu.Opening += (s, e) => { trayBad.Checked = CurrentLook.OnlyBad; };
            var bench = new WF.ToolStripMenuItem("Start benchmark" + KeyTail("bench")); bench.Click += (s, e) => Dispatcher.BeginInvoke(new Action(ToggleBench)); menu.Items.Add(bench);
            menu.Opening += (s, e) => { bench.Text = (benchOn ? "Stop" : "Start") + " benchmark" + KeyTail("bench"); trayShow.Text = "Show the overlay" + KeyTail("toggle"); trayBad.Text = "Only when something's wrong" + KeyTail("only-bad"); };
            var hist = new WF.ToolStripMenuItem("Match history"); hist.Click += (s, e) => Dispatcher.BeginInvoke(new Action(ShowHistory)); menu.Items.Add(hist);
            var move = new WF.ToolStripMenuItem("Move the overlay (drag to place)"); move.Click += (s, e) => Dispatcher.BeginInvoke(new Action(() => ToggleMove(true, null))); menu.Items.Add(move);
            menu.Opening += (s, e) => { move.Text = (ov.Moving ? "Done moving the overlay" : "Move the overlay (drag to place)") + KeyTail("move"); move.Checked = ov.Moving; };
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = new WF.ToolStripMenuItem("Quit"); quit.Click += (s, e) => Quit(); menu.Items.Add(quit);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowFromTray(); };
            hub.Enabled = () => S.UseHallowIcon;
            hub.Changed = on => Dispatcher.BeginInvoke(new Action(() => { if (tray != null && !exiting) tray.Visible = !on; WriteStatus(false); }));
            hub.Start();
        }
        public void ShowFromTray() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); Refresh(); }
        void Quit() { exiting = true; try { ov.EndMove("off"); } catch { } hub.Stop(); try { if (keys != null && keys.Live) keys.UnregisterAll(); } catch { } try { if (benchOn) StopBench(); statLog.Close(); } catch { } try { var m = rec.End(); if (m != null) MatchStore.Add(matches, m); } catch { } sampler.Stop(); try { fps.Stop(); } catch { } try { net.Stop(); serverPing.Stop(); } catch { } if (tray != null) { tray.Visible = false; tray.Dispose(); } Application.Current.Shutdown(); }

        public void SnapAll(string dir)
        {
            var x = new Sample { Cpu = 23, Gpu = 97, GpuTemp = 71, RamUsedGB = 14.2, RamTotalGB = 32, PingMs = 18, MicMuted = true, MixerOn = true, Fps = 144, FpsLow = 97, FpsLow01 = 81, NowPlaying = "Lo-fi Beats - Rainy Night", ServerLabel = "FRA", ServerMs = 23, ServerApprox = true, ServerLoss = 0, UpKbps = 96, DownKbps = 410, GpuClock = 2610, MemClock = 10501, PowerW = 241, PowerLimitW = 285, VramUsedMB = 9420, VramTotalMB = 16376, FrameMs = FakeFrames(), FrameCount = 720 };
            screens = SnapScreens; BuildLook();   // made-up screens: a 4K-ish main one at 150 % and one on its left
            preview.Child = Fit(Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42)));
            state.Text = "Waiting for a game - it appears by itself when one starts.";
            Snap.Save(this, System.IO.Path.Combine(dir, "settings.png"), 760, 0);
            var w = new Window { Background = Brushes.Transparent, Content = Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42)), FontFamily = new FontFamily("Segoe UI") };
            Snap.Save(w, System.IO.Path.Combine(dir, "panel.png"), 760, 0);
            state.Text = ExclusiveNote("Delta Force"); state.Foreground = B("#F0BE5A");
            Snap.Save(this, System.IO.Path.Combine(dir, "settings-exclusive.png"), 760, 0);
            pending = new GhRelease { Tag = "v1.12", Notes = "Benchmark mode" }; ShowUpdate(); Snap.Save(this, System.IO.Path.Combine(dir, "settings-update.png"), 760, 0); pending = null; ShowUpdate();
            Action<string, FrameworkElement, double> one = (name, el, width) => Snap.Save(new Window { Background = Brushes.Transparent, Content = el, FontFamily = new FontFamily("Segoe UI") }, System.IO.Path.Combine(dir, name), width, 0);
            one("flash.png", Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42), "Clip saved", false, false), 760);
            serverInfo.Text = Describe(IPAddress.Parse("203.0.113.7"), new ServerNames.Info { Code = "FRA", City = "Frankfurt", Host = "fra1-gs7.example.net" }, "hop", 2);
            Snap.Save(this, System.IO.Path.Combine(dir, "settings-server.png"), 760, 0);
            one("remind-only.png", Panel.Build(S, x, null, null, "Time for a break - you've played 2 hours", true, true), 760);
            // 1.3: loss and jitter, only-when-wrong, a game's own look in the settings
            var y = x.Copy(); y.LossPct = 3.3; y.JitterMs = 12; y.PingMs = 96; y.GpuTemp = 84; y.MicMuted = false;
            one("loss-jitter.png", Panel.Build(S, y, "Delta Force", TimeSpan.FromMinutes(42)), 900);
            // 1.9: GPU details and the frame-time graph
            var gl = S.LookCopy(); gl.GpuClocks = gl.GpuPower = gl.Vram = gl.FrameGraph = true; gl.MicWarn = false; gl.Ram = gl.Ping = gl.Clock = gl.Session = gl.Server = gl.NowPlaying = false;
            var gx = x.Copy(); gx.MicMuted = false; gx.PowerW = 283;
            foreach (var sz in new[] { "s", "m", "l" }) { gl.GraphSize = sz; one("gpu-graph-" + sz + ".png", Panel.Build(gl, gx, "Delta Force", null), 1000); }
            gl.Row = false; gl.GraphSize = "m"; one("gpu-graph-column.png", Panel.Build(gl, gx, "Delta Force", null), 260);
            gl.Row = true; gl.OnlyBad = true; one("gpu-graph-onlybad.png", Panel.Build(gl, gx, "Delta Force", null), 1000);
            var b = S.LookCopy(); b.OnlyBad = true;
            var tl = S.LookCopy(); tl.Traffic = true; var ty = y.Copy(); ty.Stalls = 2; one("traffic.png", Panel.Build(tl, ty, "Delta Force", null), 1100);
            one("only-bad.png", Panel.Build(b, y, "Delta Force", TimeSpan.FromMinutes(42)), 900);
            S.PerGame["Delta Force"] = S.LookCopy(); S.PerGame["Delta Force"].Corner = "br"; S.PerGame["Delta Force"].Anchor = "br"; S.PerGame["Delta Force"].OwnSpot = true; S.PerGame["Delta Force"].Row = false; S.PerGame["Delta Force"].Clock = false;
            games = new List<Game> { new Game { Id = "1", Name = "Delta Force" }, new Game { Id = "2", Name = "Deadlock" } };
            FillGames(); forBox.SelectedIndex = 1;   // -> BuildLook for Delta Force
            var pl = S.LookFor("Delta Force");
            preview.HorizontalAlignment = HorizontalAlignment.Right; preview.VerticalAlignment = VerticalAlignment.Bottom;
            preview.Child = Fit(Panel.Build(pl, x, "Delta Force", TimeSpan.FromMinutes(42)));
            state.Text = "Showing over Delta Force."; state.Foreground = (Brush)FindResource("Muted");
            Snap.Save(this, System.IO.Path.Combine(dir, "settings-pergame.png"), 760, 0);
            forBox.SelectedIndex = 2;   // Deadlock: uses the normal look (greyed out)
            Snap.Save(this, System.IO.Path.Combine(dir, "settings-pergame-off.png"), 760, 0);
            // 1.7: match summary flash and the Match history window
            var t0 = new DateTime(2026, 11, 18, 20, 5, 0);
            var fake = new List<GameMatch> {
                new GameMatch { Game = "Deadlock", Start = t0, End = t0.AddMinutes(42), Minutes = 42, FpsAvg = 141, FpsLow = 88, PingAvg = 23, PingWorst = 61, LossPct = 0.2, Stalls = 0, GpuTempAvg = 71, GpuTempMax = 79, CpuAvg = 38, GpuAvg = 92, RamPeakGB = 17.6,
                    Servers = new List<ServerUse> { new ServerUse { Region = "FRA", Ip = "203.0.113.7", Seconds = 2400, PingAvg = 23, Stalls = 0 }, new ServerUse { Region = "AMS", Ip = "203.0.113.9", Seconds = 120, PingAvg = 29, Stalls = 0 } } },
                new GameMatch { Game = "Delta Force", Start = t0.AddDays(-1), End = t0.AddDays(-1).AddMinutes(95), Minutes = 95, FpsAvg = 118, FpsLow = 71, PingAvg = 34, PingWorst = 140, LossPct = 1.4, Stalls = 3, GpuTempAvg = 76, GpuTempMax = 84, CpuAvg = 51, GpuAvg = 97, RamPeakGB = 21.2,
                    Servers = new List<ServerUse> { new ServerUse { Region = null, Ip = "198.51.100.20", Seconds = 5700, PingAvg = 34, Stalls = 3 } } },
                new GameMatch { Game = "Deadlock", Start = t0.AddDays(-2), End = t0.AddDays(-2).AddMinutes(31), Minutes = 31, FpsAvg = 152, FpsLow = 95, PingAvg = 27, PingWorst = 48, LossPct = 0, Stalls = 1, GpuTempAvg = 69, GpuTempMax = 75, CpuAvg = 35, GpuAvg = 90, RamPeakGB = 16.9,
                    Servers = new List<ServerUse> { new ServerUse { Region = "AMS", Ip = "203.0.113.9", Seconds = 1860, PingAvg = 27, Stalls = 1 } } },
            };
            one("match-flash.png", Panel.Build(S, x, null, null, fake[0].Summary(), false, true), 760);
            var hw = new MatchHistoryWindow(fake, true);
            Snap.Save(hw, System.IO.Path.Combine(dir, "history.png"), 900, 620);
            hw.Pick(1, false); Snap.Save(hw, System.IO.Path.Combine(dir, "history-2.png"), 900, 620);
            hw.Pick(0, true); Snap.Save(hw, System.IO.Path.Combine(dir, "history-servers.png"), 900, 620);
            Snap.Save(new MatchHistoryWindow(new List<GameMatch>(), true), System.IO.Path.Combine(dir, "history-empty.png"), 900, 300);
            { var ff = FakeFrames().ToList(); for (int i = 0; i < 6; i++) ff.AddRange(FakeFrames()); var br = BenchReport.Make(ff, "Delta Force", new DateTime(2026, 11, 19, 21, 4, 0), "DirectX"); br.SavePng(dir); br.SaveCsv(dir);
              var bx = x.Copy(); bx.BenchSeconds = 42; bx.MicMuted = false; one("bench-panel.png", Panel.Build(S, bx, "Delta Force", null), 1000); one("bench-flash.png", Panel.Build(S, x, null, null, br.Summary(), false, true), 760); }
            {   // 1.13: layout editor and a styled panel
                var rnd = new Random(3); for (int i = 0; i < 60; i++) { StatHistory.Add("fps", 140 + rnd.Next(-12, 12) - (i == 41 ? 50 : 0)); StatHistory.Add("cpu", 30 + rnd.Next(0, 25)); StatHistory.Add("gputemp", 66 + i / 10.0); StatHistory.Add("ping", 18 + rnd.Next(0, 5) + (i == 50 ? 30 : 0)); }
                var ly = S.LookCopy(); ly.TextSize = "l"; ly.MicWarn = false;
                ly.Styles = new List<StatStyle> { new StatStyle { Id = "fps", Label = "FPS", Color = "#4ADE80", Graph = true }, new StatStyle { Id = "ping", Graph = true, Color = "#7DD3FC" }, new StatStyle { Id = "cpu", Label = "Processor", Graph = true }, new StatStyle { Id = "gputemp", Color = "#FB923C" } };
                var lx = x.Copy(); lx.MicMuted = false;
                one("layout-panel.png", Panel.Build(ly, lx, "Delta Force", TimeSpan.FromMinutes(42)), 1300);
                ly.Row = false; one("layout-column.png", Panel.Build(ly, lx, "Delta Force", TimeSpan.FromMinutes(42)), 340);
                ly.Row = true; Snap.Save(new LayoutWindow(ly, "Delta Force", () => { }), System.IO.Path.Combine(dir, "layout-editor.png"), 720, 0);
            }
            SnapHotkeys(dir); Snap.Save(this, System.IO.Path.Combine(dir, "settings-hotkeys.png"), 760, 0);
            {   // 1.15: the position section, a screen that isn't connected, and move mode
                forBox.SelectedIndex = 0; BuildLook(); AlignPreview(S); preview.Child = Fit(Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42))); Snap.Save(this, System.IO.Path.Combine(dir, "settings-position.png"), 760, 0);
                S.Screen = @"\\.\DISPLAY3"; S.ScreenId = "MON-C"; S.ScreenName = "Screen 3"; S.Anchor = "bc"; S.OffY = -0.02; S.Margin = 24; BuildLook(); AlignPreview(S); state.Text = "Waiting for a game - it appears by itself when one starts.";
                Snap.Save(this, System.IO.Path.Combine(dir, "settings-position-missing.png"), 760, 0);
                Placement.Reset(S); BuildLook();
                forBox.SelectedIndex = Math.Max(0, forBox.Items.IndexOf("Delta Force")); AlignPreview(S.LookFor("Delta Force")); preview.Child = Fit(Panel.Build(S.LookFor("Delta Force"), x, "Delta Force", TimeSpan.FromMinutes(42)));
                Snap.Save(this, System.IO.Path.Combine(dir, "settings-pergame-spot.png"), 760, 0); forBox.SelectedIndex = 0;
                var stg = new Grid { Width = 960, Height = 540, ClipToBounds = true };
                var sb = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                sb.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#3A5A40"), 0)); sb.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#24303F"), 0.6)); sb.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#5A3A2A"), 1));
                stg.Background = sb; var cv = new Canvas(); stg.Children.Add(cv);
                var ml = S.LookCopy(); ml.Row = false; var mx = x.Copy(); mx.MicMuted = false;
                var fr = OverlayWindow.Framed(Panel.Build(ml, mx, "Delta Force", TimeSpan.FromMinutes(42)), false);
                fr.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); double fw = fr.DesiredSize.Width, fh = fr.DesiredSize.Height;
                cv.Children.Add(new System.Windows.Shapes.Line { X1 = 480, X2 = 480, Y1 = 0, Y2 = 540, Stroke = Accent.Brush(), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 6, 4 }, Opacity = 0.6 });
                Canvas.SetLeft(fr, Math.Round(480 - fw / 2)); Canvas.SetTop(fr, 14); cv.Children.Add(fr);
                TextBlock gt; Button db; var card = MoveHint.Card(() => { }, out gt, out db); gt.Text = "Snapped: top / centre";
                card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); Canvas.SetLeft(card, Math.Round(480 - card.DesiredSize.Width / 2)); Canvas.SetTop(card, 14 + fh + 8); cv.Children.Add(card);
                one("move-mode.png", stg, 960);
            }
            Snap.Save(new CrashNotice(System.IO.Path.Combine(Crash.Dir, "2026-11-19 20-14-03.txt")), System.IO.Path.Combine(dir, "crash-notice.png"), 470, 0);
            S.Row = false; x.MicMuted = false;
            var w2 = new Window { Background = Brushes.Transparent, Content = Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42)), FontFamily = new FontFamily("Segoe UI") };
            Snap.Save(w2, System.IO.Path.Combine(dir, "column.png"), 220, 0);
        }
    }
}
