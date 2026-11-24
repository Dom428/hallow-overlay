// Hallow Overlay unit tests (1.24). Nothing is ever shown on screen: the panel is built in memory and read back, settings and
// status files live in a temp folder (HALLOW_OVERLAY_DIR, HALLOW_GEAR_DIR, HALLOW_MIXER_DIR, HALLOW_CLIPS_DIR, HALLOW_THEME_FILE), and
// the "link" suite talks to a test copy of HallowOverlay.exe (its own data folder, pipe and mutex; started with --tray, no game, no
// tray icon, no hotkeys, no updates, no FPS / network tracing, ping to 127.0.0.1) that is told to quit at the end.
//   tests\OverlayTests.exe            every suite
//   tests\OverlayTests.exe unit       in-process only        tests\OverlayTests.exe link     the test copy only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace HallowOverlay.Tests
{
    public static partial class Run
    {
        static int pass, fail;
        static readonly List<string> failed = new List<string>();
        static void Ok(bool c, string name) { if (c) { pass++; Console.WriteLine("ok   " + name); } else { fail++; failed.Add(name); Console.WriteLine("FAIL " + name); } }
        static void Eq(object want, object got, string name) { Ok(Equals(want, got), name + (Equals(want, got) ? "" : "  (want [" + want + "], got [" + got + "])")); }
        static string Dir, Fixtures;
        static string Fx(string name) { return File.ReadAllText(Path.Combine(Fixtures, name)); }
        static void Suite(string name, Action a)
        {
            Console.WriteLine("--- " + name);
            try { a(); } catch (Exception e) { fail++; failed.Add(name + ": exception"); Console.WriteLine("FAIL " + name + " threw: " + e); }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            Dir = Path.Combine(Path.GetTempPath(), "HallowOverlayTest" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(Dir);
            // everything this process (and the test copy it starts) reads or writes goes to the temp folder
            Environment.SetEnvironmentVariable("HALLOW_OVERLAY_DIR", Path.Combine(Dir, "overlay"));
            Environment.SetEnvironmentVariable("HALLOW_GEAR_DIR", Path.Combine(Dir, "gear"));
            Environment.SetEnvironmentVariable("HALLOW_MIXER_DIR", Path.Combine(Dir, "mixer"));
            Environment.SetEnvironmentVariable("HALLOW_CLIPS_DIR", Path.Combine(Dir, "clips"));
            Environment.SetEnvironmentVariable("HALLOW_THEME_FILE", Path.Combine(Dir, "theme.json"));
            Environment.SetEnvironmentVariable("HALLOW_HUB_PIPE", "HallowOverlayTest.FakeHub." + Process.GetCurrentProcess().Id);
            Environment.SetEnvironmentVariable("HALLOW_SYSTEM_LIGHT", "0");
            foreach (var d in new[] { "overlay", "gear", "mixer", "clips" }) Directory.CreateDirectory(Path.Combine(Dir, d));
            Fixtures = Path.Combine(Path.GetDirectoryName(typeof(Run).Assembly.Location), "fixtures");

            bool unit = args.Length == 0 || args.Contains("unit"), link = args.Length == 0 || args.Contains("link");
            if (unit)
            {
                Suite("stats formatting", StatsFormatting);
                Suite("frame-time graph", GraphTests);
                Suite("gear chips (panel)", GearPanel);
                Suite("light / dark", LightDark);
                Suite("position memory", Position);
                Suite("position: monitors", Monitors);
                Suite("position: save and load", PositionFiles);
                Suite("gear status.json reader", GearReader);
                Suite("the game hold", GameHold);
                Suite("now playing", NowPlayingTests);
                Suite("mixer status.json (ad muted)", MixerStatus);
                Suite("clips replay saved", ClipsReplay);
                Suite("hub notify lines", HubLines);
                Suite("fake hub pipe", FakeHub);
                Suite("update prompt", UpdateText);
                Suite("settings migration", Migration);
                Suite("what's new", WhatsNewTests);
            }
            if (link) Suite("test copy over the family pipe", TestCopy);
            try { Directory.Delete(Dir, true); } catch { }
            Console.WriteLine();
            if (failed.Count > 0) { Console.WriteLine("Failed:"); foreach (var f in failed) Console.WriteLine("  " + f); }
            Console.WriteLine(pass + " passed, " + fail + " failed");
            return fail == 0 ? 0 : 1;
        }

        // ---------------- settings migration (old overlay.json files load into today's settings)
        static OverlaySettings LoadFx(string name)
        {
            var p = Path.Combine(Dir, "mig-" + name); File.WriteAllText(p, Fx(name)); return OverlaySettings.Load(p);
        }
        static void Migration()
        {
            var a = LoadFx("overlay-1.0.json");
            Eq("br", a.Anchor, "1.0: the corner becomes the spot");
            Ok(a.OffX == 0 && a.OffY == 0 && a.Margin == Placement.DefaultMargin, "1.0: no offset, the usual 14 px edge");
            Eq("1.0", a.LastSeenVersion, "1.0: a file without LastSeenVersion was written by 1.0 (so What's new shows everything since)");
            Ok(a.Hotkeys != null && a.Hotkeys["toggle"] == "" && a.Hotkeys["bench"] == "", "1.0: hotkeys turned off stay off (Hotkey=false -> both actions off)");
            Ok(a.Scale == 1.2 && a.Opacity == 0.7 && !a.Row && !a.Gpu && a.PingHost == "8.8.8.8", "1.0: its own values are kept");
            Ok(a.GearChips == "low" && a.ColorMode == "follow" && a.NowPlaying && a.Server && !a.FrameGraph && a.AutoUpdate, "1.0: newer settings get their defaults");
            Ok(a.PerGame != null && a.PerGame.Count == 0 && a.OffGames != null, "1.0: empty lists, never null");

            var b = LoadFx("overlay-1.14.json");
            Eq("tr", b.Anchor, "1.14: usual corner -> spot");
            Ok(b.PerGame.ContainsKey("Delta Force") && b.PerGame["Delta Force"].Anchor == "bl" && b.PerGame["Delta Force"].OwnSpot, "1.14: a game with a different corner keeps it as its own spot");
            Ok(b.PerGame.ContainsKey("Deadlock") && !b.PerGame["Deadlock"].OwnSpot, "1.14: a game in the same corner uses the usual spot");
            Ok(b.Hotkeys != null && b.Hotkeys["toggle"] == "Ctrl+Shift+O" && b.Hotkeys["bench"] == "", "1.14: own hotkeys kept");
            Eq("Delta Force", b.PerGameKey("delta force"), "a game's own look is found case-insensitively");

            var c = LoadFx("overlay-1.23.json");
            Ok(c.Anchor == "mr" && c.OffX == -0.0125 && c.OffY == 0.05 && c.Margin == 20 && c.ScreenId == "MON-B" && c.Screen == @"\\.\DISPLAY2", "1.23: a dragged spot on screen 2 loads exactly");
            Ok(c.ColorMode == "light" && c.GearChips == "always" && c.FrameGraph && c.GraphSize == "l", "1.23: look, Gear chips and graph settings kept");
            Ok(c.PerGame["Delta Force"].OwnSpot && c.PerGame["Delta Force"].Anchor == "bc", "1.23: a game's own spot kept");
            var look = c.LookFor("Delta Force");
            Ok(look.Anchor == "bc" && look.OffY == -0.02 && look.Margin == 24, "LookFor: a game with its own spot uses it");
            var dl = c.LookFor("Deadlock");
            Ok(object.ReferenceEquals(dl, c), "LookFor: a game without its own look uses the usual one");

            var broken = Path.Combine(Dir, "broken.json"); File.WriteAllText(broken, "{\"Anchor\":\"br\",\"Margin\":");
            var d = OverlaySettings.Load(broken);
            Ok(d.Anchor == "tl" && d.PerGame != null, "a cut-off file loads as the defaults (no crash)");
            Ok(OverlaySettings.Load(Path.Combine(Dir, "missing.json")).Anchor == "tl", "a missing file loads as the defaults");
            Eq("low", GearLink.Mode("LOW"), "Gear chips mode: unknown / other case -> low"); Eq("always", GearLink.Mode(" Always "), "Gear chips mode: ' Always ' -> always"); Eq("low", GearLink.Mode(null), "Gear chips mode: null -> low");
            Eq("follow", Look.Normalize("bogus"), "look mode: unknown -> follow"); Eq("light", Look.Normalize(" LIGHT "), "look mode: case and spaces");
        }

        // ---------------- the update prompt (1.23.1 fix: "1.23.1", not "1.23" / "1.23.1.0")
        static GhRelease Rel(string tag, bool zip = true) { return new GhRelease { Tag = tag, Assets = zip ? new[] { new GhAsset { Name = "Hallow.Overlay.zip", Url = "https://example.invalid/x.zip", Digest = "sha256:00" } } : new GhAsset[0] }; }
        static void UpdateText()
        {
            Eq("1.23.1", Updater.Show(new Version(1, 23, 1, 0)), "Show: a patch release keeps its third number");
            Eq("1.24", Updater.Show(new Version(1, 24, 0, 0)), "Show: x.y.0.0 reads x.y");
            Eq("1.24", Updater.Show(new Version(1, 24)), "Show: a two-part version");
            Eq("", Updater.Show(null), "Show: no version -> empty");
            Eq(new Version(1, 23, 1), Rel("v1.23.1").Version, "release tag v1.23.1 -> 1.23.1");
            Eq(new Version(0, 0), Rel("nightly").Version, "a tag that isn't a version -> 0.0 (never newer)");
            var t = Updater.PromptText(Rel("v1.23.1").Version, "1.23");
            Ok(t.StartsWith("Hallow Overlay 1.23.1 is available (you have 1.23).") && !t.Contains("1.23.1.0"), "prompt text: " + t);
            Eq("Update to v1.25", Updater.ButtonText(Rel("v1.25").Version), "button text for v1.25");
            Eq("Update to v1.24.2", Updater.ButtonText(Rel("V1.24.2").Version), "button text for V1.24.2 (capital V)");
            var cur = new Version(1, 24, 0, 0);
            Ok(Updater.IsNewer(Rel("v1.24.1"), cur), "1.24.1 is newer than 1.24");
            Ok(Updater.IsNewer(Rel("v1.25"), cur), "1.25 is newer than 1.24");
            Ok(!Updater.IsNewer(Rel("v1.24"), cur), "1.24 isn't newer than itself");
            Ok(!Updater.IsNewer(Rel("v1.23.1"), cur), "1.23.1 isn't newer than 1.24");
            Ok(!Updater.IsNewer(Rel("v1.9"), cur), "1.9 is older than 1.24 (numbers, not text)");
            Ok(Updater.IsNewer(Rel("v1.100"), cur), "1.100 is newer than 1.24 (numbers, not text)");
            Ok(!Updater.IsNewer(Rel("v2.0", false), cur), "a release without a zip is never offered");
            Ok(Updater.Current >= new Version(1, 24), "this build says it is 1.24 or newer: " + Updater.Current);
            Eq(Program.Version, Updater.Show(Updater.Current), "Program.Version matches the assembly version");
            // the download check: a zip that doesn't match GitHub's checksum is never unpacked
            var zip = Path.Combine(Dir, "u.zip"); File.WriteAllBytes(zip, Encoding.ASCII.GetBytes("not really a zip"));
            string script; var err = Updater.Unpack(zip, "sha256:" + new string('0', 64), Path.Combine(Dir, "unz"), out script);
            Ok(err != null && err.Contains("didn't match") && !Directory.Exists(Path.Combine(Dir, "unz")), "a checksum mismatch stops the update before unpacking");
            err = Updater.Unpack(zip, "md5:abc", Path.Combine(Dir, "unz"), out script);
            Ok(err != null && err.Contains("checksum"), "no sha256 digest -> not installed");
        }

        // ---------------- What's new
        static void WhatsNewTests()
        {
            Eq(Program.Version, WhatsNew.Notes[0].Item1, "What's new starts with this version");
            // relative to this build, not pinned to 1.24: from the previous version, only this version's notes are new
            var prev = WhatsNew.Notes[1].Item1; var since = WhatsNew.Since(prev);
            Ok(since.Count == 1 && since[0].Item1 == Program.Version, "since " + prev + " (the previous version): just the " + Program.Version + " notes");
            Ok(WhatsNew.Notes.Any(n => n.Item1 == "1.24" && n.Item2.Any(i => i.Contains("1% low"))), "the 1.24 notes keep the 1% low line");
            Ok(WhatsNew.Since("1.0").Count == 6, "since 1.0: the newest six versions at most");
            Ok(WhatsNew.Since(Program.Version).Count == 0 && WhatsNew.Since("").Count == 0, "nothing new when up to date (or nothing seen yet)");
            Ok(WhatsNew.Notes.Where(n => n.Item1 != "1.23.1").All(n => n.Item2.All(l => !System.Text.RegularExpressions.Regex.IsMatch(l, "[0-9] %"))), "no note says \"45 %\" (family percent rule; 1.23.1 quotes it)");
        }
    }
}
