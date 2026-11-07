using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;

namespace MSFamilyControl
{
    /// <summary>
    /// Kategorie-Filterlisten (Pornografie, Betrug, Phishing …) lokal auf dem PC.
    /// Speichersparend als sortierte 64-Bit-Hashes (≈ 8 Byte je Domain, 1 Mio. Domains ≈ 8 MB).
    /// </summary>
    internal class FilterStore
    {
        readonly object L = new object();
        readonly Dictionary<string, ulong[]> lists = new Dictionary<string, ulong[]>();
        readonly Dictionary<string, string> hashes = new Dictionary<string, string>();
        readonly Dictionary<string, string> names = new Dictionary<string, string>();
        static string Dir { get { return Path.Combine(App.DataDir, "filters"); } }

        public static ulong Fnv(string s)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < s.Length; i++) { h ^= (byte)s[i]; h *= 1099511628211UL; }
            return h;
        }

        static ulong[] Parse(string gzFile)
        {
            var l = new List<ulong>(100000);
            using (var fs = File.OpenRead(gzFile))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var r = new StreamReader(gz, Encoding.ASCII))
            {
                string line;
                while ((line = r.ReadLine()) != null) { line = line.Trim(); if (line.Length > 3) l.Add(Fnv(line)); }
            }
            var a = l.ToArray(); Array.Sort(a); return a;
        }

        /// <summary>Gleicht die lokalen Listen mit der Richtlinie ab (lädt nur geänderte nach)</summary>
        public void Sync(List<object> wanted, string server, string token)
        {
            Security.EnsureProtectedDir(App.DataDir);
            Directory.CreateDirectory(Dir);
            var keep = new HashSet<string>();
            foreach (var o in wanted)
            {
                var d = o as Dictionary<string, object>; if (d == null) continue;
                string key = d.Str("key"), hash = d.Str("hash");
                if (key == "" || key.Any(c => c < 'a' || c > 'z')) continue;
                keep.Add(key);
                lock (L) names[key] = d.Str("name", key);
                string file = Path.Combine(Dir, key + ".gz"), meta = Path.Combine(Dir, key + ".hash");
                string have; lock (L) hashes.TryGetValue(key, out have);
                if (have == hash) continue;
                try
                {
                    if (!(File.Exists(file) && File.Exists(meta) && File.ReadAllText(meta).Trim() == hash))
                    {
                        Download(server + "/device.php?a=filter&key=" + key, token, file + ".tmp");
                        if (File.Exists(file)) File.Delete(file);
                        File.Move(file + ".tmp", file);
                        File.WriteAllText(meta, hash);
                    }
                    var arr = Parse(file);
                    lock (L) { lists[key] = arr; hashes[key] = hash; }
                    Log.Write("Filterliste " + key + ": " + arr.Length + " Einträge");
                }
                catch (Exception ex) { Log.Write("Filterliste " + key + ": " + ex.Message); }
            }
            lock (L)
                foreach (var k in lists.Keys.ToList())
                    if (!keep.Contains(k)) { lists.Remove(k); hashes.Remove(k); try { File.Delete(Path.Combine(Dir, k + ".gz")); File.Delete(Path.Combine(Dir, k + ".hash")); } catch { } }
        }

        /// <summary>Beim Start: vorhandene Listen ohne Netz laden (Offline-Schutz)</summary>
        public void LoadLocal()
        {
            try
            {
                if (!Directory.Exists(Dir)) return;
                foreach (var f in Directory.GetFiles(Dir, "*.gz"))
                {
                    string key = Path.GetFileNameWithoutExtension(f), meta = Path.Combine(Dir, key + ".hash");
                    if (!File.Exists(meta)) continue;
                    var arr = Parse(f);
                    lock (L) { lists[key] = arr; hashes[key] = File.ReadAllText(meta).Trim(); if (!names.ContainsKey(key)) names[key] = key; }
                }
            }
            catch (Exception ex) { Log.Write("Filter laden: " + ex.Message); }
        }

        static void Download(string url, string token, string target)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 120000; req.ReadWriteTimeout = 120000; req.UserAgent = "MSFamilyControl/" + App.Version;
            req.Headers["Authorization"] = "Bearer " + token;
            using (var res = (HttpWebResponse)req.GetResponse())
            using (var s = res.GetResponseStream())
            using (var f = File.Create(target)) s.CopyTo(f);
        }

        /// <summary>Gibt Kategorie-Schlüssel zurück, wenn host (oder eine übergeordnete Domain) gelistet ist</summary>
        public string Match(string host, out string matched)
        {
            matched = "";
            host = (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
            if (host.Length < 4 || host.IndexOf('.') < 0) return null;
            lock (L)
            {
                if (lists.Count == 0) return null;
                string h = host;
                while (true)
                {
                    ulong x = Fnv(h);
                    foreach (var kv in lists) if (Array.BinarySearch(kv.Value, x) >= 0) { matched = h; return kv.Key; }
                    int dot = h.IndexOf('.');
                    if (dot < 0 || h.IndexOf('.', dot + 1) < 0) break; // nicht bis zur TLD kürzen
                    h = h.Substring(dot + 1);
                }
            }
            return null;
        }

        public string Name(string key) { lock (L) { string n; return names.TryGetValue(key, out n) ? n : key; } }
        public int Total { get { lock (L) return lists.Values.Sum(a => a.Length); } }
    }
}
