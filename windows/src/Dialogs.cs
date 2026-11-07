using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MSFamilyControl
{
    /// <summary>Basis für dunkle, randlose Dialoge im Neon-Stil</summary>
    internal class NeonDialog : Form
    {
        protected Color Accent = Theme.Cyan;
        public NeonDialog(string title, int w, int h)
        {
            Theme.StyleForm(this, title);
            FormBorderStyle = FormBorderStyle.None; ClientSize = new Size(w, h); TopMost = true; ShowInTaskbar = true; KeyPreview = true;
            Padding = new Padding(26); BackColor = Color.FromArgb(9, 20, 46);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { NativeDrag(); } };
        }
        void NativeDrag() { Capture = false; var m = Message.Create(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); WndProc(ref m); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Hq(g);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, r);
            using (var p = new Pen(Accent, 2)) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            base.OnPaint(e);
        }
        protected Label Title(string text, int y, string glyph = "")
        {
            var l = new Label { Text = text, Font = Theme.F(17f, FontStyle.Bold), ForeColor = Color.White, AutoSize = false, Bounds = new Rectangle(26, y, ClientSize.Width - 52, 38), BackColor = Color.Transparent };
            Controls.Add(l);
            var close = new Label { Text = "✕", Font = Theme.F(13f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(ClientSize.Width - 36, 10), Cursor = Cursors.Hand, BackColor = Color.Transparent };
            close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(close);
            return l;
        }
    }

    /// <summary>Kind fragt nach mehr Zeit (oder Ausnahme bei Bettzeit/Sperre)</summary>
    internal class RequestForm : NeonDialog
    {
        public int Minutes = 30; public string Reason = "school"; public string Message = "";
        public RequestForm(string reasonLock, string title = null) : base("Mehr Zeit anfragen", 560, 500)
        {
            Accent = Theme.Pink;
            Title(title ?? (reasonLock == "bedtime" || reasonLock == "early" || reasonLock == "manual" || reasonLock == "school" ? "🙋 Ausnahme anfragen" : "🙋 Mehr Zeit anfragen"), 20);
            Controls.Add(new Label { Text = "Deine Eltern bekommen sofort eine Nachricht aufs Handy.", Font = Theme.F(10f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(28, 60), BackColor = Color.Transparent });
            Controls.Add(new Label { Text = "Wie lange?", Font = Theme.F(10.5f, FontStyle.Bold), ForeColor = Theme.Cyan, AutoSize = true, Location = new Point(28, 96), BackColor = Color.Transparent });
            var mins = new[] { 15, 30, 60, 90 }; var minChips = new List<Chip>();
            for (int i = 0; i < mins.Length; i++)
            {
                var c = new Chip(mins[i] < 60 ? mins[i] + " Min." : (mins[i] / 60.0).ToString("0.#") + " Std.", mins[i].ToString()) { Location = new Point(28 + i * 126, 122), Checked = mins[i] == 30 };
                minChips.Add(c); Controls.Add(c);
                c.Click += (s, e) => { foreach (var x in minChips) x.Checked = x == s; Minutes = int.Parse(((Chip)s).Value); };
            }
            Controls.Add(new Label { Text = "Wofür brauchst du die Zeit?", Font = Theme.F(10.5f, FontStyle.Bold), ForeColor = Theme.Cyan, AutoSize = true, Location = new Point(28, 180), BackColor = Color.Transparent });
            var reasons = new[] { new[] { "📚 Schule", "school" }, new[] { "🧠 Lernen", "learn" }, new[] { "🎮 Spielen", "play" }, new[] { "👫 Freunde", "friends" }, new[] { "💬 Sonstiges", "other" } };
            var rChips = new List<Chip>();
            for (int i = 0; i < reasons.Length; i++)
            {
                var c = new Chip(reasons[i][0], reasons[i][1]) { Location = new Point(28 + (i % 4) * 126, 206 + (i / 4) * 48), Checked = i == 0 };
                rChips.Add(c); Controls.Add(c);
                c.Click += (s, e) => { foreach (var x in rChips) x.Checked = x == s; Reason = ((Chip)s).Value; };
            }
            Controls.Add(new Label { Text = "Nachricht an deine Eltern (optional)", Font = Theme.F(10.5f, FontStyle.Bold), ForeColor = Theme.Cyan, AutoSize = true, Location = new Point(28, 312), BackColor = Color.Transparent });
            var msg = Theme.Box(); msg.Bounds = new Rectangle(28, 338, 504, 34); msg.MaxLength = 280; Controls.Add(msg);
            var send = new NeonButton("Anfrage senden") { Glyph = "🚀 ", Primary = true, C1 = Theme.Cyan, C2 = Theme.Pink, Bounds = new Rectangle(28, 400, 300, 64) };
            var cancel = new NeonButton("Abbrechen") { Subtle = true, Bounds = new Rectangle(340, 404, 192, 56), Font = Theme.F(11f, FontStyle.Bold) };
            send.Click += (s, e) => { Message = msg.Text.Trim(); DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(send); Controls.Add(cancel);
        }
    }

    /// <summary>Masterpasswort-Abfrage</summary>
    internal class PasswordForm : NeonDialog
    {
        public string Password = "";
        public PasswordForm(string title, string hint) : base(title, 480, 270)
        {
            Accent = Theme.Gold;
            Title("🔑 " + title, 20);
            Controls.Add(new Label { Text = hint, Font = Theme.F(10f), ForeColor = Theme.Muted, AutoSize = false, Bounds = new Rectangle(28, 62, 424, 44), BackColor = Color.Transparent });
            var box = Theme.Box(true); box.Bounds = new Rectangle(28, 112, 424, 34); Controls.Add(box);
            var ok = new NeonButton("OK") { Primary = true, C1 = Theme.Gold, C2 = Theme.Pink, Bounds = new Rectangle(28, 176, 210, 58) };
            var cancel = new NeonButton("Abbrechen") { Subtle = true, Bounds = new Rectangle(246, 178, 206, 54), Font = Theme.F(11f, FontStyle.Bold) };
            ok.Click += (s, e) => { Password = box.Text; DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Password = box.Text; DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(ok); Controls.Add(cancel);
            Shown += (s, e) => { Activate(); box.Focus(); };
        }
    }

    /// <summary>Eltern-Bereich am PC (nach Masterpasswort)</summary>
    internal class ParentForm : NeonDialog
    {
        public ParentForm(Action onExit, string password, Dictionary<string, object> state) : base("Eltern-Bereich", 620, 560)
        {
            Accent = Theme.Gold;
            Title("👪 Eltern-Bereich", 20);
            string info = "Kind: " + state.Str("child") + "   ·   heute genutzt: " + Fmt.Dur(state.Int("used")) + "   ·   " + (state.Int("remaining") < 0 ? "ohne Limit" : "Rest: " + Fmt.Dur(state.Int("remaining")));
            if (state.Bool("paused")) info += "\nSchutz pausiert bis " + state.Str("paused_until");
            info += "\nServer: " + state.Str("server") + (state.Bool("server_ok") ? "  ✓ verbunden" : "  ⚠ " + (state.Str("sync_error") == "" ? "nicht verbunden" : state.Str("sync_error")));
            Controls.Add(new Label { Text = info, Font = Theme.F(9.5f), ForeColor = Theme.Muted, AutoSize = false, Bounds = new Rectangle(28, 60, 564, 64), BackColor = Color.Transparent });

            Controls.Add(new Label { Text = "Sperre aufheben / freigeben für:", Font = Theme.F(10.5f, FontStyle.Bold), ForeColor = Theme.Cyan, AutoSize = true, Location = new Point(28, 132), BackColor = Color.Transparent });
            var opts = new[] { new object[] { "30 Min.", 30 }, new object[] { "1 Std.", 60 }, new object[] { "2 Std.", 120 }, new object[] { "Heute", (int)Math.Max(5, (DateTime.Today.AddDays(1) - DateTime.Now).TotalMinutes) } };
            for (int i = 0; i < opts.Length; i++)
            {
                int m = (int)opts[i][1];
                var b = new NeonButton((string)opts[i][0]) { Subtle = true, C1 = Theme.Green, Bounds = new Rectangle(28 + i * 141, 158, 136, 50), Font = Theme.F(11f, FontStyle.Bold) };
                b.Click += (s, e) => { if (ParentApi.Do(password, "free", m, null, this)) Close(); };
                Controls.Add(b);
            }
            Controls.Add(new Label { Text = "Schutz komplett pausieren (keine Zeitmessung, keine Sperren):", Font = Theme.F(10.5f, FontStyle.Bold), ForeColor = Theme.Cyan, AutoSize = true, Location = new Point(28, 222), BackColor = Color.Transparent });
            var p1 = new NeonButton("1 Std.") { Subtle = true, C1 = Theme.Gold, Bounds = new Rectangle(28, 248, 136, 50), Font = Theme.F(11f, FontStyle.Bold) };
            var p2 = new NeonButton("4 Std.") { Subtle = true, C1 = Theme.Gold, Bounds = new Rectangle(169, 248, 136, 50), Font = Theme.F(11f, FontStyle.Bold) };
            var res = new NeonButton("Schutz fortsetzen") { Subtle = true, C1 = Theme.Cyan, Bounds = new Rectangle(310, 248, 277, 50), Font = Theme.F(11f, FontStyle.Bold) };
            p1.Click += (s, e) => { if (ParentApi.Do(password, "pause", 60, null, this)) Close(); };
            p2.Click += (s, e) => { if (ParentApi.Do(password, "pause", 240, null, this)) Close(); };
            res.Click += (s, e) => { if (ParentApi.Do(password, "resume", 0, null, this)) Close(); };
            Controls.AddRange(new Control[] { p1, p2, res });

            Controls.Add(new Label { Text = "Einstellungen:", Font = Theme.F(10.5f, FontStyle.Bold), ForeColor = Theme.Cyan, AutoSize = true, Location = new Point(28, 314), BackColor = Color.Transparent });
            var mpw = new NeonButton("Masterpasswort ändern") { Subtle = true, Bounds = new Rectangle(28, 340, 276, 50), Font = Theme.F(10.5f, FontStyle.Bold) };
            var srv = new NeonButton("Server-Adresse ändern") { Subtle = true, Bounds = new Rectangle(310, 340, 277, 50), Font = Theme.F(10.5f, FontStyle.Bold) };
            var web = new NeonButton("Control Center öffnen") { Subtle = true, Bounds = new Rectangle(28, 396, 276, 50), Font = Theme.F(10.5f, FontStyle.Bold) };
            var ext = new NeonButton("Browser-Erweiterungen") { Subtle = true, Bounds = new Rectangle(310, 396, 277, 50), Font = Theme.F(10.5f, FontStyle.Bold) };
            mpw.Click += (s, e) =>
            {
                var a = new PasswordForm("Neues Masterpasswort", "Mindestens 6 Zeichen. Gilt sofort auf diesem PC.");
                if (a.ShowDialog(this) != DialogResult.OK) return;
                var b2 = new PasswordForm("Wiederholen", "Bitte das neue Masterpasswort wiederholen.");
                if (b2.ShowDialog(this) != DialogResult.OK) return;
                if (a.Password != b2.Password) { MessageBox.Show(this, "Die Eingaben stimmen nicht überein.", App.Name); return; }
                if (ParentApi.Do(password, "master", 0, a.Password, this)) { password = a.Password; MessageBox.Show(this, "Masterpasswort geändert.", App.Name); }
            };
            srv.Click += (s, e) =>
            {
                var v = InputBox.Show(this, "Server-Adresse", "Neue Adresse inkl. Port, z. B. https://family.meine-domain.de oder http://192.168.178.20:8080", state.Str("server"));
                if (v != null && ParentApi.Do(password, "server", 0, v, this)) MessageBox.Show(this, "Server-Adresse gespeichert.", App.Name);
            };
            web.Click += (s, e) => { try { Process.Start(state.Str("server") + "/"); } catch { } };
            ext.Click += (s, e) => { try { Process.Start("explorer.exe", "\"" + System.IO.Path.Combine(App.InstallDir, "extensions") + "\""); } catch { } };
            Controls.AddRange(new Control[] { mpw, srv, web, ext });

            var exit = new NeonButton("App beenden (bis Neustart)") { Subtle = true, C1 = Theme.Red, Bounds = new Rectangle(28, 476, 300, 52), Font = Theme.F(10.5f, FontStyle.Bold) };
            var uninst = new NeonButton("Deinstallieren") { Subtle = true, C1 = Theme.Red, Bounds = new Rectangle(334, 476, 253, 52), Font = Theme.F(10.5f, FontStyle.Bold) };
            exit.Click += (s, e) => { if (MessageBox.Show(this, "Schutz bis zum nächsten Neustart beenden?", App.Name, MessageBoxButtons.YesNo) == DialogResult.Yes && ParentApi.Do(password, "exit", 0, null, this)) { Close(); if (onExit != null) onExit(); } };
            uninst.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--uninstall") { Verb = "runas", UseShellExecute = true }); Close(); }
                catch { MessageBox.Show(this, "Zum Deinstallieren werden Administratorrechte benötigt.", App.Name); }
            };
            Controls.AddRange(new Control[] { exit, uninst });
        }
    }

    /// <summary>Kleine Hinweis-Box unten rechts (Vorwarnung, gesperrte App, Antwort der Eltern)</summary>
    internal class ToastForm : Form
    {
        readonly Timer t;
        readonly Color accent;
        public ToastForm(string title, string text, Color accent, int seconds, string action = null, Action onAction = null)
        {
            this.accent = accent;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Theme.Panel;
            Size = new Size(430, action != null ? 168 : 120); Icon = Theme.AppIcon;
            var wa = Screen.PrimaryScreen.WorkingArea; Location = new Point(wa.Right - Width - 18, wa.Bottom - Height - 18);
            Controls.Add(new Label { Text = title, Font = Theme.F(13f, FontStyle.Bold), ForeColor = Color.White, AutoSize = false, Bounds = new Rectangle(18, 14, 380, 28), BackColor = Color.Transparent });
            Controls.Add(new Label { Text = text, Font = Theme.F(10f), ForeColor = Theme.Text, AutoSize = false, Bounds = new Rectangle(18, 46, 394, 62), BackColor = Color.Transparent });
            var x = new Label { Text = "✕", Font = Theme.F(11f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(400, 8), Cursor = Cursors.Hand };
            x.Click += (s, e) => Close(); Controls.Add(x);
            if (action != null)
            {
                var b = new NeonButton(action) { Primary = true, C1 = Theme.Cyan, C2 = Theme.Pink, Bounds = new Rectangle(14, 108, 260, 52), Font = Theme.F(11f, FontStyle.Bold) };
                b.Click += (s, e) => { Close(); if (onAction != null) onAction(); };
                Controls.Add(b);
            }
            t = new Timer { Interval = seconds * 1000 }; t.Tick += (s, e) => { t.Stop(); Close(); }; t.Start();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08 /*TOPMOST*/; return cp; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var p = new Pen(accent, 3)) e.Graphics.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            using (var b = new SolidBrush(accent)) e.Graphics.FillRectangle(b, 0, 0, 6, Height);
        }
        protected override void Dispose(bool d) { if (d) t.Dispose(); base.Dispose(d); }
    }

    internal static class InputBox
    {
        public static string Show(IWin32Window owner, string title, string hint, string value)
        {
            var f = new NeonDialog(title, 520, 250);
            f.Controls.Add(new Label { Text = title, Font = Theme.F(15f, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Location = new Point(26, 20), BackColor = Color.Transparent });
            f.Controls.Add(new Label { Text = hint, Font = Theme.F(9.5f), ForeColor = Theme.Muted, AutoSize = false, Bounds = new Rectangle(28, 58, 464, 40), BackColor = Color.Transparent });
            var box = Theme.Box(); box.Bounds = new Rectangle(28, 104, 464, 34); box.Text = value; f.Controls.Add(box);
            var ok = new NeonButton("Speichern") { Primary = true, Bounds = new Rectangle(28, 166, 220, 56) };
            var cancel = new NeonButton("Abbrechen") { Subtle = true, Bounds = new Rectangle(256, 168, 236, 52), Font = Theme.F(11f, FontStyle.Bold) };
            ok.Click += (s, e) => { f.DialogResult = DialogResult.OK; f.Close(); };
            cancel.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; f.Close(); };
            f.Controls.Add(ok); f.Controls.Add(cancel);
            return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
        }
    }

    /// <summary>Transparenz für das Kind: was wird erfasst?</summary>
    internal class InfoForm : NeonDialog
    {
        public InfoForm(Dictionary<string, object> st) : base("Was wird erfasst?", 600, 470)
        {
            Title("🛡 MS Family Control", 20);
            string remain = st.Int("remaining") < 0 ? "kein Zeitlimit" : Fmt.Dur(st.Int("remaining"));
            string text =
                "Hallo " + st.Str("child") + "! Dieser PC wird von deinen Eltern mit MS Family Control betreut.\n\n" +
                "Heute genutzt: " + Fmt.Dur(st.Int("used")) + "   ·   Restzeit: " + remain + "\n" +
                "Erlaubt heute: " + st.Str("start") + " – " + st.Str("end") + " Uhr\n\n" +
                "Das wird erfasst:\n" +
                "  •  wie lange der PC heute benutzt wird\n" +
                "  •  welches Programm gerade im Vordergrund ist (nur Name)\n" +
                "  •  welche Webseiten (nur die Adresse, z. B. youtube.com) wie lange offen sind\n\n" +
                "Das wird NICHT erfasst:\n" +
                "  •  keine Tastatureingaben, Passwörter oder Chat-Nachrichten\n" +
                "  •  keine Bildschirmfotos, keine Kamera, kein Mikrofon\n\n" +
                "Wenn du mehr Zeit brauchst, kannst du jederzeit fragen. 🙋";
            Controls.Add(new Label { Text = text, Font = Theme.F(10.5f), ForeColor = Theme.Text, AutoSize = false, Bounds = new Rectangle(28, 64, 544, 330), BackColor = Color.Transparent });
            var ok = new NeonButton("Alles klar") { Primary = true, Bounds = new Rectangle(28, 400, 220, 54) };
            ok.Click += (s, e) => Close(); Controls.Add(ok);
            Controls.Add(new Label { Text = App.Copyright, Font = Theme.F(8f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(262, 422), BackColor = Color.Transparent });
        }
    }
}
