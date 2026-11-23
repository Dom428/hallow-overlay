// Hallow Overlay tests - position memory: the 9 spots, edge offset, dragged offsets, DPI, screens by monitor id, unplugging a screen,
// the 1.19 anchor-width bug (a free spot measured at the move-mode width came back elsewhere at the normal width), and saving.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace HallowOverlay.Tests
{
    public static partial class Run
    {
        static ScreenInfo Scr(string id, string dev, double x, double y, double w, double h, double scale = 1, bool primary = false, string name = null)
        { return new ScreenInfo { Id = id, Device = dev, Bounds = new Rect(x, y, w, h), Scale = scale, Primary = primary, Name = name ?? "" }; }
        static bool Near(Rect a, Rect b) { return Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01 && Math.Abs(a.Width - b.Width) < 0.01 && Math.Abs(a.Height - b.Height) < 0.01; }
        static string R(Rect r) { return r.X + "," + r.Y + " " + r.Width + "x" + r.Height; }

        static void Position()
        {
            Eq("tl", Placement.Valid("zz"), "an unknown spot -> top left"); Eq("tl", Placement.Valid(null), "no spot -> top left"); Eq("br", Placement.Valid("br"), "br stays br");
            Eq("Centre", Placement.SpotName("mc"), "mc reads Centre"); Eq("Bottom right", Placement.SpotName("br"), "br reads Bottom right"); Eq("Middle left", Placement.SpotName("ml"), "ml reads Middle left");
            Ok(Placement.CornerOf("tc") == "tl" && Placement.CornerOf("mr") == "tr" && Placement.CornerOf("bc") == "bl" && Placement.CornerOf("br") == "br", "the 1.14 corner written for older versions");

            var s = Scr("A", @"\\.\DISPLAY1", 0, 0, 1920, 1080, 1, true);
            Ok(Near(Placement.Compute("tl", 14, 0, 0, s, 300, 40), new Rect(14, 14, 300, 40)), "top left, 14 px in");
            Ok(Near(Placement.Compute("br", 14, 0, 0, s, 300, 40), new Rect(1920 - 314, 1080 - 54, 300, 40)), "bottom right, 14 px in");
            Ok(Near(Placement.Compute("mc", 14, 0, 0, s, 300, 40), new Rect(810, 520, 300, 40)), "centre is centred (the edge offset doesn't apply)");
            Ok(Near(Placement.Compute("tc", 30, 0, 0, s, 300, 40), new Rect(810, 30, 300, 40)), "top centre, 30 px down");
            Ok(Near(Placement.Compute("tl", 14, 0.1, 0.05, s, 300, 40), new Rect(14 + 192, 14 + 54, 300, 40)), "a dragged offset is a fraction of the screen (10% right, 5% down)");
            Ok(Near(Placement.Compute("br", 14, 0.5, 0.5, s, 300, 40), new Rect(1620, 1040, 300, 40)), "never off the screen (clamped)");
            Ok(Near(Placement.Compute("tl", -5, 0, 0, s, 300, 40), new Rect(0, 0, 300, 40)), "a negative edge offset counts as 0");
            Ok(Near(Placement.Compute("tr", 14, 0, 0, s, 2500, 40), new Rect(0, 14, 2500, 40)), "wider than the screen: it starts at the left edge");
            var hi = Scr("A", "", 0, 0, 3840, 2160, 1.5, true);
            Ok(Near(Placement.Compute("tr", 14, 0, 0, hi, 300, 40), new Rect(3840 - 300 - 21, 21, 300, 40)), "150% screen: the 14 px edge offset is 21 pixels");
            Ok(Near(Placement.Compute("bl", 14, 0, 0, Scr("A", "", 0, 0, 2560, 1440, 1.25), 300, 40), new Rect(17.5, 1440 - 40 - 17.5, 300, 40)), "125% screen: 17.5 pixels");
            // the same fractions on another resolution land in the same relative place
            var a1 = Placement.Compute("tl", 0, 0.25, 0.25, s, 100, 20); var a2 = Placement.Compute("tl", 0, 0.25, 0.25, hi, 100, 20);
            Ok(Math.Abs(a1.X / 1920 - a2.X / 3840) < 1e-9 && Math.Abs(a1.Y / 1080 - a2.Y / 2160) < 1e-9, "a dragged spot survives a resolution change (same fraction of the screen)");

            // every spot, put there exactly, reads back as that spot with no offset
            foreach (var sp in Placement.Spots)
            {
                var r = Placement.Compute(sp, 14, 0, 0, s, 420, 36); string an; double ox, oy;
                Placement.FromRect(r, s, 14, out an, out ox, out oy);
                Ok(an == sp && ox == 0 && oy == 0, "on the " + sp + " spot -> " + an + " " + ox + "," + oy);
            }
            // free spots round-trip at the same size
            var rnd = new Random(5); int okTrips = 0;
            for (int i = 0; i < 200; i++)
            {
                double w = 150 + rnd.Next(500), h = 30 + rnd.Next(200); var r = new Rect(rnd.NextDouble() * (1920 - w), rnd.NextDouble() * (1080 - h), w, h);
                string an; double ox, oy; Placement.FromRect(r, s, 14, out an, out ox, out oy);
                var c = Placement.Compute(an, 14, ox, oy, s, w, h); if (Math.Abs(c.X - r.X) < 0.01 && Math.Abs(c.Y - r.Y) < 0.01) okTrips++;
            }
            Eq(200, okTrips, "200 random free spots come back where they were put");

            // 1.19 regression: moved while the panel was wide (move mode shows every stat), shown later narrower
            string a; double dx, dy;
            Placement.FromRect(new Rect(1250, 300, 600, 40), s, 14, out a, out dx, out dy);
            var back = Placement.Compute(a, 14, dx, dy, s, 300, 40);
            Ok(a[1] == 'r' && Math.Abs(back.Right - 1850) < 0.02, "1.19: near the right edge, the right edge stays put when it gets narrower (" + a + ", right " + back.Right + ")");
            Placement.FromRect(new Rect(300, 300, 600, 40), s, 14, out a, out dx, out dy);
            back = Placement.Compute(a, 14, dx, dy, s, 300, 40);
            Ok(a[1] == 'l' && Math.Abs(back.X - 300) < 0.02, "1.19: near the left edge, the left edge stays put (" + a + ")");
            Placement.FromRect(new Rect(700, 300, 600, 40), s, 14, out a, out dx, out dy);
            back = Placement.Compute(a, 14, dx, dy, s, 300, 40);
            Ok(a[1] != 'c', "1.19: a free spot near the middle never hangs off the centre by its middle (" + a + ")");
            Ok(Math.Abs(back.Right - 1300) < 0.02, "1.19: ...it keeps the edge nearest a screen edge (right gap 620 < left 700: right edge 1300)");
            Placement.FromRect(new Rect(100, 980, 300, 80), s, 14, out a, out dx, out dy);
            back = Placement.Compute(a, 14, dx, dy, s, 300, 40);
            Ok(a[0] == 'b' && Math.Abs(back.Bottom - 1060) < 0.02, "1.19: near the bottom, the bottom edge stays put when it gets shorter");
            var cw = Placement.Compute("tc", 14, 0, 0, s, 600, 40); Placement.FromRect(cw, s, 14, out a, out dx, out dy);
            back = Placement.Compute(a, 14, dx, dy, s, 300, 40);
            Ok(a == "tc" && Math.Abs(back.X - 810) < 0.01, "snapped to top centre: stays centred at any width");

            // snapping while dragging
            string gx, gy; var sn = Placement.SnapRect(new Rect(806, 17, 300, 40), s, 14, 8, out gx, out gy);
            Ok(sn.X == 810 && sn.Y == 14 && gx == "centre" && gy == "top", "within 8 px: snaps to top / centre (" + gx + " / " + gy + ")");
            sn = Placement.SnapRect(new Rect(500, 400, 300, 40), s, 14, 8, out gx, out gy);
            Ok(sn.X == 500 && sn.Y == 400 && gx == "" && gy == "", "far from everything: no snap");
            sn = Placement.SnapRect(new Rect(1618, 3, 300, 40), s, 14, 8, out gx, out gy);
            Ok(sn.X == 1620 && sn.Y == 0 && gx == "right edge" && gy == "top edge", "snaps to the screen's edges too");

            // copying and resetting
            var p = new OverlaySettings { Anchor = "br", Margin = 30, OffX = 0.1, OffY = -0.1, Screen = @"\\.\DISPLAY2", ScreenId = "B", ScreenName = "Screen 2" };
            var q = new OverlaySettings(); Placement.CopySpot(p, q);
            Ok(q.Anchor == "br" && q.Margin == 30 && q.OffX == 0.1 && q.OffY == -0.1 && q.ScreenId == "B" && q.Screen == @"\\.\DISPLAY2", "CopySpot copies spot, offsets, edge and screen");
            Placement.Reset(q); Ok(q.Anchor == "tl" && q.Margin == 14 && q.OffX == 0 && q.ScreenId == "", "Reset: top left on the main screen");
            var u = new OverlaySettings(); u.PerGame["Delta Force"] = new OverlaySettings { Anchor = "bc", OwnSpot = false, Row = false };
            u.Anchor = "tr"; var lf = u.LookFor("Delta Force");
            Ok(lf.Anchor == "tr" && !lf.Row, "a game's own look without its own spot uses your usual spot (and its own look)");
        }

        static void Monitors()
        {
            var A = Scr("MON-A", @"\\.\DISPLAY1", 0, 0, 2560, 1440, 1.25, true, "Screen 1");
            var B = Scr("MON-B", @"\\.\DISPLAY2", -1920, 0, 1920, 1080, 1, false, "Screen 2");
            var both = new List<ScreenInfo> { A, B }; bool found;
            Ok(Placement.Pick(both, "mon-b", @"\\.\DISPLAY1", out found) == B && found, "found by monitor id (case-insensitive), even when the device name moved");
            Ok(Placement.Pick(both, "", @"\\.\DISPLAY2", out found) == B && found, "no id saved (older file): by device name");
            Ok(Placement.Pick(both, "", "", out found) == A && found, "nothing saved: the main screen");
            Ok(Placement.Pick(new List<ScreenInfo> { A }, "MON-B", @"\\.\DISPLAY2", out found) == A && !found, "the saved screen unplugged: the main one for now");
            Ok(Placement.Pick(new List<ScreenInfo> { B, A }, "MON-B", "", out found) == B && found, "plugged back in (another order): back on it");
            Ok(Placement.Pick(both, "MON-Z", @"\\.\DISPLAY2", out found) == A && !found, "an id that isn't there: never guesses by device name");
            var r = Placement.Compute("tl", 14, 0, 0, B, 300, 40);
            Ok(Near(r, new Rect(-1906, 14, 300, 40)), "a screen left of the main one (negative x): " + R(r));
            r = Placement.Compute("br", 14, 0, 0, B, 300, 40); Ok(r.Right <= 0 && r.X >= -1920, "...and its bottom right stays on it");
            Ok(Placement.At(both, new Point(-100, 500)) == B && Placement.At(both, new Point(100, 500)) == A, "the screen a point is on");
            Ok(Placement.At(both, new Point(-1500, 1300)) == B && Placement.At(both, new Point(-100, 1300)) == A, "a point in the gap below screen 2 -> the nearest screen");
            var none = Placement.MainScreen(new List<ScreenInfo>());
            Ok(none.Primary && none.Bounds.Width == 1920, "no screens at all: a made-up 1920 x 1080 main screen");
            Ok(Placement.MainScreen(new List<ScreenInfo> { B }) == B, "no main flag: the first screen");
            var named = new List<ScreenInfo> { Scr("x", @"\\.\DISPLAY3", 0, 0, 100, 100), Scr("y", "odd", -100, 0, 100, 100), Scr("z", @"\\.\DISPLAY12345", 200, 0, 100, 100) };
            HallowOverlay.Monitors.Name(named);
            Ok(named[0].Name == "Screen 3" && named[1].Name == "Screen 1" && named[2].Name == "Screen 3", "screen names: the number Windows uses, else by position (" + string.Join(", ", named.Select(n => n.Name)) + ")");
            var p = new OverlaySettings { Anchor = "tr", OffX = 0.05, OffY = -0.02, Margin = 14 };
            Eq("Top right, moved 5% right and 2% up, 14 px from the edge, on Screen 1 (main)", Placement.Describe(p, A, true), "Describe a moved spot");
            Ok(Placement.Describe(new OverlaySettings(), B, false).EndsWith("the saved screen isn't connected, so it's on the main one for now"), "Describe says when the saved screen is missing");
            Ok(Placement.Describe(new OverlaySettings(), null, true).Contains("the main screen"), "Describe without a screen");
            Ok(A.Label.StartsWith("Screen 1 (main)") && A.Label.EndsWith("2560 x 1440"), "screen label: " + A.Label);
            // the test copy's made-up screens file (HALLOW_OVERLAY_SCREENS)
            var f = Path.Combine(Dir, "screens.txt"); File.WriteAllLines(f, new[] { "T-1|\\\\.\\DISPLAY7|-40000|-40000|1920|1080|1.5|1", "T-2|\\\\.\\DISPLAY8|-38080|-40000|1280|1024|1|0", "broken line" });
            Environment.SetEnvironmentVariable("HALLOW_OVERLAY_SCREENS", f);
            try
            {
                var l = HallowOverlay.Monitors.All();
                Ok(l.Count == 2 && l[0].Id == "T-1" && l[0].Scale == 1.5 && l[0].Primary && l[1].Bounds.Width == 1280 && l[0].Name == "Screen 7", "made-up screens for test copies (far off any real one)");
            }
            finally { Environment.SetEnvironmentVariable("HALLOW_OVERLAY_SCREENS", null); }
        }

        static void PositionFiles()
        {
            var f = Path.Combine(Dir, "pos", "overlay.json");
            var s = new OverlaySettings { Anchor = "mr", OffX = -0.0125, OffY = 0.05, Margin = 20, Screen = @"\\.\DISPLAY2", ScreenId = "MON-B", ScreenName = "Screen 2" };
            s.PerGame["Delta Force"] = new OverlaySettings { Anchor = "bc", OwnSpot = true, OffY = -0.02 };
            Ok(s.Save(f), "saves (the folder is made)");
            Ok(!File.Exists(f + ".tmp"), "no .tmp file is left behind");
            var t = OverlaySettings.Load(f);
            Ok(t.Anchor == "mr" && t.OffX == -0.0125 && t.OffY == 0.05 && t.Margin == 20 && t.ScreenId == "MON-B" && t.Screen == @"\\.\DISPLAY2", "the spot comes back exactly");
            Ok(t.PerGame.Count == 1 && t.PerGame["Delta Force"].OwnSpot && t.PerGame["Delta Force"].Anchor == "bc" && t.PerGame["Delta Force"].OffY == -0.02, "a game's own spot comes back");
            t.OffX = 0.2; Ok(t.Save(f) && OverlaySettings.Load(f).OffX == 0.2, "saving again replaces it");
            var json = File.ReadAllText(f); Ok(json.Contains("\"Corner\":\"tr\"") || json.Contains("\"Corner\":\"tl\""), "the 1.14 corner is still written for older versions");
            OverlaySettings.Frozen = true;
            try { var before = File.ReadAllText(f); var z = OverlaySettings.Load(f); z.Anchor = "bl"; Ok(z.Save() && File.ReadAllText(f) == before, "frozen (--snap): Save never writes"); }
            finally { OverlaySettings.Frozen = false; }
            Ok(OverlaySettings.FilePath.StartsWith(Path.Combine(Dir, "overlay")), "with HALLOW_OVERLAY_DIR the real overlay.json is never used");
            var locked = Path.Combine(Dir, "pos", "locked.json"); File.WriteAllText(locked, "{}");
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)) Ok(!new OverlaySettings().Save(locked), "a file that can't be written: Save says so (after one retry)");
            Ok(File.ReadAllText(locked) == "{}", "...and the old file is untouched");
        }
    }
}
