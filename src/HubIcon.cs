// Hallow Overlay - "One Hallow icon": while Hallow Hub hosts the family's tray icon, Overlay hides its own and its messages
// come from Hub's icon. If Hub isn't running, says no or doesn't answer, Overlay's own icon comes back (never no icon at all).
using System;
using System.Threading;

namespace HallowOverlay
{
    public class HubIcon
    {
        public Func<bool> Enabled = () => true;   // the "Use the Hallow icon" setting
        public Action<bool> Changed;              // true = Hub hosts the icon (hide ours), false = show ours; called from a background thread
        public int Every = 5000;
        volatile bool hosted, stop; readonly AutoResetEvent now = new AutoResetEvent(false);
        public bool Hosted { get { return hosted; } }

        public void Start()
        {
            new Thread(() =>
            {
                while (!stop)
                {
                    bool on = false;
                    try { on = Enabled() && AskTrayHost(); } catch { }
                    if (on != hosted) { hosted = on; if (Changed != null) try { Changed(on); } catch { } }
                    now.WaitOne(Every);
                }
            }) { IsBackground = true, Name = "overlay hub icon" }.Start();
        }
        public void CheckNow() { now.Set(); }
        public void Stop() { stop = true; now.Set(); }

        public static bool AskTrayHost()
        {
            return HostsIcon(Link.Ask(Link.HubPipe, "tray-host", 1000));
        }
        // Hub's answer to "tray-host": ok {"on":true} = it shows the one Hallow icon
        public static bool HostsIcon(string reply) { return reply != null && reply.StartsWith("ok ") && reply.Replace(" ", "").Contains("\"on\":true"); }
        // A message from Hub's icon. False when Hub didn't take it.
        public static bool Notify(string title, string text)
        {
            return Took(Link.Ask(Link.HubPipe, NotifyLine(title, text), 1000));
        }
        // 1.24 (tested): one line - "notify Overlay<TAB>title<TAB>text"; tabs and line breaks inside become spaces, so the line can't split
        public static string NotifyLine(string title, string text)
        {
            Func<string, string> clean = s => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
            return "notify Overlay\t" + clean(title) + "\t" + clean(text);
        }
        public static bool Took(string reply) { return reply != null && (reply == "ok" || reply.StartsWith("ok ")); }
    }
}
