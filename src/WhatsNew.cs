// Hallow Overlay - "What's new": shown once after an update, with the versions you skipped.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowOverlay
{
    public static class WhatsNew
    {
        // Newest first. One or two short lines per version.
        public static readonly Tuple<string, string[]>[] Notes = {
            Tuple.Create("1.10", new[] { "Hallow Remote's Live stats tab now shows your FPS, ping and the server's region while you play (never the server's IP)" }),
            Tuple.Create("1.9", new[] { "GPU clocks, power draw (W and % of the limit) and VRAM, for NVIDIA cards (Show > GPU clocks / GPU power / VRAM)", "A frame-time graph of the last 5 seconds with stutters in red (Show > Frame-time graph; Small / Medium / Large)" }),
            Tuple.Create("1.8", new[] { "Uses the Hallow accent colour you pick in Hallow Toolkit (Settings > Use the Hallow accent)", "If Hallow Overlay ever crashes, a report is kept on this PC and you're told next time - nothing is sent anywhere" }),
            Tuple.Create("1.7", new[] { "Match summary: when a game closes, a one-line summary shows on the panel (FPS, ping, stalls)", "Match history (settings or the tray menu): every session with its details, and a Servers view of the regions you got" }),
            Tuple.Create("1.6", new[] { "One Hallow icon: with Hallow Hub running, Overlay's menu lives in Hub's tray icon and Overlay's own icon hides (Settings > Use the Hallow icon)" }),
            Tuple.Create("1.5", new[] { "Game server: the server your game is talking to, its region, ping, loss and stalls (Show > Game server)", "\"Server changed\" when the game moves you, and the game's up / down traffic" }),
            Tuple.Create("1.4", new[] { "Now playing from Hallow Mixer, and MIC MUTED shows up the moment you mute (Mixer 3.14 or newer)", "\"Clip saved\" from Hallow Clips and Hallow Hub's break reminder / daily limit show up here, not as a pop-up" }),
            Tuple.Create("1.3", new[] { "Settings for: give a game its own corner, size and stats", "Only when something's wrong: stats show up only while they're in trouble (you set the limits)", "Packet loss and jitter next to ping" }),
            Tuple.Create("1.2", new[] { "FPS and 1% lows for the game you're playing, read from Windows' own frame events (like PresentMon) - no admin prompt" }),
            Tuple.Create("1.1", new[] { "Tells you when a game is in exclusive fullscreen (Windows can't draw over that - use borderless)", "Other Hallow apps can show and hide the overlay and put short messages on it" }),
        };

        static int Cmp(string a, string b) { Version x, y; return Version.TryParse(a, out x) && Version.TryParse(b, out y) ? x.CompareTo(y) : string.CompareOrdinal(a, b); }

        // The versions newer than `seen` (at most 6). Empty for a fresh install or no update.
        public static List<Tuple<string, string[]>> Since(string seen)
        {
            if (string.IsNullOrEmpty(seen)) return new List<Tuple<string, string[]>>();
            return Notes.Where(n => Cmp(n.Item1, seen) > 0 && Cmp(n.Item1, Program.Version) <= 0).Take(6).ToList();
        }
    }

    public class WhatsNewWindow : Window
    {
        static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        public WhatsNewWindow(List<Tuple<string, string[]>> items)
        {
            Title = "What's new in Hallow Overlay"; Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text");
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(new TextBlock { Text = "What's new in Hallow Overlay " + Program.Version, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Accent.Brush(), Margin = new Thickness(0, 0, 0, 12) });
            foreach (var v in items)
            {
                sp.Children.Add(new TextBlock { Text = v.Item1, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), Margin = new Thickness(0, 6, 0, 3) });
                foreach (var line in v.Item2)
                {
                    var d = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var dot = new TextBlock { Text = "•", Foreground = Accent.Brush(), Width = 16, FontWeight = FontWeights.Bold }; DockPanel.SetDock(dot, Dock.Left);
                    d.Children.Add(dot); d.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
                    sp.Children.Add(d);
                }
            }
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var all = new Button { Content = "All release notes", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            all.Click += (s, e) => { try { Process.Start(new ProcessStartInfo("https://github.com/Dom428/hallow-overlay/releases") { UseShellExecute = true }); } catch { } };
            var ok = new Button { Content = "Got it", Style = (Style)FindResource("Accent"), MinWidth = 90 };
            ok.Click += (s, e) => Close();
            foot.Children.Add(all); foot.Children.Add(ok); sp.Children.Add(foot);
            Content = sp;
        }
    }
}
