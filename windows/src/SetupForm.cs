using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MSFamilyControl
{
    /// <summary>Einrichtungsassistent bei der Erstinstallation (Server-URL mit Port, Kopplungscode, Masterpasswort)</summary>
    internal class SetupForm : Form
    {
        int page;
        readonly Panel[] pages = new Panel[4];
        readonly NeonButton next, back;
        readonly Label stepLbl;
        TextBox tbServer, tbCode, tbName, tbPw1, tbPw2;
        Label lblTest, lblErr;
        CheckedListBox users;
        CheckBox cbBrowsers;
        TextBox logBox;
        string serverOk = "", family = "";
        bool installed;

        public SetupForm()
        {
            Theme.StyleForm(this, "MS Family Control – Einrichtung");
            ClientSize = new Size(820, 600);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            var head = new Panel { Bounds = new Rectangle(0, 0, 820, 96), BackColor = Color.FromArgb(9, 16, 40) };
            head.Paint += (s, e) =>
            {
                var g = e.Graphics; Theme.Hq(g);
                var ic = Theme.Img("icon-128.png"); if (ic != null) g.DrawImage(ic, 24, 14, 68, 68);
                using (var f = Theme.F(22f, FontStyle.Bold | FontStyle.Italic)) Theme.GlowText(g, "Family Control Center", f, Theme.Cyan, new RectangleF(104, 14, 600, 44), StringFormat.GenericTypographic, 5);
                using (var f = Theme.F(10f)) using (var b = new SolidBrush(Theme.Muted)) g.DrawString("Windows-App einrichten · Version " + App.Version, f, b, 108, 58);
                using (var p = new Pen(Theme.Pink, 2)) g.DrawLine(p, 0, 94, 820, 94);
            };
            Controls.Add(head);
            stepLbl = new Label { AutoSize = true, Location = new Point(28, 112), Font = Theme.F(10f, FontStyle.Bold), ForeColor = Theme.Pink };
            Controls.Add(stepLbl);
            for (int i = 0; i < pages.Length; i++) { pages[i] = new Panel { Bounds = new Rectangle(20, 136, 780, 380), Visible = false, BackColor = Theme.Bg }; Controls.Add(pages[i]); }
            BuildPages();
            lblErr = new Label { AutoSize = false, Bounds = new Rectangle(28, 518, 560, 40), ForeColor = Theme.Red, Font = Theme.F(10f) };
            Controls.Add(lblErr);
            back = new NeonButton("Zurück") { Subtle = true, Bounds = new Rectangle(440, 528, 150, 56), Font = Theme.F(11f, FontStyle.Bold) };
            next = new NeonButton("Weiter") { Primary = true, Bounds = new Rectangle(600, 524, 200, 62) };
            back.Click += (s, e) => Show(page - 1);
            next.Click += (s, e) => Next();
            Controls.Add(back); Controls.Add(next);
            Show(0);
        }

        Label L(Panel p, string t, int x, int y, float size = 10.5f, FontStyle st = FontStyle.Regular, Color? c = null, int w = 0)
        {
            var l = new Label { Text = t, Location = new Point(x, y), Font = Theme.F(size, st), ForeColor = c ?? Theme.Text, BackColor = Color.Transparent };
            if (w > 0) { l.AutoSize = false; l.Size = new Size(w, TextRenderer.MeasureText(t, l.Font, new Size(w, 0), TextFormatFlags.WordBreak).Height + 6); } else l.AutoSize = true;
            p.Controls.Add(l); return l;
        }

        void BuildPages()
        {
            // 1 – Server
            var p = pages[0];
            L(p, "Willkommen! 👋", 8, 0, 16f, FontStyle.Bold);
            L(p, "Diese App sorgt auf dem PC deines Kindes für Bildschirmzeit, Bettzeit, App- und Webseitensperren.\nSie läuft danach als Windows-Dienst im Hintergrund und lässt sich nur mit dem Masterpasswort beenden.", 8, 38, 10f, FontStyle.Regular, Theme.Muted, 760);
            L(p, "Adresse deines Family-Control-Servers (mit Port):", 8, 104, 10.5f, FontStyle.Bold, Theme.Cyan);
            tbServer = Theme.Box(); tbServer.Bounds = new Rectangle(10, 132, 560, 34); tbServer.Text = "http://"; p.Controls.Add(tbServer);
            var test = new NeonButton("Verbindung testen") { Subtle = true, Bounds = new Rectangle(580, 124, 190, 50), Font = Theme.F(10.5f, FontStyle.Bold) };
            test.Click += (s, e) => TestServer(); p.Controls.Add(test);
            L(p, "Beispiele:  http://192.168.178.20:8080   ·   https://family.meine-domain.de", 8, 172, 9.5f, FontStyle.Regular, Theme.Muted);
            lblTest = L(p, "", 8, 206, 11f, FontStyle.Bold, Theme.Green);
            L(p, "Wichtig: Dein Kind sollte unter Windows ein Standard-Benutzerkonto (ohne Administratorrechte) haben – sonst kann es den Schutz umgehen.", 8, 300, 9.5f, FontStyle.Regular, Theme.Gold, 760);

            // 2 – Kopplung
            p = pages[1];
            L(p, "PC mit dem Kinderprofil koppeln", 8, 0, 16f, FontStyle.Bold);
            L(p, "Im Control Center (auch auf dem iPhone) unter „Geräte“ → „PC koppeln“ einen Code erzeugen.", 8, 38, 10f, FontStyle.Regular, Theme.Muted, 760);
            L(p, "Kopplungscode:", 8, 84, 10.5f, FontStyle.Bold, Theme.Cyan);
            tbCode = Theme.Box(); tbCode.Bounds = new Rectangle(10, 112, 300, 40); tbCode.Font = Theme.F(18f, FontStyle.Bold, "Consolas"); tbCode.CharacterCasing = CharacterCasing.Upper; tbCode.MaxLength = 12; p.Controls.Add(tbCode);
            L(p, "Name dieses PCs:", 8, 170, 10.5f, FontStyle.Bold, Theme.Cyan);
            tbName = Theme.Box(); tbName.Bounds = new Rectangle(10, 198, 300, 34); tbName.Text = Environment.MachineName; p.Controls.Add(tbName);

            // 3 – Masterpasswort + Konten
            p = pages[2];
            L(p, "Masterpasswort festlegen 🔑", 8, 0, 16f, FontStyle.Bold);
            L(p, "Damit können nur Eltern die App beenden, die Sperre am PC aufheben oder deinstallieren. Später auch im Control Center änderbar.", 8, 36, 10f, FontStyle.Regular, Theme.Muted, 760);
            L(p, "Masterpasswort (mind. 6 Zeichen):", 8, 84, 10.5f, FontStyle.Bold, Theme.Cyan);
            tbPw1 = Theme.Box(true); tbPw1.Bounds = new Rectangle(10, 110, 340, 34); p.Controls.Add(tbPw1);
            L(p, "Wiederholen:", 8, 154, 10.5f, FontStyle.Bold, Theme.Cyan);
            tbPw2 = Theme.Box(true); tbPw2.Bounds = new Rectangle(10, 180, 340, 34); p.Controls.Add(tbPw2);
            L(p, "Diese Windows-Konten NICHT überwachen (Eltern):", 380, 84, 10.5f, FontStyle.Bold, Theme.Cyan);
            users = new CheckedListBox { Bounds = new Rectangle(382, 112, 388, 130), BackColor = Color.FromArgb(10, 22, 51), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, CheckOnClick = true, Font = Theme.F(10.5f) };
            string me = Environment.UserName;
            foreach (var u in Installer.LocalUsers()) users.Items.Add(u, string.Equals(u, me, StringComparison.OrdinalIgnoreCase));
            p.Controls.Add(users);
            cbBrowsers = new CheckBox { Text = "Kinder-Konten: Inkognito/InPrivate, Gastmodus und neue Browser-Profile deaktivieren (empfohlen)", Checked = true, AutoSize = true, Location = new Point(10, 262), ForeColor = Theme.Text, Font = Theme.F(10f) };
            p.Controls.Add(cbBrowsers);
            L(p, "Gilt nur für die überwachten Kinder-Konten – Eltern-Konten und ihre Erweiterungen bleiben unverändert.", 30, 290, 9.5f, FontStyle.Regular, Theme.Muted, 740);

            // 4 – Installation
            p = pages[3];
            L(p, "Installation", 8, 0, 16f, FontStyle.Bold);
            logBox = new TextBox { Multiline = true, ReadOnly = true, Bounds = new Rectangle(10, 40, 760, 150), BackColor = Color.FromArgb(10, 22, 51), ForeColor = Theme.Cyan, BorderStyle = BorderStyle.FixedSingle, Font = Theme.F(9.5f, FontStyle.Regular, "Consolas"), ScrollBars = ScrollBars.Vertical };
            p.Controls.Add(logBox);
            L(p, "Danach die Browser-Erweiterung aktivieren (einmalig, je Windows-Konto des Kindes):\n" +
                 "Chrome/Edge: Erweiterungsseite öffnen → „Entwicklermodus“ an → „Entpackte Erweiterung laden“ → Ordner „chrome-edge“ wählen.\n" +
                 "Firefox: siehe ANLEITUNG im Ordner „firefox“. Browser ohne aktive Erweiterung werden automatisch geschlossen.", 8, 198, 9.5f, FontStyle.Regular, Theme.Muted, 760);
            var bChrome = new NeonButton("Chrome-Erweiterungen") { Subtle = true, Bounds = new Rectangle(6, 300, 250, 50), Font = Theme.F(10f, FontStyle.Bold) };
            var bEdge = new NeonButton("Edge-Erweiterungen") { Subtle = true, Bounds = new Rectangle(262, 300, 250, 50), Font = Theme.F(10f, FontStyle.Bold) };
            var bDir = new NeonButton("Ordner öffnen") { Subtle = true, Bounds = new Rectangle(518, 300, 250, 50), Font = Theme.F(10f, FontStyle.Bold) };
            bChrome.Click += (s, e) => Open("chrome.exe", "chrome://extensions");
            bEdge.Click += (s, e) => Open("msedge.exe", "edge://extensions");
            bDir.Click += (s, e) => Open("explorer.exe", "\"" + Path.Combine(App.InstallDir, "extensions") + "\"");
            p.Controls.Add(bChrome); p.Controls.Add(bEdge); p.Controls.Add(bDir);
        }

        static void Open(string exe, string arg) { try { Process.Start(exe, arg); } catch (Exception ex) { MessageBox.Show(ex.Message, App.Name); } }

        void Show(int i)
        {
            if (i < 0 || i >= pages.Length) return;
            page = i;
            for (int k = 0; k < pages.Length; k++) pages[k].Visible = k == i;
            stepLbl.Text = "SCHRITT " + (i + 1) + " VON 4  ·  " + new[] { "SERVER", "KOPPLUNG", "MASTERPASSWORT", "INSTALLATION" }[i];
            back.Visible = i > 0 && i < 3;
            next.Text = i == 2 ? "Installieren" : i == 3 ? "Fertig" : "Weiter";
            next.Enabled = !(i == 3 && !installed);
            lblErr.Text = "";
            if (i == 1) tbCode.Focus();
        }

        bool TestServer()
        {
            lblErr.Text = ""; lblTest.Text = "Verbinde …"; lblTest.ForeColor = Theme.Muted; Refresh();
            var url = Http.Normalize(tbServer.Text);
            try
            {
                var r = Http.Call(url, "ping", null, null, 8000);
                if (!r.Bool("ok")) throw new Exception("Unerwartete Antwort");
                serverOk = url; family = r.Str("family");
                tbServer.Text = url;
                lblTest.ForeColor = Theme.Green; lblTest.Text = "✓ Verbunden mit „" + family + "“ (Server " + r.Str("version") + ")";
                return true;
            }
            catch (Exception ex)
            {
                serverOk = ""; lblTest.ForeColor = Theme.Red; lblTest.Text = "✗ Keine Verbindung: " + ex.Message;
                return false;
            }
        }

        void Next()
        {
            lblErr.Text = "";
            switch (page)
            {
                case 0:
                    if (serverOk == "" || serverOk != Http.Normalize(tbServer.Text)) { if (!TestServer()) return; }
                    Show(1); break;
                case 1:
                    if (tbCode.Text.Replace("-", "").Trim().Length < 6) { lblErr.Text = "Bitte den Kopplungscode aus dem Control Center eingeben."; return; }
                    Show(2); break;
                case 2:
                    if (tbPw1.Text.Length < 6) { lblErr.Text = "Das Masterpasswort muss mindestens 6 Zeichen haben."; return; }
                    if (tbPw1.Text != tbPw2.Text) { lblErr.Text = "Die beiden Passwörter stimmen nicht überein."; return; }
                    var exempt = users.CheckedItems.Cast<object>().Select(x => x.ToString()).ToList();
                    if (exempt.Count == users.Items.Count && users.Items.Count > 0 && MessageBox.Show(this, "Alle Konten sind als „nicht überwachen“ markiert – dann greift der Schutz für niemanden. Trotzdem fortfahren?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    Show(3); DoInstall(exempt); break;
                case 3:
                    Close(); break;
            }
        }

        void DoInstall(List<string> exempt)
        {
            next.Enabled = false; back.Visible = false;
            string code = tbCode.Text.Replace("-", "").Replace(" ", "").Trim(), name = tbName.Text.Trim(), pw = tbPw1.Text;
            bool br = cbBrowsers.Checked, ext = false;
            Action<string> log = m => BeginInvoke((Action)(() => logBox.AppendText(m + Environment.NewLine)));
            new Thread(() =>
            {
                try
                {
                    log("Kopplung mit dem Server …");
                    var res = Http.Call(serverOk, "pair", new Dictionary<string, object> { { "code", code }, { "name", name }, { "os", Environment.OSVersion.VersionString }, { "version", App.Version } }, null);
                    var child = res.Obj("child");
                    log("✓ Gekoppelt mit Kinderprofil „" + (child != null ? child.Str("name") : "?") + "“");
                    var cfg = new Config { Server = serverOk, DeviceId = res.Int("device_id"), Token = res.Str("token"), ChildName = child != null ? child.Str("name") : "", BlockOtherExtensions = ext };
                    cfg.SetMaster(pw);
                    Installer.Install(cfg, exempt, br, log);
                    BeginInvoke((Action)(() => { installed = true; next.Enabled = true; next.Text = "Fertig"; lblErr.ForeColor = Theme.Green; lblErr.Text = "✓ Installiert. Die App startet automatisch, sobald sich das Kind anmeldet."; }));
                }
                catch (Exception ex)
                {
                    log("✗ Fehler: " + ex.Message);
                    BeginInvoke((Action)(() => { lblErr.ForeColor = Theme.Red; lblErr.Text = ex.Message; back.Visible = true; next.Enabled = false; page = 3; back.Click += (s, e) => { }; }));
                    BeginInvoke((Action)(() => { back.Visible = true; }));
                }
            }) { IsBackground = true }.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (page == 3 && !installed && !next.Enabled && logBox.Text.Length > 0 && !logBox.Text.Contains("Fehler")) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }
    }
}
