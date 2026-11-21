// Hallow Overlay - the Hallow accent colour (picked in Hallow Toolkit, %APPDATA%\Hallow\theme.json) and crash reports kept on this PC.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HallowOverlay
{
    public static class Accent
    {
        public const string Default = "#F07820";   // Overlay's own orange
        public static string Hex = Default;         // what the windows use now
        // HALLOW_THEME_FILE replaces the path for tests
        public static string FilePath
        {
            get
            {
                var e = Environment.GetEnvironmentVariable("HALLOW_THEME_FILE");
                return string.IsNullOrEmpty(e) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Hallow\theme.json") : e;
            }
        }

        // The accent in theme.json, or null when the file is missing or broken.
        public static string ReadFile()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(FilePath));
                object a; if (d == null || !d.TryGetValue("accent", out a)) return null;
                var s = (a as string ?? "").Trim();
                return Regex.IsMatch(s, "^#[0-9A-Fa-f]{6}$") ? s.ToUpperInvariant() : null;
            }
            catch { return null; }
        }
        public static string Pick(bool useHallow) { return (useHallow ? ReadFile() : null) ?? Default; }

        static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        static string H(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        public static string Lighter(string hex) { var c = C(hex); Func<byte, byte> l = v => (byte)(v + (255 - v) * 0.3); return H(Color.FromRgb(l(c.R), l(c.G), l(c.B))); }
        // dark text on a light accent, white on a dark one
        public static string TextOn(string hex)
        {
            var c = C(hex);
            Func<byte, double> lin = v => { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); };
            var l = 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B);
            return (l + 0.05) / 0.056 >= 1.05 / (l + 0.05) ? "#1A0E04" : "#FFFFFF";   // whichever contrasts more (WCAG)
        }
        public static SolidColorBrush Brush() { return new SolidColorBrush(C(Hex)); }
        // 1.20: the accent for text and thin lines in the settings windows - darkened in light mode so it stays readable
        public static SolidColorBrush Ink() { return new SolidColorBrush(C(Look.IsLight ? Look.TextAccent(Hex) : Hex)); }

        // Theme.xaml with the accent in place of Overlay's orange
        public static string Themed(string xaml, string hex)
        {
            if (hex == Default) return xaml;
            return xaml.Replace("#F07820", hex).Replace("#F59A50", Lighter(hex)).Replace("#1A0E04", TextOn(hex));
        }

        // Calls `changed` on the UI thread (300 ms after the last change) when theme.json changes. The folder may not exist yet:
        // then it looks again every 10 s.
        public static void Watch(Dispatcher ui, Action changed)
        {
            var debounce = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(300) };
            debounce.Tick += (s, e) => { debounce.Stop(); try { changed(); } catch { } };
            FileSystemWatcher w = null;
            Action poke = () => ui.BeginInvoke(new Action(() => { debounce.Stop(); debounce.Start(); }));
            Func<bool> tryWatch = () =>
            {
                try
                {
                    var dir = Path.GetDirectoryName(FilePath); if (!Directory.Exists(dir)) return false;
                    w = new FileSystemWatcher(dir, Path.GetFileName(FilePath)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size };
                    w.Changed += (s, e) => poke(); w.Created += (s, e) => poke(); w.Deleted += (s, e) => poke(); w.Renamed += (s, e) => poke();
                    w.EnableRaisingEvents = true; return true;
                }
                catch { return false; }
            };
            if (tryWatch()) return;
            var look = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(10) };
            look.Tick += (s, e) => { if (tryWatch()) { look.Stop(); poke(); } };
            look.Start();
        }
    }

    // Crash reports: written to %APPDATA%\HallowOverlay\crashes (newest 20), shown as a notice the next time. Never sent anywhere,
    // and they hold only the error itself - no settings, keys or file contents.
    public static class Crash
    {
        public static string Dir = Path.Combine(OverlaySettings.Dir, "crashes");
        public const int Keep = 20;
        static int writing;

        public static void Install(Application app)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Write(e.ExceptionObject as Exception, "unhandled");
            if (app != null) app.DispatcherUnhandledException += (s, e) => Write(e.Exception, "UI thread");   // not handled: the app still closes
            TaskScheduler.UnobservedTaskException += (s, e) => Write(e.Exception, "background task");
        }

        static string Windows()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var build = Convert.ToString(k.GetValue("CurrentBuildNumber")); int b; var name = Convert.ToString(k.GetValue("ProductName"));
                    if (int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");   // the registry still says 10 on 11
                    return name + " " + k.GetValue("DisplayVersion") + " (build " + build + "." + k.GetValue("UBR") + ")";
                }
            }
            catch { return Environment.OSVersion.VersionString; }
        }

        public static string Write(Exception ex, string where)
        {
            if (ex == null || System.Threading.Interlocked.Exchange(ref writing, 1) == 1) return null;
            try
            {
                Directory.CreateDirectory(Dir);
                var now = DateTime.Now; var path = Path.Combine(Dir, now.ToString("yyyy-MM-dd HH-mm-ss") + ".txt");
                for (int i = 2; File.Exists(path); i++) path = Path.Combine(Dir, now.ToString("yyyy-MM-dd HH-mm-ss") + " (" + i + ").txt");
                var sb = new StringBuilder();
                sb.AppendLine("Hallow Overlay " + Program.Version + " closed unexpectedly (" + where + ")");
                sb.AppendLine("Time: " + now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Windows: " + Windows() + (Environment.Is64BitProcess ? ", 64-bit" : ", 32-bit"));
                sb.AppendLine(".NET: " + Environment.Version);
                sb.AppendLine();
                for (var e = ex; e != null; e = e.InnerException)
                {
                    sb.AppendLine((e == ex ? "" : "Inner: ") + e.GetType().FullName + ": " + e.Message);
                    sb.AppendLine(e.StackTrace ?? "(no stack)");
                    sb.AppendLine();
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                foreach (var old in Files().Skip(Keep)) try { File.Delete(old); } catch { }
                return path;
            }
            catch { return null; }
            finally { writing = 0; }
        }

        // newest first
        public static List<string> Files()
        {
            try { return Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.txt").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }
        public static DateTime? Last()
        {
            var f = Files().FirstOrDefault(); if (f == null) return null;
            try { return File.GetLastWriteTime(f); } catch { return null; }
        }
    }

    // "Hallow Overlay closed unexpectedly last time" with Show details / Open folder / OK.
    public class CrashNotice : Window
    {
        public CrashNotice(string file)
        {
            Title = "Hallow Overlay"; Width = 470; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowActivated = false; Look.Track(this);
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
            sp.Children.Add(new TextBlock { Text = "Hallow Overlay closed unexpectedly last time", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Accent.Ink(), TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(new TextBlock { Text = "A crash report was saved on this PC (" + Path.GetFileName(file) + "). It only holds the error, and nothing is sent anywhere.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 8, 0, 16) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var show = new Button { Content = "Show details", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            show.Click += (s, e) => { try { Process.Start("notepad.exe", "\"" + file + "\""); } catch { } };
            var open = new Button { Content = "Open folder", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            open.Click += (s, e) => { try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { } };
            var ok = new Button { Content = "OK", Style = (Style)FindResource("Accent"), MinWidth = 80 }; ok.Click += (s, e) => Close();
            row.Children.Add(show); row.Children.Add(open); row.Children.Add(ok); sp.Children.Add(row);
            Content = sp;
        }
    }
}
