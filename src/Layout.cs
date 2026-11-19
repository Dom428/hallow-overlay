// Hallow Overlay - the layout editor: order of the stats, your own labels, a colour per stat, text or text + a small graph,
// and the text size. Stored with the look (so a game with its own look has its own layout).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HallowOverlay
{
    [DataContract]
    public class StatStyle
    {
        [DataMember] public string Id;
        [DataMember] public string Label;    // null = the normal one, "" = none
        [DataMember] public string Color;    // "#RRGGBB" or null = the normal colour (warnings stay amber / red)
        [DataMember] public bool Graph;      // a small graph of the last minute next to the number
        public StatStyle Copy() { return (StatStyle)MemberwiseClone(); }
    }

    public static class Stats
    {
        // id, name in the editor, can it have a graph
        public static readonly Tuple<string, string, bool>[] All = {
            Tuple.Create("game", "Game name", false), Tuple.Create("session", "Session time", false), Tuple.Create("fps", "FPS", true), Tuple.Create("graph", "Frame-time graph", false),
            Tuple.Create("cpu", "CPU", true), Tuple.Create("gpu", "GPU load", true), Tuple.Create("gputemp", "GPU temperature", true), Tuple.Create("clocks", "GPU clocks", true),
            Tuple.Create("power", "GPU power", true), Tuple.Create("vram", "VRAM", true), Tuple.Create("ram", "RAM", true), Tuple.Create("ping", "Ping", true),
            Tuple.Create("server", "Game server", true), Tuple.Create("traffic", "Game traffic", false), Tuple.Create("nowplaying", "Now playing", false), Tuple.Create("clock", "Clock", false) };
        public static readonly string[] Colors = { "#ECE8F4", "#A98BE0", "#7DD3FC", "#4ADE80", "#FACC15", "#FB923C", "#F472B6", "#94A3B8" };

        // the look's styles in order, with any stat it doesn't mention yet added at its normal place
        public static List<StatStyle> Ordered(OverlaySettings s)
        {
            var l = (s.Styles ?? new List<StatStyle>()).Where(x => x != null && All.Any(a => a.Item1 == x.Id)).GroupBy(x => x.Id).Select(g => g.First()).ToList();
            for (int i = 0; i < All.Length; i++)
                if (!l.Any(x => x.Id == All[i].Item1))
                {
                    int at = 0; for (int j = i - 1; j >= 0; j--) { var k = l.FindIndex(x => x.Id == All[j].Item1); if (k >= 0) { at = k + 1; break; } }
                    l.Insert(at, new StatStyle { Id = All[i].Item1 });
                }
            return l;
        }
        public static double TextFactor(string size) { return size == "s" ? 0.85 : size == "l" ? 1.2 : size == "xl" ? 1.45 : 1; }
    }

    // The last minute of each stat (once a second), for the small graphs. Fixed buffers: nothing allocated per second.
    public static class StatHistory
    {
        const int N = 60;
        static readonly Dictionary<string, double[]> data = new Dictionary<string, double[]>();
        static readonly Dictionary<string, int> fill = new Dictionary<string, int>();
        public static void Add(string id, double v)
        {
            if (v < 0) return;
            double[] a; if (!data.TryGetValue(id, out a)) { a = new double[N]; data[id] = a; fill[id] = 0; }
            Array.Copy(a, 1, a, 0, N - 1); a[N - 1] = v; if (fill[id] < N) fill[id]++;
        }
        // values oldest first (only the filled part)
        public static double[] Get(string id, out int count)
        {
            double[] a; count = 0; if (!data.TryGetValue(id, out a)) return null;
            count = fill[id]; return a;
        }
        public static void Clear() { data.Clear(); fill.Clear(); }

        // a small line graph of the last minute (one frozen geometry)
        public static FrameworkElement Spark(string id, double w, double h, Brush stroke)
        {
            int n; var a = Get(id, out n); if (a == null || n < 5) return null;
            double lo = double.MaxValue, hi = double.MinValue; for (int i = a.Length - n; i < a.Length; i++) { lo = Math.Min(lo, a[i]); hi = Math.Max(hi, a[i]); }
            if (hi - lo < 1e-6) { hi += 1; lo -= 1; }
            var g = new StreamGeometry();
            using (var c = g.Open())
                for (int i = a.Length - n, j = 0; i < a.Length; i++, j++)
                {
                    var p = new Point(w - (n - 1 - j) * w / (a.Length - 1), h - (a[i] - lo) / (hi - lo) * (h - 2) - 1);
                    if (j == 0) c.BeginFigure(p, false, false); else c.LineTo(p, true, false);
                }
            g.Freeze();
            return new Path { Data = g, Stroke = stroke, StrokeThickness = 1.2, Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center, StrokeLineJoin = PenLineJoin.Round };
        }
    }

    public class LayoutWindow : Window
    {
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        readonly OverlaySettings look; readonly Action changed; readonly string forWhat;
        StackPanel rows;

        public LayoutWindow(OverlaySettings lookToEdit, string what, Action onChange)
        {
            look = lookToEdit; changed = onChange; forWhat = what;
            Title = "Layout - Hallow Overlay"; Width = 720; Height = 720; MinWidth = 640; MinHeight = 400;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var dock = new DockPanel { Margin = new Thickness(22, 16, 22, 18) };
            var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock { Text = "Layout", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Accent.Brush() });
            head.Children.Add(new TextBlock { Text = "For " + what + ". Order, your own label, a colour, and text or text + a small graph of the last minute. Which stats show is set in the settings window; warnings stay amber and red.", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
            var ts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            ts.Children.Add(new TextBlock { Text = "Text size", FontWeight = FontWeights.SemiBold, Width = 90, VerticalAlignment = VerticalAlignment.Center });
            foreach (var o in new[] { Tuple.Create("s", "Small"), Tuple.Create("m", "Normal"), Tuple.Create("l", "Large"), Tuple.Create("xl", "Extra large") })
            {
                var key = o.Item1; var c = new ToggleButton { Content = o.Item2, Style = (Style)FindResource("Chip"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 5, 12, 5), IsChecked = (look.TextSize ?? "m") == key };
                c.Click += (s, e) => { look.TextSize = key; foreach (ToggleButton x in ts.Children.OfType<ToggleButton>()) x.IsChecked = x == c; changed(); };
                ts.Children.Add(c);
            }
            var reset = new Button { Content = "Reset layout", Style = (Style)FindResource("Btn"), Margin = new Thickness(18, 0, 0, 0) };
            reset.Click += (s, e) => { look.Styles = null; look.TextSize = "m"; foreach (ToggleButton x in ts.Children.OfType<ToggleButton>()) x.IsChecked = (string)x.Content == "Normal"; Fill(); changed(); };
            ts.Children.Add(reset);
            head.Children.Add(ts);
            dock.Children.Add(head);
            rows = new StackPanel();
            dock.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = dock;
            Fill();
        }

        void Fill()
        {
            rows.Children.Clear();
            var l = Stats.Ordered(look);
            for (int i = 0; i < l.Count; i++)
            {
                var st = l[i]; var info = Stats.All.First(a => a.Item1 == st.Id); int idx = i;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var box = new Border { Background = B("#1C1724"), BorderBrush = B("#2C2536"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 10, 5), Child = row };
                Func<string, bool, Button> arrow = (t, en) => new Button { Content = t, Style = (Style)FindResource("Btn"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), IsEnabled = en, FontSize = 11 };
                var up = arrow("▲", i > 0); var dn = arrow("▼", i < l.Count - 1);
                up.Click += (s, e) => Move(l, idx, -1); dn.Click += (s, e) => Move(l, idx, 1);
                DockPanel.SetDock(up, Dock.Left); DockPanel.SetDock(dn, Dock.Left); row.Children.Add(up); row.Children.Add(dn);
                var name = new TextBlock { Text = info.Item2, Width = 130, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) }; DockPanel.SetDock(name, Dock.Left); row.Children.Add(name);
                var label = new TextBox { Style = (Style)FindResource("Field"), Width = 110, Text = st.Label ?? "", ToolTip = "Your own label (empty = the normal one)", VerticalAlignment = VerticalAlignment.Center };
                label.TextChanged += (s, e) => { st.Label = label.Text.Length == 0 ? null : label.Text.Substring(0, Math.Min(16, label.Text.Length)); Save(l); };
                DockPanel.SetDock(label, Dock.Left); row.Children.Add(label);
                var sw = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                foreach (var col in new string[] { null }.Concat(Stats.Colors))
                {
                    var cc = col; bool on = st.Color == cc;
                    var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 5, 0), Cursor = Cursors.Hand, ToolTip = cc == null ? "Normal colour" : cc,
                        Background = cc == null ? (Brush)B("#3A3248") : B(cc), BorderThickness = new Thickness(on ? 2 : 1), BorderBrush = on ? Accent.Brush() : B("#55496A") };
                    if (cc == null) dot.Child = new TextBlock { Text = "A", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#ECE8F4") };
                    dot.MouseLeftButtonUp += (s, e) => { st.Color = cc; Save(l); Fill(); };
                    sw.Children.Add(dot);
                }
                DockPanel.SetDock(sw, Dock.Left); row.Children.Add(sw);
                if (info.Item3)
                {
                    var g = new CheckBox { Content = "Graph", IsChecked = st.Graph, Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                    g.Click += (s, e) => { st.Graph = g.IsChecked == true; Save(l); };
                    row.Children.Add(g);
                }
                else row.Children.Add(new Border());
                rows.Children.Add(box);
            }
        }
        void Move(List<StatStyle> l, int i, int d) { var x = l[i]; l.RemoveAt(i); l.Insert(i + d, x); Save(l); Fill(); }
        void Save(List<StatStyle> l) { look.Styles = l; changed(); }
    }
}
