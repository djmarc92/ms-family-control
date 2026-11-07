using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.ServiceProcess;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("MS Family Control")]
[assembly: System.Reflection.AssemblyProduct("MS Family Control")]
[assembly: System.Reflection.AssemblyDescription("Bildschirmzeit & Jugendschutz für Kinder-PCs")]
[assembly: System.Reflection.AssemblyCompany("MS-IT Service · Marc-André Straßen")]
[assembly: System.Reflection.AssemblyCopyright("Copyright © 2026 Marc-André Straßen")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace MSFamilyControl
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var a = new HashSet<string>(args.Select(x => x.ToLowerInvariant()));
            if (a.Contains("--service"))
            {
                ServiceBase.Run(new FamilyService());
                return 0;
            }
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Log.Write("UI: " + e.Exception);

            if (a.Contains("--agent")) { Agent.Run(); return 0; }
            if (a.Contains("--uninstall")) return Uninstall();
            if (a.Contains("--console")) { RunConsole(); return 0; }

            bool installed = Installer.ServiceInstalled();
            if (installed && !a.Contains("--setup"))
            {
                bool isInstalledCopy = string.Equals(System.IO.Path.GetFullPath(Application.ExecutablePath), System.IO.Path.GetFullPath(App.InstalledExe), StringComparison.OrdinalIgnoreCase);
                if (a.Contains("--parent") || (isInstalledCopy && !Security.IsAdmin())) { ParentFlow(); return 0; }
                if (!Security.IsAdmin()) return Elevate("");
                var choice = ChoiceForm.Ask();
                if (choice == "parent") { ParentFlow(); return 0; }
                if (choice == "update") return Update();
                if (choice != "setup") return 0;
                var cfg = Config.Load();
                if (cfg != null && !AskMaster(cfg, "Zum Neu-Einrichten bitte das bisherige Masterpasswort eingeben.")) return 1;
            }
            if (!Security.IsAdmin()) return Elevate(installed ? "--setup" : "");
            Application.Run(new SetupForm());
            return 0;
        }

        static int Elevate(string args)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { Verb = "runas", UseShellExecute = true });
                return 0;
            }
            catch
            {
                MessageBox.Show("Für die Installation werden Administratorrechte benötigt.\nBitte mit einem Eltern-/Administratorkonto starten.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }

        /// <summary>Update: nur Programmdateien ersetzen – Kopplung, Masterpasswort, Konten und Zeitkonto bleiben erhalten</summary>
        static int Update()
        {
            var cfg = Config.Load();
            if (cfg == null) { MessageBox.Show("Keine bestehende Konfiguration gefunden – bitte „Neu einrichten“ wählen.", App.Name); return 1; }
            if (!AskMaster(cfg, "Zum Aktualisieren bitte das Masterpasswort eingeben.")) return 1;
            var log = new List<string>();
            Cursor.Current = Cursors.WaitCursor;
            try { Installer.Update(log.Add); }
            catch (Exception ex) { MessageBox.Show("Update fehlgeschlagen: " + ex.Message + "\n\n" + string.Join("\n", log), App.Name, MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            MessageBox.Show("MS Family Control wurde auf Version " + App.Version + " aktualisiert.\n\nAlle Einstellungen, die Kopplung mit dem Server, das Masterpasswort und das heutige Zeitkonto sind erhalten geblieben.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        static bool AskMaster(Config cfg, string hint)
        {
            for (int i = 0; i < 3; i++)
            {
                var f = new PasswordForm("Masterpasswort", hint);
                if (f.ShowDialog() != DialogResult.OK) return false;
                if (cfg.CheckMaster(f.Password)) return true;
                System.Threading.Thread.Sleep(1200);
                MessageBox.Show("Masterpasswort falsch.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }

        static int Uninstall()
        {
            if (!Security.IsAdmin()) return Elevate("--uninstall");
            var cfg = Config.Load();
            if (cfg != null && !AskMaster(cfg, "Zum Deinstallieren das Masterpasswort eingeben.")) return 1;
            if (MessageBox.Show("MS Family Control wirklich vollständig entfernen?\nAlle Sperren auf diesem PC werden aufgehoben.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return 1;
            var log = new List<string>();
            try { Installer.Uninstall(cfg, log.Add); }
            catch (Exception ex) { MessageBox.Show("Fehler: " + ex.Message, App.Name); return 1; }
            MessageBox.Show("MS Family Control wurde entfernt.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Installer.ScheduleDelete();
            return 0;
        }

        /// <summary>Eltern-Bereich über Startmenü (spricht mit dem Dienst)</summary>
        static void ParentFlow()
        {
            var pf = new PasswordForm("Masterpasswort", "Eltern-Bereich von MS Family Control öffnen.");
            if (pf.ShowDialog() != DialogResult.OK) return;
            Dictionary<string, object> r, st;
            try
            {
                r = Http.Local("/agent/parent", new Dictionary<string, object> { { "password", pf.Password }, { "action", "verify" } });
                st = Http.Local("/agent/state");
            }
            catch (Exception ex) { MessageBox.Show("Der Dienst „MS Family Control“ ist nicht erreichbar: " + ex.Message, App.Name); return; }
            if (!r.Bool("ok")) { MessageBox.Show(r.Str("error"), App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Application.Run(new ParentForm(null, pf.Password, st));
        }

        /// <summary>Für Tests: Engine im Vordergrund laufen lassen</summary>
        static void RunConsole()
        {
            var e = new Engine(); e.Start();
            MessageBox.Show("Engine läuft (Testmodus). OK beendet.", App.Name);
            e.Stop(false);
        }
    }

    /// <summary>Auswahl, wenn die App schon installiert ist</summary>
    internal class ChoiceForm : NeonDialog
    {
        string result = "";
        ChoiceForm() : base("MS Family Control", 560, 360)
        {
            Controls.Add(new Label { Text = "MS Family Control ist bereits installiert", Font = Theme.F(15f, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Location = new Point(26, 22), BackColor = Color.Transparent });
            string inst = "";
            try { using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\MSFamilyControl")) inst = k == null ? "" : Convert.ToString(k.GetValue("Version", "")); } catch { }
            Controls.Add(new Label { Text = "Installiert: " + (inst == "" ? "?" : inst) + "   ·   Diese Datei: " + App.Version, Font = Theme.F(10f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(28, 58), BackColor = Color.Transparent });
            var up = new NeonButton("Aktualisieren – alles behalten") { Glyph = "⬆ ", Primary = true, Bounds = new Rectangle(26, 96, 508, 64) };
            var pa = new NeonButton("Eltern-Bereich öffnen") { Glyph = "🔑 ", Subtle = true, C1 = Theme.Gold, Bounds = new Rectangle(26, 170, 508, 56), Font = Theme.F(11.5f, FontStyle.Bold) };
            var se = new NeonButton("Neu einrichten (neu koppeln)") { Subtle = true, C1 = Theme.Red, Bounds = new Rectangle(26, 236, 508, 56), Font = Theme.F(11.5f, FontStyle.Bold) };
            up.Click += (s, e) => { result = "update"; Close(); };
            pa.Click += (s, e) => { result = "parent"; Close(); };
            se.Click += (s, e) => { result = "setup"; Close(); };
            Controls.AddRange(new Control[] { up, pa, se });
            Controls.Add(new Label { Text = "Aktualisieren behält Kopplung, Masterpasswort, Konten und Zeitkonto.", Font = Theme.F(9f), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(28, 312), BackColor = Color.Transparent });
        }
        public static string Ask() { using (var f = new ChoiceForm()) { f.ShowDialog(); return f.result; } }
    }

    internal class FamilyService : ServiceBase
    {
        readonly Engine engine = new Engine();
        public FamilyService()
        {
            ServiceName = App.ServiceName; CanStop = true; CanShutdown = true; CanHandleSessionChangeEvent = true; CanPauseAndContinue = false; AutoLog = true;
        }
        protected override void OnStart(string[] args) { engine.Start(); }
        protected override void OnStop() { engine.Stop(false); }
        protected override void OnShutdown() { engine.Stop(true); }
        protected override void OnSessionChange(SessionChangeDescription d) { engine.SessionChanged(); }
    }
}
