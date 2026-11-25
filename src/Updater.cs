using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace MyTaskManager
{
    /* Updates from GitHub Releases: checks the latest release, downloads MyTaskManager.exe,
       swaps it in next to the running one (a running .exe can be renamed, not overwritten) and restarts.
       Tasks live in %APPDATA%, so they are untouched by an update. */
    public static class Updater
    {
        public const string Repo = "trmiizi/my-task-manager";
        public const string AssetName = "MyTaskManager.exe";
        public static string ReleasesPage { get { return "https://github.com/" + Repo + "/releases/latest"; } }

        public class Release
        {
            public Version Version;
            public string Tag, Url, Notes, Sha256;
            public long Size;
        }

        public static Version Current
        {
            get { Version v = Assembly.GetExecutingAssembly().GetName().Version; return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
        }

        public static string CurrentText { get { return Current.ToString(3); } }

        public static bool IsNewer(Release r) { return r != null && r.Version != null && r.Url != null && r.Version > Current; }

        /* Parses the GitHub "latest release" JSON. Returns null when it has no usable version or asset. */
        public static Release Parse(string json)
        {
            Dictionary<string, object> d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (d == null || !d.ContainsKey("tag_name")) return null;
            Release r = new Release { Tag = Convert.ToString(d["tag_name"]) };
            Version v;
            string tag = (r.Tag ?? "").Trim().TrimStart('v', 'V');
            if (!Version.TryParse(tag, out v)) return null;
            r.Version = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            r.Notes = d.ContainsKey("body") ? Convert.ToString(d["body"]) : "";
            object assets;
            if (d.TryGetValue("assets", out assets) && assets is IEnumerable)
            {
                foreach (object o in (IEnumerable)assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null || !string.Equals(Convert.ToString(a["name"]), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    r.Url = Convert.ToString(a["browser_download_url"]);
                    r.Size = a.ContainsKey("size") ? Convert.ToInt64(a["size"]) : 0;
                    string dg = a.ContainsKey("digest") ? Convert.ToString(a["digest"]) : null;
                    if (dg != null && dg.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) r.Sha256 = dg.Substring(7).ToLowerInvariant();
                }
            }
            return r;
        }

        static void Tls() { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }

        /* Blocking: call from a background thread. Throws on network problems. */
        public static Release CheckLatest()
        {
            Tls();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
            req.UserAgent = "MyTaskManager-Updater";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 20000;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                return Parse(sr.ReadToEnd());
        }

        static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }
        static string NewPath { get { return Path.Combine(Path.GetDirectoryName(ExePath), "MyTaskManager.update.exe"); } }
        static string OldPath { get { return Path.Combine(Path.GetDirectoryName(ExePath), "MyTaskManager.old.exe"); } }

        /* Blocking: downloads and checks the new .exe. Returns an error message, or null when it is ready to swap in. */
        public static string Download(Release r)
        {
            try
            {
                Tls();
                if (File.Exists(NewPath)) File.Delete(NewPath);
                using (WebClient wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "MyTaskManager-Updater";
                    wc.DownloadFile(r.Url, NewPath);
                }
                string bad = Verify(NewPath, r);
                if (bad != null) { try { File.Delete(NewPath); } catch { } return bad; }
                return null;
            }
            catch (UnauthorizedAccessException) { return "This folder is read-only, so the app can't update itself here."; }
            catch (Exception ex) { Log.Write(ex); return "Download failed: " + ex.Message; }
        }

        public static string Verify(string file, Release r)
        {
            FileInfo fi = new FileInfo(file);
            if (!fi.Exists || fi.Length < 50 * 1024) return "The download looks incomplete.";
            if (r.Size > 0 && fi.Length != r.Size) return "The download is the wrong size.";
            byte[] head = new byte[2];
            using (FileStream fs = File.OpenRead(file)) { fs.Read(head, 0, 2); }
            if (head[0] != 'M' || head[1] != 'Z') return "The download is not a program file.";
            if (r.Sha256 != null)
            {
                using (FileStream fs = File.OpenRead(file))
                using (SHA256 sha = SHA256.Create())
                {
                    string h = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                    if (h != r.Sha256) return "The download failed its safety check (checksum mismatch).";
                }
            }
            return null;
        }

        /* Swaps the downloaded .exe in and starts it. The caller must shut down right after a null (success) result. */
        public static string SwapAndRestart()
        {
            string exe = ExePath;
            try
            {
                if (File.Exists(OldPath)) File.Delete(OldPath);
            }
            catch { }
            try
            {
                File.Move(exe, OldPath);
                try { File.Move(NewPath, exe); }
                catch { File.Move(OldPath, exe); throw; }
                Process.Start(exe, "--after-update " + Process.GetCurrentProcess().Id);
                return null;
            }
            catch (Exception ex) { Log.Write(ex); return "Could not install the update: " + ex.Message; }
        }

        /* On start: wait for the old copy to close, then tidy up the leftover file. */
        public static void AfterUpdate(string[] args)
        {
            int i = Array.IndexOf(args, "--after-update");
            if (i >= 0 && i + 1 < args.Length)
            {
                int pid;
                if (int.TryParse(args[i + 1], out pid))
                {
                    try { Process.GetProcessById(pid).WaitForExit(15000); } catch { }
                }
            }
            try { if (File.Exists(OldPath)) File.Delete(OldPath); } catch { }
            try { if (File.Exists(NewPath)) File.Delete(NewPath); } catch { }
        }
    }
}
