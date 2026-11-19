// Hallow Overlay - match summaries: what each game session looked like (FPS, ping, loss, stalls, servers, temperatures, load),
// kept in %APPDATA%\HallowOverlay\matches.json (newest first, at most 500), plus the Match history window.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HallowOverlay
{
    [DataContract]
    public class ServerUse
    {
        [DataMember] public string Region;   // "FRA", or null when the name told us nothing
        [DataMember] public string Ip;
        [DataMember] public double Seconds, PingAvg = -1;
        [DataMember] public int Stalls;
        public string Label(bool hideIp) { return Region != null ? ServerNames.Named(Region) : hideIp || Ip == null ? "Unknown region" : Ip; }
    }

    [DataContract]
    public class GameMatch
    {
        [DataMember] public string Game;
        [DataMember] public DateTime Start, End;
        [DataMember] public double Minutes;
        [DataMember] public double FpsAvg = -1, FpsLow = -1, PingAvg = -1, PingWorst = -1, LossPct = -1;
        [DataMember] public int Stalls;
        [DataMember] public List<ServerUse> Servers = new List<ServerUse>();
        [DataMember] public double GpuTempAvg = -1, GpuTempMax = -1, CpuAvg = -1, GpuAvg = -1, RamPeakGB = -1;
        [DataMember] public double PowerAvgW = -1, VramPeakGB = -1;
        [OnDeserializing] void D(StreamingContext c) { PowerAvgW = VramPeakGB = -1; }   // matches saved by 1.7 / 1.8

        public static string Length(double minutes) { return minutes >= 60 ? ((int)(minutes / 60)) + "h " + ((int)minutes % 60).ToString("00") + "m" : ((int)Math.Round(minutes)) + "m"; }
        // "Deadlock 42m - 141 fps avg, 23 ms, 0 stalls"
        public string Summary()
        {
            var parts = new List<string>();
            if (FpsAvg >= 0) parts.Add(FpsAvg.ToString("0") + " fps avg");
            if (PingAvg >= 0) parts.Add(PingAvg.ToString("0") + " ms");
            if (Servers.Count > 0 || Stalls > 0) parts.Add(Stalls + (Stalls == 1 ? " stall" : " stalls"));
            return Game + " " + Length(Minutes) + (parts.Count > 0 ? " - " + string.Join(", ", parts) : "");
        }
        public Dictionary<string, object> Short()
        {
            return new Dictionary<string, object> { { "game", Game }, { "end", End.ToString("s") }, { "minutes", Math.Round(Minutes) },
                { "fps", FpsAvg >= 0 ? (object)Math.Round(FpsAvg) : null }, { "ping", PingAvg >= 0 ? (object)Math.Round(PingAvg) : null }, { "stalls", Stalls }, { "summary", Summary() } };
        }
    }

    // Collects one session, a sample a second.
    public class MatchRecorder
    {
        public const double MinMinutes = 2;
        string game; DateTime start, last; bool on;
        double fpsSum, lowSum, pingSum, lossSum, tempSum, cpuSum, gpuSum, powSum, vramPeak; int fpsN, lowN, pingN, lossN, tempN, cpuN, gpuN, powN;
        double pingWorst, tempMax, ramPeak; int stalls, lastTotal = -1;
        List<ServerUse> servers;
        public bool Recording { get { return on; } }
        public string Game { get { return game; } }

        public void Begin(string gameName, DateTime since)
        {
            game = gameName; start = since; last = since; on = true;
            fpsSum = lowSum = pingSum = lossSum = tempSum = cpuSum = gpuSum = powSum = 0; fpsN = lowN = pingN = lossN = tempN = cpuN = gpuN = powN = 0; vramPeak = -1;
            pingWorst = tempMax = ramPeak = -1; stalls = 0; lastTotal = -1; servers = new List<ServerUse>();
        }

        // serverRegion / serverIp: null when no server is known; totalStalls: the server finder's running count.
        public void Add(Sample x, string serverRegion, string serverIp, int totalStalls, DateTime now)
        {
            if (!on) return;
            last = now;
            if (x.Fps >= 0) { fpsSum += x.Fps; fpsN++; }
            if (x.FpsLow >= 0) { lowSum += x.FpsLow; lowN++; }
            double ping = serverIp != null && x.ServerMs >= 0 ? x.ServerMs : x.PingMs;
            if (ping >= 0) { pingSum += ping; pingN++; pingWorst = Math.Max(pingWorst, ping); }
            double loss = serverIp != null && x.ServerLoss >= 0 ? x.ServerLoss : x.LossPct;
            if (loss >= 0) { lossSum += loss; lossN++; }
            if (x.GpuTemp >= 0) { tempSum += x.GpuTemp; tempN++; tempMax = Math.Max(tempMax, x.GpuTemp); }
            if (x.Cpu >= 0) { cpuSum += x.Cpu; cpuN++; }
            if (x.Gpu >= 0) { gpuSum += x.Gpu; gpuN++; }
            if (x.RamUsedGB >= 0) ramPeak = Math.Max(ramPeak, x.RamUsedGB);
            if (x.PowerW >= 0) { powSum += x.PowerW; powN++; }
            if (x.VramUsedMB >= 0) vramPeak = Math.Max(vramPeak, x.VramUsedMB / 1024);
            int add = lastTotal >= 0 && totalStalls >= lastTotal ? totalStalls - lastTotal : 0;
            if (totalStalls >= 0) lastTotal = totalStalls;
            stalls += add;
            if (serverIp != null)
            {
                var u = servers.FirstOrDefault(s => s.Ip == serverIp);
                if (u == null) { u = new ServerUse { Ip = serverIp, Region = serverRegion }; servers.Add(u); }
                if (u.Region == null) u.Region = serverRegion;   // the name can arrive a moment later
                if (x.ServerMs >= 0) u.PingAvg = u.PingAvg < 0 ? x.ServerMs : (u.PingAvg * u.Seconds + x.ServerMs) / (u.Seconds + 1);
                u.Seconds += 1; u.Stalls += add;
            }
        }

        // The finished session, or null when it was shorter than 2 minutes (or nothing was recording).
        public GameMatch End()
        {
            if (!on) return null; on = false;
            var mins = (last - start).TotalMinutes;
            if (mins < MinMinutes) return null;
            Func<double, int, double> avg = (s, n) => n > 0 ? s / n : -1;
            return new GameMatch { Game = game, Start = start, End = last, Minutes = mins, FpsAvg = avg(fpsSum, fpsN), FpsLow = avg(lowSum, lowN),
                PingAvg = avg(pingSum, pingN), PingWorst = pingWorst, LossPct = avg(lossSum, lossN), Stalls = stalls,
                Servers = servers.OrderByDescending(s => s.Seconds).ToList(), GpuTempAvg = avg(tempSum, tempN), GpuTempMax = tempMax,
                CpuAvg = avg(cpuSum, cpuN), GpuAvg = avg(gpuSum, gpuN), RamPeakGB = ramPeak, PowerAvgW = avg(powSum, powN), VramPeakGB = vramPeak };
        }
    }

    public static class MatchStore
    {
        public const int Max = 500;
        public static string FilePath = Path.Combine(OverlaySettings.Dir, "matches.json");
        public static List<GameMatch> Load()
        {
            try { if (File.Exists(FilePath)) using (var f = File.OpenRead(FilePath)) { var l = (List<GameMatch>)new DataContractJsonSerializer(typeof(List<GameMatch>)).ReadObject(f); if (l != null) return l; } } catch { }
            return new List<GameMatch>();
        }
        // Adds a match (newest first, at most 500) and saves the list.
        public static void Add(List<GameMatch> list, GameMatch m)
        {
            list.Insert(0, m); if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);
            if (Snap.On) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); var tmp = FilePath + ".tmp";
                using (var f = File.Create(tmp)) new DataContractJsonSerializer(typeof(List<GameMatch>)).WriteObject(f, list);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
            }
            catch { }
        }
    }

    // Match history: the list (filter by game) with the details of the one picked, or the "Servers" view.
    public class MatchHistoryWindow : Window
    {
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        readonly List<GameMatch> all; readonly bool hideIp;
        string filter; bool serversView; GameMatch picked;
        ComboBox games; Border body; ToggleButton cm, cs;

        public MatchHistoryWindow(List<GameMatch> matches, bool hideServerIp)
        {
            all = matches; hideIp = hideServerIp; BuildUi();
        }
        public void Rebuild() { BuildUi(); }   // after the accent colour changed
        void BuildUi()
        {
            Title = "Match history - Hallow Overlay"; Width = 900; Height = 620; MinWidth = 720; MinHeight = 420;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var dock = new DockPanel { Margin = new Thickness(22, 16, 22, 18) };
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(top, Dock.Top);
            top.Children.Add(new TextBlock { Text = "Match history", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Accent.Brush(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) });
            DockPanel.SetDock(top.Children[0], Dock.Left);
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            cm = new ToggleButton { Content = "Matches", Style = (Style)FindResource("Chip"), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0), IsChecked = true };
            cs = new ToggleButton { Content = "Servers", Style = (Style)FindResource("Chip"), Padding = new Thickness(12, 5, 12, 5), IsChecked = false };
            cm.Click += (s, e) => { serversView = false; cm.IsChecked = true; cs.IsChecked = false; Show2(); };
            cs.Click += (s, e) => { serversView = true; cs.IsChecked = true; cm.IsChecked = false; Show2(); };
            chips.Children.Add(cm); chips.Children.Add(cs); top.Children.Add(chips);
            games = new ComboBox { Style = (Style)FindResource("Combo"), Width = 220, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            games.Items.Add("All games"); foreach (var g in all.Select(m => m.Game).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.OrdinalIgnoreCase)) games.Items.Add(g);
            games.SelectedIndex = 0; games.SelectionChanged += (s, e) => { filter = games.SelectedIndex <= 0 ? null : games.SelectedItem as string; picked = null; Show2(); };
            top.Children.Add(games);
            dock.Children.Add(top);
            body = new Border(); dock.Children.Add(body);
            Content = dock;
            Show2();
        }
        // for --snap
        public void Pick(int i, bool servers) { serversView = servers; var l = Shown(); picked = i < l.Count ? l[i] : null; Show2(); }

        List<GameMatch> Shown() { return all.Where(m => filter == null || string.Equals(m.Game, filter, StringComparison.OrdinalIgnoreCase)).ToList(); }

        void Show2()
        {
            var list = Shown(); cm.IsChecked = !serversView; cs.IsChecked = serversView;
            if (list.Count == 0) { body.Child = new TextBlock { Text = all.Count == 0 ? "No matches yet - play a game for 2 minutes or more and its summary shows up here." : "No matches for this game.", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(2, 10, 0, 0), TextWrapping = TextWrapping.Wrap }; return; }
            body.Child = serversView ? ServersView(list) : MatchesView(list);
        }

        FrameworkElement MatchesView(List<GameMatch> list)
        {
            if (picked == null || !list.Contains(picked)) picked = list[0];
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var items = new StackPanel();
            foreach (var m in list)
            {
                var mm = m; bool sel = m == picked;
                var b = new Border { Background = sel ? B("#2E2440") : B("#1C1724"), BorderBrush = sel ? (Brush)Accent.Brush() : B("#2C2536"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 6), Cursor = Cursors.Hand };
                var sp = new StackPanel();
                var head = new DockPanel();
                var when = new TextBlock { Text = m.End.ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture), Foreground = (Brush)FindResource("Muted"), FontSize = 11.5 }; DockPanel.SetDock(when, Dock.Right); head.Children.Add(when);
                head.Children.Add(new TextBlock { Text = m.Game, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                sp.Children.Add(head);
                var line = GameMatch.Length(m.Minutes) + (m.FpsAvg >= 0 ? "  •  " + m.FpsAvg.ToString("0") + " fps" : "") + (m.PingAvg >= 0 ? "  •  " + m.PingAvg.ToString("0") + " ms" : "") + (m.Servers.Count > 0 || m.Stalls > 0 ? "  •  " + m.Stalls + (m.Stalls == 1 ? " stall" : " stalls") : "");
                sp.Children.Add(new TextBlock { Text = line, Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
                b.Child = sp; b.MouseLeftButtonUp += (s, e) => { picked = mm; Show2(); };
                items.Children.Add(b);
            }
            var left = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; g.Children.Add(left);
            var right = new ScrollViewer { Content = Details(picked), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 0, 0, 0) }; Grid.SetColumn(right, 1); g.Children.Add(right);
            return g;
        }

        FrameworkElement Details(GameMatch m)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = m.Game, FontSize = 18, FontWeight = FontWeights.Bold });
            sp.Children.Add(new TextBlock { Text = m.Start.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture) + " - " + m.End.ToString("HH:mm", CultureInfo.CurrentCulture) + "  (" + GameMatch.Length(m.Minutes) + ")", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 2, 0, 12) });
            Action<string, string> row = (k, v) =>
            {
                if (v == null) return;
                var d = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var kt = new TextBlock { Text = k, Width = 150, Foreground = (Brush)FindResource("Muted") }; DockPanel.SetDock(kt, Dock.Left); d.Children.Add(kt);
                d.Children.Add(new TextBlock { Text = v, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }); sp.Children.Add(d);
            };
            Func<double, string, string> n = (v, unit) => v >= 0 ? v.ToString("0") + unit : null;
            row("FPS", m.FpsAvg >= 0 ? m.FpsAvg.ToString("0") + " average" + (m.FpsLow >= 0 ? ",  1% low " + m.FpsLow.ToString("0") : "") : null);
            row("Ping", m.PingAvg >= 0 ? m.PingAvg.ToString("0") + " ms average,  worst " + m.PingWorst.ToString("0") + " ms" : null);
            row("Packet loss", m.LossPct >= 0 ? m.LossPct.ToString("0.#") + "%" : null);
            row("Stalls", m.Servers.Count > 0 || m.Stalls > 0 ? m.Stalls.ToString() : null);
            row("GPU temperature", m.GpuTempAvg >= 0 ? m.GpuTempAvg.ToString("0") + "°C average,  max " + m.GpuTempMax.ToString("0") + "°C" : null);
            row("CPU", n(m.CpuAvg, "% average")); row("GPU load", n(m.GpuAvg, "% average"));
            row("RAM", m.RamPeakGB >= 0 ? m.RamPeakGB.ToString("0.0") + " GB at most" : null);
            row("GPU power", n(m.PowerAvgW, " W average"));
            row("VRAM", m.VramPeakGB >= 0 ? m.VramPeakGB.ToString("0.0") + " GB at most" : null);
            if (m.Servers.Count > 0)
            {
                sp.Children.Add(new TextBlock { Text = "Servers", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
                foreach (var u in m.Servers)
                    row(u.Label(hideIp), GameMatch.Length(u.Seconds / 60) + (u.PingAvg >= 0 ? ",  " + u.PingAvg.ToString("0") + " ms" : "") + ",  " + u.Stalls + (u.Stalls == 1 ? " stall" : " stalls"));
            }
            return sp;
        }

        // Regions you got: how often, for how long, their average ping and stalls.
        FrameworkElement ServersView(List<GameMatch> list)
        {
            var rows = list.SelectMany(m => m.Servers.Select(u => new { M = m, U = u })).GroupBy(x => x.U.Region ?? (hideIp ? null : x.U.Ip) ?? "Unknown region")
                .Select(gr => new { Label = gr.Key, Matches = gr.Select(x => x.M).Distinct().Count(), Secs = gr.Sum(x => x.U.Seconds), Stalls = gr.Sum(x => x.U.Stalls),
                    Ping = gr.Where(x => x.U.PingAvg >= 0).Sum(x => x.U.Seconds) > 0 ? gr.Where(x => x.U.PingAvg >= 0).Sum(x => x.U.PingAvg * x.U.Seconds) / gr.Where(x => x.U.PingAvg >= 0).Sum(x => x.U.Seconds) : -1 })
                .OrderByDescending(r => r.Secs).ToList();
            var sp = new StackPanel();
            if (rows.Count == 0) { sp.Children.Add(new TextBlock { Text = "No servers recorded yet (the Game server stat finds them while you play).", Foreground = (Brush)FindResource("Muted") }); return sp; }
            var g = new Grid(); foreach (var w in new[] { 200.0, 100, 120, 120, 100 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            Action<int, int, string, bool> cell = (r, c, t, head) => { var tb = new TextBlock { Text = t, Margin = new Thickness(0, 4, 0, 4), FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal, Foreground = head ? (Brush)FindResource("Muted") : (Brush)FindResource("Text") }; Grid.SetRow(tb, r); Grid.SetColumn(tb, c); g.Children.Add(tb); };
            g.RowDefinitions.Add(new RowDefinition());
            cell(0, 0, "Region", true); cell(0, 1, "Matches", true); cell(0, 2, "Time", true); cell(0, 3, "Average ping", true); cell(0, 4, "Stalls", true);
            int i = 1;
            foreach (var r in rows)
            {
                g.RowDefinitions.Add(new RowDefinition());
                cell(i, 0, ServerNames.Named(r.Label), false); cell(i, 1, r.Matches.ToString(), false); cell(i, 2, GameMatch.Length(r.Secs / 60), false);
                cell(i, 3, r.Ping >= 0 ? r.Ping.ToString("0") + " ms" : "-", false); cell(i, 4, r.Stalls.ToString(), false); i++;
            }
            sp.Children.Add(g);
            sp.Children.Add(new TextBlock { Text = "Regions come from the servers' names. Games that use a relay network (Valve's SDR, for example) show the relay's region.", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }
}
