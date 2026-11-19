// Hallow Overlay - which server the game is talking to, its ping, loss, jitter and "stalls", its region, and the game's traffic.
// Read from Windows' own Winsock events (ETW, Microsoft-Windows-Winsock-AFD) for the game's processes: nothing is injected into the
// game and no packets are captured - only who sent how many bytes to which address. Like FPS, Windows allows this for admins and
// members of "Performance Log Users"; otherwise the server stays unknown and the Ping stat keeps going to 1.1.1.1.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;

namespace HallowOverlay
{
    // The game's network traffic (from ETW) -> ServerFinder
    public class NetMonitor
    {
        static readonly Guid Afd = new Guid("E53C6823-7BB8-44BB-90DC-3F86090D48A6");   // Microsoft-Windows-Winsock-AFD
        readonly EtwSession etw;
        public readonly ServerFinder Finder = new ServerFinder();
        volatile HashSet<int> pids = new HashSet<int>();
        // socket (AFD endpoint) -> what we know: protocol from "create", remote address from "connect"
        class Sock { public bool Udp; public IPAddress Ip; public int Port; }
        readonly Dictionary<long, Sock> socks = new Dictionary<long, Sock>();

        public NetMonitor() { etw = new EtwSession("HallowOverlayNet", new[] { Afd }, new[] { ulong.MaxValue }, OnEvent); }
        public bool Running { get { return etw.Running; } }
        public bool Denied { get { return etw.Denied; } }
        public bool Start() { return etw.Start(); }
        public void Stop() { etw.Stop(); lock (socks) socks.Clear(); Finder.Clear(); }
        public void SetPids(IEnumerable<int> p) { pids = new HashSet<int>(p); }

        // sockaddr at `at`: IPv4 (family 2) or IPv6 (23, IPv4-mapped turned into IPv4)
        static IPAddress Addr(IntPtr ud, int at, int len, out int port)
        {
            port = 0; if (len < at + 8) return null;
            int fam = (ushort)Marshal.ReadInt16(ud, at);
            port = (Marshal.ReadByte(ud, at + 2) << 8) | Marshal.ReadByte(ud, at + 3);
            if (fam == 2) { var b = new byte[4]; Marshal.Copy(ud + at + 4, b, 0, 4); return new IPAddress(b); }
            if (fam == 23 && len >= at + 24) { var b = new byte[16]; Marshal.Copy(ud + at + 8, b, 0, 16); var a = new IPAddress(b); return a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a; }
            return null;
        }

        // Winsock-AFD payloads start with EnterExit (0 = call, 1 = done), Location, Process, Endpoint (at 16).
        //   1000 create (enter): family at 24, type at 28, protocol at 32     1018 connect (enter): sockaddr at 44
        //   1007 send-to / 1009 receive-from (exit): bytes at 36, status at 40, sockaddr at 48
        //   1003 send / 1004 receive on a connected socket (exit): bytes at 36, status at 40
        void OnEvent(IntPtr rec)
        {
            int pid = Marshal.ReadInt32(rec, 12);
            if (!pids.Contains(pid)) return;
            int id = (ushort)Marshal.ReadInt16(rec, 40);
            if (id != 1000 && id != 1018 && id != 1007 && id != 1009 && id != 1003 && id != 1004) return;
            int len = (ushort)Marshal.ReadInt16(rec, 86); IntPtr ud = Marshal.ReadIntPtr(rec, 96);
            if (len < 24) return;
            int enterExit = Marshal.ReadInt32(ud, 0); long ep = Marshal.ReadInt64(ud, 16); long ts = Marshal.ReadInt64(rec, 16);
            if (id == 1000)
            {
                if (enterExit != 0 || len < 36) return;
                lock (socks) { if (socks.Count > 4000) socks.Clear(); socks[ep] = new Sock { Udp = Marshal.ReadInt32(ud, 32) == 17 }; }
                return;
            }
            if (id == 1018)
            {
                if (enterExit != 0) return; int port; var ip = Addr(ud, 44, len, out port); if (ip == null) return;
                lock (socks) { Sock s; if (!socks.TryGetValue(ep, out s)) { s = new Sock { Udp = false }; socks[ep] = s; } s.Ip = ip; s.Port = port; }
                return;
            }
            if (enterExit != 1 || len < 44 || Marshal.ReadInt32(ud, 40) != 0) return;
            int bytes = Marshal.ReadInt32(ud, 36); if (bytes <= 0) return;
            bool send = id == 1007 || id == 1003;
            if (id == 1007 || id == 1009)
            {
                int port; var ip = Addr(ud, 48, len, out port); if (ip == null) return;
                Finder.Add(ts, ip, port, true, send, bytes);
            }
            else
            {
                Sock s; lock (socks) socks.TryGetValue(ep, out s);
                if (s == null || s.Ip == null) return;   // a socket opened before we started watching
                Finder.Add(ts, s.Ip, s.Port, s.Udp, send, bytes);
            }
        }
    }

    // Picks the game server from the traffic: the public address with the most UDP packets in the last 5 s (TCP if there's no UDP),
    // switching only after a new one has led for 3 s. Also counts the game's up / down traffic and "stalls" (the server went quiet
    // for 0.5 s or more while the game kept sending). Times are UTC file times (100 ns).
    public class ServerFinder
    {
        public struct Pkt { public long Ts; public IPAddress Ip; public int Port; public bool Udp, Send; public int Bytes; }
        const long S = 10000000L;
        readonly object gate = new object();
        readonly Queue<Pkt> recent = new Queue<Pkt>();
        IPAddress current, pending; long pendingSince;
        readonly List<long> stalls = new List<long>(); long lastStallEnd;
        public IPAddress Server { get { lock (gate) return current; } }
        public int Changes { get; private set; }   // goes up each time the server changes from one to another
        public double UpKbps = -1, DownKbps = -1;
        public int Stalls { get { lock (gate) return stalls.Count; } }   // in the last minute
        public int TotalStalls { get; private set; }                     // since this finder was made (match summaries count the difference)

        public void Clear() { lock (gate) { recent.Clear(); current = pending = null; stalls.Clear(); UpKbps = DownKbps = -1; } }

        public void Add(long ts, IPAddress ip, int port, bool udp, bool send, int bytes)
        {
            if (IPAddress.IsLoopback(ip)) return;
            lock (gate) { recent.Enqueue(new Pkt { Ts = ts, Ip = ip, Port = port, Udp = udp, Send = send, Bytes = bytes }); if (recent.Count > 60000) recent.Dequeue(); }
        }

        // Addresses that are never the game server: private / LAN, CGNAT and Tailscale (100.64/10), link-local, multicast, broadcast.
        public static bool Ignored(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();
            if (b.Length == 4)
                return b[0] == 10 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254)
                    || (b[0] == 100 && b[1] >= 64 && b[1] < 128) || b[0] >= 224;
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC || ip.Equals(IPAddress.IPv6Any);
        }
        // DNS, and web traffic (HTTPS / QUIC: store pages, voice and CDNs) aren't the game.
        public static bool IgnoreDns = true;   // (tests turn this off to use DNS queries as stand-in game traffic)
        public static bool IgnoredPort(int port, bool udp) { return (IgnoreDns && port == 53) || port == 443 || port == 80 || port == 5353; }

        // Call about once a second.
        public void Tick(long now)
        {
            lock (gate)
            {
                while (recent.Count > 0 && recent.Peek().Ts < now - 70 * S) recent.Dequeue();
                var all = recent.ToArray();
                // traffic: the last 2 complete seconds (events arrive up to ~1 s late)
                long a = now - 3 * S, z = now - S; long up = 0, down = 0;
                foreach (var p in all) if (p.Ts >= a && p.Ts < z) { if (p.Send) up += p.Bytes; else down += p.Bytes; }
                UpKbps = up * 8 / 2000.0; DownKbps = down * 8 / 2000.0;
                // the busiest public address in the last 5 s
                var win = all.Where(p => p.Ts >= now - 6 * S && !Ignored(p.Ip) && !IgnoredPort(p.Port, p.Udp)).ToList();
                IPAddress top = Busiest(win.Where(p => p.Udp)) ?? Busiest(win.Where(p => !p.Udp));
                if (top != null && !top.Equals(current))
                {
                    if (current == null) { current = top; pending = null; ResetStalls(now); }
                    else if (pending == null || !pending.Equals(top)) { pending = top; pendingSince = now; }
                    else if (now - pendingSince >= 3 * S) { current = top; pending = null; Changes++; ResetStalls(now); }
                }
                else pending = null;
                if (current != null && !all.Any(p => p.Ip.Equals(current) && p.Ts >= now - 20 * S)) { current = null; pending = null; }   // the game left the server
                if (current != null) FindStalls(all, now);
                stalls.RemoveAll(t => t < now - 60 * S);
            }
        }
        static IPAddress Busiest(IEnumerable<Pkt> ps)
        {
            var g = ps.GroupBy(p => p.Ip).Select(x => new { Ip = x.Key, N = x.Count() }).OrderByDescending(x => x.N).FirstOrDefault();
            return g != null && g.N >= 10 ? g.Ip : null;
        }
        void ResetStalls(long now) { stalls.Clear(); lastStallEnd = now - 3 * S; }   // ignore what happened before this server
        // A stall: nothing received from the server for 0.5 s or more while the game sent at least twice in that time.
        void FindStalls(Pkt[] all, long now)
        {
            long from = Math.Max(lastStallEnd, now - 15 * S), to = now - 2 * S; if (to <= from) return;
            var mine = all.Where(p => p.Ip.Equals(current) && p.Ts > from && p.Ts <= to).OrderBy(p => p.Ts).ToList();
            var recv = mine.Where(p => !p.Send).Select(p => p.Ts).ToList();
            if (recv.Count == 0) return;   // nothing heard at all: that's "left the server", not a stall
            for (int i = 1; i < recv.Count; i++)
            {
                long g0 = recv[i - 1], g1 = recv[i];
                if (g1 - g0 >= S / 2 && mine.Count(p => p.Send && p.Ts > g0 && p.Ts < g1) >= 2) { stalls.Add(g1); lastStallEnd = g1; TotalStalls++; }
            }
        }
    }

    // Pings the server once a second. If it doesn't answer ping, a traceroute (once) finds the last hop that does, and that's
    // pinged instead (Approx: shown with "~"). Loss and jitter are over the last minute of whichever target is used.
    public class ServerPing
    {
        readonly object gate = new object();
        IPAddress want; volatile bool stop; Thread thread;
        public IPAddress Target { get; private set; }   // what's pinged now (the server or a hop), null = nothing answers
        public bool Approx { get; private set; }
        public int HopsBefore { get; private set; }     // the hop is this many hops before where the trace ended
        public double Ms = -1, LossPct = -1, JitterMs = -1;
        public string Mode = "";                        // "direct", "hop", "none", "" (no server / still finding out)

        public void Set(IPAddress server)
        {
            lock (gate)
            {
                if (server == null ? want == null : server.Equals(want)) return;
                want = server; Target = null; Approx = false; Ms = LossPct = JitterMs = -1; Mode = "";
                if (thread == null) { thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "overlay server ping" }; thread.Start(); }
            }
        }
        public void Stop() { stop = true; }

        void Loop()
        {
            var ping = new Ping(); IPAddress forServer = null; var last = new Queue<long>(); int failsAtStart = 0;
            while (!stop)
            {
                IPAddress srv; lock (gate) srv = want;
                if (srv == null) { forServer = null; Thread.Sleep(500); continue; }
                if (!srv.Equals(forServer)) { forServer = srv; last.Clear(); failsAtStart = 0; lock (gate) { Target = srv; Approx = false; Mode = ""; } }
                var t0 = DateTime.UtcNow; IPAddress target; lock (gate) target = Target;
                if (target == null) { Thread.Sleep(1000); continue; }
                long rtt = -1;
                try { var r = ping.Send(target, 900); if (r.Status == IPStatus.Success) rtt = r.RoundtripTime; } catch { }
                if (!Approx && Mode == "")
                {
                    if (rtt >= 0) lock (gate) Mode = "direct";
                    else if (++failsAtStart >= 4)
                    {
                        // the server ignores ping: trace the route once and use the last hop that answers
                        int hopsBefore; var hop = Trace(srv, out hopsBefore);
                        lock (gate)
                        {
                            if (!srv.Equals(want)) continue;
                            Target = hop; Approx = hop != null; HopsBefore = hopsBefore; Mode = hop != null ? "hop" : "none"; Ms = LossPct = JitterMs = -1;
                        }
                        last.Clear(); continue;
                    }
                }
                if (Mode == "" && rtt < 0) { Thread.Sleep(Math.Max(0, 1000 - (int)(DateTime.UtcNow - t0).TotalMilliseconds)); continue; }   // not decided yet: don't count as loss
                last.Enqueue(rtt); while (last.Count > 60) last.Dequeue();
                var a = last.ToArray(); int lost = a.Count(v => v < 0); double jit = 0; int pairs = 0;
                for (int i = 1; i < a.Length; i++) if (a[i] >= 0 && a[i - 1] >= 0) { jit += Math.Abs(a[i] - a[i - 1]); pairs++; }
                lock (gate)
                {
                    if (srv.Equals(want))
                    {
                        Ms = rtt >= 0 ? rtt : (a.Any(v => v >= 0) ? Ms : -1);
                        LossPct = a.Length >= 10 ? lost * 100.0 / a.Length : -1; JitterMs = pairs >= 5 ? jit / pairs : -1;
                    }
                }
                var wait = 1000 - (int)(DateTime.UtcNow - t0).TotalMilliseconds; if (wait > 0) Thread.Sleep(wait);
            }
        }

        // TTL-limited pings, up to 30 hops; stops after 4 silent hops in a row once something answered.
        public static IPAddress Trace(IPAddress dest, out int hopsBefore)
        {
            hopsBefore = 0; IPAddress lastHop = null; int lastTtl = 0, silent = 0, ttl;
            using (var p = new Ping())
            {
                for (ttl = 1; ttl <= 30; ttl++)
                {
                    PingReply r = null;
                    try { r = p.Send(dest, 800, new byte[32], new PingOptions(ttl, true)); } catch { }
                    if (r != null && (r.Status == IPStatus.TtlExpired || r.Status == IPStatus.Success) && r.Address != null && !r.Address.Equals(IPAddress.Any))
                    {
                        lastHop = r.Address; lastTtl = ttl; silent = 0;
                        if (r.Status == IPStatus.Success) break;
                    }
                    else if (lastHop != null && ++silent >= 4) break;
                }
            }
            hopsBefore = lastHop == null ? 0 : Math.Max(0, Math.Min(ttl, 30) - lastTtl);
            return lastHop;
        }
    }

    // Server names: reverse DNS (in the background, 2 s at most) and a small table of city / region codes found in host names.
    public static class ServerNames
    {
        public class Info { public string Host, Code, City; }
        static readonly Dictionary<string, Info> cache = new Dictionary<string, Info>();
        public static Info Get(IPAddress ip)
        {
            var key = ip.ToString();
            lock (cache)
            {
                Info i; if (cache.TryGetValue(key, out i)) return i;
                i = new Info(); cache[key] = i;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var ar = Dns.BeginGetHostEntry(ip, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(2000)) return;
                        var h = Dns.EndGetHostEntry(ar).HostName;
                        if (string.IsNullOrEmpty(h) || h == key) return;
                        string city; var code = Region(h, out city);
                        lock (cache) { i.Host = h; i.Code = code; i.City = city; }
                    }
                    catch { }
                });
                return i;
            }
        }

        // code -> city (airport / city codes that game and cloud hosts put in their names)
        static readonly Dictionary<string, string> Codes = new Dictionary<string, string> {
            { "fra", "Frankfurt" }, { "ams", "Amsterdam" }, { "lhr", "London" }, { "lon", "London" }, { "cdg", "Paris" }, { "par", "Paris" }, { "mad", "Madrid" },
            { "mxp", "Milan" }, { "mil", "Milan" }, { "waw", "Warsaw" }, { "sto", "Stockholm" }, { "arn", "Stockholm" }, { "hel", "Helsinki" }, { "vie", "Vienna" },
            { "zrh", "Zurich" }, { "prg", "Prague" }, { "dub", "Dublin" }, { "cph", "Copenhagen" }, { "osl", "Oslo" }, { "ist", "Istanbul" }, { "bud", "Budapest" },
            { "buh", "Bucharest" }, { "otp", "Bucharest" }, { "sof", "Sofia" }, { "lis", "Lisbon" }, { "bru", "Brussels" }, { "dus", "Dusseldorf" }, { "muc", "Munich" }, { "ber", "Berlin" },
            { "iad", "Washington DC" }, { "was", "Washington DC" }, { "ash", "Ashburn" }, { "nyc", "New York" }, { "jfk", "New York" }, { "ewr", "Newark" },
            { "ord", "Chicago" }, { "chi", "Chicago" }, { "dfw", "Dallas" }, { "dal", "Dallas" }, { "atl", "Atlanta" }, { "mia", "Miami" }, { "lax", "Los Angeles" },
            { "sjc", "San Jose" }, { "sfo", "San Francisco" }, { "sea", "Seattle" }, { "den", "Denver" }, { "phx", "Phoenix" }, { "slc", "Salt Lake City" }, { "yyz", "Toronto" }, { "tor", "Toronto" },
            { "yul", "Montreal" }, { "gru", "Sao Paulo" }, { "sao", "Sao Paulo" }, { "scl", "Santiago" }, { "lim", "Lima" }, { "bog", "Bogota" }, { "eze", "Buenos Aires" },
            { "mex", "Mexico City" }, { "sin", "Singapore" }, { "sgp", "Singapore" }, { "hkg", "Hong Kong" }, { "tyo", "Tokyo" }, { "nrt", "Tokyo" }, { "hnd", "Tokyo" },
            { "osa", "Osaka" }, { "kix", "Osaka" }, { "icn", "Seoul" }, { "sel", "Seoul" }, { "syd", "Sydney" }, { "mel", "Melbourne" }, { "per", "Perth" }, { "akl", "Auckland" },
            { "bom", "Mumbai" }, { "maa", "Chennai" }, { "dxb", "Dubai" }, { "jnb", "Johannesburg" }, { "tpe", "Taipei" }, { "bkk", "Bangkok" },
            { "kul", "Kuala Lumpur" }, { "cgk", "Jakarta" }, { "mnl", "Manila" }, { "bah", "Bahrain" }, { "tlv", "Tel Aviv" },
        };
        static readonly Dictionary<string, string> Cities = new Dictionary<string, string> {
            { "frankfurt", "fra" }, { "amsterdam", "ams" }, { "london", "lhr" }, { "paris", "cdg" }, { "madrid", "mad" }, { "warsaw", "waw" }, { "stockholm", "sto" },
            { "chicago", "ord" }, { "dallas", "dfw" }, { "seattle", "sea" }, { "ashburn", "ash" }, { "virginia", "iad" }, { "singapore", "sin" }, { "tokyo", "tyo" },
            { "sydney", "syd" }, { "mumbai", "bom" }, { "dubai", "dxb" }, { "seoul", "icn" }, { "hongkong", "hkg" }, { "toronto", "yyz" }, { "losangeles", "lax" },
        };
        // cloud region names (AWS-style) -> code
        static readonly Dictionary<string, string> Clouds = new Dictionary<string, string> {
            { "eu-central-1", "fra" }, { "eu-west-1", "dub" }, { "eu-west-2", "lhr" }, { "eu-west-3", "cdg" }, { "eu-north-1", "sto" }, { "eu-south-1", "mil" },
            { "us-east-1", "iad" }, { "us-east-2", "cmh" }, { "us-west-1", "sfo" }, { "us-west-2", "pdx" }, { "ca-central-1", "yul" }, { "sa-east-1", "gru" },
            { "ap-northeast-1", "tyo" }, { "ap-northeast-2", "icn" }, { "ap-northeast-3", "osa" }, { "ap-southeast-1", "sin" }, { "ap-southeast-2", "syd" },
            { "ap-south-1", "bom" }, { "ap-east-1", "hkg" }, { "me-south-1", "bah" }, { "af-south-1", "jnb" },
        };

        // "FRA" -> "Frankfurt (FRA)"; anything else as it is
        public static string Named(string code)
        {
            string n; if (code != null && code.Length == 3 && Codes.TryGetValue(code.ToLowerInvariant(), out n)) return n + " (" + code + ")";
            return code == "CMH" ? "Ohio (CMH)" : code == "PDX" ? "Oregon (PDX)" : code;
        }
        // "fra1-gs12.example.net" -> "FRA" (city "Frankfurt"); null when nothing is recognized.
        public static string Region(string host, out string city)
        {
            city = null; if (string.IsNullOrEmpty(host)) return null;
            var h = host.ToLowerInvariant();
            foreach (var c in Clouds) if (h.Contains(c.Key)) { string n; city = Codes.TryGetValue(c.Value, out n) ? n : (c.Value == "cmh" ? "Ohio" : c.Value == "pdx" ? "Oregon" : null); return c.Value.ToUpperInvariant(); }
            foreach (var raw in h.Split('.', '-', '_'))
            {
                var t = raw.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                string n;
                if (t.Length == 3 && Codes.TryGetValue(t, out n)) { city = n; return t.ToUpperInvariant(); }
                if (Cities.TryGetValue(t, out n)) { city = Codes[n]; return n.ToUpperInvariant(); }
            }
            return null;
        }
    }
}
