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
[assembly: AssemblyVersion("1.7.0.0")]
[assembly: AssemblyFileVersion("1.7.0.0")]

namespace HallowOverlay
{
    public static class Program
    {
        public const string Version = "1.7";
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
                Snap.On = true; OverlaySettings.Frozen = true;
                var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(sa);
                new MainWindow(true).SnapAll(args[si + 1]); return 0;
            }
            bool created;
            var mutex = new Mutex(true, @"Local\HallowOverlay", out created);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HallowOverlayShow");
            if (!created) { showEvent.Set(); return 0; }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(app);
            var w = new MainWindow(args.Contains("--tray"));
            new Thread(() => { while (showEvent.WaitOne()) w.Dispatcher.BeginInvoke(new Action(w.ShowFromTray)); }) { IsBackground = true }.Start();
            app.Run();
            GC.KeepAlive(mutex);
            return 0;
        }
        static void LoadTheme(Application app)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowOverlay.Theme.xaml"))
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(s));
        }
    }

    // The panel itself (used by the overlay and by the preview in settings).
    public static class Panel
    {
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        public static FrameworkElement Build(OverlaySettings s, Sample x, string game, TimeSpan? session) { return Build(s, x, game, session, null, false, false); }
        // msg: a short message from another Hallow app (flash, or a stronger remind); msgOnly: show just the message
        public static FrameworkElement Build(OverlaySettings s, Sample x, string game, TimeSpan? session, string msg, bool strong, bool msgOnly)
        {
            var k = s.Scale; var items = new List<FrameworkElement>();
            if (msgOnly) { s = new OverlaySettings { Scale = s.Scale, Row = s.Row, Opacity = s.Opacity, MicWarn = false, Cpu = false, Gpu = false, GpuTemp = false, Ram = false, Ping = false, Clock = false, Session = false, Game = false, Fps = false, NowPlaying = false, Server = false, Traffic = false }; }
            bool bad = s.OnlyBad;   // only stats that are in trouble (and the MIC MUTED warning / messages)
            Action<string, string, int> item = (icon, text, level) =>
            {
                if (bad && level == 0) return;
                var col = level == 2 ? "#FF6B6B" : level == 1 ? "#F0BE5A" : "#ECE8F4";
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = s.Row ? new Thickness(0, 0, 14 * k, 0) : new Thickness(0, 1, 0, 1) };
                var ic = Icons.Make(icon, 12 * k, B(level == 0 ? "#A98BE0" : col)); ic.Margin = new Thickness(0, 0, 6 * k, 0); ic.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(ic);
                sp.Children.Add(new TextBlock { Text = text, FontSize = 13 * k, FontWeight = FontWeights.SemiBold, Foreground = B(col), VerticalAlignment = VerticalAlignment.Center });
                items.Add(sp);
            };
            if (s.Game && game != null && !bad) item("gamepad", game, 0);
            if (s.Session && session != null && !bad) item("stopwatch", session.Value.TotalHours >= 1 ? ((int)session.Value.TotalHours + ":" + session.Value.Minutes.ToString("00")) : session.Value.Minutes + " min", 0);
            if (s.Fps && x.Fps >= 0) item("gauge-high", x.Fps.ToString("0") + " FPS" + (x.FpsLow >= 0 ? "  1% " + x.FpsLow.ToString("0") : ""), x.Fps < 30 ? 2 : x.Fps < 50 ? 1 : 0);
            if (s.Cpu && x.Cpu >= 0) item("microchip", "CPU " + x.Cpu.ToString("0") + "%", x.Cpu >= 90 ? 1 : 0);
            if (s.Gpu && x.Gpu >= 0) item("display", "GPU " + x.Gpu.ToString("0") + "%", 0);
            if (s.GpuTemp && x.GpuTemp >= 0) item("temperature-half", x.GpuTemp.ToString("0") + "°C", x.GpuTemp >= s.HotGpu + 7 ? 2 : x.GpuTemp >= s.HotGpu ? 1 : 0);
            if (s.Ram && x.RamUsedGB >= 0)
            {
                var pct = x.RamUsedGB / x.RamTotalGB * 100;
                item("memory", x.RamUsedGB.ToString("0.0") + " / " + x.RamTotalGB.ToString("0") + " GB", pct > s.RamFull + 7 ? 2 : pct > s.RamFull ? 1 : 0);
            }
            if (s.Ping)
            {
                int lv = x.PingMs < 0 ? 2 : x.PingMs > s.HighPing + 70 ? 2 : x.PingMs > s.HighPing ? 1 : 0;
                var text = x.PingMs >= 0 ? x.PingMs.ToString("0") + " ms" : "no net";
                if (s.LossJitter && x.LossPct >= 0)
                {
                    if (x.JitterMs >= 0) text += "  ±" + x.JitterMs.ToString("0");
                    text += "  " + x.LossPct.ToString("0.#") + "% loss";
                    lv = Math.Max(lv, x.LossPct >= 5 ? 2 : x.LossPct >= 2 ? 1 : x.JitterMs >= 30 ? 1 : 0);
                }
                item("signal", text, lv);
            }
            if (s.Server && x.ServerLabel != null)
            {
                var text = x.ServerLabel + "  " + (x.ServerMs >= 0 ? (x.ServerApprox ? "~" : "") + x.ServerMs.ToString("0") + " ms" : "no ping");
                if (x.ServerLoss >= 0) text += "  " + x.ServerLoss.ToString("0.#") + "% loss";
                if (x.Stalls > 0) text += "  " + x.Stalls + (x.Stalls == 1 ? " stall" : " stalls");
                int lv = x.ServerMs > s.HighPing + 70 ? 2 : x.ServerMs > s.HighPing ? 1 : 0;
                lv = Math.Max(lv, x.ServerLoss >= 5 ? 2 : x.ServerLoss >= 2 ? 1 : 0); if (x.Stalls >= 3) lv = Math.Max(lv, 2); else if (x.Stalls > 0) lv = Math.Max(lv, 1);
                item("server", text, lv);
            }
            if (s.Traffic && x.UpKbps >= 0) item("arrow-right-arrow-left", "\u2191" + x.UpKbps.ToString("0") + "  \u2193" + x.DownKbps.ToString("0") + " kbps", 0);
            if (s.NowPlaying && !string.IsNullOrEmpty(x.NowPlaying)) item("music", x.NowPlaying.Length > 48 ? x.NowPlaying.Substring(0, 47) + "\u2026" : x.NowPlaying, 0);
            if (s.Clock) item("clock", DateTime.Now.ToString("HH:mm"), 0);
            var list = new StackPanel { Orientation = s.Row ? Orientation.Horizontal : Orientation.Vertical };
            foreach (var i in items) list.Children.Add(i);
            var outer = new StackPanel { Orientation = s.Row ? Orientation.Horizontal : Orientation.Vertical };
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

    // Click-through, never takes focus, never in the taskbar or Alt+Tab.
    public class OverlayWindow : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        public OverlayWindow()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; Focusable = false; FontFamily = new FontFamily("Segoe UI"); IsHitTestVisible = false;
            SourceInitialized += (s, e) => { var h = new WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x08000000 | 0x80000); };
        }
        public void Place(string corner)
        {
            UpdateLayout();
            const double m = 14; double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;   // whole screen: games cover the taskbar
            Left = corner.EndsWith("r") ? sw - ActualWidth - m : m;
            Top = corner.StartsWith("b") ? sh - ActualHeight - m : m;
        }
    }

    public class MainWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }

        readonly OverlaySettings S = OverlaySettings.Load();
        readonly Sampler sampler = new Sampler();
        readonly Tracker tracker = new Tracker();
        readonly FpsMeter fps = new FpsMeter(); DateTime fpsRetry; TextBlock foot;
        List<Game> games = new List<Game>();
        readonly OverlayWindow ov = new OverlayWindow();
        bool shown = true, exiting, toldTray; readonly bool startHidden;
        Border preview; TextBlock state; WF.NotifyIcon tray; WF.ToolStripMenuItem trayShow;
        DispatcherTimer tick, poll, msgTimer; readonly HubIcon hub = new HubIcon(); WF.ToolStripMenuItem trayBad;
        string msg; bool msgStrong; DateTime msgUntil;            // flash / remind from another Hallow app
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
            Content = Build();
            SourceInitialized += (s, e) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, 4); };
            Closing += (s, e) =>
            {
                if (!exiting && S.TrayOnClose && !Snap.On)
                {
                    e.Cancel = true; Hide();
                    if (!toldTray && tray != null) { toldTray = true; Balloon("Hallow Overlay", "Still running - the overlay shows up when you play. Ctrl+Shift+O hides it. Right-click the icon to quit.", 4000); }
                }
                else Quit();
            };
            if (Snap.On) return;
            try { games = Library.Scan(new List<Game>()); } catch { }
            sampler.PingHost = S.PingHost; sampler.Start();
            SetupTray();
            // hotkey: Ctrl+Shift+O on this window's handle (it exists even while the window is hidden)
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            if (S.Hotkey) RegisterHotKey(hwnd, 1, 0x4006, 0x4F);   // MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 'O'
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) => { if (msg == 0x0312 && wp.ToInt32() == 1) { Toggle(); handled = true; } return IntPtr.Zero; });
            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; tick.Tick += (s, e) => Refresh(); tick.Start();
            poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; poll.Tick += (s, e) => { try { tracker.Poll(games.Concat(games.SelectMany(g => g.Alts)).ToList()); } catch { } CheckExclusive(); RunFps(); RunNet(); }; poll.Start();
            var rescan = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) }; rescan.Tick += (s, e) => { try { games = Library.Scan(new List<Game>()); } catch { } }; rescan.Start();
            msgTimer = new DispatcherTimer(); msgTimer.Tick += (s, e) => { msgTimer.Stop(); Refresh(); };
            Link.Serve(Dispatcher, OnCommand);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { fps.Stop(); net.Stop(); } catch { } };   // never leave a trace session behind
            WriteStatus(true);
            IsVisibleChanged += (s, e) => { if (IsVisible) Dispatcher.BeginInvoke(new Action(ShowWhatsNew), DispatcherPriority.ApplicationIdle); };
            if (!startHidden) Show();
        }

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
                case "ping": return "ok " + Link.Json(new Dictionary<string, object> { { "app", "Overlay" }, { "version", Program.Version } });
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
            l.Add(new Dictionary<string, object> { { "id", "history" }, { "label", "Match history" } });
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
                default: return "error unknown item";
            }
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
        public void ShowMessage(string text, bool strong, double seconds)
        {
            msg = text; msgStrong = strong; msgUntil = DateTime.Now.AddSeconds(seconds);
            msgTimer.Stop(); msgTimer.Interval = msgUntil - DateTime.Now; msgTimer.Start();
            Refresh();
        }
        string StatusJson()
        {
            return Link.Json(new Dictionary<string, object> { { "app", "Overlay" }, { "version", Program.Version }, { "updated", DateTime.Now.ToString("s") },
                { "visible", ov.IsVisible }, { "offGames", S.OffGames.ToArray() }, { "hallowIcon", hub.Hosted }, { "matches", matches.Take(5).Select(m => m.Short()).ToArray() } });
        }
        void WriteStatus(bool force)
        {
            var now = ov.IsVisible + "|" + hub.Hosted + "|" + matches.Count + "|" + string.Join("|", S.OffGames);
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
            bool want = S.Fps && Current != null;
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
            double f, low; x.Fps = x.FpsLow = -1;
            if (cur != null && fps.Running && fps.Read(cur.Pids, out f, out low)) { x.Fps = f; x.FpsLow = low; }
            x.ServerLabel = null; x.UpKbps = x.DownKbps = -1; x.ServerMs = x.ServerLoss = -1; x.Stalls = 0; string srvRegion = null, srvIp = null;
            if (cur != null && net.Running)
            {
                var fd = net.Finder; fd.Tick(DateTime.UtcNow.ToFileTimeUtc());
                var srv = fd.Server; serverPing.Set(srv);
                x.UpKbps = fd.UpKbps; x.DownKbps = fd.DownKbps;
                if (srv != null)
                {
                    var info = ServerNames.Get(srv);
                    x.ServerLabel = info.Code ?? (S.HideServerIp ? "Server" : srv.ToString()); srvRegion = info.Code; srvIp = srv.ToString();
                    x.ServerMs = serverPing.Ms; x.ServerApprox = serverPing.Approx; x.ServerLoss = serverPing.LossPct; x.Stalls = fd.Stalls;
                }
                if (fd.Changes != netChanges) { netChanges = fd.Changes; if (S.LookFor(game).Server && msg == null) { ShowMessage("Server changed", false); return; } }
            }
            // match summary: one per game session
            var recNow = cur == null ? null : game + "|" + cur.Since.Ticks;
            if (recNow != recKey)
            {
                recKey = recNow; var done = rec.End();
                if (cur != null) rec.Begin(game, cur.Since);
                if (done != null) MatchDone(done);
            }
            if (cur != null) rec.Add(x, srvRegion, srvIp, net.Running ? net.Finder.TotalStalls : -1, DateTime.Now);
            bool off = IsOff(game);
            bool want = shown && !off && (!S.OnlyInGame || cur != null);
            if (msg != null && DateTime.Now >= msgUntil) msg = null;
            var look = S.LookFor(game);
            if (want || msg != null)
            {
                ov.Content = Panel.Build(look, x, game, session, msg, msgStrong, !want);   // messages still show briefly when the panel is hidden
                if (!ov.IsVisible) ov.Show();
                ov.Place(look.Corner);
            }
            else if (ov.IsVisible) ov.Hide();
            if (IsVisible)
            {
                var px = x; if (px.Fps < 0 && !fps.Denied) { px = x.Copy(); px.Fps = 144; px.FpsLow = 97; }   // what it looks like in a game
                var pl = editGame != null ? S.LookFor(editGame) : look;
                preview.HorizontalAlignment = pl.Corner.EndsWith("r") ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                preview.VerticalAlignment = pl.Corner.StartsWith("b") ? VerticalAlignment.Bottom : VerticalAlignment.Top;
                preview.Child = Fit(Panel.Build(pl, px, editGame ?? game ?? "Delta Force", session ?? TimeSpan.FromMinutes(42)));
                foot.Text = FootText(); serverInfo.Text = ServerText(cur != null);
                state.Text = exclusiveGame != null && exclusiveGame == game ? ExclusiveNote(game) :
                    !shown ? "Hidden - press Ctrl+Shift+O (or use the tray menu) to show it again." :
                    off ? "Turned off for " + game + " (in Hallow Hub's game settings)." :
                    cur != null ? "Showing over " + game + "." : S.OnlyInGame ? "Waiting for a game - it appears by itself when one starts." : "Showing on your screen now.";
                state.Foreground = exclusiveGame != null && exclusiveGame == game ? B("#F0BE5A") : (Brush)FindResource("Muted");
            }
            if (trayShow != null) trayShow.Checked = shown;
            WriteStatus(false);
        }

        void Toggle() { shown = !shown; Refresh(); }
        static UIElement Fit(FrameworkElement el) { return new Viewbox { Child = el, StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform }; }   // the preview shrinks a wide panel to fit

        // ================================================================ settings window
        UIElement Build()
        {
            var sp = new StackPanel { Margin = new Thickness(26, 16, 26, 20) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            try { title.Children.Add(new Image { Source = AppIcon(), Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) }); } catch { }
            title.Children.Add(new TextBlock { Text = "HALLOW", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = B("#F07820"), VerticalAlignment = VerticalAlignment.Center });
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
            ownBox = new CheckBox { Content = "Own corner, size and stats for this game", Style = (Style)FindResource("Switch"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
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
            var hb = new Button { Content = "Match history", Style = (Style)FindResource("Btn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
            hb.Click += (s, e) => ShowHistory(); sp.Children.Add(hb);
            var more = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            Action<string, bool, Action<bool>> sw2 = (text, on, set) =>
            {
                var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on, Style = (Style)FindResource("Switch") };
                c.Click += (s, e) => { set(c.IsChecked == true); S.Save(); Refresh(); }; more.Children.Add(c);
            };
            sw2("Only while a game is running (Steam, Battle.net, Epic, EA, Ubisoft, GOG, Riot, Xbox)", S.OnlyInGame, v => S.OnlyInGame = v);
            sw2("Keep running in the tray when I close this window", S.TrayOnClose, v => S.TrayOnClose = v);
            sw2("Use the Hallow icon: when Hallow Hub is running, its one tray icon has Overlay's menu (Overlay's own icon hides)", S.UseHallowIcon, v => { S.UseHallowIcon = v; hub.CheckNow(); });
            sw2("Start with Windows (in the tray)", StartsWithWindows(), v => SetStartWithWindows(v));
            sp.Children.Add(more);
            foot = new TextBlock { Text = FootText(), Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
            sp.Children.Add(foot);
            sp.Children.Add(new TextBlock { Text = "Hallow Overlay " + Program.Version + "  -  icons: Font Awesome Free (CC BY 4.0)", Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ---- per-game looks
        string editGame; bool filling; ComboBox forBox; CheckBox ownBox; StackPanel lookHost;
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
            lookHost.Children.Add(chips("Corner", new[] { Tuple.Create("tl", "Top left"), Tuple.Create("tr", "Top right"), Tuple.Create("bl", "Bottom left"), Tuple.Create("br", "Bottom right") }, () => T.Corner, v => T.Corner = v));
            lookHost.Children.Add(chips("Layout", new[] { Tuple.Create("row", "One line"), Tuple.Create("col", "Column") }, () => T.Row ? "row" : "col", v => T.Row = v == "row"));
            lookHost.Children.Add(chips("Size", new[] { Tuple.Create("0.85", "Small"), Tuple.Create("1", "Medium"), Tuple.Create("1.25", "Large") },
                () => T.Scale < 0.95 ? "0.85" : T.Scale > 1.1 ? "1.25" : "1", v => T.Scale = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));
            var op = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            op.Children.Add(new TextBlock { Text = "Background", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }); DockPanel.SetDock(op.Children[0], Dock.Left);
            var sl = new Slider { Style = (Style)FindResource("HFader"), Minimum = 0.2, Maximum = 1, Value = T.Opacity, Foreground = B("#F07820"), VerticalAlignment = VerticalAlignment.Center, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
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
            sw("FPS and 1% low", T.Fps, v => T.Fps = v); sw("Packet loss and jitter", T.LossJitter, v => T.LossJitter = v); sw("Now playing (Mixer)", T.NowPlaying, v => T.NowPlaying = v); sw("Game server", T.Server, v => T.Server = v); sw("Game traffic (up / down)", T.Traffic, v => T.Traffic = v);
            lookHost.Children.Add(grid);
            var bad = new CheckBox { Content = new TextBlock { Text = "Only when something's wrong: a stat shows up only while it's in trouble (hot GPU, high ping or packet loss, RAM almost full, low FPS, busy CPU)", TextWrapping = TextWrapping.Wrap },
                IsChecked = T.OnlyBad, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 8, 0, 0) };
            bad.Click += (s, e) => { T.OnlyBad = bad.IsChecked == true; S.Save(); Refresh(); };
            lookHost.Children.Add(bad);
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

        string FootText()
        {
            return "Ctrl+Shift+O shows or hides it. Ping goes to " + S.PingHost + " (your internet, not the game server). GPU numbers need an NVIDIA card. " +
                "FPS comes from Windows' own frame events, like PresentMon (DirectX games). " +
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
            trayShow = new WF.ToolStripMenuItem("Show the overlay   (Ctrl+Shift+O)") { Checked = true }; trayShow.Click += (s, e) => Dispatcher.BeginInvoke(new Action(Toggle)); menu.Items.Add(trayShow);
            trayBad = new WF.ToolStripMenuItem("Only when something's wrong"); trayBad.Click += (s, e) => Dispatcher.BeginInvoke(new Action(ToggleOnlyBad)); menu.Items.Add(trayBad);
            menu.Opening += (s, e) => { trayBad.Checked = CurrentLook.OnlyBad; };
            var hist = new WF.ToolStripMenuItem("Match history"); hist.Click += (s, e) => Dispatcher.BeginInvoke(new Action(ShowHistory)); menu.Items.Add(hist);
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = new WF.ToolStripMenuItem("Quit"); quit.Click += (s, e) => Quit(); menu.Items.Add(quit);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowFromTray(); };
            hub.Enabled = () => S.UseHallowIcon;
            hub.Changed = on => Dispatcher.BeginInvoke(new Action(() => { if (tray != null && !exiting) tray.Visible = !on; WriteStatus(false); }));
            hub.Start();
        }
        public void ShowFromTray() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); Refresh(); }
        void Quit() { exiting = true; hub.Stop(); try { var m = rec.End(); if (m != null) MatchStore.Add(matches, m); } catch { } sampler.Stop(); try { fps.Stop(); } catch { } try { net.Stop(); serverPing.Stop(); } catch { } if (tray != null) { tray.Visible = false; tray.Dispose(); } Application.Current.Shutdown(); }

        public void SnapAll(string dir)
        {
            var x = new Sample { Cpu = 23, Gpu = 97, GpuTemp = 71, RamUsedGB = 14.2, RamTotalGB = 32, PingMs = 18, MicMuted = true, MixerOn = true, Fps = 144, FpsLow = 97, NowPlaying = "Lo-fi Beats - Rainy Night", ServerLabel = "FRA", ServerMs = 23, ServerApprox = true, ServerLoss = 0, UpKbps = 96, DownKbps = 410 };
            preview.Child = Fit(Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42)));
            state.Text = "Waiting for a game - it appears by itself when one starts.";
            Snap.Save(this, System.IO.Path.Combine(dir, "settings.png"), 760, 0);
            var w = new Window { Background = Brushes.Transparent, Content = Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42)), FontFamily = new FontFamily("Segoe UI") };
            Snap.Save(w, System.IO.Path.Combine(dir, "panel.png"), 760, 0);
            state.Text = ExclusiveNote("Delta Force"); state.Foreground = B("#F0BE5A");
            Snap.Save(this, System.IO.Path.Combine(dir, "settings-exclusive.png"), 760, 0);
            Action<string, FrameworkElement, double> one = (name, el, width) => Snap.Save(new Window { Background = Brushes.Transparent, Content = el, FontFamily = new FontFamily("Segoe UI") }, System.IO.Path.Combine(dir, name), width, 0);
            one("flash.png", Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42), "Clip saved", false, false), 760);
            serverInfo.Text = Describe(IPAddress.Parse("203.0.113.7"), new ServerNames.Info { Code = "FRA", City = "Frankfurt", Host = "fra1-gs7.example.net" }, "hop", 2);
            Snap.Save(this, System.IO.Path.Combine(dir, "settings-server.png"), 760, 0);
            one("remind-only.png", Panel.Build(S, x, null, null, "Time for a break - you've played 2 hours", true, true), 760);
            // 1.3: loss and jitter, only-when-wrong, a game's own look in the settings
            var y = x.Copy(); y.LossPct = 3.3; y.JitterMs = 12; y.PingMs = 96; y.GpuTemp = 84; y.MicMuted = false;
            one("loss-jitter.png", Panel.Build(S, y, "Delta Force", TimeSpan.FromMinutes(42)), 900);
            var b = S.LookCopy(); b.OnlyBad = true;
            var tl = S.LookCopy(); tl.Traffic = true; var ty = y.Copy(); ty.Stalls = 2; one("traffic.png", Panel.Build(tl, ty, "Delta Force", null), 1100);
            one("only-bad.png", Panel.Build(b, y, "Delta Force", TimeSpan.FromMinutes(42)), 900);
            S.PerGame["Delta Force"] = S.LookCopy(); S.PerGame["Delta Force"].Corner = "br"; S.PerGame["Delta Force"].Row = false; S.PerGame["Delta Force"].Clock = false;
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
            S.Row = false; x.MicMuted = false;
            var w2 = new Window { Background = Brushes.Transparent, Content = Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42)), FontFamily = new FontFamily("Segoe UI") };
            Snap.Save(w2, System.IO.Path.Combine(dir, "column.png"), 220, 0);
        }
    }
}
