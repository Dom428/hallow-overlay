// Hallow Overlay - a small see-through panel over your game: CPU, GPU, temperature, RAM, ping, session time,
// and a warning when your mic is muted (Hallow Mixer). Its own window - nothing is injected into the game.
using System;
using System.Collections.Generic;
using System.Linq;
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
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace HallowOverlay
{
    public static class Program
    {
        public const string Version = "1.1";
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
            if (msgOnly) { s = new OverlaySettings { Scale = s.Scale, Row = s.Row, Opacity = s.Opacity, MicWarn = false, Cpu = false, Gpu = false, GpuTemp = false, Ram = false, Ping = false, Clock = false, Session = false, Game = false }; }
            Action<string, string, int> item = (icon, text, level) =>
            {
                var col = level == 2 ? "#FF6B6B" : level == 1 ? "#F0BE5A" : "#ECE8F4";
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = s.Row ? new Thickness(0, 0, 14 * k, 0) : new Thickness(0, 1, 0, 1) };
                var ic = Icons.Make(icon, 12 * k, B(level == 0 ? "#A98BE0" : col)); ic.Margin = new Thickness(0, 0, 6 * k, 0); ic.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(ic);
                sp.Children.Add(new TextBlock { Text = text, FontSize = 13 * k, FontWeight = FontWeights.SemiBold, Foreground = B(col), VerticalAlignment = VerticalAlignment.Center });
                items.Add(sp);
            };
            if (s.Game && game != null) item("gamepad", game, 0);
            if (s.Session && session != null) item("stopwatch", session.Value.TotalHours >= 1 ? ((int)session.Value.TotalHours + ":" + session.Value.Minutes.ToString("00")) : session.Value.Minutes + " min", 0);
            if (s.Cpu && x.Cpu >= 0) item("microchip", "CPU " + x.Cpu.ToString("0") + "%", x.Cpu >= 90 ? 1 : 0);
            if (s.Gpu && x.Gpu >= 0) item("display", "GPU " + x.Gpu.ToString("0") + "%", 0);
            if (s.GpuTemp && x.GpuTemp >= 0) item("temperature-half", x.GpuTemp.ToString("0") + "°C", x.GpuTemp >= 85 ? 2 : x.GpuTemp >= 78 ? 1 : 0);
            if (s.Ram && x.RamUsedGB >= 0) item("memory", x.RamUsedGB.ToString("0.0") + " / " + x.RamTotalGB.ToString("0") + " GB", x.RamUsedGB / x.RamTotalGB > 0.92 ? 2 : x.RamUsedGB / x.RamTotalGB > 0.85 ? 1 : 0);
            if (s.Ping) item("signal", x.PingMs >= 0 ? x.PingMs.ToString("0") + " ms" : "no net", x.PingMs < 0 ? 2 : x.PingMs > 150 ? 2 : x.PingMs > 80 ? 1 : 0);
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
        List<Game> games = new List<Game>();
        readonly OverlayWindow ov = new OverlayWindow();
        bool shown = true, exiting, toldTray; readonly bool startHidden;
        Border preview; TextBlock state; WF.NotifyIcon tray; WF.ToolStripMenuItem trayShow;
        DispatcherTimer tick, poll, msgTimer;
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
                    if (!toldTray && tray != null) { toldTray = true; tray.ShowBalloonTip(4000, "Hallow Overlay", "Still running - the overlay shows up when you play. Ctrl+Shift+O hides it. Right-click the icon to quit.", WF.ToolTipIcon.None); }
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
            poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; poll.Tick += (s, e) => { try { tracker.Poll(games.Concat(games.SelectMany(g => g.Alts)).ToList()); } catch { } CheckExclusive(); }; poll.Start();
            var rescan = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) }; rescan.Tick += (s, e) => { try { games = Library.Scan(new List<Game>()); } catch { } }; rescan.Start();
            msgTimer = new DispatcherTimer(); msgTimer.Tick += (s, e) => { msgTimer.Stop(); Refresh(); };
            Link.Serve(Dispatcher, OnCommand);
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
                case "game-on":
                case "game-off":
                    if (arg.Length == 0) return "error no game name";
                    S.OffGames.RemoveAll(n => string.Equals(n, arg, StringComparison.OrdinalIgnoreCase));
                    if (cmd == "game-off") S.OffGames.Add(arg);
                    S.Save(); Refresh(); return "ok";
                default: return "error unknown command";
            }
        }
        public void ShowMessage(string text, bool strong)
        {
            msg = text; msgStrong = strong; msgUntil = DateTime.Now.AddSeconds(strong ? 8 : 2.5);
            msgTimer.Stop(); msgTimer.Interval = msgUntil - DateTime.Now; msgTimer.Start();
            Refresh();
        }
        string StatusJson()
        {
            return Link.Json(new Dictionary<string, object> { { "app", "Overlay" }, { "version", Program.Version }, { "updated", DateTime.Now.ToString("s") },
                { "visible", ov.IsVisible }, { "offGames", S.OffGames.ToArray() } });
        }
        void WriteStatus(bool force)
        {
            var now = ov.IsVisible + "|" + string.Join("|", S.OffGames);
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
                tray.ShowBalloonTip(8000, "Hallow Overlay can't show over " + exclusiveGame, exclusiveGame + " is in exclusive fullscreen - Windows doesn't let anything draw over that. Switch it to borderless (windowed fullscreen) in the game's video settings.", WF.ToolTipIcon.None);
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
            var x = sampler.Latest; var cur = Current;
            var game = cur == null ? null : (cur.Game.Parent ?? cur.Game).Name;
            TimeSpan? session = cur == null ? (TimeSpan?)null : DateTime.Now - cur.Since;
            bool off = IsOff(game);
            bool want = shown && !off && (!S.OnlyInGame || cur != null);
            if (msg != null && DateTime.Now >= msgUntil) msg = null;
            if (want || msg != null)
            {
                ov.Content = Panel.Build(S, x, game, session, msg, msgStrong, !want);   // messages still show briefly when the panel is hidden
                if (!ov.IsVisible) ov.Show();
                ov.Place(S.Corner);
            }
            else if (ov.IsVisible) ov.Hide();
            if (IsVisible)
            {
                preview.Child = Panel.Build(S, x, game ?? "Delta Force", session ?? TimeSpan.FromMinutes(42));
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
            Action place = () =>
            {
                preview.HorizontalAlignment = S.Corner.EndsWith("r") ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                preview.VerticalAlignment = S.Corner.StartsWith("b") ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            };
            place();
            Func<string, IEnumerable<Tuple<string, string>>, Func<string>, Action<string>, FrameworkElement> chips = (label, opts, get, set) =>
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
                DockPanel.SetDock(row.Children[0], Dock.Left);
                var wp = new WrapPanel();
                foreach (var o in opts)
                {
                    var key = o.Item1; var c = new ToggleButton { Content = o.Item2, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 0), IsChecked = get() == key, Padding = new Thickness(12, 5, 12, 5) };
                    c.Click += (s, e) => { set(key); S.Save(); foreach (ToggleButton x in wp.Children) x.IsChecked = x == c; place(); Refresh(); };
                    wp.Children.Add(c);
                }
                row.Children.Add(wp); return row;
            };
            sp.Children.Add(chips("Corner", new[] { Tuple.Create("tl", "Top left"), Tuple.Create("tr", "Top right"), Tuple.Create("bl", "Bottom left"), Tuple.Create("br", "Bottom right") }, () => S.Corner, v => S.Corner = v));
            sp.Children.Add(chips("Layout", new[] { Tuple.Create("row", "One line"), Tuple.Create("col", "Column") }, () => S.Row ? "row" : "col", v => S.Row = v == "row"));
            sp.Children.Add(chips("Size", new[] { Tuple.Create("0.85", "Small"), Tuple.Create("1", "Medium"), Tuple.Create("1.25", "Large") },
                () => S.Scale < 0.95 ? "0.85" : S.Scale > 1.1 ? "1.25" : "1", v => S.Scale = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));
            var op = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            op.Children.Add(new TextBlock { Text = "Background", Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold }); DockPanel.SetDock(op.Children[0], Dock.Left);
            var sl = new Slider { Style = (Style)FindResource("HFader"), Minimum = 0.2, Maximum = 1, Value = S.Opacity, Foreground = B("#F07820"), VerticalAlignment = VerticalAlignment.Center, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            sl.ValueChanged += (s, e) => { S.Opacity = Math.Round(sl.Value, 2); S.Save(); Refresh(); };
            op.Children.Add(sl); sp.Children.Add(op);
            sp.Children.Add(new TextBlock { Text = "Show", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) });
            var grid = new UniformGrid { Columns = 3 };
            Action<string, bool, Action<bool>> sw = (text, on, set) =>
            {
                var c = new CheckBox { Content = text, IsChecked = on, Style = (Style)FindResource("Switch"), Margin = new Thickness(0, 4, 10, 4) };
                c.Click += (s, e) => { set(c.IsChecked == true); S.Save(); Refresh(); }; grid.Children.Add(c);
            };
            sw("MIC MUTED warning", S.MicWarn, v => S.MicWarn = v); sw("CPU", S.Cpu, v => S.Cpu = v); sw("GPU load", S.Gpu, v => S.Gpu = v);
            sw("GPU temperature", S.GpuTemp, v => S.GpuTemp = v); sw("RAM", S.Ram, v => S.Ram = v); sw("Ping", S.Ping, v => S.Ping = v);
            sw("Session time", S.Session, v => S.Session = v); sw("Clock", S.Clock, v => S.Clock = v); sw("Game name", S.Game, v => S.Game = v);
            sp.Children.Add(grid);
            var more = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            Action<string, bool, Action<bool>> sw2 = (text, on, set) =>
            {
                var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on, Style = (Style)FindResource("Switch") };
                c.Click += (s, e) => { set(c.IsChecked == true); S.Save(); Refresh(); }; more.Children.Add(c);
            };
            sw2("Only while a game is running (Steam, Battle.net, Epic, EA, Ubisoft, GOG, Riot, Xbox)", S.OnlyInGame, v => S.OnlyInGame = v);
            sw2("Keep running in the tray when I close this window", S.TrayOnClose, v => S.TrayOnClose = v);
            sw2("Start with Windows (in the tray)", StartsWithWindows(), v => SetStartWithWindows(v));
            sp.Children.Add(more);
            sp.Children.Add(new TextBlock { Text = "Ctrl+Shift+O shows or hides it. Ping goes to " + S.PingHost + " (your internet, not the game server). GPU numbers need an NVIDIA card. " +
                "It's a normal see-through window - nothing touches the game, so it's safe with anti-cheat - but it can't draw over games in exclusive fullscreen: use borderless / windowed fullscreen.",
                Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            sp.Children.Add(new TextBlock { Text = "Hallow Overlay " + Program.Version + "  -  icons: Font Awesome Free (CC BY 4.0)", Foreground = (Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
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
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = new WF.ToolStripMenuItem("Quit"); quit.Click += (s, e) => Quit(); menu.Items.Add(quit);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowFromTray(); };
        }
        public void ShowFromTray() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); Refresh(); }
        void Quit() { exiting = true; sampler.Stop(); if (tray != null) { tray.Visible = false; tray.Dispose(); } Application.Current.Shutdown(); }

        public void SnapAll(string dir)
        {
            var x = new Sample { Cpu = 23, Gpu = 97, GpuTemp = 71, RamUsedGB = 14.2, RamTotalGB = 32, PingMs = 18, MicMuted = true, MixerOn = true };
            preview.Child = Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42));
            state.Text = "Waiting for a game - it appears by itself when one starts.";
            Snap.Save(this, System.IO.Path.Combine(dir, "settings.png"), 760, 0);
            var w = new Window { Background = Brushes.Transparent, Content = Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42)), FontFamily = new FontFamily("Segoe UI") };
            Snap.Save(w, System.IO.Path.Combine(dir, "panel.png"), 760, 0);
            state.Text = ExclusiveNote("Delta Force"); state.Foreground = B("#F0BE5A");
            Snap.Save(this, System.IO.Path.Combine(dir, "settings-exclusive.png"), 760, 0);
            Action<string, FrameworkElement, double> one = (name, el, width) => Snap.Save(new Window { Background = Brushes.Transparent, Content = el, FontFamily = new FontFamily("Segoe UI") }, System.IO.Path.Combine(dir, name), width, 0);
            one("flash.png", Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42), "Clip saved", false, false), 760);
            one("remind-only.png", Panel.Build(S, x, null, null, "Time for a break - you've played 2 hours", true, true), 760);
            S.Row = false; x.MicMuted = false;
            var w2 = new Window { Background = Brushes.Transparent, Content = Panel.Build(S, x, "Delta Force", TimeSpan.FromMinutes(42)), FontFamily = new FontFamily("Segoe UI") };
            Snap.Save(w2, System.IO.Path.Combine(dir, "column.png"), 220, 0);
        }
    }
}
