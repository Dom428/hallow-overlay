// Hallow Overlay - updates from GitHub releases (asks first, checks the download's SHA-256 before installing).
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace HallowOverlay
{
    [DataContract] public class GhAsset
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "browser_download_url")] public string Url;
        [DataMember(Name = "digest")] public string Digest;   // "sha256:..."
    }
    [DataContract] public class GhRelease
    {
        [DataMember(Name = "tag_name")] public string Tag;
        [DataMember(Name = "body")] public string Notes;
        [DataMember(Name = "assets")] public GhAsset[] Assets;
        public Version Version { get { Version v; return Version.TryParse((Tag ?? "").TrimStart('v', 'V'), out v) ? v : new Version(0, 0); } }
        public GhAsset Zip { get { return Assets == null ? null : Assets.FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)); } }
    }

    public static class Updater
    {
        public const string Repo = "Dom428/hallow-overlay";
        public static Version Current { get { return typeof(Updater).Assembly.GetName().Version; } }
        public static string Show(Version v) { return v == null ? "" : v.Build > 0 ? v.ToString(3) : v.ToString(2); }   // 1.23.1: "1.23.1", "1.23"
        // 1.24 (tested): the update row's text and button - versions as people write them ("1.24", "1.23.1", never "1.24.0.0")
        public static string PromptText(Version newer, string have) { return "Hallow Overlay " + Show(newer) + " is available (you have " + have + "). It's checked against GitHub's checksum before it installs, then Overlay reopens."; }
        public static string ButtonText(Version newer) { return "Update to v" + Show(newer); }
        public static bool IsNewer(GhRelease r, Version current) { return r != null && r.Zip != null && r.Version > new Version(current.Major, current.Minor, Math.Max(0, current.Build)); }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "HallowOverlay";
            return wc;
        }

        // Newest release on GitHub, or null if it can't be reached.
        public static GhRelease Latest()
        {
            try
            {
                using (var wc = Client())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    var json = wc.DownloadData("https://api.github.com/repos/" + Repo + "/releases/latest");
                    using (var ms = new MemoryStream(json)) return (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(ms);
                }
            }
            catch { return null; }
        }
        public static bool IsNewer(GhRelease r) { return IsNewer(r, Current); }

        // Downloads the release zip, checks its SHA-256 against GitHub's, unpacks it and runs its installer
        // (which closes this copy, installs the new one and opens it). Returns an error message, or null when it started.
        public static string Install(GhRelease r)
        {
            var a = r.Zip; if (a == null) return "That release has no zip.";
            if (string.IsNullOrEmpty(a.Digest) || !a.Digest.StartsWith("sha256:")) return "GitHub didn't give a checksum for the download, so it wasn't installed.";
            var dir = Path.Combine(Path.GetTempPath(), "HallowOverlayUpdate");
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                var zip = Path.Combine(dir, "update.zip");
                using (var wc = Client()) wc.DownloadFile(a.Url, zip);
                string script; var err = Unpack(zip, a.Digest, Path.Combine(dir, "files"), out script); if (err != null) return err;
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true });
                return null;
            }
            catch (Exception e) { return "Update failed: " + e.Message; }
        }

        // Checks the zip's SHA-256 against GitHub's digest ("sha256:..."), unpacks it and finds its installer. An error message, or null.
        public static string Unpack(string zip, string digest, string into, out string script)
        {
            script = null;
            if (string.IsNullOrEmpty(digest) || !digest.StartsWith("sha256:")) return "GitHub didn't give a checksum for the download, so it wasn't installed.";
            string hash;
            using (var f = File.OpenRead(zip)) using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            if (hash != digest.Substring(7).ToLowerInvariant()) return "The download didn't match GitHub's checksum, so it wasn't installed.";
            ZipFile.ExtractToDirectory(zip, into);
            script = Directory.GetFiles(into, "Install.ps1", SearchOption.AllDirectories).FirstOrDefault();   // the zip has a "Hallow Overlay" folder
            return script == null ? "The update is missing its installer." : null;
        }
    }
}
