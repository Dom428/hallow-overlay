// Hallow Overlay - "now playing" straight from Windows (the same info as the volume pop-up / media keys), used when
// Hallow Mixer isn't running. Read only, through Windows' media session API; nothing is sent anywhere.
using System;
using System.Linq;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;

namespace HallowOverlay
{
    public static class WinMedia
    {
        static GlobalSystemMediaTransportControlsSessionManager mgr;

        static T Wait<T>(IAsyncOperation<T> op)
        {
            var end = DateTime.UtcNow.AddMilliseconds(2000);
            while (op.Status == AsyncStatus.Started)
            {
                if (DateTime.UtcNow > end) { try { op.Cancel(); } catch { } throw new TimeoutException(); }
                Thread.Sleep(10);
            }
            if (op.Status != AsyncStatus.Completed) throw new InvalidOperationException("media session: " + op.Status);
            return op.GetResults();
        }

        // "Artist - Title" like Mixer's, only while something is actually playing; null otherwise.
        public static string Line(string title, string artist) { return NowPlaying.Line(title, artist); }

        // 1.24: the playing session as a MediaInfo (the choice and the text are NowPlaying's, so tests can use made-up sessions)
        public static MediaInfo ReadInfo()
        {
            try
            {
                if (mgr == null) mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                var s = mgr.GetSessions().FirstOrDefault(x => { try { return x.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { return false; } });
                if (s == null) return null;
                var p = Wait(s.TryGetMediaPropertiesAsync());
                string app = null; try { app = s.SourceAppUserModelId; } catch { }
                return p == null ? null : NowPlaying.Choose(new[] { new MediaInfo(app, p.Title, p.Artist, true) });
            }
            catch { mgr = null; return null; }
        }
        public static string Read() { return NowPlaying.Display(ReadInfo(), false); }
    }
}
