using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MSFamilyControl
{
    internal static class ParentApi
    {
        public static bool Do(string password, string action, int minutes, string value, IWin32Window owner)
        {
            try
            {
                var r = Http.Local("/agent/parent", new Dictionary<string, object> { { "password", password }, { "action", action }, { "minutes", minutes }, { "value", value ?? "" } }, action == "server" ? 15000 : 4000);
                if (!r.Bool("ok")) { MessageBox.Show(owner, r.Str("error"), App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
                return true;
            }
            catch (Exception ex) { MessageBox.Show(owner, "Dienst nicht erreichbar: " + ex.Message, App.Name); return false; }
        }
    }

    /// <summary>
    /// Sichtbare App in der Windows-Sitzung des Kindes: Tray-Symbol, Sperrbildschirm, Vorwarnungen, Anfragen.
    /// Wird vom Dienst gestartet und bei Beenden sofort neu gestartet (Beenden nur mit Masterpasswort).
    /// </summary>
    internal class Agent : ApplicationContext
    {
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem miStatus, miRequest;
        readonly List<Form> locks = new List<Form>();
        string lockKey = "";
        readonly SynchronizationContext ui;
        readonly Control invoker;
        Dictionary<string, object> state;
        volatile bool exiting;
        public bool DialogOpen;
        DateTime started = DateTime.Now, lastBrowserCheck = DateTime.MinValue, serviceLost = DateTime.MinValue;
        readonly Dictionary<string, DateTime> browserEvent = new Dictionary<string, DateTime>();
        string lastRequestStatus = "";
        static Mutex mutex;

        public static void Run()
        {
            bool created;
            mutex = new Mutex(true, "Local\\MSFamilyControlAgent", out created);
            if (!created) return;
            Application.Run(new Agent());
        }

        Agent()
        {
            invoker = new Control(); invoker.CreateControl();
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            var menu = new ContextMenuStrip { BackColor = Theme.Panel, ForeColor = Color.White, ShowImageMargin = false, Font = Theme.F(10f) };
            miStatus = new ToolStripMenuItem("MS Family Control") { Enabled = false };
            miRequest = new ToolStripMenuItem("🙋  Mehr Zeit anfragen …", null, (s, e) => ShowRequestDialog(null));
            menu.Items.Add(miStatus);
            miUnread = new ToolStripMenuItem("📨  Nachricht", null, (s, e) => NextUnread()) { Visible = false, Font = Theme.F(10f, FontStyle.Bold) };
            menu.Items.Add(miUnread);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miRequest);
            menu.Items.Add(new ToolStripMenuItem("🛡  Was wird erfasst?", null, (s, e) => { if (state != null) Modal(new InfoForm(state), null); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("🔑  Eltern-Bereich …", null, (s, e) => ParentDialog(null)));
            tray = new NotifyIcon { Icon = Theme.AppIcon, Text = "MS Family Control", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += (s, e) => { if (state != null) Modal(new InfoForm(state), null); };
            Microsoft.Win32.SystemEvents.SessionEnding += (s, e) => { exiting = true; foreach (var l in locks) ((ILockScreen)l).AllowClose = true; };
            new Thread(PollLoop) { IsBackground = true, Name = "poll" }.Start();
        }

        // ---------------- Abfrage beim Dienst ----------------
        void PollLoop()
        {
            while (!exiting)
            {
                try
                {
                    string exe = "", label = ""; bool fs = false;
                    try { Foreground(out exe, out label, out fs); } catch { }
                    string q = "/agent/state?idle=" + Native.IdleSeconds() + "&fg=" + Uri.EscapeDataString(exe) + "&label=" + Uri.EscapeDataString(label) + "&fs=" + (fs ? "1" : "0") + "&pid=" + Process.GetCurrentProcess().Id;
                    var st = Http.Local(q);
                    serviceLost = DateTime.MinValue;
                    ui.Post(_ => Apply(st), null);
                }
                catch
                {
                    if (serviceLost == DateTime.MinValue) serviceLost = DateTime.Now;
                    ui.Post(_ => { tray.Text = "MS Family Control – Dienst nicht erreichbar"; }, null);
                }
                Thread.Sleep(1000);
            }
        }

        static void Foreground(out string exe, out string label, out bool fullscreen)
        {
            exe = ""; label = ""; fullscreen = false;
            var h = Native.GetForegroundWindow();
            if (h == IntPtr.Zero || h == Native.GetShellWindow() || h == Native.GetDesktopWindow()) return;
            uint pid; Native.GetWindowThreadProcessId(h, out pid);
            if (pid == 0 || pid == Process.GetCurrentProcess().Id) return;
            using (var p = Process.GetProcessById((int)pid))
            {
                exe = (p.ProcessName + ".exe").ToLowerInvariant();
                try { label = p.MainModule.FileVersionInfo.FileDescription; } catch { }
                if (exe == "applicationframehost.exe") { label = Native.WindowTitle(h); exe = "app:" + label.ToLowerInvariant(); }
                if (string.IsNullOrWhiteSpace(label)) label = p.ProcessName;
                if (exe == "explorer.exe") label = "Windows-Explorer / Desktop";
            }
            Native.RECT r;
            if (Native.GetWindowRect(h, out r))
            {
                var sc = Screen.FromHandle(h).Bounds;
                fullscreen = r.Left <= sc.Left && r.Top <= sc.Top && r.Right >= sc.Right && r.Bottom >= sc.Bottom;
            }
        }

        // ---------------- Zustand anwenden ----------------
        void Apply(Dictionary<string, object> st)
        {
            if (exiting) return;
            state = st;
            bool exempt = st.Bool("exempt"), paused = st.Bool("paused");
            if (paused && st.Str("paused_until") == "Neustart") { ExitApp(); return; }

            int remain = st.Int("remaining"), toLock = st.Int("to_lock");
            string status;
            if (exempt) status = "Eltern-Konto – keine Einschränkung";
            else if (paused) status = "Schutz pausiert bis " + st.Str("paused_until");
            else if (st.Bool("locked")) status = "Gesperrt";
            else if (toLock < 0) status = "Kein Zeitlimit heute";
            else status = "Noch " + Fmt.Dur(toLock) + (st.Str("lock_kind") == "bedtime" ? " bis zur Bettzeit" : "");
            if (st.Bool("test_mode")) status = "🧪 Testmodus · " + status;
            miStatus.Text = st.Str("child") + ": " + status;
            var tt = "MS Family Control – " + status;
            tray.Text = tt.Length > 63 ? tt.Substring(0, 63) : tt;
            miRequest.Visible = !exempt;

            // Hinweise
            foreach (var o in st.Arr("notices"))
            {
                var n = o as Dictionary<string, object>; if (n == null) continue;
                switch (n.Str("type"))
                {
                    case "warn":
                        int m = n.Int("minutes");
                        string kind = n.Str("kind") == "bedtime" ? "bis zur Bettzeit" : "PC-Zeit übrig";
                        Toast("⏳ Noch " + m + (m == 1 ? " Minute " : " Minuten ") + kind, m <= 1 ? "Gleich wird der PC gesperrt. Speichere jetzt dein Spiel! 💾" : "Speichere rechtzeitig, damit nichts verloren geht.", m <= 5 ? Theme.Gold : Theme.Cyan, 12, "🙋 Mehr Zeit anfragen", () => ShowRequestDialog(null));
                        break;
                    case "blocked_app":
                        ShowBlocked(n.Str("name"), n.Str("exe"), n.Str("kind") == "game", n.Str("kind") == "unknown");
                        break;
                    case "message":
                        int mid = n.Int("message_id");
                        if (mid > 0 && shownMessages.Contains(mid)) break; // nicht doppelt anzeigen
                        ShowMessage(n.Str("from", "Eltern"), DateTime.Now, n.Str("text"), mid, n.Str("priority", "normal"));
                        break;
                    case "request":
                        bool ok = n.Str("status") == "approved";
                        string what = n.Str("kind") == "app" ? "„" + n.Str("label", n.Str("domain")) + "“" + (ok ? " ist für " + n.Int("granted") + " Min. freigegeben. 🎮" : " bleibt gesperrt.") :
                            n.Str("kind") == "site" ? n.Str("domain") + (ok ? " ist für " + n.Int("granted") + " Min. freigegeben." : " bleibt gesperrt.") : (ok ? "Du hast " + n.Int("granted") + " Minuten mehr Zeit. Viel Spaß! 🎉" : "Deine Eltern haben die Anfrage abgelehnt.");
                        Toast(ok ? "✅ Genehmigt!" : "❌ Leider abgelehnt", what, ok ? Theme.Green : Theme.Red, 12);
                        break;
                }
            }

            // Sperre
            bool shouldLock = st.Bool("locked") && !exempt && !paused;
            if (shouldLock) ShowLock(st); else HideLock();

            // Browser ohne Erweiterung
            if (!exempt && !paused && st.Bool("enforce_ext", true) && (DateTime.Now - lastBrowserCheck).TotalSeconds > 5 && (DateTime.Now - started).TotalSeconds > 60)
            {
                lastBrowserCheck = DateTime.Now;
                ThreadPool.QueueUserWorkItem(_ => CheckBrowsers(st.Obj("ext") ?? new Dictionary<string, object>()));
            }
        }

        string RequestLine(Dictionary<string, object> st, out Color c)
        {
            c = Theme.Muted;
            var r = st.Obj("request");
            if (r == null) return "";
            DateTime created; DateTime.TryParse(r.Str("created"), out created);
            if ((DateTime.Now - created).TotalHours > 3) return "";
            switch (r.Str("status"))
            {
                case "queued": c = Theme.Gold; return "📨 Anfrage wird gesendet … (" + r.Int("minutes") + " Min.)" + (st.Bool("server_ok") ? "" : " – Server gerade nicht erreichbar");
                case "pending": c = Theme.Gold; return "⏳ Anfrage über " + r.Int("minutes") + " Min. gesendet – warte auf Antwort deiner Eltern …";
                case "approved": c = Theme.Green; return "✅ Genehmigt: +" + r.Int("granted") + " Min.";
                case "denied": c = Theme.Red; return "❌ Deine Eltern haben die Anfrage abgelehnt.";
                case "error": c = Theme.Red; return "⚠ " + r.Str("message");
            }
            return "";
        }

        static string LockEvent(string reason) { return reason == "time" ? "time_expired" : reason == "bedtime" ? "bedtime" : "access_blocked"; }

        void ShowLock(Dictionary<string, object> st)
        {
            string reason = st.Str("reason"), until = st.Str("until"), child = st.Str("child");
            var spec = ThemeSpec.From(st.Obj("theme"), LockEvent(reason));
            string key = spec.Theme + "|" + reason + "|" + until + "|" + spec.Title + "|" + spec.Text + "|" + spec.Accent + "|" + spec.Scale;
            if (locks.Count > 0 && key != lockKey) HideLock(); // Design oder Grund geändert → neu aufbauen
            if (locks.Count == 0)
            {
                lockKey = key;
                foreach (var sc in Screen.AllScreens)
                {
                    Form f;
                    if (spec.Theme == "anime")
                    {
                        var lf = new LockForm(this, sc, sc.Primary);
                        if (spec.Custom) lf.SetCustom(spec.Fill(spec.Title, "", until, child), spec.Fill(spec.Text, "", until, child));
                        f = lf;
                    }
                    else f = BuildThemedLock(spec, sc, reason, until, child);
                    locks.Add(f); f.Show();
                }
                var p = locks.FirstOrDefault(x => x.Bounds.Contains(Screen.PrimaryScreen.Bounds.Location)) ?? locks[0];
                p.Activate();
            }
            Color c; string line = RequestLine(st, out c);
            foreach (var l in locks) ((ILockScreen)l).SetState(reason, until, child, line, c);
        }

        Form BuildThemedLock(ThemeSpec spec, Screen sc, string reason, string until, string child)
        {
            var f = new ThemeScreen(spec, sc, true, sc.Primary) { DialogOpen = () => DialogOpen };
            string glyph = reason == "time" ? "⏰" : reason == "bedtime" ? "🌙" : reason == "school" ? "📚" : "✋";
            string header = reason == "time" ? "Bildschirmzeit" : reason == "bedtime" ? "Schlafenszeit" : "Zugriffskontrolle";
            string art = reason == "bedtime" ? (spec.Theme == "manga" ? "girl" : "sleep") : "robot";
            f.Setup(spec.Fill(spec.Title, "", until, child), spec.Fill(spec.Text, "", until, child), glyph, header, art);
            string info = reason == "early" ? "Freigabe heute ab " + until + " Uhr" : reason == "manual" ? (until != "" ? "Von deinen Eltern gesperrt bis " + until + " Uhr" : "Von deinen Eltern gesperrt")
                : reason == "school" ? "Schulzeit – PC frei ab " + until + " Uhr" : reason == "bedtime" && until != "" ? "Bettzeit seit " + until + " Uhr" : "";
            f.SetInfo(info);
            if (sc.Primary)
            {
                f.AddButton(reason == "time" ? "Mehr Zeit anfragen" : "Ausnahme anfragen", "🙋", true, () => ShowRequestDialog(f));
                f.AddButton("Herunterfahren", "⏻", false, () => Shutdown(f));
                f.AddButton("Abmelden", "", false, () => Logoff(f), true);
                f.AddButton("Eltern", "🔑", false, () => ParentDialog(f), true);
            }
            return f;
        }

        void HideLock()
        {
            if (locks.Count == 0) return;
            foreach (var l in locks) { ((ILockScreen)l).AllowClose = true; l.Close(); l.Dispose(); }
            locks.Clear(); lockKey = "";
        }

        Form LockOwner() { return locks.FirstOrDefault(); }

        void Toast(string title, string text, Color c, int sec, string action = null, Action onAction = null)
        {
            var t = new ToastForm(title, text, c, sec, action, onAction);
            t.Show();
        }

        Form MessageFormFor(string title, string text)
        {
            var f = new NeonDialog(title, 520, 240);
            f.Controls.Add(new Label { Text = title, Font = Theme.F(15f, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Location = new Point(26, 20), BackColor = Color.Transparent });
            f.Controls.Add(new Label { Text = text, Font = Theme.F(13f), ForeColor = Theme.Text, AutoSize = false, Bounds = new Rectangle(28, 64, 464, 90), BackColor = Color.Transparent });
            var ok = new NeonButton("OK") { Primary = true, Bounds = new Rectangle(28, 160, 200, 56) };
            ok.Click += (s, e) => f.Close(); f.Controls.Add(ok);
            return f;
        }

        DialogResult Modal(Form f, IWin32Window owner)
        {
            DialogOpen = true;
            try { return owner != null ? f.ShowDialog(owner) : f.ShowDialog(); }
            finally { DialogOpen = false; f.Dispose(); }
        }

        // ---------------- Aktionen ----------------
        // ---------------- Vollbild-Hinweise (App/Spiel gesperrt, Nachricht) ----------------
        Form blockedOverlay, messageOverlay;
        readonly List<object[]> unread = new List<object[]>(); // [from, time, text, id, priority]
        readonly HashSet<int> shownMessages = new HashSet<int>();
        System.Windows.Forms.Timer snooze;
        ToolStripMenuItem miUnread;

        static bool Alive(Form f) { return f != null && !f.IsDisposed && f.Visible; }

        void ShowBlocked(string name, string exe, bool game, bool unknown = false)
        {
            if (Alive(blockedOverlay)) { var o = blockedOverlay as OverlayForm; if (o != null) o.ForceFront(); else ((ThemeScreen)blockedOverlay).ForceFront(); return; }
            if (Alive(messageOverlay)) return; // Nachricht hat Vorrang
            var spec = ThemeSpec.From(state != null ? state.Obj("theme") : null, game ? "game_blocked" : "app_blocked");
            if (spec.Theme == "anime")
                blockedOverlay = OverlayForm.Blocked(name, game, () => AskApp(name, exe, game));
            else
            {
                var f = new ThemeScreen(spec, Screen.PrimaryScreen, false, true) { DialogOpen = () => false };
                f.Setup(spec.Fill(spec.Title, name, "", ""), unknown ? "„" + name + "“ ist neu auf diesem PC. Neue Programme müssen erst von deinen Eltern erlaubt werden – frag sie einfach!" : spec.Fill(spec.Text, name, "", ""), game ? "🎮" : "🧩", unknown ? "Familienschutz · Neue App" : game ? "Familienschutz · Spielzeit" : "Familienschutz · App-Kontrolle", game ? "girl" : "boy");
                f.AddButton("Eltern fragen", "👪", true, () => { f.CloseScreen(); AskApp(name, exe, game); });
                f.AddButton("Schließen", "✕", false, () => f.CloseScreen());
                f.StartFade(state.Obj("theme") == null || state.Obj("theme").Bool("anim", true));
                try { System.Media.SystemSounds.Hand.Play(); } catch { }
                blockedOverlay = f;
            }
            blockedOverlay.FormClosed += (s, e) => { blockedOverlay = null; DialogOpen = false; };
            DialogOpen = true;
            blockedOverlay.Show();
        }

        void AskApp(string name, string exe, bool game)
        {
            var f = new RequestForm("app", (game ? "🎮 " : "🧩 ") + name + " freischalten?");
            if (Modal(f, LockOwner()) != DialogResult.OK) return;
            try
            {
                var r = Http.Local("/agent/request", new Dictionary<string, object> { { "kind", "app" }, { "domain", exe }, { "label", name }, { "minutes", f.Minutes }, { "reason", f.Reason }, { "message", f.Message } });
                if (!r.Bool("ok")) Toast("⚠ Nicht gesendet", r.Str("error"), Theme.Red, 8);
                else Toast("📨 Anfrage gesendet", "Deine Eltern bekommen eine Nachricht aufs Handy. Die Antwort erscheint hier.", Theme.Cyan, 8);
            }
            catch (Exception ex) { Toast("⚠ Fehler", ex.Message, Theme.Red, 8); }
        }

        void ShowMessage(string from, DateTime at, string text, int id = 0, string prio = "normal")
        {
            if (id > 0 && shownMessages.Contains(id) && !unread.Any(u => (int)u[3] == id)) { } // erneute Anzeige über „Später lesen“ erlaubt
            if (id > 0) shownMessages.Add(id);
            if (Alive(blockedOverlay)) blockedOverlay.Close();
            if (Alive(messageOverlay)) { unread.Add(new object[] { from, at, text, id, prio }); UpdateUnread(); return; }
            Action read = () => { Confirm(text, id); NextUnread(); };
            Action later = () => { unread.Add(new object[] { from, at, text, id, prio }); UpdateUnread(); StartSnooze(); };
            var spec = ThemeSpec.From(state != null ? state.Obj("theme") : null, "parent_message");
            if (spec.Theme == "anime" && prio != "high")
                messageOverlay = OverlayForm.Message(from, at, text, read, later);
            else
            {
                var f = new ThemeScreen(spec.Theme == "anime" ? new ThemeSpec { Theme = "neon", Event = "parent_message", Title = spec.Title, Text = spec.Text, Scale = spec.Scale, Accent = spec.Accent } : spec, Screen.PrimaryScreen, false, true) { DialogOpen = () => false };
                f.Setup(spec.Fill(spec.Title, from, "", ""), text, "✉", "Nachricht", "robot");
                f.SetInfo("Von: " + from + " · " + (at.Date == DateTime.Today ? "Heute " : at.ToString("dd.MM. ")) + at.ToString("HH:mm") + " Uhr");
                if (prio == "high") f.SetBadge("❗ WICHTIG");
                f.AddButton("Verstanden", "✔", true, () => { f.CloseScreen(); read(); });
                f.AddButton("Später lesen", "🕑", false, () => { f.CloseScreen(); later(); });
                f.StartFade(state == null || state.Obj("theme") == null || state.Obj("theme").Bool("anim", true));
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                messageOverlay = f;
            }
            DialogOpen = true;
            messageOverlay.FormClosed += (s, e) => { messageOverlay = null; DialogOpen = false; };
            messageOverlay.Show();
        }

        void Confirm(string text, int id)
        {
            string shortText = text.Length > 60 ? text.Substring(0, 60) + "…" : text;
            ThreadPool.QueueUserWorkItem(_ => { try { Http.Local("/agent/event", new Dictionary<string, object> { { "type", "message_read" }, { "title", "Nachricht gelesen: „" + shortText + "“" }, { "detail", "mit „Verstanden“ bestätigt" }, { "ref", id } }); } catch { } });
        }

        void NextUnread()
        {
            if (unread.Count == 0) { UpdateUnread(); return; }
            var m = unread[0]; unread.RemoveAt(0); UpdateUnread();
            ShowMessage((string)m[0], (DateTime)m[1], (string)m[2], (int)m[3], (string)m[4]);
        }

        void StartSnooze()
        {
            if (snooze == null) { snooze = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 }; snooze.Tick += (s, e) => { snooze.Stop(); if (unread.Count > 0) NextUnread(); }; }
            snooze.Stop(); snooze.Start();
        }

        void UpdateUnread()
        {
            if (miUnread == null) return;
            miUnread.Visible = unread.Count > 0;
            miUnread.Text = "📨  " + unread.Count + (unread.Count == 1 ? " ungelesene Nachricht" : " ungelesene Nachrichten");
        }

        public void ShowRequestDialog(IWin32Window owner)
        {
            if (state == null) return;
            var rq = state.Obj("request");
            if (rq != null && (rq.Str("status") == "pending" || rq.Str("status") == "queued"))
            {
                Toast("⏳ Schon gefragt", "Deine Anfrage ist unterwegs. Warte bitte auf die Antwort deiner Eltern.", Theme.Gold, 8);
                return;
            }
            var f = new RequestForm(state.Str("reason"));
            if (Modal(f, owner ?? (IWin32Window)LockOwner()) != DialogResult.OK) return;
            try
            {
                var r = Http.Local("/agent/request", new Dictionary<string, object> { { "kind", "time" }, { "minutes", f.Minutes }, { "reason", f.Reason }, { "message", f.Message } });
                if (!r.Bool("ok")) Toast("⚠ Nicht gesendet", r.Str("error"), Theme.Red, 8);
                else Toast("📨 Anfrage gesendet", "Deine Eltern bekommen jetzt eine Nachricht. Die Antwort erscheint hier.", Theme.Cyan, 8);
            }
            catch (Exception ex) { Toast("⚠ Fehler", ex.Message, Theme.Red, 8); }
        }

        public void ParentDialog(IWin32Window owner)
        {
            var pf = new PasswordForm("Masterpasswort", "Nur für Eltern: Masterpasswort eingeben, um die Einstellungen zu öffnen.");
            if (Modal(pf, owner) != DialogResult.OK) return;
            Dictionary<string, object> r;
            try { r = Http.Local("/agent/parent", new Dictionary<string, object> { { "password", pf.Password }, { "action", "verify" } }); }
            catch (Exception ex) { MessageBox.Show("Dienst nicht erreichbar: " + ex.Message, App.Name); return; }
            if (!r.Bool("ok")) { Toast("🔑 Falsch", r.Str("error"), Theme.Red, 6); return; }
            Modal(new ParentForm(ExitApp, pf.Password, state ?? new Dictionary<string, object>()), owner);
        }

        public void Shutdown(IWin32Window owner)
        {
            if (MessageBox.Show(owner, "PC jetzt herunterfahren?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            exiting = true; foreach (var l in locks) ((ILockScreen)l).AllowClose = true;
            try { Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        }

        public void Logoff(IWin32Window owner)
        {
            if (MessageBox.Show(owner, "Jetzt von Windows abmelden?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            exiting = true; foreach (var l in locks) ((ILockScreen)l).AllowClose = true;
            Native.ExitWindowsEx(0 /*EWX_LOGOFF*/, 0);
        }

        public void ExitApp()
        {
            exiting = true; HideLock(); tray.Visible = false; tray.Dispose(); ExitThread();
        }

        // ---------------- Browser ohne Schutz-Erweiterung schließen ----------------
        void CheckBrowsers(Dictionary<string, object> ext)
        {
            var map = new Dictionary<string, string> { { "chrome", "chrome" }, { "msedge", "edge" }, { "firefox", "firefox" } };
            uint mySession; Native.ProcessIdToSessionId((uint)Process.GetCurrentProcess().Id, out mySession);
            foreach (var kv in map)
            {
                Process[] ps;
                try { ps = Process.GetProcessesByName(kv.Key); } catch { continue; }
                try
                {
                    var windowed = ps.Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && Native.SessionOf(p) == mySession && (DateTime.Now - p.StartTime).TotalSeconds > 60; } catch { return false; } }).ToList();
                    if (windowed.Count == 0) continue;
                    int age = ext.ContainsKey(kv.Value) ? Convert.ToInt32(ext[kv.Value]) : int.MaxValue;
                    if (age < 90) continue;
                    foreach (var p in ps) { try { p.CloseMainWindow(); } catch { } }
                    Thread.Sleep(2500);
                    foreach (var p in Process.GetProcessesByName(kv.Key)) { try { if (Native.SessionOf(p) == mySession) p.Kill(); } catch { } }
                    string nice = kv.Value == "edge" ? "Microsoft Edge" : kv.Value == "chrome" ? "Google Chrome" : "Firefox";
                    ui.Post(_ => Toast("🧩 " + nice + " wurde geschlossen", "Die MS-Family-Control-Erweiterung ist in diesem Browser nicht aktiv. Bitte deine Eltern, sie einzurichten.", Theme.Gold, 12), null);
                    DateTime last;
                    if (!browserEvent.TryGetValue(kv.Value, out last) || (DateTime.Now - last).TotalMinutes > 15)
                    {
                        browserEvent[kv.Value] = DateTime.Now;
                        try { Http.Local("/agent/event", new Dictionary<string, object> { { "type", "browser_closed" }, { "title", nice + " geschlossen: Schutz-Erweiterung fehlt" }, { "detail", "Erweiterung deaktiviert oder entfernt?" } }); } catch { }
                    }
                }
                finally { foreach (var p in ps) p.Dispose(); }
            }
        }
    }
}
