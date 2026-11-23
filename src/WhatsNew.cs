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
            Tuple.Create("1.23.1", new[] { "Percentages read 45% instead of 45 %" }),
            Tuple.Create("1.23", new[] { "Gear battery chips: Bluetooth earbuds and controllers have their own icons now (they showed a plain battery)" }),
            Tuple.Create("1.22", new[] { "Battery chips from Hallow Gear: your mouse and headset's battery on the panel while you play - only when one is low (the default), always, or off (Settings > Gear battery)", "Hallow Gear doesn't pop up during a game: its low-battery alert shows on the panel as a quiet line instead" }),
            Tuple.Create("1.21", new[] { "Light mode readability fixes: buttons and selected options in your accent colour always have readable text (a violet accent had dark text that was a little too faint), and the sliders use the darker accent so they stand out" }),
            Tuple.Create("1.20", new[] { "Light and dark mode for the settings windows: they follow the Light / Dark / Same as Windows choice in Hallow Toolkit, live", "Your own choice in Settings > Look: Follow Hallow, Light or Dark", "The in-game panel stays as it is - see-through and dark over your games" }),
            Tuple.Create("1.19", new[] { "The overlay remembers where you put it: after a restart or in a game it comes back to the same place (a panel put somewhere free keeps the edges nearest the screen's edges where you put them)", "Every move is saved straight away - a drag, the arrow keys, Done / Enter, and also when move mode ends by itself in the middle of a drag", "After a move it says where it went: \"Saved for <game>\" (a game with its own spot) or \"Saved as your usual spot\"" }),
            Tuple.Create("1.18", new[] { "Now playing works without Hallow Mixer: the song or video comes straight from Windows (Spotify, YouTube in a browser, any media app) - with Mixer running it still comes from Mixer" }),
            Tuple.Create("1.17", new[] { "\"Move it\" right after Overlay starts (before any game) works now - the panel could stay click-through, so it couldn't be dragged" }),
            Tuple.Create("1.16", new[] { "Dragging the panel in \"Move it\" mode works reliably now - and you can drag the \"Drag me\" card too", "After you finish moving, the panel stays in view for a moment so you see where it went" }),
            Tuple.Create("1.15", new[] { "Put it anywhere: 9 spots (top / middle / bottom, left / centre / right), any screen, and an edge offset slider", "Move it: drag the panel where you want it (it snaps to the spots and the screen's edges and centre lines; arrow keys nudge) - from the settings, the tray menu or an optional hotkey", "A game can have its own spot, or use your usual one" }),
            Tuple.Create("1.14", new[] { "Your own hotkeys: change any of them in the settings (click the box, press the keys), turn one off, or reset to the defaults", "If another app already owns a combo you're told so and the old one keeps working; new optional hotkeys for \"Only when something's wrong\" and opening the settings" }),
            Tuple.Create("1.13", new[] { "Layout editor: put the stats in your order, give them your own labels and colours, add a small graph of the last minute, and pick the text size" }),
            Tuple.Create("1.12", new[] { "Benchmark: Ctrl+Shift+F9 starts and stops it - average FPS, 1% and 0.1% lows, frame-time percentiles and a chart (PNG + CSV)", "Optional CSV log of every stat while you play" }),
            Tuple.Create("1.11", new[] { "Updates itself: it looks once a day and asks before installing (checked against GitHub's checksum)", "FPS for Vulkan and OpenGL games too, and an optional 0.1% low" }),
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
        static SolidColorBrush B(string hex) { return Pal.B(hex); }   // 1.20: light mode swaps the colours
        public WhatsNewWindow(List<Tuple<string, string[]>> items)
        {
            Title = "What's new in Hallow Overlay"; Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Text");
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5; WindowStartupLocation = WindowStartupLocation.CenterOwner; Look.Track(this);
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
            sp.Children.Add(new TextBlock { Text = "What's new in Hallow Overlay " + Program.Version, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Accent.Ink(), Margin = new Thickness(0, 0, 0, 12) });
            foreach (var v in items)
            {
                sp.Children.Add(new TextBlock { Text = v.Item1, FontWeight = FontWeights.Bold, Foreground = B("#A98BE0"), Margin = new Thickness(0, 6, 0, 3) });
                foreach (var line in v.Item2)
                {
                    var d = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var dot = new TextBlock { Text = "•", Foreground = Accent.Ink(), Width = 16, FontWeight = FontWeights.Bold }; DockPanel.SetDock(dot, Dock.Left);
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
