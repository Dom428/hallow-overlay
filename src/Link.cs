// Hallow Overlay - the Hallow family link: a named pipe other Hallow apps (and "HallowOverlay.exe --cmd") send one-line
// commands to, and %APPDATA%\HallowOverlay\status.json. The pipe is for your Windows account only and never on the network.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace HallowOverlay
{
    public static class Link
    {
        public static string PipeName { get { return "HallowFamily.Overlay." + Environment.UserName; } }
        public static string StatusPath { get { return Path.Combine(OverlaySettings.Dir, "status.json"); } }

        // Reads one line (up to \n or the end), giving up after `ms`.
        static string ReadLine(Stream s, int ms)
        {
            var buf = new byte[4096]; int n = 0; var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (n < buf.Length)
            {
                var left = (int)(until - DateTime.UtcNow).TotalMilliseconds; if (left <= 0) return null;
                var ar = s.BeginRead(buf, n, buf.Length - n, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(left)) return null;
                int r = s.EndRead(ar); if (r <= 0) break;
                n += r; if (Array.IndexOf(buf, (byte)'\n', 0, n) >= 0) break;
            }
            var text = Encoding.UTF8.GetString(buf, 0, n); int nl = text.IndexOf('\n');
            return (nl >= 0 ? text.Substring(0, nl) : text).TrimEnd('\r');
        }

        // Server: one connection at a time; each command runs on the UI thread with a 5 s limit.
        public static void Serve(Dispatcher ui, Func<string, string> handle)
        {
            new Thread(() =>
            {
                while (true)
                {
                    NamedPipeServerStream p = null;
                    try
                    {
                        var sec = new PipeSecurity();
                        sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                        p = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, sec);
                        p.WaitForConnection();
                        var line = ReadLine(p, 2000); string reply;
                        if (string.IsNullOrWhiteSpace(line)) reply = "error no command";
                        else
                        {
                            try
                            {
                                var op = ui.BeginInvoke(handle, line);
                                if (op.Wait(TimeSpan.FromSeconds(5)) == DispatcherOperationStatus.Completed) reply = (op.Result as string) ?? "error";
                                else { op.Abort(); reply = "error busy"; }
                            }
                            catch (Exception e) { reply = "error " + e.Message; }
                        }
                        var b = Encoding.UTF8.GetBytes(reply.Replace("\r", " ").Replace("\n", " ") + "\n");
                        p.Write(b, 0, b.Length); p.Flush();
                        try { p.WaitForPipeDrain(); } catch { }
                    }
                    catch { Thread.Sleep(500); }
                    finally { if (p != null) try { p.Dispose(); } catch { } }
                }
            }) { IsBackground = true, Name = "Hallow family link" }.Start();
        }

        // Client for --cmd: 0 = ok, 1 = error, 2 = Hallow Overlay isn't running.
        public static int Send(string line, out string reply)
        {
            reply = null;
            Mutex m; if (!Mutex.TryOpenExisting(@"Local\HallowOverlay", out m)) { reply = "error Hallow Overlay isn't running"; return 2; }
            m.Dispose();
            try
            {
                using (var c = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    c.Connect(1500);
                    var b = Encoding.UTF8.GetBytes(line.Trim() + "\n"); c.Write(b, 0, b.Length); c.Flush();
                    reply = ReadLine(c, 7000);
                }
            }
            catch (TimeoutException) { reply = "error Hallow Overlay didn't answer"; return 1; }
            catch (Exception e) { reply = "error " + e.Message; return 1; }
            if (reply == null) { reply = "error no answer"; return 1; }
            return reply == "ok" || reply.StartsWith("ok ") ? 0 : 1;
        }

        public static string Json(Dictionary<string, object> d) { return new JavaScriptSerializer().Serialize(d); }

        // Hallow Hub's pipe (HALLOW_HUB_PIPE replaces it for tests with a fake Hub)
        public static string HubPipe
        {
            get { var e = Environment.GetEnvironmentVariable("HALLOW_HUB_PIPE"); return string.IsNullOrEmpty(e) ? "HallowFamily.Hub." + Environment.UserName : e; }
        }
        // One command to another app's pipe: its reply, or null when it isn't there or doesn't answer in time.
        public static string Ask(string pipe, string line, int ms)
        {
            try
            {
                using (var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    c.Connect(ms);
                    var b = Encoding.UTF8.GetBytes(line.Replace("\n", " ") + "\n"); c.Write(b, 0, b.Length); c.Flush();
                    return ReadLine(c, ms + 1000);
                }
            }
            catch { return null; }
        }

        // status.json, written atomically (tmp file, then replace)
        public static void WriteStatus(string json)
        {
            if (Snap.On) return;
            try
            {
                Directory.CreateDirectory(OverlaySettings.Dir);
                var tmp = StatusPath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(StatusPath)) File.Replace(tmp, StatusPath, null); else File.Move(tmp, StatusPath);
            }
            catch { }
        }
    }
}
