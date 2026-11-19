// Hallow Overlay - benchmark reports (every frame between start and stop: averages, 1% / 0.1% lows, frame-time percentiles, a chart
// as PNG and every frame as CSV) and the optional CSV log of every stat while you play. Files go to Documents\Hallow Overlay.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HallowOverlay
{
    public class BenchReport
    {
        public string Game, Source; public DateTime Start; public double Seconds;
        public int Frames, Stutters; public double AvgFps, Low1, Low01, P50, P90, P95, P99, P999, MaxMs;
        public List<double> Ms;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string Folder = System.IO.Path.Combine(Out, "Benchmarks");
        // Documents\Hallow Overlay (HALLOW_OVERLAY_OUT replaces it for tests)
        public static string Out { get { var e = Environment.GetEnvironmentVariable("HALLOW_OVERLAY_OUT"); return string.IsNullOrEmpty(e) ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hallow Overlay") : e; } }

        // percentile of sorted values (nearest rank)
        static double Pct(List<double> sorted, double p) { int i = (int)Math.Ceiling(p / 100 * sorted.Count) - 1; return sorted[Math.Max(0, Math.Min(sorted.Count - 1, i))]; }

        public static BenchReport Make(List<double> ms, string game, DateTime start, string source)
        {
            if (ms == null || ms.Count < 30) return null;
            var s = ms.OrderBy(v => v).ToList(); var total = ms.Sum(); var med = Pct(s, 50);
            return new BenchReport { Game = game, Source = source, Start = start, Seconds = total / 1000, Frames = ms.Count, Ms = ms,
                AvgFps = ms.Count * 1000.0 / total, Low1 = FpsMeter.Lows(ms, 0.01), Low01 = FpsMeter.Lows(ms, 0.001),
                P50 = med, P90 = Pct(s, 90), P95 = Pct(s, 95), P99 = Pct(s, 99), P999 = Pct(s, 99.9), MaxMs = s[s.Count - 1],
                Stutters = ms.Count(v => v >= Math.Max(med * 2, med + 4)) };
        }
        public string Summary()
        {
            return Game + " benchmark: " + AvgFps.ToString("0") + " fps avg" + (Low1 >= 0 ? ", 1% " + Low1.ToString("0") : "") + (Low01 >= 0 ? ", 0.1% " + Low01.ToString("0") : "") + ", " + Stutters + (Stutters == 1 ? " stutter" : " stutters");
        }
        string BaseName() { var g = new string((Game ?? "Game").Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()); return g + " " + Start.ToString("yyyy-MM-dd HH-mm-ss"); }

        // CSV: the numbers first (as "# name,value" lines), then one row per frame
        public string SaveCsv(string folder)
        {
            Directory.CreateDirectory(folder); var path = System.IO.Path.Combine(folder, BaseName() + ".csv");
            var sb = new StringBuilder();
            Action<string, string> kv = (k, v) => sb.Append("# ").Append(k).Append(',').Append(v).Append("\r\n");
            Func<double, string> n = v => v < 0 ? "" : v.ToString("0.###", Inv);
            kv("game", "\"" + (Game ?? "").Replace("\"", "'") + "\""); kv("start", Start.ToString("s")); kv("seconds", n(Seconds)); kv("frames", Frames.ToString(Inv)); kv("frames from", Source);
            kv("average fps", n(AvgFps)); kv("1% low fps", n(Low1)); kv("0.1% low fps", n(Low01));
            kv("frame time p50 ms", n(P50)); kv("p90 ms", n(P90)); kv("p95 ms", n(P95)); kv("p99 ms", n(P99)); kv("p99.9 ms", n(P999)); kv("max ms", n(MaxMs)); kv("stutters", Stutters.ToString(Inv));
            sb.Append("frame,time_s,frame_time_ms\r\n");
            double t = 0; for (int i = 0; i < Ms.Count; i++) { t += Ms[i] / 1000; sb.Append(i + 1).Append(',').Append(t.ToString("0.0000", Inv)).Append(',').Append(Ms[i].ToString("0.###", Inv)).Append("\r\n"); }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        // PNG: title, the numbers, and frame times over time with p50 / p99 lines (UI thread)
        public string SavePng(string folder)
        {
            Directory.CreateDirectory(folder); var path = System.IO.Path.Combine(folder, BaseName() + ".png");
            Func<string, SolidColorBrush> B = h => new SolidColorBrush((Color)ColorConverter.ConvertFromString(h));
            double W = 1200, H = 640, cx = 60, cy = 150, cw = W - 90, ch = H - cy - 50;
            var c = new Canvas { Width = W, Height = H, Background = B("#16121C") };
            Action<string, double, double, double, string, FontWeight> text = (s, x, y, size, col, w) => { var tb = new TextBlock { Text = s, FontSize = size, Foreground = B(col), FontFamily = new FontFamily("Segoe UI"), FontWeight = w }; Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); c.Children.Add(tb); };
            text((Game ?? "Game") + "  -  benchmark", 28, 18, 26, Accent.Hex, FontWeights.Bold);
            text(Start.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture) + "  -  " + Seconds.ToString("0.0") + " s, " + Frames + " frames (" + Source + ")", 30, 56, 14, "#8F88A0", FontWeights.Normal);
            var cells = new[] { Tuple.Create("Average", AvgFps.ToString("0") + " fps"), Tuple.Create("1% low", Low1 >= 0 ? Low1.ToString("0") + " fps" : "-"), Tuple.Create("0.1% low", Low01 >= 0 ? Low01.ToString("0") + " fps" : "-"),
                Tuple.Create("Frame time p50 / p99", P50.ToString("0.0") + " / " + P99.ToString("0.0") + " ms"), Tuple.Create("p99.9 / max", P999.ToString("0.0") + " / " + MaxMs.ToString("0.0") + " ms"), Tuple.Create("Stutters", Stutters.ToString()) };
            for (int i = 0; i < cells.Length; i++) { text(cells[i].Item1, 30 + i * 192, 88, 12, "#8F88A0", FontWeights.Normal); text(cells[i].Item2, 30 + i * 192, 106, 19, "#ECE8F4", FontWeights.SemiBold); }
            double top = Math.Max(P999 * 1.3, P50 * 3); top = Math.Ceiling(top / 20) * 20;
            c.Children.Add(new Rectangle { Width = cw, Height = ch, Fill = B("#1C1724"), Stroke = B("#3A3248") }); Canvas.SetLeft(c.Children[c.Children.Count - 1], cx); Canvas.SetTop(c.Children[c.Children.Count - 1], cy);
            for (int g = 0; g <= 4; g++) { var v = top * g / 4; var y = cy + ch - v / top * ch; text(v.ToString("0") + " ms", 8, y - 9, 11, "#8F88A0", FontWeights.Normal); var gl = new Line { X1 = cx, X2 = cx + cw, Y1 = y, Y2 = y, Stroke = B("#2C2536") }; c.Children.Add(gl); }
            int cols = (int)cw; var mx = new double[cols];
            for (int i = 0; i < Ms.Count; i++) { int k = (int)((long)i * cols / Ms.Count); if (Ms[i] > mx[k]) mx[k] = Ms[i]; }
            var geo = new StreamGeometry(); var spikes = new StreamGeometry(); double spike = Math.Max(P50 * 2, P50 + 4);
            using (var g = geo.Open()) using (var sg = spikes.Open())
                for (int i = 0; i < cols; i++)
                {
                    var p = new Point(cx + i, cy + ch - Math.Min(mx[i], top) / top * ch);
                    if (i == 0) g.BeginFigure(p, false, false); else g.LineTo(p, true, false);
                    if (mx[i] >= spike) { sg.BeginFigure(new Point(p.X, cy + ch), false, false); sg.LineTo(p, true, false); }
                }
            geo.Freeze(); spikes.Freeze();
            c.Children.Add(new System.Windows.Shapes.Path { Data = geo, Stroke = B("#A98BE0"), StrokeThickness = 1.2 });
            c.Children.Add(new System.Windows.Shapes.Path { Data = spikes, Stroke = B("#FF6B6B"), StrokeThickness = 1.5 });
            Action<double, string, string> mark = (v, label, col) => { var y = cy + ch - Math.Min(v, top) / top * ch; c.Children.Add(new Line { X1 = cx, X2 = cx + cw, Y1 = y, Y2 = y, Stroke = B(col), StrokeDashArray = new DoubleCollection { 4, 3 } }); text(label, cx + cw - 110, y - 17, 11, col, FontWeights.SemiBold); };
            mark(P50, "p50 " + P50.ToString("0.0") + " ms", "#22C55E"); mark(P99, "p99 " + P99.ToString("0.0") + " ms", "#F0BE5A");
            text("frame times over " + Seconds.ToString("0") + " s - red: stutters (over twice the typical frame time)  -  Hallow Overlay " + Program.Version, cx, cy + ch + 12, 11, "#8F88A0", FontWeights.Normal);
            c.Measure(new Size(W, H)); c.Arrange(new Rect(0, 0, W, H)); c.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)W, (int)H, 96, 96, PixelFormats.Pbgra32); bmp.Render(c);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using (var f = File.Create(path)) enc.Save(f);
            return path;
        }
    }

    // One CSV per game session with every stat once a second (optional, Settings > Log every stat).
    public class StatLog
    {
        public static string Folder = System.IO.Path.Combine(BenchReport.Out, "Logs");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        StreamWriter w; string key;
        public string Path { get; private set; }
        public const string Header = "time,game,fps,low1,low01,cpu_pct,gpu_pct,gpu_temp_c,gpu_clock_mhz,mem_clock_mhz,gpu_power_w,vram_mb,ram_gb,ping_ms,loss_pct,jitter_ms,server,server_ping_ms,server_loss_pct,stalls,up_kbps,down_kbps";

        // call once a second: game == null ends the file
        public void Write(string game, string sessionKey, Sample x, string server, DateTime now)
        {
            if (game == null || sessionKey != key) Close();
            if (game == null) return;
            try
            {
                if (w == null)
                {
                    Directory.CreateDirectory(Folder); key = sessionKey;
                    var g = new string(game.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
                    Path = System.IO.Path.Combine(Folder, g + " " + now.ToString("yyyy-MM-dd HH-mm-ss") + ".csv");
                    w = new StreamWriter(Path, false, new UTF8Encoding(false)); w.Write(Header + "\r\n");
                }
                Func<double, string> n = v => v < 0 ? "" : v.ToString("0.##", Inv);
                w.Write(string.Join(",", new[] { now.ToString("s"), "\"" + game.Replace("\"", "'") + "\"", n(x.Fps), n(x.FpsLow), n(x.FpsLow01), n(x.Cpu), n(x.Gpu), n(x.GpuTemp), n(x.GpuClock), n(x.MemClock), n(x.PowerW), n(x.VramUsedMB),
                    n(x.RamUsedGB), n(x.PingMs), n(x.LossPct), n(x.JitterMs), server ?? "", n(server != null ? x.ServerMs : -1), n(server != null ? x.ServerLoss : -1), server != null ? x.Stalls.ToString(Inv) : "", n(x.UpKbps), n(x.DownKbps) }) + "\r\n");
                w.Flush();
            }
            catch { Close(); }
        }
        public void Close() { if (w != null) try { w.Dispose(); } catch { } w = null; key = null; }
    }
}
