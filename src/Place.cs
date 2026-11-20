// Hallow Overlay - where the panel goes (1.15): 9 spots on any screen, an edge offset, and a dragged spot saved as
// anchor + offset (a fraction of the screen) so it survives resolution and DPI changes. Screens are found by their
// monitor id (falls back to the device name, then the main screen), so re-ordering screens doesn't move the panel.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace HallowOverlay
{
    public class ScreenInfo
    {
        public string Device = "", Id = "", Model = "", Name = "";   // \\.\DISPLAY2, the monitor's interface id, its model, "Screen 2"
        public Rect Bounds;        // in the coordinates windows are placed in (the whole screen: games cover the taskbar)
        public double Scale = 1;   // pixels per 1/96 inch on this screen
        public bool Primary;
        public string Label { get { return Name + (Primary ? " (main)" : "") + (Model.Length > 0 && !Model.StartsWith("Generic", StringComparison.OrdinalIgnoreCase) ? " - " + Model : "") + " - " + Bounds.Width + " x " + Bounds.Height; } }
    }

    public static class Placement
    {
        public static readonly string[] Spots = { "tl", "tc", "tr", "ml", "mc", "mr", "bl", "bc", "br" };
        public const double DefaultMargin = 14;
        public static string Valid(string a) { return a != null && Spots.Contains(a) ? a : "tl"; }
        public static string SpotName(string a)
        {
            a = Valid(a); if (a == "mc") return "Centre";
            var v = a[0] == 't' ? "Top" : a[0] == 'b' ? "Bottom" : "Middle"; var h = a[1] == 'l' ? "left" : a[1] == 'r' ? "right" : "centre";
            return v + " " + h;
        }
        // 1.14's corner nearest to a spot (written too, so an older Overlay still finds its corner)
        public static string CornerOf(string a) { a = Valid(a); return (a[0] == 'b' ? "b" : "t") + (a[1] == 'r' ? "r" : "l"); }

        // The screen a placement asks for: by monitor id, else by device name (only when no id was saved), else the main screen.
        public static ScreenInfo Pick(IList<ScreenInfo> l, string id, string device, out bool found)
        {
            found = true;
            if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(device)) return MainScreen(l);
            ScreenInfo s = null;
            if (!string.IsNullOrEmpty(id)) s = l.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            else s = l.FirstOrDefault(x => string.Equals(x.Device, device, StringComparison.OrdinalIgnoreCase));
            if (s != null) return s;
            found = false; return MainScreen(l);
        }
        public static ScreenInfo MainScreen(IList<ScreenInfo> l)
        {
            return l.FirstOrDefault(x => x.Primary) ?? l.FirstOrDefault() ?? new ScreenInfo { Name = "Screen 1", Bounds = new Rect(0, 0, 1920, 1080), Primary = true };
        }
        // The screen a point is on (the nearest one when it's in a gap between screens).
        public static ScreenInfo At(IList<ScreenInfo> l, Point p)
        {
            var s = l.FirstOrDefault(x => x.Bounds.Contains(p)); if (s != null) return s;
            return l.OrderBy(x => Dist(x.Bounds, p)).FirstOrDefault() ?? MainScreen(l);
        }
        static double Dist(Rect r, Point p) { double dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right), dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom); return dx * dx + dy * dy; }

        static double AX(char c, Rect b, double w, double m) { return c == 'l' ? b.Left + m : c == 'r' ? b.Right - w - m : b.Left + (b.Width - w) / 2; }
        static double AY(char c, Rect b, double h, double m) { return c == 't' ? b.Top + m : c == 'b' ? b.Bottom - h - m : b.Top + (b.Height - h) / 2; }

        // Where a w x h panel goes (pixels): the spot, the edge offset (1/96 inch, scaled for the screen) and the dragged offset
        // (a fraction of the screen's width / height), kept fully on the screen.
        public static Rect Compute(string anchor, double marginDip, double offX, double offY, ScreenInfo s, double w, double h)
        {
            anchor = Valid(anchor); var b = s.Bounds; double m = Math.Max(0, marginDip) * s.Scale;
            var x = AX(anchor[1], b, w, m) + offX * b.Width; var y = AY(anchor[0], b, h, m) + offY * b.Height;
            return Clamp(new Rect(x, y, w, h), b);
        }
        public static Rect Clamp(Rect r, Rect b)
        {
            double x = r.Width >= b.Width ? b.Left : Math.Max(b.Left, Math.Min(r.X, b.Right - r.Width));
            double y = r.Height >= b.Height ? b.Top : Math.Max(b.Top, Math.Min(r.Y, b.Bottom - r.Height));
            return new Rect(x, y, r.Width, r.Height);
        }
        // A panel put somewhere by hand -> the nearest spot on each axis and the offset from it as a fraction of the screen.
        public static void FromRect(Rect r, ScreenInfo s, double marginDip, out string anchor, out double offX, out double offY)
        {
            var b = s.Bounds; double m = Math.Max(0, marginDip) * s.Scale;
            char h = 'l', v = 't'; double best = double.MaxValue;
            foreach (var c in "lcr") { var d = Math.Abs(r.X - AX(c, b, r.Width, m)); if (d < best - 1e-9) { best = d; h = c; } }
            best = double.MaxValue;
            foreach (var c in "tmb") { var d = Math.Abs(r.Y - AY(c, b, r.Height, m)); if (d < best - 1e-9) { best = d; v = c; } }
            anchor = v.ToString() + h;
            offX = Math.Round((r.X - AX(h, b, r.Width, m)) / b.Width, 6);
            offY = Math.Round((r.Y - AY(v, b, r.Height, m)) / b.Height, 6);
            if (Math.Abs(offX) < 1e-9) offX = 0; if (Math.Abs(offY) < 1e-9) offY = 0;
        }
        // Snapping while dragging: each axis jumps to a spot (with the edge offset), the screen's edge or its centre line when it's
        // within dist pixels. guideX / guideY say what it snapped to ("" = nothing).
        public static Rect SnapRect(Rect r, ScreenInfo s, double marginDip, double dist, out string guideX, out string guideY)
        {
            var b = s.Bounds; double m = Math.Max(0, marginDip) * s.Scale;
            var xs = new[] { Tuple.Create("left", AX('l', b, r.Width, m)), Tuple.Create("left edge", b.Left), Tuple.Create("centre", AX('c', b, r.Width, m)), Tuple.Create("right", AX('r', b, r.Width, m)), Tuple.Create("right edge", b.Right - r.Width) };
            var ys = new[] { Tuple.Create("top", AY('t', b, r.Height, m)), Tuple.Create("top edge", b.Top), Tuple.Create("middle", AY('m', b, r.Height, m)), Tuple.Create("bottom", AY('b', b, r.Height, m)), Tuple.Create("bottom edge", b.Bottom - r.Height) };
            double x = r.X, y = r.Y; guideX = guideY = "";
            var bx = xs.OrderBy(t => Math.Abs(t.Item2 - r.X)).First(); if (Math.Abs(bx.Item2 - r.X) <= dist) { x = bx.Item2; guideX = bx.Item1; }
            var by = ys.OrderBy(t => Math.Abs(t.Item2 - r.Y)).First(); if (Math.Abs(by.Item2 - r.Y) <= dist) { y = by.Item2; guideY = by.Item1; }
            return Clamp(new Rect(x, y, r.Width, r.Height), b);
        }
        // How a placement reads in the settings and status.
        public static string Describe(OverlaySettings p, ScreenInfo s, bool found)
        {
            var t = SpotName(p.Anchor);
            if (p.OffX != 0 || p.OffY != 0)
            {
                var parts = new List<string>();
                if (p.OffX != 0) parts.Add(Math.Abs(p.OffX * 100).ToString("0.#") + "% " + (p.OffX > 0 ? "right" : "left"));
                if (p.OffY != 0) parts.Add(Math.Abs(p.OffY * 100).ToString("0.#") + "% " + (p.OffY > 0 ? "down" : "up"));
                t += ", moved " + string.Join(" and ", parts);
            }
            t += ", " + p.Margin.ToString("0") + " px from the edge, on " + (s == null ? "the main screen" : s.Name + (s.Primary ? " (main)" : ""));
            if (!found) t += " - the saved screen isn't connected, so it's on the main one for now";
            return t;
        }
        // 1.14 -> 1.15: the corner becomes a spot with the same 14 px offset on the main screen, so the panel stays exactly where it was.
        // A game whose own look had a different corner keeps it as its own spot; the others use the usual spot.
        public static void Migrate(OverlaySettings s)
        {
            if (s.Anchor == null) { s.Anchor = Valid(s.Corner); s.OffX = s.OffY = 0; }
            if (s.PerGame != null)
                foreach (var g in s.PerGame.Values)
                {
                    if (g == null || g.Anchor != null) continue;
                    g.Anchor = Valid(g.Corner); g.OwnSpot = Valid(g.Corner) != s.Anchor || g.Margin != s.Margin; g.OffX = g.OffY = 0;
                }
        }
        // Copies the spot (spot, offsets, edge offset, screen) from one look to another.
        public static void CopySpot(OverlaySettings from, OverlaySettings to)
        {
            to.Anchor = from.Anchor; to.Corner = from.Corner; to.Margin = from.Margin; to.OffX = from.OffX; to.OffY = from.OffY; to.Screen = from.Screen; to.ScreenId = from.ScreenId; to.ScreenName = from.ScreenName;
        }
        public static void Reset(OverlaySettings p) { p.Anchor = "tl"; p.Corner = "tl"; p.Margin = DefaultMargin; p.OffX = p.OffY = 0; p.Screen = ""; p.ScreenId = ""; p.ScreenName = ""; }
    }

    // The screens Windows has now (read again every time - cheap - so plugging one in or out is picked up at once).
    public static class Monitors
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        delegate bool EnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFOEX mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string dev, uint i, ref DISPLAY_DEVICE dd, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr h, int type, out uint x, out uint y);
        [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int i);

        // Per-monitor aware: every screen's own DPI. Otherwise (WPF's default, system aware) Windows scales the other screens
        // for us and every screen uses the system DPI in these coordinates.
        static bool PerMonitor() { try { return GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) == 2; } catch { return false; } }
        public static double SystemScale() { var dc = GetDC(IntPtr.Zero); try { var d = GetDeviceCaps(dc, 88); return d > 0 ? d / 96.0 : 1; } finally { ReleaseDC(IntPtr.Zero, dc); } }

        public static List<ScreenInfo> All()
        {
            var l = new List<ScreenInfo>();
            try
            {
                bool pm = PerMonitor(); double sys = SystemScale();
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) =>
                {
                    var mi = new MONITORINFOEX(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                    if (!GetMonitorInfo(h, ref mi)) return true;
                    var s = new ScreenInfo { Device = mi.szDevice ?? "", Primary = (mi.dwFlags & 1) != 0, Scale = sys,
                        Bounds = new Rect(mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right - mi.rcMonitor.left, mi.rcMonitor.bottom - mi.rcMonitor.top) };
                    if (pm) { uint dx, dy; try { if (GetDpiForMonitor(h, 0, out dx, out dy) == 0 && dx > 0) s.Scale = dx / 96.0; } catch { } }
                    var dd = new DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    try { if (EnumDisplayDevices(s.Device, 0, ref dd, 1)) { s.Id = dd.DeviceID ?? ""; s.Model = dd.DeviceString ?? ""; } } catch { }
                    l.Add(s); return true;
                }, IntPtr.Zero);
            }
            catch { }
            Name(l); return l;
        }
        // "Screen 2" from \\.\DISPLAY2 (the number Windows' display settings use), else by position.
        public static void Name(List<ScreenInfo> l)
        {
            int i = 0;
            foreach (var s in l.OrderBy(x => x.Bounds.Left).ThenBy(x => x.Bounds.Top))
            {
                i++; var digits = new string((s.Device ?? "").Where(char.IsDigit).ToArray());
                if (string.IsNullOrEmpty(s.Name)) s.Name = "Screen " + (digits.Length > 0 && digits.Length < 4 ? digits : i.ToString());
            }
        }
    }
}
