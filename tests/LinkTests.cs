// Hallow Overlay tests - what it reads from the other Hallow apps: Gear's status.json (old and new fields), the game hold, now
// playing (Mixer's API, made-up Windows media sessions, Spotify ads, "Ad muted" from Mixer's status.json), "Replay saved" from
// Clips' status.json, Hub's notify lines (and a fake Hub on a test pipe), and a test copy of the app over the family pipe.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowOverlay.Tests
{
    public static partial class Run
    {
        static string Iso(DateTime t) { return t.ToString("s"); }

        static void GearReader()
        {
            var g = GearLink.Parse(Fx("gear-1.0.json"));
            Ok(g != null && g.Running && g.LowAt == 20 && g.VeryLowAt == 10, "Gear 1.0 file: running, low at 20%, very low at 10%");
            Eq(2, g.Devices.Count, "Gear 1.0: two chips (a keyboard without a battery has none)");
            Ok(g.Devices[0].Kind == "mouse" && g.Devices[0].Pct == 64 && g.Devices[0].Level == 0, "mouse 64%: fine");
            Ok(g.Devices[1].Pct == 9 && g.Devices[1].Level == 2, "headset 9%: very low");
            Ok(g.Alert == "Headset battery very low: 9%" && g.AlertLevel == "critical", "Gear's held alert and its level");
            Eq("64%", GearLink.Text(g.Devices[0]), "chip text \"64%\"");

            var n = GearLink.Parse(Fx("gear-1.3.json"));
            Ok(n != null && n.Devices.Count == 4, "Gear 1.3 file: 4 chips (keyboard on a cable, a not-connected and an asleep device left out) - got " + (n == null ? -1 : n.Devices.Count));
            var buds = n.Devices.First(d => d.Kind == "earbuds"); var pad = n.Devices.First(d => d.Kind == "controller"); var head = n.Devices.First(d => d.Kind == "headset");
            Ok(buds.Pct == 45 && buds.Estimated && GearLink.Text(buds) == "~45%", "earbuds 45.0 (a decimal) estimated -> \"~45%\"");
            Ok(pad.Pct == 8 && pad.Level == 2 && GearLink.Text(pad) == "8%", "a battery written as \"8%\" (text) reads 8 -> very low");
            Ok(head.Pct == 18 && head.Level == 1, "headset 18%: low (amber)");
            Ok(n.Alert == "Mouse battery low: 18%" && GearLink.HeldLevel(n) == 1, "a 'low' alert is level 1");
            Eq("earbuds", GearLink.Icon(buds.Kind), "earbuds icon"); Eq("gamepad", GearLink.Icon(pad.Kind), "controller -> gamepad icon"); Eq("battery-half", GearLink.Icon("speaker"), "anything else -> battery");

            var st = GearLink.Parse(Fx("gear-states-only.json"));
            var chg = st.Devices[0]; var full = st.Devices[1]; var wired = st.Devices[2];
            Ok(chg.Kind == "headset" && chg.Charging && chg.Level == 0 && GearLink.Mark(chg) == "bolt", "state \"charging\" (no yes/no fields): not low, a bolt; kind \"Headset\" -> headset");
            Ok(full.Full && GearLink.Mark(full) == "bolt", "state \"full\": a bolt");
            Ok(wired.Wired && wired.Level == 0 && GearLink.Mark(wired) == "plug", "state \"wired\" at 5%: on a cable, so not low - a plug");
            Ok(GearLink.Mark(g.Devices[0]) == null, "on battery: no mark");
            Ok(GearLink.Parse("{\"app\":\"Gear\",\"devices\":[{\"kind\":\"mouse\",\"battery\":30,\"state\":\"charging\",\"charging\":false}]}").Devices[0].Charging == false, "the yes/no field wins over state when both are there");

            Eq("100%", GearLink.Text(new GearChip { Pct = 140 }), "a bad percentage is kept to 100%"); Eq("", GearLink.Text(null), "no chip: no text");
            var custom = GearLink.Parse("{\"app\":\"Gear\",\"lowAt\":30,\"veryLowAt\":50,\"devices\":[{\"kind\":\"mouse\",\"battery\":25},{\"kind\":\"mouse\",\"battery\":101},{\"kind\":\"mouse\",\"battery\":-1}]}");
            Ok(custom.LowAt == 30 && custom.VeryLowAt == 30 && custom.Devices.Count == 1 && custom.Devices[0].Level == 2, "your own low level (30%); very low can't be above it; 101% / -1% ignored");
            Ok(GearLink.Parse("{\"app\":\"Mixer\"}") == null && GearLink.Parse("not json") == null && GearLink.Parse(null) == null && GearLink.Parse("{\"app\":\"Gear\",\"devices\":\"x\"}").Devices.Count == 0, "not Gear / broken / odd: no chips, no crash");
            Ok(!GearLink.Parse("{\"app\":\"Gear\",\"running\":false}").Running, "running:false = Gear quit");

            Eq(4, GearLink.Chips(n, "always").Count, "always: all 4"); Eq(2, GearLink.Chips(n, "low").Count, "low: just the 2 low ones"); Eq(0, GearLink.Chips(n, "off").Count, "off: none"); Eq(0, GearLink.Chips(null, "always").Count, "no Gear: none");

            // staleness and the 10 s cache
            var now = new DateTime(2026, 11, 23, 12, 0, 0);
            var fresh = new GearStatus { Running = true, Updated = now.AddMinutes(-5) };
            Ok(GearLink.Usable(fresh, now), "5 minutes old: used");
            var old = new GearStatus { Running = true, Updated = now.AddMinutes(-20) };
            GearLink.GearRunning = () => false; Ok(!GearLink.Usable(old, now), "20 minutes old and Gear's pipe gone: not used");
            GearLink.GearRunning = () => true; Ok(GearLink.Usable(old, now.AddSeconds(11)), "20 minutes old but Gear still running (a long game): used");
            Ok(!GearLink.Usable(new GearStatus { Running = false, Updated = now }, now) && !GearLink.Usable(null, now), "Gear quit / nothing: not used");
            var gdir = Environment.GetEnvironmentVariable("HALLOW_GEAR_DIR"); var gf = Path.Combine(gdir, "status.json");
            GearLink.Forget(); Ok(GearLink.Read(DateTime.Now) == null, "no status.json: no chips");
            File.WriteAllText(gf, Fx("gear-1.0.json").Replace("2026-11-20T10:00:00", Iso(DateTime.Now)));
            var t0 = DateTime.Now; Ok(GearLink.Read(t0) == null, "a new file isn't looked at again within 10 s");
            var r1 = GearLink.Read(t0.AddSeconds(10)); Ok(r1 != null && r1.Devices.Count == 2, "10 s later it's read");
            File.WriteAllText(gf, "{\"app\":\"Gear\",\"running\":false,\"updated\":\"" + Iso(DateTime.Now) + "\"}"); File.SetLastWriteTimeUtc(gf, DateTime.UtcNow.AddSeconds(5));
            Ok(GearLink.Read(t0.AddSeconds(21)) == null, "Gear quit (running:false): chips go away");
            File.WriteAllText(gf, "{broken"); File.SetLastWriteTimeUtc(gf, DateTime.UtcNow.AddSeconds(10));
            Ok(GearLink.Read(t0.AddSeconds(32)) == null, "a broken file: no chips, no crash");
            File.Delete(gf); GearLink.Forget(); GearLink.GearRunning = () => false;
        }

        static void GameHold()
        {
            var g = GearLink.Parse(Fx("gear-1.0.json"));
            Ok(GearLink.HeldAlert(g, false) == null, "no game: Gear shows its own pop-up, the panel doesn't");
            Eq("Headset battery very low: 9%", GearLink.HeldAlert(g, true), "during a game: Gear's held alert is on the panel");
            Eq(2, GearLink.HeldLevel(g), "critical -> red");
            Ok(GearLink.HeldAlert(new GearStatus { Alert = "  " }, true) == null && GearLink.HeldAlert(null, true) == null, "an empty alert / no Gear: nothing");
            Eq(1, GearLink.HeldLevel(null), "no level: amber");
            Ok(MainWindow.WantPanel(true, false, true, true), "shown, game running: the panel shows");
            Ok(!MainWindow.WantPanel(true, false, true, false), "only while a game runs, no game: hidden");
            Ok(MainWindow.WantPanel(true, false, false, false), "always on: shows without a game");
            Ok(!MainWindow.WantPanel(false, false, false, true), "hidden with the hotkey: hidden");
            Ok(!MainWindow.WantPanel(true, true, true, true), "turned off for this game in Hallow Hub: hidden");
            Ok(MainWindow.MoveAllowed(true, true) && MainWindow.MoveAllowed(false, false) && !MainWindow.MoveAllowed(false, true), "move mode never starts by itself over a game that has the focus");
            Ok(MainWindow.ExclusiveNote("Delta Force").StartsWith("Delta Force is in exclusive fullscreen"), "the exclusive-fullscreen note names the game");
            var x = new Sample(); var live = LiveStats.For("Delta Force", x, false, null, "host.example", true);
            Ok(live["fps"] == null && live["ping"] == null && live["region"] == null, "live stats for Remote: unknown numbers are null");
            x.Fps = 143.6; x.PingMs = 18; x.ServerMs = 23.4; live = LiveStats.For("Delta Force", x, true, null, "host.example", true);
            Ok(Convert.ToDouble(live["fps"]) == 144 && Convert.ToDouble(live["ping"]) == 23 && (string)live["pingTo"] == "server" && live["region"] == null, "live stats: rounded, the server's ping, never its host while the IP is hidden");
            live = LiveStats.For("Delta Force", x, true, "FRA", "host.example", false); Eq("FRA", live["region"], "live stats: the region code");
        }

        static List<MediaInfo> Sessions(params MediaInfo[] l) { return l.ToList(); }
        static void NowPlayingTests()
        {
            Eq("Lo-fi Beats - Rainy Night", NowPlaying.Line(" Rainy Night ", " Lo-fi Beats "), "\"Artist - Title\", trimmed");
            Eq("Rainy Night", NowPlaying.Line("Rainy Night", ""), "no artist: just the title"); Ok(NowPlaying.Line(" ", "x") == null, "no title: nothing");
            Eq(NowPlaying.Line("a", "b"), WinMedia.Line("a", "b"), "Windows' line is the same as Mixer's");
            Ok(NowPlaying.IsAd("Advertisement", "") && NowPlaying.IsAd("advertisement", "Spotify") && NowPlaying.IsAd("Spotify", null) && NowPlaying.IsAd("Advertisement", "Advertisement"), "Spotify ads: \"Advertisement\" / \"Spotify\" with no real artist");
            Ok(!NowPlaying.IsAd("Advertisement", "Real Band") && !NowPlaying.IsAd("Rainy Night", "") && !NowPlaying.IsAd("Spotify", "Some DJ"), "a real artist's song called \"Advertisement\" is music");
            // made-up Windows media sessions (what the system media controls list)
            var paused = new MediaInfo("Spotify.exe", "Paused Song", "Band", false); var yt = new MediaInfo("msedge", "Lo-fi stream", "Lofi Girl", true); var empty = new MediaInfo("x", "", "", true);
            Ok(NowPlaying.Choose(Sessions(paused, empty, yt)) == yt, "the first session that is playing with a title");
            Ok(NowPlaying.Choose(Sessions(paused)) == null && NowPlaying.Choose(Sessions()) == null && NowPlaying.Choose(null) == null, "nothing playing: nothing");
            Eq("Lofi Girl - Lo-fi stream", NowPlaying.Display(yt, false), "a playing video");
            var ad = new MediaInfo("Spotify.exe", "Advertisement", "", true);
            Eq("Advertisement", NowPlaying.Display(ad, false), "a Spotify ad without Mixer: \"Advertisement\"");
            Eq("Ad muted", NowPlaying.Display(ad, true), "...while Mixer mutes it: \"Ad muted\"");
            Eq("Ad muted", NowPlaying.Display(null, true), "Mixer muting an ad with no session listed: still \"Ad muted\"");
            Ok(NowPlaying.Display(null, false) == null && NowPlaying.Display(paused, false) == null, "nothing / paused: nothing");
            var longOne = NowPlaying.Display(new MediaInfo("x", new string('t', 60), "Artist", true), false);
            Ok(longOne.Length == NowPlaying.MaxChars && longOne.EndsWith("\u2026") && longOne.StartsWith("Artist - "), "long lines are cut to 48 with \u2026");
            Ok(NowPlaying.Fit("short") == "short" && NowPlaying.Fit(null) == null, "short lines unchanged");

            var m = NowPlaying.ParseMixer(Fx("mixer-state-ad.json"));
            Ok(m != null && m.HasAdField && m.AdMuted && m.Rev == 41 && !m.MicMuted, "Mixer 3.27 /state during an ad: spotifyAdMuted, rev 41");
            Eq("Ad muted", NowPlaying.Display(m.Title, m.Artist, m.Playing, m.AdMuted), "Mixer's ad -> \"Ad muted\"");
            Eq("Advertisement", NowPlaying.Display(m.Title, m.Artist, m.Playing, false), "the same ad not muted -> \"Advertisement\"");
            var s = NowPlaying.ParseMixer(Fx("mixer-state-song.json"));
            Ok(s.MicMuted && !s.AdMuted && s.Line == "Lo-fi Beats - Rainy Night" && s.Rev == 42, "Mixer /state with a song and the mic muted");
            var o = NowPlaying.ParseMixer(Fx("mixer-state-3.26.json"));
            Ok(o != null && !o.HasAdField && !o.AdMuted && o.Line == null && o.Rev == -1, "Mixer 3.26 (no ad field, paused, no rev): nothing playing, no ad");
            Ok(NowPlaying.ParseMixer("<html>") == null && NowPlaying.ParseMixer(null) == null, "not JSON: null (the loop treats it as no Mixer)");
            Ok(NowPlaying.ParseMixer("{\"mic\":\"weird\",\"nowPlaying\":5,\"spotifyAdMuted\":\"yes\"}").AdMuted == false, "odd values: no crash, not muted");
        }

        static void MixerStatus()
        {
            var now = new DateTime(2026, 11, 23, 12, 0, 0);
            Func<string, string> st = extra => "{\"app\":\"Mixer\",\"version\":\"3.27\",\"updated\":\"" + Iso(now.AddSeconds(-30)) + "\"" + extra + "}";
            Eq((bool?)true, NowPlaying.AdMutedInStatus(st(",\"spotifyAdMuted\":true,\"adsMutedToday\":4"), now), "status.json: spotifyAdMuted true");
            Eq((bool?)false, NowPlaying.AdMutedInStatus(st(",\"spotifyAdMuted\":false"), now), "status.json: false");
            Eq(null, NowPlaying.AdMutedInStatus(st(""), now), "an older Mixer without the field: says nothing");
            Eq(null, NowPlaying.AdMutedInStatus(st(",\"spotifyAdMuted\":true,\"running\":false"), now), "Mixer quit: says nothing");
            Eq(null, NowPlaying.AdMutedInStatus(st(",\"spotifyAdMuted\":true").Replace(Iso(now.AddSeconds(-30)), Iso(now.AddMinutes(-10))), now), "10 minutes old: says nothing");
            Eq(null, NowPlaying.AdMutedInStatus("{\"app\":\"Clips\",\"spotifyAdMuted\":true,\"updated\":\"" + Iso(now) + "\"}", now), "another app's file: ignored");
            Eq(null, NowPlaying.AdMutedInStatus("{nope", now), "broken: says nothing");
            var f = Path.Combine(NowPlaying.MixerDir, "status.json");
            Ok(NowPlaying.MixerDir == Path.Combine(Dir, "mixer") && Sampler.MixerApiFile.StartsWith(Path.Combine(Dir, "mixer")), "HALLOW_MIXER_DIR: the real Mixer's files are never read by tests");
            NowPlaying.Forget(); Ok(NowPlaying.AdMutedNow(DateTime.Now) == null, "no Mixer status.json: nothing");
            File.WriteAllText(f, "{\"app\":\"Mixer\",\"updated\":\"" + Iso(DateTime.Now) + "\",\"spotifyAdMuted\":true}");
            var t0 = DateTime.Now; Ok(NowPlaying.AdMutedNow(t0) == null, "looked at every 2 s at most");
            Eq((bool?)true, NowPlaying.AdMutedNow(t0.AddSeconds(2)), "...then reads \"muted\"");
            File.WriteAllText(f, "{\"app\":\"Mixer\",\"updated\":\"" + Iso(DateTime.Now) + "\",\"spotifyAdMuted\":false}"); File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddSeconds(3));
            Eq((bool?)false, NowPlaying.AdMutedNow(t0.AddSeconds(4)), "the ad ends: \"not muted\" as soon as the file changes");
            File.Delete(f); NowPlaying.Forget();
        }

        static void ClipsReplay()
        {
            var t = new DateTime(2026, 11, 23, 12, 0, 0);
            Eq((DateTime?)t, ClipsLink.SavedAt("{\"app\":\"Clips\",\"replaySaved\":\"" + Iso(t) + "\"}"), "replaySaved as a time");
            Eq((DateTime?)t, ClipsLink.SavedAt("{\"app\":\"Clips\",\"replaySaved\":{\"time\":\"" + Iso(t) + "\",\"file\":\"x.mp4\"}}"), "replaySaved as an object with a time");
            Eq((DateTime?)t, ClipsLink.SavedAt("{\"app\":\"Clips\",\"lastSaved\":{\"at\":\"" + Iso(t) + "\"}}"), "lastSaved {at} also works");
            Eq(null, ClipsLink.SavedAt(Fx("clips-status-1.15.json")), "Clips 1.15 (no such field): nothing - nothing depends on it");
            Eq(null, ClipsLink.SavedAt("{\"app\":\"Mixer\",\"replaySaved\":\"" + Iso(t) + "\"}"), "another app's file: ignored");
            Eq(null, ClipsLink.SavedAt("{\"app\":\"Clips\",\"replaySaved\":null}"), "replaySaved null: nothing");
            Eq(null, ClipsLink.SavedAt("garbage"), "broken: nothing");

            var f = Path.Combine(Dir, "clips", "watch.json"); var w = new ClipsLink.Watch { File = () => f }; var now = DateTime.Now;
            Func<DateTime, string> body = at => "{\"app\":\"Clips\",\"recording\":true,\"replaySaved\":\"" + Iso(at) + "\"}";
            File.WriteAllText(f, body(now.AddSeconds(-5)));
            Ok(!w.Check(now), "the first look never shows an old save (Overlay just started)");
            File.WriteAllText(f, body(now.AddSeconds(3))); File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddSeconds(1));
            Ok(!w.Check(now.AddSeconds(2)), "looked at every 3 s at most");
            Ok(w.Check(now.AddSeconds(4)), "a new save: \"Replay saved\" once");
            Ok(!w.Check(now.AddSeconds(8)), "...and not again for the same save");
            File.WriteAllText(f, body(now.AddSeconds(-60))); File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddSeconds(2));
            Ok(!w.Check(now.AddSeconds(12)), "an older time: nothing");
            File.WriteAllText(f, body(now.AddSeconds(-40))); File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddSeconds(3));
            var w2 = new ClipsLink.Watch { File = () => f + ".none" }; Ok(!w2.Check(now), "no Clips file: nothing");
            File.WriteAllText(f + ".none", body(now.AddSeconds(-1))); Ok(w2.Check(now.AddSeconds(3)), "Clips appears after Overlay started: its new save shows");
            var w3 = new ClipsLink.Watch { File = () => f }; w3.Check(now); File.WriteAllText(f, body(now.AddSeconds(-30))); File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddSeconds(4));
            Ok(!w3.Check(now.AddSeconds(3)), "a save more than 20 s old (Overlay was busy): not shown late");
            Eq("Replay saved", ClipsLink.Text, "the text");
        }

        static void HubLines()
        {
            Eq("notify Overlay\tBenchmark saved\tAverage 141 fps", HubIcon.NotifyLine("Benchmark saved", "Average 141 fps"), "notify line: app, title, text separated by tabs");
            Eq("notify Overlay\tA B\tline 1  line 2 x", HubIcon.NotifyLine("A\tB", "line 1\r\nline 2\tx"), "tabs and line breaks inside become spaces (the line can't split)");
            Eq("notify Overlay\t\t", HubIcon.NotifyLine(null, null), "nothing: empty fields, no crash");
            Ok(HubIcon.NotifyLine("x", "y").Split('\t').Length == 3, "always exactly three fields");
            Ok(HubIcon.HostsIcon("ok {\"on\": true}") && HubIcon.HostsIcon("ok {\"on\":true,\"app\":\"Hub\"}"), "tray-host: Hub shows the one icon");
            Ok(!HubIcon.HostsIcon("ok {\"on\":false}") && !HubIcon.HostsIcon("error unknown command") && !HubIcon.HostsIcon(null), "tray-host: no / an error / no Hub -> our own icon");
            Ok(HubIcon.Took("ok") && HubIcon.Took("ok shown") && !HubIcon.Took("okay") && !HubIcon.Took("error busy") && !HubIcon.Took(null), "notify reply: only ok counts");
        }

        // a made-up Hub on the test pipe: answers one command per connection with the reply for it
        static Thread FakeHubServer(string pipe, Func<string, string> reply, List<string> got, int conns)
        {
            var th = new Thread(() =>
            {
                for (int i = 0; i < conns; i++)
                    using (var s = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        var ar = s.BeginWaitForConnection(null, null); if (!ar.AsyncWaitHandle.WaitOne(8000)) return; s.EndWaitForConnection(ar);
                        var buf = new byte[4096]; int n = 0;
                        while (n < buf.Length) { var r = s.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; if (Array.IndexOf(buf, (byte)'\n', 0, n) >= 0) break; }
                        var line = Encoding.UTF8.GetString(buf, 0, n).TrimEnd('\n', '\r'); lock (got) got.Add(line);
                        var b = Encoding.UTF8.GetBytes(reply(line) + "\n"); s.Write(b, 0, b.Length); s.Flush(); try { s.WaitForPipeDrain(); } catch { }
                    }
            }) { IsBackground = true }; th.Start(); return th;
        }
        static void FakeHub()
        {
            var pipe = Link.HubPipe; Ok(pipe.StartsWith("HallowOverlayTest.FakeHub."), "tests use a fake Hub pipe (HALLOW_HUB_PIPE), never the real Hub");
            var got = new List<string>();
            var th = FakeHubServer(pipe, l => l.StartsWith("tray-host") ? "ok {\"on\":true}" : l.StartsWith("notify ") ? "ok" : "error unknown command", got, 2);
            Ok(HubIcon.AskTrayHost(), "Hub says it hosts the one icon");
            Ok(HubIcon.Notify("Benchmark saved", "Average 141 fps\nopen it from settings"), "Hub takes the message");
            th.Join(3000);
            Ok(got.Count == 2 && got[0] == "tray-host" && got[1] == HubIcon.NotifyLine("Benchmark saved", "Average 141 fps\nopen it from settings"), "Hub got exactly: tray-host, then the notify line");
            var sw = Stopwatch.StartNew(); bool took = HubIcon.Notify("x", "y");
            Ok(!took && sw.ElapsedMilliseconds < 4000, "no Hub: Notify says no quickly (" + sw.ElapsedMilliseconds + " ms) so Overlay's own icon shows it");
        }

        // ---------------- a test copy of the real exe, over the family pipe (no window, no tray, no game)
        static void TestCopy()
        {
            var exe = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Run).Assembly.Location), "..", "HallowOverlay.exe"));
            Ok(File.Exists(exe), "the app was built next to the tests: " + exe);
            if (!File.Exists(exe)) return;
            var od = OverlaySettings.Dir; Directory.CreateDirectory(od);
            // its own settings: ping only this PC, a remembered spot to check it's kept, made-up screens far off any real one
            var seed = new OverlaySettings { PingHost = "127.0.0.1", Anchor = "br", OffX = -0.01, OffY = -0.02, Margin = 20, ScreenId = "T-2", Screen = @"\\.\DISPLAY8", LastSeenVersion = Program.Version, AutoUpdate = false, UseHallowIcon = false };
            Ok(seed.Save(OverlaySettings.FilePath), "test copy settings written to the temp folder");
            var scr = Path.Combine(Dir, "copy-screens.txt"); File.WriteAllLines(scr, new[] { "T-1|\\\\.\\DISPLAY7|-40000|-40000|1920|1080|1|1", "T-2|\\\\.\\DISPLAY8|-38080|-40000|2560|1440|1.25|0" });
            File.WriteAllText(Path.Combine(ClipsLink.Dir, "status.json"), "{\"app\":\"Clips\",\"version\":\"1.15\",\"updated\":\"" + Iso(DateTime.Now) + "\",\"clips\":[]}");
            var psi = new ProcessStartInfo(exe, "--tray") { UseShellExecute = false, CreateNoWindow = true };
            psi.EnvironmentVariables["HALLOW_OVERLAY_SCREENS"] = scr;   // the rest (data folders, fake Hub, theme) is inherited from this process
            Process p = null;
            try
            {
                p = Process.Start(psi); try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                string reply = null; int code = 2; var until = DateTime.Now.AddSeconds(30);
                while (DateTime.Now < until && !p.HasExited) { code = Link.Send("ping", out reply); if (code == 0) break; Thread.Sleep(300); }
                Ok(code == 0 && reply.Contains("\"app\":\"Overlay\"") && reply.Contains("\"version\":\"" + Program.Version + "\""), "ping: the test copy answers with this version (" + reply + ")");
                Ok(reply != null && reply.Contains("\"look\":\"dark\""), "ping says the look (dark)");
                Thread.Sleep(1500);   // its first refresh (once a second) places the panel
                var js = new JavaScriptSerializer();
                Func<Dictionary<string, object>> status = () => { string r; Link.Send("status", out r); return r != null && r.StartsWith("ok ") ? js.Deserialize<Dictionary<string, object>>(r.Substring(3)) : null; };
                var st = status();
                Ok(st != null && (string)st["app"] == "Overlay" && (string)st["version"] == Program.Version, "status: app and version");
                Ok(st != null && !(bool)st["visible"], "no game: the panel isn't shown");
                var pos = st == null ? null : st["position"] as Dictionary<string, object>;
                Ok(pos != null && (string)pos["anchor"] == "br" && Convert.ToDouble(pos["offsetX"]) == -0.01 && (string)pos["screen"] == @"\\.\DISPLAY8" && (bool)pos["screenFound"], "status: the remembered spot on the made-up screen 8");
                Ok(pos != null && Convert.ToDouble(pos["width"]) == 0 && !(bool)pos["moving"], "...and the panel was never put on any screen (never shown: width 0, not moving)");
                string r2; Eq(0, Link.Send("game-off Delta Force", out r2), "game-off from Hub: ok");
                st = status(); Ok(st != null && ((System.Collections.ArrayList)st["offGames"]).Contains("Delta Force"), "status lists the game it's off in");
                Link.Send("game-on delta force", out r2); st = status(); Ok(st != null && ((System.Collections.ArrayList)st["offGames"]).Count == 0, "game-on (any case) turns it back on");
                Ok(Link.Send("game-off", out r2) == 1 && r2.Contains("no game name"), "game-off without a name: an error");
                Ok(Link.Send("bogus", out r2) == 1 && r2 == "error unknown command", "an unknown command: \"error unknown command\"");
                Ok(Link.Send("flash", out r2) == 1, "flash without text: an error");
                string menu; Link.Send("menu", out menu); Ok(menu != null && menu.Contains("\"id\":\"toggle\"") && menu.Contains("\"id\":\"settings\""), "menu: the items Hub shows");
                // "Replay saved" from Clips' status.json (Clips didn't flash anything over the pipe)
                Thread.Sleep(3500);
                File.WriteAllText(Path.Combine(ClipsLink.Dir, "status.json"), "{\"app\":\"Clips\",\"updated\":\"" + Iso(DateTime.Now) + "\",\"replaySaved\":\"" + Iso(DateTime.Now) + "\",\"clips\":[]}");
                string msg = null; until = DateTime.Now.AddSeconds(10);
                while (DateTime.Now < until) { st = status(); msg = st == null ? null : st["testMessage"] as string; if (msg == ClipsLink.Text) break; Thread.Sleep(500); }
                Eq(ClipsLink.Text, msg, "Clips' status.json says a replay was saved: \"Replay saved\" on the panel");
                Eq(0, Link.Send("flash Clip saved", out r2), "Clips flashes \"Clip saved\" over the pipe");
                Thread.Sleep(3500);
                File.WriteAllText(Path.Combine(ClipsLink.Dir, "status.json"), "{\"app\":\"Clips\",\"updated\":\"" + Iso(DateTime.Now) + "\",\"replaySaved\":\"" + Iso(DateTime.Now.AddSeconds(1)) + "\",\"clips\":[]}");
                Thread.Sleep(5000); st = status();
                Eq("Clip saved", st == null ? null : st["testMessage"], "...and its status.json for the same save doesn't show a second message");
                Eq(0, Link.Send("hide", out r2), "hide"); Eq(0, Link.Send("show", out r2), "show");
                Eq(0, Link.Send("quit", out r2), "quit: ok");
                Ok(p.WaitForExit(10000), "the test copy quits within 10 s");
                Ok(File.ReadAllText(OverlaySettings.FilePath).Contains("\"Anchor\":\"br\"") && OverlaySettings.Load().OffY == -0.02, "its remembered spot is still in its overlay.json after quitting");
                Ok(File.Exists(Link.StatusPath), "it wrote status.json into its own (temp) folder");
            }
            finally
            {
                if (p != null && !p.HasExited) { try { p.Kill(); Console.WriteLine("(test copy killed)"); } catch { } }
            }
        }
    }
}
