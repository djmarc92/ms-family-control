using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace MSFamilyControl
{
    /// <summary>
    /// Herzstück im Windows-Dienst (läuft als SYSTEM): Zeitkonto, Regeln, Sperrentscheidung, Watchdog für die
    /// Benutzer-App, App-/Webseitensperren, Synchronisation mit dem Server, lokale Schnittstelle für App + Browser-Erweiterung.
    /// </summary>
    internal class Engine
    {
        readonly object L = new object();
        Config cfg;
        Thread tickThread, syncThread;
        HttpListener http;
        volatile bool running;
        readonly AutoResetEvent syncNow = new AutoResetEvent(false);
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // --- Zustand ---
        Dictionary<string, object> policy;         // zuletzt vom Server
        string serverUsedDay = ""; int serverUsed;  // Nutzung laut Server (alle Geräte des Kindes)
        Batch current = new Batch();
        List<Batch> pending = new List<Batch>();
        List<Dictionary<string, object>> events = new List<Dictionary<string, object>>();
        List<LocalRequest> requests = new List<LocalRequest>();
        DateTime localFreeUntil = DateTime.MinValue, pausedUntil = DateTime.MinValue;
        bool pausedUntilReboot;
        string warnedDay = ""; HashSet<int> warned = new HashSet<int>();
        string lastReason = "";
        readonly Dictionary<string, DateTime> extSeen = new Dictionary<string, DateTime>();
        string lastDomain = ""; DateTime lastDomainAt = DateTime.MinValue;
        readonly Queue<Dictionary<string, object>> notices = new Queue<Dictionary<string, object>>();
        DateTime lastSyncOk = DateTime.MinValue, offlineSince = DateTime.MinValue;
        string syncError = "";
        bool revoked;

        // Agent (Benutzer-App)
        int agentPid; uint agentSession = uint.MaxValue; DateTime agentSeen = DateTime.MinValue, agentStarted = DateTime.MinValue;
        int agentIdle; string agentExe = "", agentLabel = ""; bool agentFullscreen;
        DateTime lastTamper = DateTime.MinValue, activeSince = DateTime.MinValue;
        uint lastSession = uint.MaxValue;
        string policySid = ""; DateTime policyTry = DateTime.MinValue;

        // Uhr: Serverzeit + monotone Zeit → Zeitmanipulation am PC wirkt nicht
        DateTime anchorTime = DateTime.MinValue; long anchorTicks;
        DateTime lastTimeJumpEvent = DateTime.MinValue;

        // Master-Passwort Fehlversuche
        int masterFails; DateTime masterBlockedUntil = DateTime.MinValue;

        string appliedHosts = null;
        readonly FilterStore filters = new FilterStore();
        readonly Dictionary<string, string> checkCache = new Dictionary<string, string>();
        DateTime lastAppKill = DateTime.MinValue;
        readonly Dictionary<string, DateTime> appKillEvent = new Dictionary<string, DateTime>();

        class Batch
        {
            public string Id = Guid.NewGuid().ToString("N");
            public string Day = DateTime.Now.ToString("yyyy-MM-dd");
            public Dictionary<int, int> Hours = new Dictionary<int, int>();
            public Dictionary<string, object[]> Apps = new Dictionary<string, object[]>();   // exe → [label, sec]
            public Dictionary<string, int> Web = new Dictionary<string, int>();
            public int Total { get { return Hours.Values.Sum(); } }
            public bool Empty { get { return Hours.Count == 0 && Web.Count == 0; } }
            public Dictionary<string, object> ToJson()
            {
                return new Dictionary<string, object> {
                    {"id", Id}, {"day", Day},
                    {"hours", Hours.ToDictionary(k => k.Key.ToString(CI), v => (object)v.Value)},
                    {"apps", Apps.ToDictionary(k => k.Key, v => (object)new Dictionary<string, object>{{"label", v.Value[0]}, {"sec", v.Value[1]}})},
                    {"web", Web.ToDictionary(k => k.Key, v => (object)v.Value)},
                };
            }
            public static Batch FromJson(Dictionary<string, object> d)
            {
                var b = new Batch { Id = d.Str("id"), Day = d.Str("day") };
                var h = d.Obj("hours"); if (h != null) foreach (var kv in h) b.Hours[int.Parse(kv.Key, CI)] = Convert.ToInt32(kv.Value, CI);
                var a = d.Obj("apps"); if (a != null) foreach (var kv in a) { var o = kv.Value as Dictionary<string, object>; if (o != null) b.Apps[kv.Key] = new object[] { o.Str("label"), o.Int("sec") }; }
                var w = d.Obj("web"); if (w != null) foreach (var kv in w) b.Web[kv.Key] = Convert.ToInt32(kv.Value, CI);
                return b;
            }
        }

        class LocalRequest
        {
            public string LocalId = Guid.NewGuid().ToString("N").Substring(0, 20);
            public string Kind = "time", Reason = "other", Message = "", Domain = "", Status = "queued", Label = "";
            public int Minutes = 30, ServerId, Granted;
            public DateTime Created = DateTime.Now;
            public Dictionary<string, object> ToJson()
            {
                return new Dictionary<string, object> { {"local_id", LocalId}, {"kind", Kind}, {"reason", Reason}, {"message", Message}, {"domain", Domain}, {"status", Status},
                    {"minutes", Minutes}, {"server_id", ServerId}, {"granted", Granted}, {"created", Created.ToString("o")}, {"label", Label} };
            }
            public static LocalRequest FromJson(Dictionary<string, object> d)
            {
                DateTime c; DateTime.TryParse(d.Str("created"), CI, DateTimeStyles.RoundtripKind, out c);
                return new LocalRequest { LocalId = d.Str("local_id"), Kind = d.Str("kind", "time"), Reason = d.Str("reason"), Message = d.Str("message"), Domain = d.Str("domain"),
                    Status = d.Str("status"), Minutes = d.Int("minutes", 30), ServerId = d.Int("server_id"), Granted = d.Int("granted"), Label = d.Str("label"), Created = c == DateTime.MinValue ? DateTime.Now : c };
            }
        }

        // ================= Start / Stopp =================
        public void Start()
        {
            cfg = Config.Load();
            if (cfg == null) { Log.Write("Keine Konfiguration – Dienst wartet."); }
            LoadState();
            filters.LoadLocal();
            testMode = policy != null && policy.Bool("test_mode");
            running = true;
            AddEvent("agent_start", "Schutz gestartet (" + Environment.MachineName + ")", "Version " + App.Version);
            StartHttp();
            tickThread = new Thread(TickLoop) { IsBackground = true, Name = "tick" }; tickThread.Start();
            syncThread = new Thread(SyncLoop) { IsBackground = true, Name = "sync" }; syncThread.Start();
            Log.Write("Dienst gestartet");
            // Frisch eingeschaltet (nicht nur Dienst-Neustart nach Update)? → Einschalt-Alarm, sobald das Netz da ist
            try
            {
                if (GetTickCount64() < 10UL * 60 * 1000)
                    new Thread(() => { Thread.Sleep(20000); PowerOn("eingeschaltet"); }) { IsBackground = true }.Start();
            }
            catch { }
        }

        public void Stop(bool shutdown)
        {
            running = false;
            try
            {
                lock (L) { RotateBatch(); }
                if (shutdown) SyncOnce(true, 4000);
            }
            catch { }
            try { http.Stop(); } catch { }
            SaveState();
            Log.Write("Dienst beendet");
        }

        public void SessionChanged() { syncNow.Set(); }

        // ================= Zeit =================
        DateTime Now()
        {
            if (anchorTime == DateTime.MinValue) return DateTime.Now;
            double sec = (Stopwatch.GetTimestamp() - anchorTicks) / (double)Stopwatch.Frequency;
            return anchorTime.AddSeconds(sec);
        }

        // ================= Haupttakt (1 s) =================
        void TickLoop()
        {
            DateTime lastSave = DateTime.Now, lastHosts = DateTime.MinValue;
            while (running)
            {
                try
                {
                    Tick();
                    if ((DateTime.Now - lastSave).TotalSeconds > 30) { SaveState(); lastSave = DateTime.Now; }
                    if ((DateTime.Now - lastHosts).TotalSeconds > 20) { ApplyHosts(); lastHosts = DateTime.Now; }
                }
                catch (Exception ex) { Log.Write("Tick: " + ex.Message); }
                Thread.Sleep(1000);
            }
        }

        bool Paused { get { return pausedUntilReboot || pausedUntil > Now(); } }

        void Tick()
        {
            if (cfg == null) { cfg = Config.Load(); if (cfg == null) return; }
            var now = Now();
            // Zeitmanipulation erkennen
            if (anchorTime != DateTime.MinValue && Math.Abs((DateTime.Now - now).TotalMinutes) > 5 && (DateTime.Now - lastTimeJumpEvent).TotalHours > 1)
            {
                lastTimeJumpEvent = DateTime.Now;
                AddEvent("time_jump", "Uhrzeit am PC wurde verstellt", "PC: " + DateTime.Now.ToString("dd.MM. HH:mm") + " · tatsächlich: " + now.ToString("dd.MM. HH:mm"));
            }
            uint session = Native.WTSGetActiveConsoleSessionId();
            string user = session == 0xFFFFFFFF ? "" : Native.SessionUser(session);
            bool childSession = user.Length > 0 && !IsExempt(user);
            if (session != lastSession) { lastSession = session; activeSince = DateTime.MinValue; }

            lock (L)
            {
                if (current.Day != now.ToString("yyyy-MM-dd")) RotateBatch(now);
                var ev = Evaluate(now);
                string reason = ev.Reason;

                // Zählen
                bool winLocked = childSession && Native.SessionLocked(session);
                bool agentFresh = (DateTime.Now - agentSeen).TotalSeconds < 6;
                bool idle = agentFresh && agentIdle > 600 && !agentFullscreen;
                if (childSession && !winLocked && reason == "" && !idle && !Paused)
                {
                    int h = now.Hour;
                    int v; current.Hours.TryGetValue(h, out v); current.Hours[h] = v + 1;
                    string exe = agentFresh && agentExe != "" ? agentExe : "unbekannt";
                    object[] a; if (!current.Apps.TryGetValue(exe, out a)) { a = new object[] { agentLabel != "" ? agentLabel : exe, 0 }; current.Apps[exe] = a; }
                    if (agentLabel != "") a[0] = agentLabel;
                    a[1] = (int)a[1] + 1;
                    if (activeSince == DateTime.MinValue) activeSince = now;
                }

                // Sperr-Ereignis + Vorwarnungen
                if (childSession && reason != lastReason)
                {
                    if (reason != "") AddEvent("lock", reason == "time" ? cfgName() + ": Bildschirmzeit aufgebraucht" : reason == "bedtime" ? cfgName() + ": Bettzeit – PC gesperrt" : reason == "manual" ? cfgName() + ": PC von Eltern gesperrt" : reason == "school" ? cfgName() + ": Schulzeit – PC gesperrt" : cfgName() + ": PC noch nicht freigegeben", "");
                    lastReason = reason;
                    syncNow.Set();
                }
                if (warnedDay != now.ToString("yyyy-MM-dd")) { warnedDay = now.ToString("yyyy-MM-dd"); warned.Clear(); }
                if (childSession && reason == "" && !Paused)
                {
                    int remain = ev.SecondsToLock;
                    foreach (var w in WarnMinutes())
                        if (remain > 0 && remain <= w * 60 && remain > w * 60 - 30 && !warned.Contains(w))
                        {
                            warned.Add(w);
                            notices.Enqueue(new Dictionary<string, object> { { "type", "warn" }, { "minutes", w }, { "kind", ev.LockKind } });
                        }
                }
            }

            bool safe; int yt; lock (L) { safe = policy == null || policy.Bool("safesearch", true); yt = policy == null ? 0 : policy.Int("youtube_restrict"); }
            string sig = session + ":" + user + ":" + safe + ":" + yt;
            if (!childSession && user.Length > 0 && policySid != "rm:" + sig)
            {
                // Eltern-Konto (Testmodus aus): eventuell gesetzte Richtlinien wieder entfernen
                try { var sid = BrowserPolicy.SidOfSession(session); if (sid != null) { BrowserPolicy.Remove(sid); policySid = "rm:" + sig; } } catch { }
            }
            if (childSession && policySid != sig && (DateTime.Now - policyTry).TotalSeconds > 20)
            {
                policyTry = DateTime.Now;
                try
                {
                    if (BrowserPoliciesEnabled() && BrowserPolicy.Apply(BrowserPolicy.SidOfSession(session), safe, yt)) { policySid = sig; Log.Write("Browser-Richtlinien für " + user + " gesetzt"); }
                    else if (!BrowserPoliciesEnabled()) policySid = sig;
                }
                catch (Exception ex) { Log.Write("Browser-Richtlinien: " + ex.Message); }
            }
            if (childSession && !Paused)
            {
                Watchdog(session, user);
                if ((DateTime.Now - lastAppKill).TotalSeconds >= 2) { lastAppKill = DateTime.Now; KillBlockedApps(); }
                // Rückfallebene: Sperre nötig, aber App reagiert nicht → Windows-Sitzung trennen (Anmeldebildschirm)
                string r; lock (L) r = lastReason;
                if (r != "" && agentStarted != DateTime.MinValue && (DateTime.Now - agentStarted).TotalSeconds > 25 && (DateTime.Now - agentSeen).TotalSeconds > 20 && !Native.SessionLocked(session))
                {
                    Log.Write("Sperre ohne App-Antwort → Sitzung wird getrennt");
                    AddEvent("tamper", "Sperre umgangen? Sitzung wurde getrennt", "Die Familien-App hat nicht reagiert.");
                    Native.WTSDisconnectSession(IntPtr.Zero, session, false);
                    agentSeen = DateTime.Now;
                }
            }
        }

        string cfgName() { var c = policy != null ? policy.Obj("child") : null; return c != null ? c.Str("name", "Kind") : (cfg != null && cfg.ChildName != "" ? cfg.ChildName : "Kind"); }

        IEnumerable<int> WarnMinutes()
        {
            var l = policy != null ? policy.Arr("warn_minutes").Select(x => Convert.ToInt32(x, CI)).ToList() : new List<int>();
            return l.Count > 0 ? l : new List<int> { 15, 5, 1 };
        }

        static bool BrowserPoliciesEnabled()
        {
            try { using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\MSFamilyControl")) return k == null || Convert.ToInt32(k.GetValue("BrowserPolicies", 1)) == 1; }
            catch { return true; }
        }

        volatile bool testMode;

        bool IsExempt(string user)
        {
            if (testMode) return false; // Testmodus: Eltern-Konto wird wie ein Kinder-Konto behandelt
            try
            {
                var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\MSFamilyControl");
                var v = key == null ? null : key.GetValue("ExemptUsers") as string[];
                if (v == null) return false;
                return v.Any(x => string.Equals(x, user, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        // ================= Regeln auswerten (identisch zur Serverlogik) =================
        internal class Eval { public string Reason = ""; public int Remaining = -1; public bool Unlimited = true; public string Until = ""; public int SecondsToLock = int.MaxValue; public string LockKind = "time"; public string Start = "", End = ""; public int Used; public int LimitMin = 1440; public int Bonus; }

        static DateTime ParseDT(string s)
        {
            DateTime d;
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CI, DateTimeStyles.AssumeLocal, out d)) return d;
            if (DateTime.TryParse(s, CI, DateTimeStyles.AssumeLocal, out d)) return d;
            return DateTime.MinValue;
        }

        static int Hm(string t) { var p = (t ?? "00:00").Split(':'); int h, m; int.TryParse(p[0], out h); int.TryParse(p.Length > 1 ? p[1] : "0", out m); return h * 60 + m; }

        string DayType(DateTime now)
        {
            string mode = policy.Str("mode", "auto");
            if (mode == "holiday") return "holiday";
            string d = now.ToString("yyyy-MM-dd");
            if (mode == "auto")
                foreach (var o in policy.Arr("holidays"))
                {
                    var a = o as object[] ?? (o as System.Collections.ArrayList)?.ToArray();
                    if (a == null) { var l = o as System.Collections.IList; if (l != null) { a = new object[l.Count]; l.CopyTo(a, 0); } }
                    if (a != null && a.Length >= 2 && string.CompareOrdinal(d, Convert.ToString(a[0])) >= 0 && string.CompareOrdinal(d, Convert.ToString(a[1])) <= 0) return "holiday";
                }
            return (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday) ? "weekend" : "school";
        }

        int UsedToday(DateTime now)
        {
            string day = now.ToString("yyyy-MM-dd");
            int used = serverUsedDay == day ? serverUsed : 0;
            foreach (var b in pending) if (b.Day == day) used += b.Total;
            if (current.Day == day) used += current.Total;
            return used;
        }

        Eval Evaluate(DateTime now)
        {
            var e = new Eval();
            if (policy == null || policy.Obj("today") == null) return e;
            var today = policy.Obj("today");
            string day = now.ToString("yyyy-MM-dd");
            bool same = today.Str("day") == day;
            string type = same ? today.Str("day_type") : DayType(now);
            var rules = policy.Obj("rules");
            var rule = rules != null ? rules.Obj(type) : null;
            int limit = rule != null ? rule.Int("limit_min", 1440) : today.Int("limit_min", 1440);
            string start = rule != null ? rule.Str("start_time", "00:00") : today.Str("start", "00:00");
            // Abend richtet sich nach dem nächsten Tag (Fr-Abend = Wochenende, So-Abend = Schultag, Ferien = Ferien)
            string night = policy.Bool("evening_next_day") ? DayType(now.AddDays(1)) : type;
            var nrule = rules != null ? rules.Obj(night) : null;
            string end = nrule != null ? nrule.Str("end_time", "24:00") : today.Str("end", "24:00");
            int bonus = same ? today.Int("bonus_min") : 0;
            e.Used = UsedToday(now); e.LimitMin = limit; e.Bonus = bonus; e.Start = start; e.End = end;
            e.Unlimited = limit >= 1440;
            e.Remaining = e.Unlimited ? -1 : Math.Max(0, (limit + bonus) * 60 - e.Used);
            var freeUntil = ParseDT(today.Str("free_until"));
            var lockUntil = ParseDT(today.Str("lock_until"));
            int nowMin = now.Hour * 60 + now.Minute;
            int secToEnd = end == "24:00" ? int.MaxValue : (Hm(end) * 60 - (nowMin * 60 + now.Second));

            if (localFreeUntil > now) { e.Until = localFreeUntil.ToString("HH:mm"); return e; }
            if (freeUntil > now) { e.Until = freeUntil.ToString("HH:mm"); return e; }
            if (lockUntil > now) { e.Reason = "manual"; e.Until = lockUntil.Date == now.Date ? lockUntil.ToString("HH:mm") : lockUntil.ToString("dd.MM. HH:mm"); return e; }
            if (nowMin < Hm(start)) { e.Reason = "early"; e.Until = start; return e; }
            if (end != "24:00" && nowMin >= Hm(end)) { e.Reason = "bedtime"; e.Until = end; return e; }
            var school = policy.Obj("school");
            if (type == "school" && school != null && school.Bool("lock") && nowMin >= Hm(school.Str("from", "07:00")) && nowMin < Hm(school.Str("to", "15:00")))
            { e.Reason = "school"; e.Until = school.Str("to", "15:00"); return e; }
            if (!e.Unlimited && e.Remaining <= 0) { e.Reason = "time"; return e; }
            e.SecondsToLock = Math.Min(e.Unlimited ? int.MaxValue : e.Remaining, secToEnd);
            e.LockKind = (!e.Unlimited && e.Remaining <= secToEnd) ? "time" : "bedtime";
            return e;
        }

        // ================= Watchdog =================
        void Watchdog(uint session, string user)
        {
            bool alive = false;
            if (agentPid > 0)
            {
                try { var p = Process.GetProcessById(agentPid); alive = !p.HasExited && Native.SessionOf(p) == session; } catch { alive = false; }
            }
            if (!alive)
            {
                // läuft evtl. schon (z. B. nach Dienst-Neustart)?
                foreach (var p in Process.GetProcessesByName("MSFamilyControl"))
                    try { if (Native.SessionOf(p) == session && p.Id != Process.GetCurrentProcess().Id) { agentPid = p.Id; alive = true; agentSession = session; break; } } catch { }
            }
            if (alive) return;
            if (agentPid > 0 && agentSession == session && (DateTime.Now - agentStarted).TotalSeconds > 10)
            {
                if ((DateTime.Now - lastTamper).TotalMinutes > 5)
                {
                    lastTamper = DateTime.Now;
                    AddEvent("tamper", cfgName() + " hat versucht, die Familien-App zu beenden", "Windows-Benutzer: " + user + " · App wurde automatisch neu gestartet");
                    syncNow.Set();
                }
            }
            try
            {
                string exe = File.Exists(App.InstalledExe) ? App.InstalledExe : Process.GetCurrentProcess().MainModule.FileName;
                agentPid = Native.StartInSession(session, exe, "--agent", true);
                agentSession = session; agentStarted = DateTime.Now; agentSeen = DateTime.Now;
            }
            catch (Exception ex)
            {
                agentPid = 0; // Benutzer meldet sich evtl. gerade an
                if ((DateTime.Now - agentStarted).TotalSeconds > 60) { Log.Write("Agent-Start: " + ex.Message); agentStarted = DateTime.Now; }
            }
        }

        // ================= Apps sperren =================
        // Windows-Bordmittel: im strengen Modus immer erlaubt (zusätzlich zur Liste vom Server)
        static readonly string[] SystemExes = { "unbekannt", "msfamilycontrol.exe", "explorer.exe", "searchhost.exe", "searchapp.exe", "searchui.exe", "startmenuexperiencehost.exe",
            "shellexperiencehost.exe", "applicationframehost.exe", "systemsettings.exe", "textinputhost.exe", "lockapp.exe", "logonui.exe", "taskmgr.exe", "control.exe",
            "rundll32.exe", "dllhost.exe", "consent.exe", "sihost.exe", "ctfmon.exe", "conhost.exe", "openwith.exe", "pickerhost.exe", "credentialuibroker.exe",
            "useroobebroker.exe", "werfault.exe", "smartscreen.exe", "notepad.exe", "calc.exe", "calculatorapp.exe", "mspaint.exe", "snippingtool.exe",
            "screenclippinghost.exe", "screensketch.exe", "photos.exe", "microsoft.photos.exe", "securityhealthhost.exe", "mmc.exe", "msiexec.exe" };
        readonly Dictionary<string, DateTime> strictReported = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Strenger Modus: Programm im Vordergrund ist weder in der Liste noch ein Windows-Bordmittel → schließen und melden.</summary>
        void StrictCheck(HashSet<string> blocked)
        {
            string fg, label; bool fresh;
            HashSet<string> allowed = new HashSet<string>(SystemExes, StringComparer.OrdinalIgnoreCase);
            lock (L)
            {
                if (policy == null || !policy.Bool("strict_apps")) return;
                fg = agentExe; label = agentLabel; fresh = (DateTime.Now - agentSeen).TotalSeconds < 6;
                foreach (var x in policy.Arr("known_apps")) allowed.Add(Convert.ToString(x, CI).ToLowerInvariant());
                foreach (var x in policy.Arr("system_apps")) allowed.Add(Convert.ToString(x, CI).ToLowerInvariant());
                foreach (var x in policy.Arr("app_grants")) allowed.Add(Convert.ToString(x, CI).ToLowerInvariant());
            }
            fg = (fg ?? "").ToLowerInvariant();
            if (!fresh || fg.Length < 5 || !fg.EndsWith(".exe") || allowed.Contains(fg) || blocked.Contains(fg)) return;
            uint session = Native.WTSGetActiveConsoleSessionId();
            string pname = fg.Substring(0, fg.Length - 4);
            int killed = 0;
            foreach (var p in Process.GetProcessesByName(pname))
            {
                try { if (Native.SessionOf(p) == session) { p.Kill(); killed++; } }
                catch { }
                finally { p.Dispose(); }
            }
            if (killed == 0) return;
            string nm = label != "" && !label.Equals(fg, StringComparison.OrdinalIgnoreCase) ? label : pname;
            DateTime last;
            if (!strictReported.TryGetValue(fg, out last) || (DateTime.Now - last).TotalMinutes > 10)
            {
                strictReported[fg] = DateTime.Now;
                lock (L) events.Add(new Dictionary<string, object> { { "type", "unknown_app" }, { "title", Trim(nm, 150) }, { "detail", fg }, { "ts", Now().ToString("s") } });
                Log.Write("Strenger Modus: unbekanntes Programm geschlossen: " + fg);
            }
            DateTime lastN; string nk = "n:" + fg;
            if (!appKillEvent.TryGetValue(nk, out lastN) || (DateTime.Now - lastN).TotalSeconds > 3)
            {
                appKillEvent[nk] = DateTime.Now;
                lock (L) notices.Enqueue(new Dictionary<string, object> { { "type", "blocked_app" }, { "name", nm }, { "exe", fg }, { "kind", "unknown" } });
            }
            lock (L) agentExe = ""; // bis zur nächsten Meldung des Agents nicht erneut auswerten
        }

        void KillBlockedApps()
        {
            HashSet<string> blocked; Dictionary<string, string> names = new Dictionary<string, string>(), kinds = new Dictionary<string, string>();
            lock (L)
            {
                if (policy == null) return;
                blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var o in policy.Arr("blocked_apps"))
                {
                    var d = o as Dictionary<string, object>; if (d == null) continue;
                    var exe = d.Str("exe").ToLowerInvariant(); if (exe.Length == 0) continue;
                    blocked.Add(exe); names[exe] = d.Str("name", exe); kinds[exe] = d.Str("kind", "app");
                }
            }
            try { StrictCheck(blocked); } catch (Exception ex) { Log.Write("Strenger Modus: " + ex.Message); }
            if (blocked.Count == 0) return;
            uint session = Native.WTSGetActiveConsoleSessionId();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string exe = (p.ProcessName + ".exe").ToLowerInvariant();
                    if (!blocked.Contains(exe) || Native.SessionOf(p) != session) continue;
                    p.Kill();
                    string nm = names.ContainsKey(exe) ? names[exe] : exe;
                    DateTime last;
                    if (!appKillEvent.TryGetValue(nm, out last) || (DateTime.Now - last).TotalMinutes > 10)
                    {
                        appKillEvent[nm] = DateTime.Now;
                        AddEvent("blocked_app", "Gesperrte App geschlossen: " + nm, exe);
                    }
                    // Vollbild-Hinweis bei jedem Startversuch (höchstens alle 3 s)
                    DateTime lastN; string nk = "n:" + exe;
                    if (!appKillEvent.TryGetValue(nk, out lastN) || (DateTime.Now - lastN).TotalSeconds > 3)
                    {
                        appKillEvent[nk] = DateTime.Now;
                        lock (L) notices.Enqueue(new Dictionary<string, object> { { "type", "blocked_app" }, { "name", nm }, { "exe", exe }, { "kind", kinds.ContainsKey(exe) ? kinds[exe] : "app" } });
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        // ================= hosts-Datei (systemweite Webseitensperre) =================
        void ApplyHosts()
        {
            List<string> domains;
            lock (L)
            {
                if (policy == null) return;
                var grants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var o in policy.Arr("site_grants")) { var g = o as Dictionary<string, object>; if (g != null && ParseDT(g.Str("until")) > Now()) grants.Add(g.Str("domain")); }
                domains = Paused ? new List<string>() : policy.Arr("blocked_sites").Select(x => Convert.ToString(x)).Where(x => !grants.Contains(x) && x.Contains(".")).Distinct().ToList();
            }
            var sb = new StringBuilder();
            foreach (var d in domains)
                foreach (var pre in new[] { "", "www.", "m.", "mobile." })
                    sb.Append("0.0.0.0 ").Append(pre).Append(d).Append("\r\n");
            string block = sb.ToString();
            if (block == appliedHosts) return;
            try
            {
                Hosts.Write(block);
                appliedHosts = block;
                Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch (Exception ex) { Log.Write("hosts: " + ex.Message); }
        }

        // ================= Synchronisation =================
        void SyncLoop()
        {
            Thread.Sleep(1500);
            while (running)
            {
                int wait = 15;
                try
                {
                    if (cfg != null) SyncOnce(false, 15000);
                    lock (L)
                    {
                        if (policy != null) wait = Math.Max(5, Math.Min(120, policy.Int("sync_interval", 15)));
                        if (requests.Any(r => r.Status == "pending" || r.Status == "queued")) wait = 5;
                        if (lastSyncOk < DateTime.Now.AddMinutes(-2)) wait = Math.Min(wait, 30);
                    }
                }
                catch (Exception ex) { Log.Write("Sync: " + ex.Message); }
                syncNow.WaitOne(TimeSpan.FromSeconds(wait));
            }
        }

        void RotateBatch(DateTime? now = null)
        {
            if (!current.Empty) pending.Add(current);
            if (pending.Count > 3000) pending.RemoveRange(0, pending.Count - 3000);
            current = new Batch { Day = (now ?? Now()).ToString("yyyy-MM-dd") };
        }

        void SyncOnce(bool shuttingDown, int timeout)
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.Server)) return;
            // 1) Offene Anfragen senden
            List<LocalRequest> toSend;
            lock (L) toSend = requests.Where(r => r.Status == "queued").ToList();
            foreach (var r in toSend)
            {
                try
                {
                    var res = Http.Call(cfg.Server, "request", new Dictionary<string, object> { { "local_id", r.LocalId }, { "kind", r.Kind }, { "minutes", r.Minutes }, { "reason", r.Reason }, { "message", r.Message }, { "domain", r.Domain }, { "label", r.Label } }, cfg.Token, timeout);
                    lock (L) { r.ServerId = res.Int("id"); r.Status = res.Str("status", "pending"); }
                }
                catch (ApiException ex) { lock (L) { r.Status = "error"; r.Message = ex.Message; } }
                catch (WebException) { break; }
            }
            // 2) Nutzung + Status
            List<Batch> sending; List<Dictionary<string, object>> evs; Dictionary<string, object> status;
            lock (L)
            {
                RotateBatch();
                sending = pending.Take(150).ToList();
                evs = events.Take(50).ToList();
                var ev = Evaluate(Now());
                uint session = Native.WTSGetActiveConsoleSessionId();
                status = new Dictionary<string, object> {
                    {"user", session == 0xFFFFFFFF ? "" : Native.SessionUser(session)},
                    {"app", (DateTime.Now - agentSeen).TotalSeconds < 10 ? agentExe : ""}, {"app_label", (DateTime.Now - agentSeen).TotalSeconds < 10 ? agentLabel : ""},
                    {"domain", (DateTime.Now - lastDomainAt).TotalSeconds < 40 ? lastDomain : ""},
                    {"locked", ev.Reason != ""}, {"reason", Paused ? "paused" : ev.Reason}, {"idle", agentIdle}, {"shutdown", shuttingDown},
                    {"active_since", activeSince == DateTime.MinValue ? "" : activeSince.ToString("s")},
                    {"ext", extSeen.Where(x => (DateTime.Now - x.Value).TotalSeconds < 120).ToDictionary(x => x.Key, x => (object)(int)(DateTime.Now - x.Value).TotalSeconds)},
                    {"offline_sec", offlineSince == DateTime.MinValue ? 0 : (int)(DateTime.Now - offlineSince).TotalSeconds},
                };
            }
            Dictionary<string, object> pol;
            try
            {
                pol = Http.Call(cfg.Server, "sync", new Dictionary<string, object> {
                    {"batches", sending.Select(b => (object)b.ToJson()).ToList()}, {"events", evs.Cast<object>().ToList()}, {"status", status}, {"version", App.Version}, {"master_rev", cfg.MasterRev},
                }, cfg.Token, timeout);
            }
            catch (ApiException ex)
            {
                if (ex.Status == 410 || ex.Status == 401) { lock (L) { revoked = true; syncError = ex.Message; pending.RemoveAll(sending.Contains); events.RemoveAll(evs.Contains); } }
                else lock (L) syncError = ex.Message;
                MarkOffline(); return;
            }
            catch (Exception ex) { lock (L) syncError = ex.Message; MarkOffline(); return; }

            lock (L)
            {
                revoked = false; syncError = "";
                pending.RemoveAll(sending.Contains);
                events.RemoveAll(evs.Contains);
                lastSyncOk = DateTime.Now; offlineSince = DateTime.MinValue;
                if (pol.Bool("unassigned")) return;
                // Serverzeit verankern
                DateTime st;
                if (DateTime.TryParse(pol.Str("server_time"), CI, DateTimeStyles.None, out st)) { anchorTime = st; anchorTicks = Stopwatch.GetTimestamp(); }
                policy = pol;
                testMode = pol.Bool("test_mode");
                var today = pol.Obj("today");
                if (today != null)
                {
                    serverUsedDay = today.Str("day");
                    // Nicht gesendete Daten des aktuellen Zeitraums sind in pending/current und werden addiert
                    serverUsed = today.Int("used_sec");
                }
                // Anfragen-Status
                foreach (var o in pol.Arr("requests"))
                {
                    var d = o as Dictionary<string, object>; if (d == null) continue;
                    var lr = requests.FirstOrDefault(r => r.LocalId == d.Str("local_id"));
                    if (lr == null) continue;
                    string ns = d.Str("status");
                    if (lr.Status != ns && (ns == "approved" || ns == "denied"))
                        notices.Enqueue(new Dictionary<string, object> { { "type", "request" }, { "status", ns }, { "granted", d.Int("granted") }, { "kind", lr.Kind }, { "domain", lr.Domain }, { "label", lr.Label } });
                    lr.Status = ns; lr.Granted = d.Int("granted");
                }
                requests.RemoveAll(r => (DateTime.Now - r.Created).TotalHours > 24);
                // Befehle
                foreach (var o in pol.Arr("commands"))
                {
                    var c = o as Dictionary<string, object>; if (c == null) continue;
                    var payload = c.Obj("payload") ?? new Dictionary<string, object>();
                    switch (c.Str("type"))
                    {
                        case "message": notices.Enqueue(new Dictionary<string, object> { { "type", "message" }, { "text", payload.Str("text") }, { "from", payload.Str("from", "Eltern") }, { "message_id", payload.Int("message_id") }, { "priority", payload.Str("priority", "normal") } }); break;
                        case "shutdown":
                            notices.Enqueue(new Dictionary<string, object> { { "type", "message" }, { "text", "Der PC wird in 60 Sekunden heruntergefahren. Bitte jetzt speichern!" }, { "from", payload.Str("from", "Eltern") } });
                            try { Process.Start(new ProcessStartInfo("shutdown", "/s /t 60 /c \"MS Family Control: Deine Eltern fahren den PC herunter.\"") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
                            break;
                        case "logoff":
                            uint s = Native.WTSGetActiveConsoleSessionId();
                            ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(3000); Native.WTSLogoffSession(IntPtr.Zero, s, false); });
                            break;
                    }
                }
                // Neues Masterpasswort (vom Handy gesetzt)
                var m = pol.Obj("master");
                if (m != null && m.Int("rev") > cfg.MasterRev && m.Str("hash") != "")
                {
                    cfg.MasterSalt = m.Str("salt"); cfg.MasterHash = m.Str("hash"); cfg.MasterIter = m.Int("iter", 120000); cfg.MasterRev = m.Int("rev");
                    try { cfg.Save(); Log.Write("Masterpasswort aktualisiert"); } catch (Exception ex) { Log.Write("Config: " + ex.Message); }
                }
                var ch = pol.Obj("child");
                if (ch != null && cfg.ChildName != ch.Str("name")) { cfg.ChildName = ch.Str("name"); try { cfg.Save(); } catch { } }
            }
            ApplyHosts();
            try
            {
                List<object> fl; lock (L) fl = policy != null ? policy.Arr("filters") : new List<object>();
                filters.Sync(fl, cfg.Server, cfg.Token);
                lock (L) checkCache.Clear();
            }
            catch (Exception ex) { Log.Write("Filter-Sync: " + ex.Message); }
            try { CheckUpdate(); } catch (Exception ex) { Log.Write("Update-Prüfung: " + ex.Message); }
        }

        // ================= Einschalt-Alarm =================
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        DateTime lastPowerAlert = DateTime.MinValue;

        static string ReasonText(string r)
        {
            switch (r)
            {
                case "bedtime": return "der Bettzeit";
                case "early": return "der Sperrzeit (noch zu früh)";
                case "school": return "der Schulzeit";
                case "manual": return "einer Sperre durch die Eltern";
                case "time": return "aufgebrauchter Bildschirmzeit";
                default: return "einer gesperrten Zeit";
            }
        }

        /// <summary>PC wurde eingeschaltet / angemeldet / entsperrt / geweckt – während einer gesperrten Zeit? → sofort an die Eltern</summary>
        public void PowerOn(string how)
        {
            try
            {
                if (cfg == null) return;
                uint session = Native.WTSGetActiveConsoleSessionId();
                string user = session == 0xFFFFFFFF ? "" : Native.SessionUser(session);
                if (user.Length > 0 && IsExempt(user)) return; // Eltern-Konto
                Eval ev;
                lock (L) { if (policy == null) return; ev = Evaluate(Now()); }
                if (ev.Reason == "" || Paused) return;
                if ((DateTime.Now - lastPowerAlert).TotalMinutes < 10) return;
                lastPowerAlert = DateTime.Now;
                var de = new CultureInfo("de-DE");
                AddEvent("power_on", "PC " + how + " während " + ReasonText(ev.Reason),
                    "PC-Uhr: " + DateTime.Now.ToString("dddd, dd.MM.yyyy · HH:mm:ss", de) + " Uhr · " + Environment.MachineName + (user.Length > 0 ? " · Konto: " + user : ""));
                SaveStateAsync();
                syncNow.Set();
                Log.Write("Einschalt-Alarm: " + how + " (" + ev.Reason + ")");
            }
            catch (Exception ex) { Log.Write("Einschalt-Alarm: " + ex.Message); }
        }

        // ================= Stilles Auto-Update =================
        volatile bool updating;
        DateTime updateSeen = DateTime.MinValue, lastUpdateTry = DateTime.MinValue;
        string updateVersion = "";

        static bool Newer(string a, string b)
        {
            try { return new Version(a) > new Version(b); } catch { return false; }
        }

        void CheckUpdate()
        {
            Dictionary<string, object> u;
            lock (L) u = policy != null ? policy.Obj("update") : null;
            if (u == null || updating || cfg == null) return;
            string v = u.Str("version"), sha = u.Str("sha256").ToLowerInvariant();
            if (!Newer(v, App.Version) || sha.Length != 64) return;
            if (v != updateVersion) { updateVersion = v; updateSeen = DateTime.Now; lastUpdateTry = DateTime.MinValue; }
            if ((DateTime.Now - lastUpdateTry).TotalHours < 2) return;
            // Ruhiger Moment: kein Kind angemeldet, Bildschirm gesperrt oder 5 Min. keine Eingabe – spätestens nach 24 h
            uint session = Native.WTSGetActiveConsoleSessionId();
            string user = session == 0xFFFFFFFF ? "" : Native.SessionUser(session);
            bool childActive = user.Length > 0 && !IsExempt(user);
            bool quiet = !childActive || Native.SessionLocked(session) || (agentIdle >= 300 && !agentFullscreen);
            if (!quiet && (DateTime.Now - updateSeen).TotalHours < 24) return;
            lastUpdateTry = DateTime.Now;
            updating = true;
            string server = cfg.Server, token = cfg.Token;
            new Thread(() =>
            {
                try { RunUpdate(server, token, v, sha); }
                catch (Exception ex) { Log.Write("Update fehlgeschlagen: " + ex.Message); }
                finally { updating = false; }
            }) { IsBackground = true, Name = "update" }.Start();
        }

        void RunUpdate(string server, string token, string version, string sha)
        {
            Security.EnsureProtectedDir(App.DataDir);
            string dir = Path.Combine(App.DataDir, "update");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "MSFamilyControl-Update.exe"), tmp = file + ".part";
            var req = (HttpWebRequest)WebRequest.Create(server + "/device.php?a=update");
            req.Timeout = 120000; req.ReadWriteTimeout = 120000; req.UserAgent = "MSFamilyControl/" + App.Version;
            req.Headers["Authorization"] = "Bearer " + token;
            using (var res = (HttpWebResponse)req.GetResponse())
            using (var s = res.GetResponseStream())
            using (var f = File.Create(tmp)) s.CopyTo(f);
            string got;
            using (var sh = System.Security.Cryptography.SHA256.Create())
            using (var f = File.OpenRead(tmp)) got = BitConverter.ToString(sh.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            if (got != sha) { File.Delete(tmp); throw new Exception("Prüfsumme stimmt nicht – Update verworfen"); }
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
            Log.Write("Update " + version + " geladen und geprüft – wird installiert");
            AddEvent("app_update", "Windows-App wird automatisch aktualisiert: " + App.Version + " → " + version, Environment.MachineName);
            lock (L) RotateBatch();
            SaveState();
            Process.Start(new ProcessStartInfo(file, "--silent-update") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir });
        }

        void MarkOffline() { lock (L) if (offlineSince == DateTime.MinValue) offlineSince = DateTime.Now; }

        void AddEvent(string type, string title, string detail)
        {
            lock (L)
            {
                events.Add(new Dictionary<string, object> { { "type", type }, { "title", title }, { "detail", detail }, { "ts", Now().ToString("s") } });
                if (events.Count > 300) events.RemoveRange(0, events.Count - 300);
            }
        }

        // ================= Persistenz =================
        void LoadState()
        {
            try
            {
                if (!File.Exists(App.StateFile)) return;
                var d = Json.Decode(File.ReadAllText(App.StateFile, Encoding.UTF8));
                lock (L)
                {
                    policy = d.Obj("policy");
                    serverUsedDay = d.Str("server_used_day"); serverUsed = d.Int("server_used");
                    var c = d.Obj("current"); if (c != null) current = Batch.FromJson(c);
                    pending = d.Arr("pending").OfType<Dictionary<string, object>>().Select(Batch.FromJson).ToList();
                    events = d.Arr("events").OfType<Dictionary<string, object>>().ToList();
                    requests = d.Arr("requests").OfType<Dictionary<string, object>>().Select(LocalRequest.FromJson).ToList();
                    localFreeUntil = ParseDT(d.Str("local_free_until"));
                    pausedUntil = ParseDT(d.Str("paused_until"));
                    warnedDay = d.Str("warned_day"); warned = new HashSet<int>(d.Arr("warned").Select(x => Convert.ToInt32(x, CI)));
                    lastReason = d.Str("last_reason");
                }
            }
            catch (Exception ex) { Log.Write("State laden: " + ex.Message); }
        }

        void SaveState()
        {
            try
            {
                string txt;
                lock (L)
                {
                    txt = Json.Encode(new Dictionary<string, object> {
                        {"policy", policy}, {"server_used_day", serverUsedDay}, {"server_used", serverUsed}, {"current", current.ToJson()},
                        {"pending", pending.Select(b => (object)b.ToJson()).ToList()}, {"events", events}, {"requests", requests.Select(r => (object)r.ToJson()).ToList()},
                        {"local_free_until", localFreeUntil == DateTime.MinValue ? "" : localFreeUntil.ToString("yyyy-MM-dd HH:mm:ss")},
                        {"paused_until", pausedUntil == DateTime.MinValue ? "" : pausedUntil.ToString("yyyy-MM-dd HH:mm:ss")},
                        {"warned_day", warnedDay}, {"warned", warned.ToList()}, {"last_reason", lastReason},
                    });
                }
                Security.EnsureProtectedDir(App.DataDir);
                var tmp = App.StateFile + ".tmp";
                File.WriteAllText(tmp, txt, Encoding.UTF8);
                if (File.Exists(App.StateFile)) File.Delete(App.StateFile);
                File.Move(tmp, App.StateFile);
            }
            catch (Exception ex) { Log.Write("State speichern: " + ex.Message); }
        }

        // ================= Lokale Schnittstelle (nur 127.0.0.1) =================
        void StartHttp()
        {
            try
            {
                http = new HttpListener();
                http.Prefixes.Add("http://127.0.0.1:" + App.LocalPort + "/");
                http.Start();
                new Thread(() =>
                {
                    while (running)
                    {
                        try { var ctx = http.GetContext(); ThreadPool.QueueUserWorkItem(_ => Handle(ctx)); }
                        catch { if (!running) break; Thread.Sleep(200); }
                    }
                }) { IsBackground = true, Name = "http" }.Start();
            }
            catch (Exception ex) { Log.Write("HTTP-Start: " + ex.Message); }
        }

        void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request; var res = ctx.Response;
            Dictionary<string, object> outp;
            int code = 200;
            try
            {
                string origin = req.Headers["Origin"] ?? "";
                bool fromExt = origin.StartsWith("chrome-extension://") || origin.StartsWith("moz-extension://") || origin.StartsWith("extension://");
                // Webseiten dürfen die Schnittstelle nicht nutzen
                if ((origin != "" && !fromExt) || string.IsNullOrEmpty(req.Headers["X-MSFC"])) { code = 403; outp = new Dictionary<string, object> { { "ok", false } }; }
                else
                {
                    if (fromExt) { res.AddHeader("Access-Control-Allow-Origin", origin); }
                    Dictionary<string, object> body = new Dictionary<string, object>();
                    if (req.HttpMethod == "POST")
                        using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) { var t = r.ReadToEnd(); if (t.Length < 20000) body = Json.Decode(t); }
                    outp = Route(req.Url.AbsolutePath, req, body, fromExt);
                }
            }
            catch (Exception ex) { code = 500; outp = new Dictionary<string, object> { { "ok", false }, { "error", ex.Message } }; }
            try
            {
                var data = Encoding.UTF8.GetBytes(Json.Encode(outp));
                res.StatusCode = code; res.ContentType = "application/json; charset=utf-8"; res.AddHeader("Cache-Control", "no-store");
                res.ContentLength64 = data.Length; res.OutputStream.Write(data, 0, data.Length); res.OutputStream.Close();
            }
            catch { }
        }

        Dictionary<string, object> Route(string path, HttpListenerRequest req, Dictionary<string, object> b, bool fromExt)
        {
            var q = req.QueryString;
            switch (path)
            {
                case "/agent/state":
                    if (fromExt) break;
                    lock (L)
                    {
                        agentSeen = DateTime.Now;
                        int.TryParse(q["idle"], out agentIdle);
                        agentExe = (q["fg"] ?? "").ToLowerInvariant(); if (agentExe.Length > 120) agentExe = agentExe.Substring(0, 120);
                        agentLabel = q["label"] ?? ""; if (agentLabel.Length > 160) agentLabel = agentLabel.Substring(0, 160);
                        agentFullscreen = q["fs"] == "1";
                        int pid; if (int.TryParse(q["pid"], out pid) && pid > 0) agentPid = pid;
                        return AgentState();
                    }
                case "/agent/request":
                case "/ext/request":
                    lock (L)
                    {
                        if (requests.Count(r => r.Status == "queued" || r.Status == "pending") >= 3) return Err("Es sind schon Anfragen offen – bitte auf die Antwort warten.");
                        string kind = b.Str("kind", path.StartsWith("/ext") ? "site" : "time"); if (kind != "site" && kind != "app") kind = "time";
                        var lr = new LocalRequest { Label = Trim(b.Str("label"), 160), Kind = kind, Minutes = b.Int("minutes", 30), Reason = b.Str("reason", "other"), Message = Trim(b.Str("message"), 300), Domain = Trim(b.Str("domain"), 190) };
                        if (lr.Kind == "site" && lr.Domain == "") return Err("Webseite fehlt");
                        requests.Add(lr);
                        syncNow.Set();
                        return new Dictionary<string, object> { { "ok", true }, { "local_id", lr.LocalId } };
                    }
                case "/agent/event":
                    if (fromExt) break;
                    var allowed = new[] { "browser_closed", "ext_missing", "message_read" };
                    if (allowed.Contains(b.Str("type")))
                    {
                        AddEvent(b.Str("type"), Trim(b.Str("title"), 190), Trim(b.Str("detail"), 500));
                        if (b.Int("ref") > 0) lock (L) events[events.Count - 1]["ref"] = b.Int("ref");
                        syncNow.Set();
                    }
                    return Ok();
                case "/agent/parent":
                    if (fromExt) break;
                    return Parent(b);
                case "/ext/policy":
                    lock (L)
                    {
                        string br = Browser(q["b"]);
                        if (br != "") extSeen[br] = DateTime.Now;
                        var ev = Evaluate(Now());
                        var grants = policy == null ? new List<object>() : policy.Arr("site_grants");
                        var mine = requests.Where(r => r.Kind == "site").Select(r => (object)new Dictionary<string, object> { { "domain", r.Domain }, { "status", r.Status }, { "granted", r.Granted } }).ToList();
                        return new Dictionary<string, object> {
                            {"ok", true}, {"child", cfgName()}, {"paused", Paused},
                            {"blocked", Paused || policy == null ? new List<object>() : policy.Arr("blocked_sites")}, {"grants", grants},
                            {"locked", !Paused && ev.Reason != ""}, {"reason", ev.Reason}, {"requests", mine}, {"server_ok", lastSyncOk > DateTime.Now.AddMinutes(-2)},
                            {"theme", WebTheme()},
                        };
                    }
                case "/ext/check":
                    {
                        string host = Trim((q["h"] ?? "").ToLowerInvariant(), 190);
                        string br = Browser(q["b"]);
                        if (br != "") lock (L) extSeen[br] = DateTime.Now;
                        if (Paused || host.Length < 4) return new Dictionary<string, object> { { "ok", true }, { "blocked", false } };
                        lock (L)
                        {
                            if (policy != null)
                            {
                                foreach (var a in policy.Arr("allow_sites")) { var d = Convert.ToString(a); if (host == d || host.EndsWith("." + d)) return new Dictionary<string, object> { { "ok", true }, { "blocked", false } }; }
                                foreach (var o in policy.Arr("site_grants")) { var g = o as Dictionary<string, object>; if (g != null && ParseDT(g.Str("until")) > Now()) { var d = g.Str("domain"); if (host == d || host.EndsWith("." + d)) return new Dictionary<string, object> { { "ok", true }, { "blocked", false } }; } }
                            }
                        }
                        string m; string cat = filters.Match(host, out m);
                        if (cat == null) return new Dictionary<string, object> { { "ok", true }, { "blocked", false } };
                        return new Dictionary<string, object> { { "ok", true }, { "blocked", true }, { "domain", m }, { "category", cat }, { "label", filters.Name(cat) } };
                    }
                case "/ext/visit":
                    lock (L)
                    {
                        string br = Browser(b.Str("b"));
                        if (br != "") extSeen[br] = DateTime.Now;
                        string dom = Trim(b.Str("domain").ToLowerInvariant(), 190);
                        int sec = Math.Max(0, Math.Min(60, b.Int("sec")));
                        if (dom.Length > 3 && dom.Contains("."))
                        {
                            lastDomain = dom; lastDomainAt = DateTime.Now;
                            if (sec > 0 && Evaluate(Now()).Reason == "" && !Paused) { int v; current.Web.TryGetValue(dom, out v); current.Web[dom] = v + sec; }
                        }
                        if (b.Str("blocked") != "")
                        {
                            DateTime last;
                            string k = "site:" + b.Str("blocked");
                            if (!appKillEvent.TryGetValue(k, out last) || (DateTime.Now - last).TotalMinutes > 15) { appKillEvent[k] = DateTime.Now; events.Add(new Dictionary<string, object> { { "type", "blocked_site" }, { "title", "Gesperrte Seite aufgerufen: " + Trim(b.Str("blocked"), 150) }, { "detail", (b.Str("category") != "" ? "Filter: " + Trim(b.Str("category"), 80) + " · " : "") + br }, { "ts", Now().ToString("s") } }); }
                        }
                        return Ok();
                    }
            }
            return Err("Unbekannt");
        }

        Dictionary<string, object> WebTheme()
        {
            var t = policy != null ? policy.Obj("theme") : null;
            var e = t != null && t.Obj("events") != null ? t.Obj("events").Obj("website_blocked") : null;
            if (e == null) return null;
            return new Dictionary<string, object> { { "theme", e.Str("theme", "anime") }, { "title", e.Str("title") }, { "text", e.Str("text") }, { "accent", t.Str("accent") } };
        }

        static string Browser(string b) { b = (b ?? "").ToLowerInvariant(); return b == "chrome" || b == "edge" || b == "firefox" ? b : ""; }
        static string Trim(string s, int n) { s = (s ?? "").Trim(); return s.Length > n ? s.Substring(0, n) : s; }
        static Dictionary<string, object> Ok() { return new Dictionary<string, object> { { "ok", true } }; }
        static Dictionary<string, object> Err(string m) { return new Dictionary<string, object> { { "ok", false }, { "error", m } }; }

        Dictionary<string, object> AgentState()
        {
            var now = Now();
            var ev = Evaluate(now);
            var n = new List<object>();
            while (notices.Count > 0) n.Add(notices.Dequeue());
            var lastTime = requests.Where(r => r.Kind == "time").OrderByDescending(r => r.Created).FirstOrDefault();
            uint session = Native.WTSGetActiveConsoleSessionId();
            string user = session == 0xFFFFFFFF ? "" : Native.SessionUser(session);
            var ext = new Dictionary<string, object>();
            foreach (var x in extSeen) ext[x.Key] = (int)(DateTime.Now - x.Value).TotalSeconds;
            return new Dictionary<string, object> {
                {"ok", true}, {"child", cfgName()}, {"exempt", user != "" && IsExempt(user)}, {"paused", Paused}, {"test_mode", testMode},
                {"paused_until", pausedUntilReboot ? "Neustart" : pausedUntil > now ? pausedUntil.ToString("HH:mm") : ""},
                {"locked", !Paused && ev.Reason != ""}, {"reason", ev.Reason}, {"until", ev.Until},
                {"remaining", ev.Unlimited ? -1 : ev.Remaining}, {"to_lock", ev.SecondsToLock == int.MaxValue ? -1 : ev.SecondsToLock}, {"lock_kind", ev.LockKind},
                {"used", ev.Used}, {"limit", ev.LimitMin}, {"bonus", ev.Bonus}, {"start", ev.Start}, {"end", ev.End},
                {"server_ok", lastSyncOk > DateTime.Now.AddMinutes(-2)}, {"last_sync", lastSyncOk == DateTime.MinValue ? "" : lastSyncOk.ToString("HH:mm:ss")}, {"sync_error", syncError}, {"revoked", revoked},
                {"server", cfg != null ? cfg.Server : ""},
                {"request", lastTime == null ? null : lastTime.ToJson()},
                {"notices", n}, {"ext", ext}, {"enforce_ext", policy == null || policy.Bool("enforce_extension", true)},
                {"time", now.ToString("HH:mm")}, {"version", App.Version}, {"theme", policy != null ? policy.Obj("theme") : null},
            };
        }

        Dictionary<string, object> Parent(Dictionary<string, object> b)
        {
            lock (L)
            {
                if (masterBlockedUntil > DateTime.Now) return Err("Zu viele Fehlversuche. Bitte " + Math.Ceiling((masterBlockedUntil - DateTime.Now).TotalMinutes) + " Minuten warten.");
                if (cfg == null || !cfg.CheckMaster(b.Str("password")))
                {
                    masterFails++;
                    if (masterFails >= 5) { masterBlockedUntil = DateTime.Now.AddMinutes(5); masterFails = 0; }
                    if (masterFails == 3 || masterBlockedUntil > DateTime.Now) AddEvent("master_fail", "Falsches Masterpasswort am PC eingegeben", "mehrere Versuche");
                    syncNow.Set();
                    return Err("Masterpasswort falsch");
                }
                masterFails = 0;
                string action = b.Str("action");
                int min = Math.Max(5, Math.Min(24 * 60, b.Int("minutes", 30)));
                switch (action)
                {
                    case "verify": return Ok();
                    case "free":
                        localFreeUntil = Now().AddMinutes(min);
                        AddEvent("parent_unlock", "Am PC mit Masterpasswort freigegeben (" + min + " min)", "bis " + localFreeUntil.ToString("HH:mm"));
                        break;
                    case "pause":
                        pausedUntil = Now().AddMinutes(min);
                        AddEvent("parent_unlock", "Schutz am PC pausiert (" + min + " min)", "bis " + pausedUntil.ToString("HH:mm"));
                        break;
                    case "resume":
                        pausedUntil = DateTime.MinValue; pausedUntilReboot = false; localFreeUntil = DateTime.MinValue;
                        break;
                    case "exit":
                        pausedUntilReboot = true;
                        AddEvent("parent_unlock", "Familien-App mit Masterpasswort beendet (bis Neustart)", "");
                        break;
                    case "master":
                        var np = b.Str("value");
                        if (np.Length < 6) return Err("Mindestens 6 Zeichen");
                        cfg.SetMaster(np); cfg.Save();
                        break;
                    case "server":
                        var url = Http.Normalize(b.Str("value"));
                        if (url == "") return Err("Ungültige Adresse");
                        try { var p = Http.Call(url, "ping", null, null, 8000); if (!p.Bool("ok")) return Err("Server antwortet nicht korrekt"); }
                        catch (Exception ex) { return Err("Server nicht erreichbar: " + ex.Message); }
                        cfg.Server = url; cfg.Save(); syncNow.Set();
                        break;
                    default: return Err("Unbekannte Aktion");
                }
                syncNow.Set(); SaveStateAsync();
                return Ok();
            }
        }

        void SaveStateAsync() { ThreadPool.QueueUserWorkItem(_ => SaveState()); }
    }

    /// <summary>Verwalteter Abschnitt in der hosts-Datei</summary>
    internal static class Hosts
    {
        const string Begin = "# >>> MS Family Control (automatisch – nicht bearbeiten)";
        const string End = "# <<< MS Family Control";
        static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts"); } }

        public static void Write(string block)
        {
            string txt = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
            int a = txt.IndexOf(Begin, StringComparison.Ordinal), e = txt.IndexOf(End, StringComparison.Ordinal);
            if (a >= 0 && e > a) txt = txt.Substring(0, a).TrimEnd('\r', '\n') + "\r\n" + txt.Substring(e + End.Length).TrimStart('\r', '\n');
            txt = txt.TrimEnd('\r', '\n') + "\r\n";
            if (block.Length > 0) txt += Begin + "\r\n" + block + End + "\r\n";
            File.WriteAllText(FilePath, txt, new UTF8Encoding(false));
        }

        public static void Remove() { try { Write(""); } catch { } }
    }
}
