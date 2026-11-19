// Hallow Overlay - live numbers for Hallow Remote's "Live stats" tab, in status (pipe and status.json) under "live":
// {"game","fps","fpsLow","ping","pingTo","region","updated"}. Never the server's IP: its region code, or its host name only when
// "Hide the server's IP address" is off. Values that aren't known are null.
using System;
using System.Collections.Generic;

namespace HallowOverlay
{
    public static class LiveStats
    {
        public static Dictionary<string, object> For(string game, Sample x, bool haveServer, string region, string host, bool hideIp)
        {
            object ping = null; string to = null;
            if (haveServer && x.ServerMs >= 0) { ping = Math.Round(x.ServerMs); to = "server"; }
            else if (x.PingMs >= 0) { ping = Math.Round(x.PingMs); to = "internet"; }
            string where = haveServer ? (region ?? (hideIp ? null : host)) : null;
            return new Dictionary<string, object> {
                { "game", game }, { "fps", x.Fps >= 0 ? (object)Math.Round(x.Fps) : null }, { "fpsLow", x.FpsLow >= 0 ? (object)Math.Round(x.FpsLow) : null },
                { "ping", ping }, { "pingTo", to }, { "region", where }, { "updated", DateTime.Now.ToString("s") } };
        }
    }
}
