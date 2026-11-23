// Hallow Overlay tests - the in-game panel, built in memory and read back (never shown): stats formatting and warning colours,
// the frame-time graph with its 1% low, Gear's chip group, and light / dark (the panel always stays dark).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using WShapes = System.Windows.Shapes;

namespace HallowOverlay.Tests
{
    public static partial class Run
    {
        const string Red = "#FF6B6B", Amber = "#F0BE5A", White = "#ECE8F4";

        // every element under el, depth first, in order
        static IEnumerable<DependencyObject> Walk(DependencyObject el)
        {
            if (el == null) yield break;
            yield return el;
            var p = el as System.Windows.Controls.Panel;
            if (p != null) foreach (UIElement c in p.Children) foreach (var d in Walk(c)) yield return d;
            var dec = el as Decorator; if (dec != null) foreach (var d in Walk(dec.Child)) yield return d;
            var cc = el as ContentControl; if (cc != null && cc.Content is DependencyObject) foreach (var d in Walk((DependencyObject)cc.Content)) yield return d;
        }
        static List<TextBlock> Blocks(FrameworkElement el) { return Walk(el).OfType<TextBlock>().ToList(); }
        static List<string> Texts(FrameworkElement el) { return Blocks(el).Select(t => t.Text).ToList(); }
        static string Hex(Brush b) { var s = b as SolidColorBrush; return s == null ? null : "#" + s.Color.R.ToString("X2") + s.Color.G.ToString("X2") + s.Color.B.ToString("X2"); }
        static TextBlock Find(FrameworkElement el, string start) { return Blocks(el).FirstOrDefault(t => t.Text.StartsWith(start)); }
        static string ColourOf(FrameworkElement el, string start) { var t = Find(el, start); return t == null ? null : Hex(t.Foreground); }
        static bool HasIcon(FrameworkElement el, string icon) { var g = Icons.Get(icon); return Walk(el).OfType<WShapes.Path>().Any(p => object.ReferenceEquals(p.Data, g)); }

        static OverlaySettings Look0() { var s = new OverlaySettings(); s.Clock = false; s.MicWarn = true; return s; }
        static Sample Sample0()
        {
            return new Sample { Cpu = 23, Gpu = 97, GpuTemp = 71, RamUsedGB = 14.2, RamTotalGB = 32, PingMs = 18, Fps = 144, FpsLow = 97, FpsLow01 = 81, LossPct = 0, JitterMs = 3,
                NowPlaying = "Lo-fi Beats - Rainy Night", ServerLabel = "FRA", ServerMs = 23, ServerApprox = true, ServerLoss = 0, UpKbps = 96, DownKbps = 410,
                GpuClock = 2610, MemClock = 10501, PowerW = 241, PowerLimitW = 285, VramUsedMB = 9420, VramTotalMB = 16376 };
        }
        static FrameworkElement P(OverlaySettings s, Sample x) { return Panel.Build(s, x, "Delta Force", TimeSpan.FromMinutes(42)); }

        static void StatsFormatting()
        {
            var s = Look0(); var x = Sample0();
            var p = P(s, x); var t = Texts(p);
            Ok(t.Contains("144 FPS  1% 97"), "FPS with its 1% low: \"144 FPS  1% 97\"");
            Eq(White, ColourOf(p, "144 FPS"), "144 FPS is a normal colour");
            Ok(t.Contains("CPU 23%") && t.Contains("GPU 97%"), "CPU / GPU read \"23%\" with no space");
            Ok(t.Contains("71°C"), "GPU temperature \"71°C\"");
            Ok(t.Contains("14.2 / 32 GB"), "RAM \"14.2 / 32 GB\"");
            Ok(t.Contains("18 ms  ±3  0% loss"), "ping with jitter and loss: \"18 ms  ±3  0% loss\"");
            Ok(t.Contains("FRA  ~23 ms  0% loss"), "game server: \"FRA  ~23 ms  0% loss\" (~ = measured another way)");
            Ok(t.Contains("Lo-fi Beats - Rainy Night"), "now playing");
            Ok(t.Contains("42 min"), "session under an hour: \"42 min\"");
            Ok(!t.Any(z => z.Contains("Delta Force")), "the game's name is off by default");
            Ok(!t.Any(z => z.Contains("MHz") || z.Contains(" W ") || z.Contains("VRAM") || z.Contains("kbps")), "GPU clocks, power, VRAM and traffic are off by default");

            var s2 = Look0(); s2.Fps01 = true; s2.Game = true; s2.GpuClocks = s2.GpuPower = s2.Vram = s2.Traffic = true;
            var t2 = Texts(Panel.Build(s2, x, "Delta Force", TimeSpan.FromMinutes(65)));
            Ok(t2.Contains("144 FPS  1% 97  0.1% 81"), "0.1% low when it's on");
            Ok(t2.Contains("Delta Force"), "the game's name when it's on");
            Ok(t2.Contains("1:05"), "session over an hour: \"1:05\"");
            Ok(t2.Contains("2610 / 10501 MHz"), "GPU clocks \"2610 / 10501 MHz\"");
            Ok(t2.Contains("241 W  85%"), "GPU power \"241 W  85%\" of the limit");
            Ok(t2.Contains("VRAM 9.2 / 16 GB"), "VRAM \"9.2 / 16 GB\"");
            Ok(t2.Contains("↑96  ↓410 kbps"), "traffic \"↑96  ↓410 kbps\"");

            // warning colours
            var y = x.Copy(); y.Fps = 45; Eq(Amber, ColourOf(P(s, y), "45 FPS"), "45 FPS is amber");
            y.Fps = 25; Eq(Red, ColourOf(P(s, y), "25 FPS"), "25 FPS is red");
            y = x.Copy(); y.Cpu = 95; Eq(Amber, ColourOf(P(s, y), "CPU 95%"), "CPU at 95% is amber");
            y = x.Copy(); y.GpuTemp = 80; Eq(Amber, ColourOf(P(s, y), "80°C"), "GPU at the hot limit (80°C) is amber");
            y.GpuTemp = 87; Eq(Red, ColourOf(P(s, y), "87°C"), "GPU 7°C over it is red");
            var hot = Look0(); hot.HotGpu = 90; Eq(White, ColourOf(P(hot, y), "87°C"), "your own hot limit (90°C): 87°C is fine");
            y = x.Copy(); y.RamUsedGB = 28; Eq(Amber, ColourOf(P(s, y), "28.0 / 32 GB"), "RAM 87% used is amber (over 85%)");
            y.RamUsedGB = 31; Eq(Red, ColourOf(P(s, y), "31.0 / 32 GB"), "RAM 97% used is red");
            y = x.Copy(); y.PingMs = -1; Eq(Red, ColourOf(P(s, y), "no net"), "no ping reply: \"no net\" in red");
            y.PingMs = 120; Eq(Amber, ColourOf(P(s, y), "120 ms"), "120 ms ping is amber (over 80)");
            y.PingMs = 160; Eq(Red, ColourOf(P(s, y), "160 ms"), "160 ms ping is red");
            y = x.Copy(); y.LossPct = 3.3; Ok(Texts(P(s, y)).Contains("18 ms  ±3  3.3% loss"), "loss \"3.3% loss\""); Eq(Amber, ColourOf(P(s, y), "18 ms"), "3.3% loss is amber");
            y.LossPct = 6; Eq(Red, ColourOf(P(s, y), "18 ms"), "6% loss is red");
            y = x.Copy(); y.JitterMs = 31; Eq(Amber, ColourOf(P(s, y), "18 ms"), "jitter over 30 ms is amber");
            var nl = Look0(); nl.LossJitter = false; Ok(Texts(P(nl, x)).Contains("18 ms"), "loss and jitter off: just \"18 ms\"");
            y = x.Copy(); y.Stalls = 1; Ok(Texts(P(s, y)).Contains("FRA  ~23 ms  0% loss  1 stall"), "one stall"); Eq(Amber, ColourOf(P(s, y), "FRA"), "one stall is amber");
            y.Stalls = 3; Ok(Texts(P(s, y)).Any(z => z.EndsWith("3 stalls")), "three stalls"); Eq(Red, ColourOf(P(s, y), "FRA"), "three stalls are red");
            y = x.Copy(); y.PowerW = 283; Eq(Amber, ColourOf(Panel.Build(s2, y, "Delta Force", null), "283 W"), "at the power limit (99%) is amber");

            // missing numbers and other cases
            y = x.Copy(); y.FpsLow = -1; Ok(Texts(P(s, y)).Contains("144 FPS"), "no 1% low yet: just \"144 FPS\"");
            y.Fps = -1; Ok(!Texts(P(s, y)).Any(z => z.Contains("FPS")), "no FPS (no frames / not allowed): nothing shown");
            y = x.Copy(); y.NowPlaying = new string('a', 30) + " - " + new string('b', 30); var np = Texts(P(s, y)).First(z => z.StartsWith("aaa"));
            Ok(np.Length == NowPlaying.MaxChars && np.EndsWith("…"), "a long song is cut to 48 characters with …");
            var lab = Look0(); lab.Styles = new List<StatStyle> { new StatStyle { Id = "fps", Label = "Frames" }, new StatStyle { Id = "cpu", Label = "Processor", Color = "#4ADE80" } };
            var lp = P(lab, x); var lt = Texts(lp);
            Ok(lt.Contains("Frames 144  1% 97"), "your own FPS label replaces \"FPS\": \"Frames 144  1% 97\"");
            Ok(lt.Contains("Processor 23%") && ColourOf(lp, "Processor") == "#4ADE80", "your own label and colour for CPU");
            Ok(lt.IndexOf("Frames 144  1% 97") < lt.IndexOf("Processor 23%"), "your order: FPS before CPU");
            var ord = Look0(); ord.Styles = new List<StatStyle> { new StatStyle { Id = "cpu" }, new StatStyle { Id = "fps" } };
            var ot = Texts(P(ord, x)); Ok(ot.IndexOf("CPU 23%") < ot.IndexOf("144 FPS  1% 97"), "layout order: CPU moved before FPS");
            y = x.Copy(); y.Cpu = 95; lab.Styles[1].Color = "#4ADE80"; Eq(Amber, ColourOf(P(lab, y), "Processor"), "warnings stay amber over your own colour");

            // only when something's wrong
            var bad = Look0(); bad.OnlyBad = true;
            var bp = P(bad, x); Ok(Texts(bp).Count == 0 && !(bp is Border && ((Border)bp).Child != null), "only-bad with nothing wrong: an empty panel");
            y = x.Copy(); y.PingMs = 160; y.Cpu = 95; var bt = Texts(P(bad, y));
            Ok(bt.Contains("160 ms  ±3  0% loss") && bt.Contains("CPU 95%") && !bt.Contains("GPU 97%") && !bt.Any(z => z.Contains("FPS")), "only-bad: just the ping and CPU in trouble");

            // MIC MUTED, messages, the benchmark marker
            y = x.Copy(); y.MicMuted = true; Ok(Texts(P(s, y)).Contains("MIC MUTED"), "MIC MUTED shows when Mixer says the mic is muted");
            var nm = Look0(); nm.MicWarn = false; Ok(!Texts(P(nm, y)).Contains("MIC MUTED"), "...and not when the warning is off");
            var fp = Panel.Build(s, x, "Delta Force", null, "Clip saved", false, false); var ft = Texts(fp);
            Ok(ft[0] == "Clip saved" && ft.Contains("CPU 23%"), "a flash shows first, with the stats");
            var mo = Texts(Panel.Build(s, x, null, null, "Time for a break", true, true));
            Ok(mo.Count == 1 && mo[0] == "Time for a break", "a message-only panel (panel hidden): just the message");
            var lm = Texts(Panel.Build(s, x, null, null, new string('x', 120), false, true))[0];
            Ok(lm.Length == 80 && lm.EndsWith("…"), "a long message is cut to 80 characters");
            y = x.Copy(); y.BenchSeconds = 62; var bn = Texts(P(ord, y));
            Ok(bn[0] == "BENCHMARK 1:02", "the benchmark marker comes first: \"BENCHMARK 1:02\"");

            // the panel's own look
            var root = (Border)P(s, x); var bg = (SolidColorBrush)root.Background;
            Eq((byte)(0.85 * 255), bg.Color.A, "opacity 0.85 -> background alpha 216");
            var see = Look0(); see.Opacity = 0.05; Eq((byte)(0.2 * 255), ((SolidColorBrush)((Border)P(see, x)).Background).Color.A, "opacity is never under 0.2 (always readable)");
            var big = Look0(); big.Scale = 1.5; big.TextSize = "l";
            Ok(Math.Abs(Find(P(big, x), "CPU").FontSize - 13 * 1.5 * 1.2) < 0.01, "scale 1.5 and large text: 23.4 pt");
            Ok(Math.Abs(Find(root, "CPU").FontSize - 13) < 0.01, "normal size: 13 pt");
        }

        static void GraphTests()
        {
            var ms = MainWindow.FakeFrames(); double med, low1, top, spike;
            Panel.GraphStats(ms, ms.Length, out med, out low1, out top, out spike);
            var sorted = ms.OrderBy(v => v).ToArray();
            Ok(Math.Abs(med - sorted[ms.Length / 2]) < 1e-9 && med > 6.9 && med < 7.8, "median frame time of a 144 fps game: " + med.ToString("0.00") + " ms");
            var slow = sorted.Reverse().Take(ms.Length / 100).Average();
            Ok(Math.Abs(low1 - slow) < 1e-9, "1% low frame time = the average of the slowest 1% (" + low1.ToString("0.0") + " ms)");
            var lows = FpsMeter.Lows(ms.ToList(), 0.01);
            Ok(Math.Abs(1000 / low1 - lows) < 1e-6, "the graph's 1% low matches the FPS stat's 1% low (" + lows.ToString("0") + " fps)");
            Eq("1% " + lows.ToString("0") + " fps", Panel.GraphLowLabel(low1), "the 1% low label");
            Eq(Panel.GraphLabel(med), med.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms", "the median label");
            Ok(top == Math.Max(med * 3, 20) && spike == Math.Max(med * 2, med + 4), "graph top and spike line");
            int spikes; var g = (StackPanel)Panel.Graph(ms, ms.Length, "m", 1, out spikes);
            Ok(spikes >= 2, "the two hitches show as spikes (" + spikes + " columns)");
            var grid = (Grid)g.Children[0];
            Ok(Math.Abs(grid.Width - 170) < 0.01 && Math.Abs(grid.Height - 32) < 0.01, "medium graph is 170 x 32");
            var dashed = grid.Children.OfType<WShapes.Line>().Where(l => l.StrokeDashArray != null && l.StrokeDashArray.Count > 0).ToList();
            Ok(dashed.Count == 1, "one dashed 1% low line");
            Ok(dashed.Count == 1 && Math.Abs(dashed[0].Y1 - (32 - Math.Min(low1, top) / top * 32)) < 0.01, "the 1% low line sits at the 1% low frame time");
            var labels = Texts(g); Ok(labels.Count == 2 && labels[1] == Panel.GraphLowLabel(low1), "labels: median and 1% low");
            Ok(Math.Abs(((Grid)((StackPanel)Panel.Graph(ms, ms.Length, "s", 1, out spikes)).Children[0]).Width - 110) < 0.01 &&
               Math.Abs(((Grid)((StackPanel)Panel.Graph(ms, ms.Length, "l", 2, out spikes)).Children[0]).Width - 520) < 0.01, "small 110 px; large at scale 2 is 520 px");

            var few = ms.Take(60).ToArray(); Panel.GraphStats(few, few.Length, out med, out low1, out top, out spike);
            Ok(low1 < 0, "under 100 frames: no 1% low yet");
            var gf = (StackPanel)Panel.Graph(few, few.Length, "m", 1, out spikes);
            Ok(((Grid)gf.Children[0]).Children.OfType<WShapes.Line>().All(l => l.StrokeDashArray == null || l.StrokeDashArray.Count == 0) && Texts(gf).Count == 1, "...so no dashed line and no 1% label");
            var steady = Enumerable.Repeat(6.94, 700).ToArray(); Panel.Graph(steady, steady.Length, "m", 1, out spikes);
            Ok(spikes == 0, "steady frames: no spikes");
            Panel.GraphStats(null, 0, out med, out low1, out top, out spike); Ok(med == 0 && low1 < 0 && top == 20, "no frames: nothing breaks");

            var s = Look0(); s.FrameGraph = true; var x = Sample0(); x.FrameMs = ms; x.FrameCount = ms.Length;
            Ok(Walk(P(s, x)).OfType<Grid>().Any(gr => Math.Abs(gr.Width - 170) < 0.01), "frame-time graph on: it's in the panel");
            x.FrameCount = 5; Ok(!Walk(P(s, x)).OfType<Grid>().Any(), "fewer than 10 frames: no graph");
            var b = Look0(); b.FrameGraph = true; b.OnlyBad = true; var sx = Sample0(); sx.FrameMs = steady; sx.FrameCount = steady.Length;
            Ok(!Walk(P(b, sx)).OfType<Grid>().Any(), "only-bad: a smooth graph hides");
            sx.FrameMs = ms; sx.FrameCount = ms.Length; Ok(Walk(P(b, sx)).OfType<Grid>().Any(), "only-bad: a graph with stutters shows");
        }

        static GearChip Chip(string kind, int pct, int level, bool charging = false, bool wired = false, bool est = false, string name = "") { return new GearChip { Kind = kind, Pct = pct, Level = level, Charging = charging, Wired = wired, Estimated = est, Name = name }; }
        static void GearPanel()
        {
            var chips = new List<GearChip> { Chip("mouse", 64, 0, name: "PRO X 2"), Chip("headset", 9, 2, name: "ASTRO A50"), Chip("earbuds", 45, 0, est: true), Chip("controller", 15, 1, charging: true), Chip("headset", 71, 0, wired: true) };
            var g = (StackPanel)Panel.GearChips(chips, false, 1, 1, true);
            var pills = g.Children.OfType<Border>().ToList();
            Eq(5, pills.Count, "one pill per device");
            Eq(Orientation.Horizontal, g.Orientation, "the pills sit side by side");
            Ok(Texts(g).SequenceEqual(new[] { "64%", "9%", "~45%", "15%", "71%" }), "pill texts: 64% 9% ~45% 15% 71% (" + string.Join(" ", Texts(g)) + ")");
            Ok(Texts(g).All(z => !z.Contains(" %")), "never \"45 %\"");
            Eq("PRO X 2", pills[0].ToolTip, "a pill's tooltip is the device's name");
            Ok(pills[2].ToolTip == null, "no name: no tooltip");
            Eq(Red, ColourOf(g, "9%"), "very low is red"); Eq(Amber, ColourOf(g, "15%"), "low is amber"); Eq(White, ColourOf(g, "64%"), "fine is normal");
            Ok(((SolidColorBrush)pills[1].Background).Color.R == 0xFF && ((SolidColorBrush)pills[1].Background).Color.A > 22, "a very low pill gets a red tint");
            Ok(HasIcon(pills[3], "bolt") && HasIcon(pills[4], "plug") && !HasIcon(pills[0], "bolt") && !HasIcon(pills[0], "plug"), "bolt while charging, plug on a cable, nothing on battery");
            Ok(HasIcon(pills[0], "computer-mouse") && HasIcon(pills[1], "headphones") && HasIcon(pills[2], "earbuds") && HasIcon(pills[3], "gamepad"), "device icons: mouse, headphones, earbuds, gamepad");
            Ok(pills[0].Margin.Left == 0 && pills[1].Margin.Left > 0, "a small gap between pills, none before the first");
            var low = (StackPanel)Panel.GearChips(chips, true, 1, 1, true);
            Ok(Texts(low).SequenceEqual(new[] { "9%", "15%" }), "only-bad: just the low ones");
            Ok(Panel.GearChips(new List<GearChip> { Chip("mouse", 64, 0) }, true, 1, 1, true) == null && Panel.GearChips(null, false, 1, 1, true) == null && Panel.GearChips(new List<GearChip>(), false, 1, 1, true) == null, "nothing to show: no group");

            var s = Look0(); var x = Sample0(); x.Gear = chips;
            var list = Walk(P(s, x)).OfType<StackPanel>().First(sp => sp.Children.OfType<FrameworkElement>().Any(c => "gear".Equals(c.Tag)));
            Ok("gear".Equals(((FrameworkElement)list.Children[list.Children.Count - 1]).Tag), "the chips come last in the panel");
            var col = Look0(); col.Row = false; var cg = Walk(P(col, x)).OfType<StackPanel>().First(sp => "gear".Equals(sp.Tag));
            Ok(cg.Orientation == Orientation.Horizontal && cg.Children.Count == 5, "a column panel keeps the chips together on one line");
            x.GearAlert = "Headset battery very low: 9%"; x.GearAlertLevel = 2;
            Eq(Red, ColourOf(P(s, x), "Headset battery"), "Gear's held critical alert is a red line");
            x.GearAlertLevel = 1; Eq(Amber, ColourOf(P(s, x), "Headset battery"), "a low alert is amber");
            var mo = Texts(Panel.Build(s, x, null, null, "Clip saved", false, true));
            Ok(mo.Count == 1, "a message-only panel never shows the chips or the alert");
            x.GearAlert = new string('b', 90); Ok(Texts(P(s, x)).Any(z => z.Length == 60 && z.EndsWith("…")), "a long alert is cut to 60 characters");
        }

        static void LightDark()
        {
            var theme = Path.Combine(Dir, "theme-mode.json");
            Eq("dark", Look.ReadFamilyMode(Path.Combine(Dir, "none.json")), "no theme.json: dark");
            File.WriteAllText(theme, "{\"accent\":\"#F07820\",\"mode\":\"light\"}"); Eq("light", Look.ReadFamilyMode(theme), "theme.json mode light");
            File.WriteAllText(theme, "{\"mode\":\" System \"}"); Eq("system", Look.ReadFamilyMode(theme), "theme.json mode System (case, spaces)");
            File.WriteAllText(theme, "{\"mode\":\"sepia\"}"); Eq("dark", Look.ReadFamilyMode(theme), "an unknown mode: dark");
            File.WriteAllText(theme, "{\"mode\":"); Eq("dark", Look.ReadFamilyMode(theme), "a broken theme.json: dark");
            Ok(Look.Resolve("light", "dark", () => false), "your Light wins over the family's dark");
            Ok(!Look.Resolve("dark", "light", () => true), "your Dark wins over the family's light");
            Ok(Look.Resolve("follow", "light", () => false) && !Look.Resolve("follow", "dark", () => true), "Follow Hallow uses the family mode");
            Ok(Look.Resolve("follow", "system", () => true) && !Look.Resolve("follow", "system", () => false), "Same as Windows follows Windows");
            Ok(!Look.Resolve(null, "system", null), "no Windows reader: dark");

            Ok(Look.Contrast(Look.LText, Look.LBg) >= 4.5 && Look.Contrast(Look.LMuted, Look.LPanel) >= 4.5 && Look.Contrast(Look.LMuted, Look.LBg) >= 4.5, "light text and muted text reach 4.5:1");
            foreach (var acc in new[] { "#F07820", "#8B5CF6", "#22C55E", "#3B82F6", "#EAB308" })
            {
                var ink = Look.TextAccent(acc); var fill = Look.LightFill(acc);
                Ok(Look.Contrast(ink, Look.LPanel) >= 4.5 && Look.Contrast(ink, Look.LBg) >= 4.5, "accent " + acc + " as text on light: " + ink);
                Ok(Look.Contrast(fill, Look.FillText(fill)) >= 4.5, "accent " + acc + " as a light fill with readable text: " + fill + " / " + Look.FillText(fill));
            }
            string xaml; using (var st = typeof(Program).Assembly.GetManifestResourceStream("HallowOverlay.Theme.xaml")) using (var r = new StreamReader(st)) xaml = r.ReadToEnd();
            xaml = xaml.Replace("assembly=HallowOverlay", "assembly=" + typeof(Program).Assembly.GetName().Name);   // the converters live in this test exe
            var lightX = Pal.Theme(xaml, "#F07820", true); var darkX = Pal.Theme(xaml, "#F07820", false);
            Ok(!lightX.Contains("#16121C") && lightX.Contains(Look.LBg), "light theme: the dark background is swapped");
            Ok(darkX.Contains("#16121C") && !darkX.Contains(Look.LBg), "dark theme: unchanged");
            Ok(XamlReader.Parse(lightX) is ResourceDictionary && XamlReader.Parse(darkX) is ResourceDictionary, "both themes are valid XAML");
            Look.IsLight = true;
            try
            {
                Eq(Look.LText, Pal.Hex("#ECE8F4"), "light: code colours swap (text)");
                var bg = ((SolidColorBrush)((Border)P(Look0(), Sample0())).Background).Color;
                Ok(bg.R == 0x16 && bg.G == 0x12 && bg.B == 0x1C, "light mode: the in-game panel stays dark");
                Eq(White, ColourOf(P(Look0(), Sample0()), "CPU"), "light mode: panel text stays light");
            }
            finally { Look.IsLight = false; }
            Eq("#ECE8F4", Pal.Hex("#ECE8F4"), "dark: code colours unchanged");
        }
    }
}
