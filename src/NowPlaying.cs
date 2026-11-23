// Hallow Overlay 1.24 - what the "now playing" stat says, in one place (tested in tests\OverlayTests.exe):
//   - from Hallow Mixer's local API (/state or /api/wait): mic muted, the song, and Mixer 3.27's "spotifyAdMuted";
//   - from Windows' media sessions (WinMedia, used when Mixer isn't running): the first session that is playing;
//   - Mixer's %APPDATA%\HallowMixer\status.json (read only): "spotifyAdMuted" when the API is off or hasn't said yet.
// A Spotify ad ("Advertisement", or "Spotify" with no artist) shows as "Advertisement"; while Mixer mutes it: "Ad muted".
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace HallowOverlay
{
    // one media session as Windows (or a test) describes it
    public class MediaInfo
    {
        public string App, Title, Artist; public bool Playing;
        public MediaInfo() { }
        public MediaInfo(string app, string title, string artist, bool playing) { App = app; Title = title; Artist = artist; Playing = playing; }
    }

    // what Mixer's API answered
    public class MixerState
    {
        public bool MicMuted, AdMuted, HasAdField; public string Title, Artist; public bool Playing; public int Rev = -1;
        public string Line { get { return Playing ? NowPlaying.Line(Title, Artist) : null; } }
    }

    public static class NowPlaying
    {
        public const int MaxChars = 48;
        public const string AdText = "Advertisement", AdMutedText = "Ad muted";

        static bool Eq(string a, string b) { return string.Equals((a ?? "").Trim(), b, StringComparison.OrdinalIgnoreCase); }

        // "Artist - Title", or just the title; null without a title
        public static string Line(string title, string artist)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            return string.IsNullOrWhiteSpace(artist) ? title.Trim() : artist.Trim() + " - " + title.Trim();
        }

        // A Spotify ad, the way Mixer 3.27 tells: "Advertisement" or "Spotify" with no real artist. A real artist's song that is
        // called "Advertisement" is music, not an ad.
        public static bool IsAd(string title, string artist)
        {
            bool noArtist = string.IsNullOrWhiteSpace(artist) || Eq(artist, "Spotify") || Eq(artist, AdText);
            return noArtist && (Eq(title, AdText) || Eq(title, "Spotify"));
        }

        // the first session that is playing and has a title (Windows lists them in its own order)
        public static MediaInfo Choose(IEnumerable<MediaInfo> sessions)
        {
            if (sessions == null) return null;
            return sessions.FirstOrDefault(s => s != null && s.Playing && !string.IsNullOrWhiteSpace(s.Title));
        }

        // What the panel shows: "Ad muted" while Mixer mutes an ad, "Advertisement" for an ad, else the line (cut to fit).
        public static string Display(string title, string artist, bool playing, bool adMuted)
        {
            if (adMuted) return AdMutedText;
            if (!playing) return null;
            if (IsAd(title, artist)) return AdText;
            return Fit(Line(title, artist));
        }
        public static string Display(MediaInfo m, bool adMuted) { return m == null ? (adMuted ? AdMutedText : null) : Display(m.Title, m.Artist, m.Playing, adMuted); }
        public static string Fit(string line) { return line == null || line.Length <= MaxChars ? line : line.Substring(0, MaxChars - 1).TrimEnd() + "…"; }

        static bool B(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v; }

        // Mixer's /state (or /api/wait) JSON; null when it isn't JSON
        public static MixerState ParseMixer(string json)
        {
            Dictionary<string, object> st;
            try { st = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json ?? ""); } catch { return null; }
            if (st == null) return null;
            var m = new MixerState(); object o;
            if (st.TryGetValue("mic", out o)) m.MicMuted = B(o as Dictionary<string, object>, "muted");
            if (st.TryGetValue("nowPlaying", out o) && o is Dictionary<string, object>)
            {
                var d = (Dictionary<string, object>)o; object t, a;
                d.TryGetValue("title", out t); d.TryGetValue("artist", out a);
                m.Title = t as string; m.Artist = a as string; m.Playing = B(d, "playing");
            }
            if (st.TryGetValue("spotifyAdMuted", out o)) { m.HasAdField = true; m.AdMuted = o is bool && (bool)o; }
            if (st.TryGetValue("rev", out o)) try { m.Rev = Convert.ToInt32(o); } catch { }
            return m;
        }

        // ---- Mixer's status.json: "spotifyAdMuted" (Mixer 3.27+), only while Mixer runs and the file is fresh (it's rewritten
        // every minute and whenever an ad starts or ends)
        // null = the file says nothing usable (missing, not Mixer, Mixer quit, older than 3 minutes, an older Mixer without the field)
        public static bool? AdMutedInStatus(string json, DateTime now)
        {
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json ?? "");
                if (d == null) return null;
                object v; if (!d.TryGetValue("app", out v) || !Eq(v as string, "Mixer")) return null;
                if (d.ContainsKey("running") && !B(d, "running")) return null;
                DateTime t; if (!d.TryGetValue("updated", out v) || !DateTime.TryParse(v as string ?? "", out t) || Math.Abs((now - t).TotalMinutes) > 3) return null;
                if (!d.TryGetValue("spotifyAdMuted", out v)) return null;
                return v is bool && (bool)v;
            }
            catch { return null; }
        }
        public static string MixerDir { get { var e = Environment.GetEnvironmentVariable("HALLOW_MIXER_DIR"); return string.IsNullOrEmpty(e) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowMixer") : e; } }
        static readonly object gate = new object(); static DateTime fileTime = DateTime.MinValue, readAt = DateTime.MinValue; static string fileText;
        // read at most every 2 s, and only again when the file changed
        public static bool? AdMutedNow(DateTime now)
        {
            lock (gate)
            {
                if ((now - readAt).TotalSeconds >= 2)
                {
                    readAt = now;
                    try
                    {
                        var f = Path.Combine(MixerDir, "status.json");
                        if (!File.Exists(f)) { fileText = null; fileTime = DateTime.MinValue; }
                        else { var t = File.GetLastWriteTimeUtc(f); if (t != fileTime) { fileTime = t; fileText = File.ReadAllText(f); } }
                    }
                    catch { fileText = null; }
                }
                return fileText == null ? null : AdMutedInStatus(fileText, now);
            }
        }
        public static void Forget() { lock (gate) { fileTime = readAt = DateTime.MinValue; fileText = null; } }
    }

    // ---- Hallow Clips: "Replay saved" when Clips' status.json says a replay was just saved. Clips may or may not write it (field
    // "replaySaved", or "lastReplay" / "lastSaved": a time, or an object with "time"); without it nothing changes. Clips' own
    // "flash Clip saved" over the pipe still comes first, so the same save never shows twice.
    public static class ClipsLink
    {
        public static string Dir { get { var e = Environment.GetEnvironmentVariable("HALLOW_CLIPS_DIR"); return string.IsNullOrEmpty(e) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HallowClips") : e; } }
        public const string Text = "Replay saved";
        public static readonly string[] Fields = { "replaySaved", "lastReplay", "lastSaved" };

        // when the last replay was saved, if the status says
        public static DateTime? SavedAt(string json)
        {
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json ?? "");
                if (d == null) return null;
                object app; if (d.TryGetValue("app", out app) && !string.Equals(app as string, "Clips", StringComparison.OrdinalIgnoreCase)) return null;
                foreach (var k in Fields)
                {
                    object v; if (!d.TryGetValue(k, out v) || v == null) continue;
                    var o = v as Dictionary<string, object>; object t;
                    var s = o != null ? (o.TryGetValue("time", out t) || o.TryGetValue("at", out t) ? t as string : null) : v as string;
                    DateTime when; if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, out when)) return when;
                }
            }
            catch { }
            return null;
        }

        // Watches one status file: True once per new save, only while it's fresh (20 s). The first look never shows an old one.
        public class Watch
        {
            public Func<string> File = () => Path.Combine(Dir, "status.json");
            DateTime readAt = DateTime.MinValue, fileTime = DateTime.MinValue; DateTime? seen; bool first = true;
            public bool Check(DateTime now)
            {
                if ((now - readAt).TotalSeconds < 3) return false;
                readAt = now; DateTime? at = null;
                try
                {
                    var f = File(); if (!System.IO.File.Exists(f)) { first = false; return false; }
                    var t = System.IO.File.GetLastWriteTimeUtc(f); if (t == fileTime) { first = false; return false; }
                    fileTime = t; at = SavedAt(System.IO.File.ReadAllText(f));
                }
                catch { return false; }
                if (first) { first = false; seen = at; return false; }
                if (at == null || (seen != null && at <= seen)) return false;
                seen = at;
                return Math.Abs((now - at.Value).TotalSeconds) <= 20;
            }
        }
    }
}
