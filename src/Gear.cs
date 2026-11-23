// Hallow Overlay 1.22 / 1.23 - Hallow Gear's batteries on the panel. Reads %APPDATA%\HallowGear\status.json (written by Hallow Gear 1.0+)
// at most every 10 s (only when the file changed). Nothing is sent to Gear. A missing, old, broken or stale file = no chips.
//   chips: one compact chip per device with a battery - "always", "low" (default: only while it's at / under Gear's low level,
//   20% unless changed there, and not charging) or "off" (Settings > Gear battery).
//   the held alert: while a game runs Hallow Gear doesn't pop up; its low-battery alert ("alert" in its status) shows here as a small line.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace HallowOverlay
{
    // Level: 0 fine, 1 low (amber), 2 very low (red). 1.24: Wired (on a cable, not charging), Estimated (Gear guessed it from the voltage)
    public class GearChip { public string Kind, Name; public int Pct; public bool Charging, Full, Wired, Estimated; public int Level; }
    public class GearStatus
    {
        public bool Running; public DateTime Updated; public int LowAt = 20, VeryLowAt = 10; public string Alert, AlertLevel;
        public List<GearChip> Devices = new List<GearChip>();
    }
    public static class GearLink
    {
        public static string Dir { get { var e = Environment.GetEnvironmentVariable("HALLOW_GEAR_DIR"); return string.IsNullOrEmpty(e) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowGear") : e; } }
        static DateTime readAt = DateTime.MinValue, fileTime = DateTime.MinValue; static GearStatus cached;

        // the status as of now (cached; the file is looked at every 10 s at most); null = no Gear (or it isn't running / is stale)
        public static GearStatus Read(DateTime now)
        {
            if (Snap.On) return SnapStatus;
            if ((now - readAt).TotalSeconds >= 10)
            {
                readAt = now;
                try
                {
                    var f = Path.Combine(Dir, "status.json");
                    if (!File.Exists(f)) { cached = null; fileTime = DateTime.MinValue; }
                    else { var t = File.GetLastWriteTimeUtc(f); if (t != fileTime) { fileTime = t; cached = Parse(File.ReadAllText(f)); } }
                }
                catch { cached = null; }
            }
            return Usable(cached, now) ? cached : null;
        }
        public static void Forget() { readAt = DateTime.MinValue; fileTime = DateTime.MinValue; cached = null; }
        public static GearStatus SnapStatus;   // --snap: made-up gear (never the real status)
        // "running": false = Gear quit. Gear rewrites its status whenever it reads (every minute or two); when the file is older than 15
        // minutes, it still counts while Gear's pipe is there (Gear running quietly through a long game), and not after a crash.
        public static Func<bool> GearRunning = () => PipeThere("HallowFamily.Gear." + Environment.UserName);
        static DateTime pipeAt = DateTime.MinValue; static bool pipeOk;
        public static bool Usable(GearStatus g, DateTime now)
        {
            if (g == null || !g.Running) return false;
            if (g.Updated == DateTime.MinValue || (now - g.Updated).TotalMinutes <= 15) return true;
            if ((now - pipeAt).TotalSeconds >= 10) { pipeAt = now; try { pipeOk = GearRunning(); } catch { pipeOk = false; } }
            return pipeOk;
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)] static extern bool WaitNamedPipe(string name, int timeout);
        static bool PipeThere(string name) { return WaitNamedPipe(@"\\.\pipe\" + name, 1) || System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 2; }

        // 1.24: a number, or a text like "45%" / "45 %" / "45" (older or hand-written files)
        static int? Int(object o)
        {
            if (o is int) return (int)o; if (o is long) return (int)(long)o; if (o is decimal) return (int)Math.Round((decimal)o); if (o is double) return (int)Math.Round((double)o);
            var s = o as string; int v;
            if (s != null && int.TryParse(s.Replace("%", "").Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }
        static bool Bool(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is bool && (bool)v; }
        static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v as string : null; }
        // tolerant: missing fields are fine, anything broken gives null
        public static GearStatus Parse(string json)
        {
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json ?? "");
                if (d == null || !string.Equals(Str(d, "app"), "Gear", StringComparison.OrdinalIgnoreCase)) return null;
                var g = new GearStatus { Running = !d.ContainsKey("running") || Bool(d, "running") };
                DateTime t; if (DateTime.TryParse(Str(d, "updated") ?? "", out t)) g.Updated = t;
                object v;
                if (d.TryGetValue("lowAt", out v) && Int(v).HasValue) g.LowAt = Math.Max(1, Math.Min(90, Int(v).Value));
                if (d.TryGetValue("veryLowAt", out v) && Int(v).HasValue) g.VeryLowAt = Math.Max(0, Math.Min(g.LowAt, Int(v).Value));
                var al = d.TryGetValue("alert", out v) ? v as Dictionary<string, object> : null;
                if (al != null) { g.Alert = Str(al, "text"); g.AlertLevel = Str(al, "level"); if (string.IsNullOrWhiteSpace(g.Alert)) g.Alert = null; }
                if (d.TryGetValue("devices", out v) && v is System.Collections.IEnumerable && !(v is string))
                    foreach (var o in (System.Collections.IEnumerable)v)
                    {
                        var x = o as Dictionary<string, object>; if (x == null) continue;
                        object b; var pct = x.TryGetValue("battery", out b) ? Int(b) : null;
                        if (!pct.HasValue || pct < 0 || pct > 100) continue;                                            // a keyboard, or not known yet
                        if (x.ContainsKey("connected") && !Bool(x, "connected")) continue; if (Bool(x, "asleep")) continue;  // nothing current to show
                        // 1.24: Gear 1.1+ also says "state" (on-battery, charging, full, wired, asleep, not-connected); the yes/no fields win when both are there
                        var state = (Str(x, "state") ?? "").ToLowerInvariant();
                        if (state == "asleep" || state == "not-connected") continue;
                        var c = new GearChip { Kind = (Str(x, "kind") ?? "device").ToLowerInvariant(), Name = Str(x, "name") ?? "", Pct = pct.Value,
                            Charging = x.ContainsKey("charging") ? Bool(x, "charging") : state == "charging", Full = x.ContainsKey("full") ? Bool(x, "full") : state == "full",
                            Wired = x.ContainsKey("wired") ? Bool(x, "wired") : state == "wired", Estimated = Bool(x, "estimated") };
                        c.Level = c.Charging || c.Full || c.Wired ? 0 : c.Pct <= g.VeryLowAt ? 2 : c.Pct <= g.LowAt ? 1 : 0;
                        g.Devices.Add(c);
                    }
                return g;
            }
            catch { return null; }
        }
        // which chips to show for the setting: "always", "low" (only low ones), "off"
        public static List<GearChip> Chips(GearStatus g, string mode)
        {
            if (g == null || mode == "off") return new List<GearChip>();
            return g.Devices.Where(c => mode == "always" || c.Level > 0).ToList();
        }
        // 1.23: earbuds and controllers (Bluetooth, from Gear 1.1) get their own icons - the same glyphs as Hallow Gear 1.2's cards and notifications
        public static string Icon(string kind) { return kind == "headset" ? "headphones" : kind == "mouse" ? "computer-mouse" : kind == "keyboard" ? "keyboard" : kind == "earbuds" ? "earbuds" : kind == "controller" ? "gamepad" : "battery-half"; }
        public static string Mode(string s) { s = (s ?? "").Trim().ToLowerInvariant(); return s == "always" || s == "off" ? s : "low"; }
        // 1.24: the chip text - "45%" (never "45 %"), "~45%" when Gear estimated it
        public static string Text(GearChip c) { return c == null ? "" : (c.Estimated ? "~" : "") + Math.Max(0, Math.Min(100, c.Pct)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"; }
        // the small mark after the number: a bolt while charging / full, a plug on a cable
        public static string Mark(GearChip c) { return c == null ? null : c.Charging || c.Full ? "bolt" : c.Wired ? "plug" : null; }
        // the game hold: Gear holds its low-battery pop-up while a game runs and it shows here instead - only then
        public static string HeldAlert(GearStatus g, bool inGame) { return inGame && g != null && !string.IsNullOrWhiteSpace(g.Alert) ? g.Alert.Trim() : null; }
        public static int HeldLevel(GearStatus g) { return g != null && string.Equals(g.AlertLevel, "critical", StringComparison.OrdinalIgnoreCase) ? 2 : 1; }
    }
}
