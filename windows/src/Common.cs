using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace MSFamilyControl
{
    internal static class App
    {
        public const string Name = "MS Family Control";
        public const string Version = "1.5.1";
        public const string ServiceName = "MSFamilyControl";
        public const int LocalPort = 47613;
        public const string Copyright = "Copyright © 2026 · Entworfen und entwickelt by Marc-André Straßen mit ❤";
        public static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MS Family Control"); } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "MSFamilyControl.exe"); } }
        public static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MSFamilyControl"); } }
        public static string ConfigFile { get { return Path.Combine(DataDir, "config.json"); } }
        public static string StateFile { get { return Path.Combine(DataDir, "state.json"); } }
        public static string LogFile { get { return Path.Combine(DataDir, "service.log"); } }
        public static string ExtensionId = "EXTENSION_ID_PLACEHOLDER";
        public static string FirefoxId = "family-control@mstrassen-it.de";
    }

    internal static class Json
    {
        static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        public static string Encode(object o) { return S.Serialize(o); }
        public static Dictionary<string, object> Decode(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new Dictionary<string, object>();
            var o = S.DeserializeObject(s) as Dictionary<string, object>;
            return o ?? new Dictionary<string, object>();
        }
        public static string Str(this Dictionary<string, object> d, string k, string def = "")
        {
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }
        public static int Int(this Dictionary<string, object> d, string k, int def = 0)
        {
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return def; }
        }
        public static bool Bool(this Dictionary<string, object> d, string k, bool def = false)
        {
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            var s = Convert.ToString(v); return s == "1" || s.ToLowerInvariant() == "true";
        }
        public static Dictionary<string, object> Obj(this Dictionary<string, object> d, string k)
        {
            object v; if (d == null || !d.TryGetValue(k, out v)) return null;
            return v as Dictionary<string, object>;
        }
        public static List<object> Arr(this Dictionary<string, object> d, string k)
        {
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null) return new List<object>();
            var a = v as IEnumerable; var l = new List<object>();
            if (a == null || v is string) return l;
            foreach (var x in a) l.Add(x);
            return l;
        }
    }

    /// <summary>Gespeicherte Installation (nur SYSTEM/Administratoren lesbar)</summary>
    internal class Config
    {
        public string Server = "";
        public int DeviceId;
        public string Token = "";
        public string ChildName = "";
        public string MasterSalt = "", MasterHash = "";
        public int MasterIter = 120000, MasterRev;
        public bool BlockOtherExtensions = true;
        public string InstalledAt = "";

        public static Config Load()
        {
            try
            {
                if (!File.Exists(App.ConfigFile)) return null;
                var d = Json.Decode(File.ReadAllText(App.ConfigFile, Encoding.UTF8));
                return new Config
                {
                    Server = d.Str("server"), DeviceId = d.Int("device_id"), Token = d.Str("token"), ChildName = d.Str("child"),
                    MasterSalt = d.Str("master_salt"), MasterHash = d.Str("master_hash"), MasterIter = d.Int("master_iter", 120000), MasterRev = d.Int("master_rev"),
                    BlockOtherExtensions = d.Bool("block_ext", true), InstalledAt = d.Str("installed_at"),
                };
            }
            catch { return null; }
        }

        public void Save()
        {
            Security.EnsureProtectedDir(App.DataDir);
            var d = new Dictionary<string, object>
            {
                {"server", Server}, {"device_id", DeviceId}, {"token", Token}, {"child", ChildName}, {"master_salt", MasterSalt}, {"master_hash", MasterHash},
                {"master_iter", MasterIter}, {"master_rev", MasterRev}, {"block_ext", BlockOtherExtensions}, {"installed_at", InstalledAt}, {"version", App.Version},
            };
            var tmp = App.ConfigFile + ".tmp";
            File.WriteAllText(tmp, Json.Encode(d), Encoding.UTF8);
            if (File.Exists(App.ConfigFile)) File.Delete(App.ConfigFile);
            File.Move(tmp, App.ConfigFile);
        }

        public void SetMaster(string password)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            MasterSalt = Convert.ToBase64String(salt);
            MasterIter = 120000;
            MasterHash = Convert.ToBase64String(Security.Pbkdf2(password, salt, MasterIter, 32));
        }

        public bool CheckMaster(string password)
        {
            if (string.IsNullOrEmpty(MasterHash) || password == null) return false;
            var h = Security.Pbkdf2(password, Convert.FromBase64String(MasterSalt), MasterIter, 32);
            return Security.FixedEquals(h, Convert.FromBase64String(MasterHash));
        }
    }

    internal static class Security
    {
        /// <summary>PBKDF2-HMAC-SHA256 (kompatibel zu PHP hash_pbkdf2)</summary>
        public static byte[] Pbkdf2(string password, byte[] salt, int iterations, int length)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(password)))
            {
                var result = new byte[length];
                int blocks = (length + 31) / 32, pos = 0;
                for (int i = 1; i <= blocks; i++)
                {
                    var input = new byte[salt.Length + 4];
                    Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
                    input[salt.Length] = (byte)(i >> 24); input[salt.Length + 1] = (byte)(i >> 16); input[salt.Length + 2] = (byte)(i >> 8); input[salt.Length + 3] = (byte)i;
                    var u = hmac.ComputeHash(input); var t = (byte[])u.Clone();
                    for (int n = 1; n < iterations; n++) { u = hmac.ComputeHash(u); for (int k = 0; k < t.Length; k++) t[k] ^= u[k]; }
                    int c = Math.Min(32, length - pos); Buffer.BlockCopy(t, 0, result, pos, c); pos += c;
                }
                return result;
            }
        }

        public static bool FixedEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int r = 0; for (int i = 0; i < a.Length; i++) r |= a[i] ^ b[i];
            return r == 0;
        }

        /// <summary>Ordner nur für SYSTEM und Administratoren (Kinder mit Standardkonto können Konfiguration nicht lesen/ändern)</summary>
        public static void EnsureProtectedDir(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            try
            {
                var sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);
                var inh = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                Directory.SetAccessControl(dir, sec);
            }
            catch { }
        }

        public static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; }
        }

        public static bool IsSystem()
        {
            try { return WindowsIdentity.GetCurrent().IsSystem; } catch { return false; }
        }
    }

    internal static class Http
    {
        static Http()
        {
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288 /*TLS1.3*/; }
            catch { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
            ServicePointManager.Expect100Continue = false;
        }

        public static string Normalize(string server)
        {
            server = (server ?? "").Trim();
            if (server.Length == 0) return "";
            if (!server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !server.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) server = "http://" + server;
            server = server.TrimEnd('/');
            foreach (var s in new[] { "/device.php", "/index.php", "/install.php" })
                if (server.EndsWith(s, StringComparison.OrdinalIgnoreCase)) server = server.Substring(0, server.Length - s.Length);
            return server;
        }

        /// <summary>Ruft die Geräte-API auf. Wirft WebException bei Netzwerkfehlern, ApiException bei Serverfehlern.</summary>
        public static Dictionary<string, object> Call(string server, string action, object body, string token, int timeoutMs = 15000)
        {
            var req = (HttpWebRequest)WebRequest.Create(server + "/device.php?a=" + action);
            req.Method = body == null ? "GET" : "POST";
            req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "MSFamilyControl/" + App.Version;
            req.Accept = "application/json";
            if (!string.IsNullOrEmpty(token)) req.Headers["Authorization"] = "Bearer " + token;
            if (body != null)
            {
                var data = Encoding.UTF8.GetBytes(Json.Encode(body));
                req.ContentType = "application/json"; req.ContentLength = data.Length;
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            }
            try
            {
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    return Json.Decode(r.ReadToEnd());
            }
            catch (WebException ex)
            {
                var res = ex.Response as HttpWebResponse;
                if (res == null) throw;
                string txt;
                using (var r = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) txt = r.ReadToEnd();
                Dictionary<string, object> d;
                try { d = Json.Decode(txt); } catch { d = new Dictionary<string, object>(); }
                throw new ApiException((int)res.StatusCode, d.Str("error", "Serverfehler " + (int)res.StatusCode), d);
            }
        }

        /// <summary>Lokaler Aufruf Agent → Dienst</summary>
        public static Dictionary<string, object> Local(string path, object body = null, int timeoutMs = 4000)
        {
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + App.LocalPort + path);
            req.Proxy = null;
            req.Method = body == null ? "GET" : "POST";
            req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs;
            req.Headers["X-MSFC"] = "agent";
            if (body != null)
            {
                var data = Encoding.UTF8.GetBytes(Json.Encode(body));
                req.ContentType = "application/json"; req.ContentLength = data.Length;
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            }
            using (var res = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                return Json.Decode(r.ReadToEnd());
        }
    }

    internal class ApiException : Exception
    {
        public int Status; public Dictionary<string, object> Data2;
        public ApiException(int status, string msg, Dictionary<string, object> d) : base(msg) { Status = status; Data2 = d; }
    }

    internal static class Log
    {
        static readonly object L = new object();
        public static void Write(string msg)
        {
            try
            {
                lock (L)
                {
                    if (!Directory.Exists(App.DataDir)) return;
                    var fi = new FileInfo(App.LogFile);
                    if (fi.Exists && fi.Length > 1024 * 1024) { File.Delete(App.LogFile + ".old"); File.Move(App.LogFile, App.LogFile + ".old"); }
                    File.AppendAllText(App.LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    internal static class Fmt
    {
        public static string Dur(int sec)
        {
            if (sec < 0) sec = 0;
            int m = sec / 60, h = m / 60;
            if (h > 0) return h + " h " + (m % 60).ToString("00") + " min";
            if (m > 0) return m + " min";
            return sec + " s";
        }
    }
}
