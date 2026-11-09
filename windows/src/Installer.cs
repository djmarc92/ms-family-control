using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace MSFamilyControl
{
    /// <summary>Installation / Deinstallation (läuft mit Administratorrechten)</summary>
    internal static class Installer
    {
        public static bool ServiceInstalled()
        {
            try { return ServiceController.GetServices().Any(s => s.ServiceName == App.ServiceName); } catch { return false; }
        }

        static string Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(30000); return o.Trim(); }
        }

        /// <summary>Lokale Benutzerkonten mit Profil (für die Auswahl „Eltern-Konten nicht überwachen“)</summary>
        public static List<string> LocalUsers()
        {
            var list = new List<string>();
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
                    foreach (var sid in k.GetSubKeyNames())
                    {
                        if (!sid.StartsWith("S-1-5-21-") && !sid.StartsWith("S-1-12-1-")) continue;
                        try
                        {
                            var name = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
                            var shortName = name.Contains("\\") ? name.Substring(name.IndexOf('\\') + 1) : name;
                            if (!list.Contains(shortName)) list.Add(shortName);
                        }
                        catch { }
                    }
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static void Install(Config cfg, List<string> exemptUsers, bool lockBrowsers, Action<string> log)
        {
            // 1) Alte Instanz stoppen (Reparatur / Update)
            if (ServiceInstalled())
            {
                log("Vorhandene Installation wird aktualisiert …");
                Run("sc.exe", "stop " + App.ServiceName);
                System.Threading.Thread.Sleep(2500);
                foreach (var p in Process.GetProcessesByName("MSFamilyControl")) { try { if (p.Id != Process.GetCurrentProcess().Id) p.Kill(); } catch { } }
            }
            // 2) Dateien
            log("Programmdateien kopieren …");
            Directory.CreateDirectory(App.InstallDir);
            string self = Process.GetCurrentProcess().MainModule.FileName;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(App.InstalledExe), StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < 10; i++) { try { File.Copy(self, App.InstalledExe, true); break; } catch { System.Threading.Thread.Sleep(800); if (i == 9) throw; } }
            }
            log("Browser-Erweiterungen entpacken …");
            ExtractExtension("ext-chromium.zip", Path.Combine(App.InstallDir, "extensions", "chrome-edge"));
            ExtractExtension("ext-firefox.zip", Path.Combine(App.InstallDir, "extensions", "firefox"));
            // 3) Konfiguration
            log("Konfiguration speichern (nur für Administratoren lesbar) …");
            cfg.InstalledAt = DateTime.Now.ToString("s");
            cfg.Save();
            using (var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\MSFamilyControl"))
            {
                k.SetValue("ExemptUsers", exemptUsers.ToArray(), RegistryValueKind.MultiString);
                k.SetValue("InstallDir", App.InstallDir); k.SetValue("Version", App.Version);
            }
            // 4) Browser-Richtlinien
            log("Browser-Richtlinien: gelten nur für die Kinder-Konten (werden bei deren Anmeldung gesetzt) …");
            BrowserPolicy.RemoveMachineWide();
            using (var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\MSFamilyControl")) k.SetValue("BrowserPolicies", lockBrowsers ? 1 : 0, RegistryValueKind.DWord);
            foreach (var u in exemptUsers) { try { BrowserPolicy.Remove(new NTAccount(u).Translate(typeof(SecurityIdentifier)).Value); } catch { } }
            // 5) Dienst
            log("Windows-Dienst einrichten …");
            if (!ServiceInstalled())
            {
                var o = Run("sc.exe", "create " + App.ServiceName + " binPath= \"\\\"" + App.InstalledExe + "\\\" --service\" start= auto DisplayName= \"MS Family Control\"");
                if (!ServiceInstalled()) throw new Exception("Dienst konnte nicht angelegt werden: " + o);
            }
            else Run("sc.exe", "config " + App.ServiceName + " binPath= \"\\\"" + App.InstalledExe + "\\\" --service\" start= auto");
            Run("sc.exe", "description " + App.ServiceName + " \"Bildschirmzeit und Jugendschutz für Kinder (MS Family Control)\"");
            Run("sc.exe", "failure " + App.ServiceName + " reset= 60 actions= restart/1000/restart/1000/restart/5000");
            Run("sc.exe", "failureflag " + App.ServiceName + " 1");
            // 6) Startmenü + Programme & Features
            log("Startmenü-Einträge anlegen …");
            string sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "MS Family Control");
            Directory.CreateDirectory(sm);
            Shortcut(Path.Combine(sm, "MS Family Control (Eltern).lnk"), App.InstalledExe, "--parent", "Eltern-Bereich (Masterpasswort)");
            Shortcut(Path.Combine(sm, "MS Family Control deinstallieren.lnk"), App.InstalledExe, "--uninstall", "Deinstallation (Masterpasswort)");
            using (var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MSFamilyControl"))
            {
                k.SetValue("DisplayName", "MS Family Control"); k.SetValue("DisplayVersion", App.Version); k.SetValue("Publisher", "MS-IT Service · Marc-André Straßen");
                k.SetValue("DisplayIcon", App.InstalledExe); k.SetValue("InstallLocation", App.InstallDir);
                k.SetValue("UninstallString", "\"" + App.InstalledExe + "\" --uninstall"); k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            // 7) Start
            log("Dienst starten …");
            Run("sc.exe", "start " + App.ServiceName);
            log("Fertig ✓");
        }

        /// <summary>Nur Programmdateien ersetzen; config.json, state.json, Registry-Einstellungen bleiben unverändert</summary>
        public static void Update(Action<string> log)
        {
            log("Dienst anhalten …");
            Run("sc.exe", "stop " + App.ServiceName);
            for (int i = 0; i < 20; i++)
            {
                try { using (var sc = new ServiceController(App.ServiceName)) { if (sc.Status == ServiceControllerStatus.Stopped) break; } } catch { break; }
                System.Threading.Thread.Sleep(500);
            }
            foreach (var p in Process.GetProcessesByName("MSFamilyControl")) { try { if (p.Id != Process.GetCurrentProcess().Id) { p.Kill(); p.WaitForExit(3000); } } catch { } }
            log("Programm ersetzen …");
            Directory.CreateDirectory(App.InstallDir);
            string self = Process.GetCurrentProcess().MainModule.FileName;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(App.InstalledExe), StringComparison.OrdinalIgnoreCase))
                for (int i = 0; i < 10; i++) { try { File.Copy(self, App.InstalledExe, true); break; } catch { System.Threading.Thread.Sleep(800); if (i == 9) throw; } }
            // signierte Firefox-Erweiterung des Nutzers beibehalten
            string xpi = Path.Combine(App.InstallDir, "extensions", "firefox-signed.xpi"), xpiTmp = Path.Combine(Path.GetTempPath(), "msfc-firefox-signed.xpi");
            if (File.Exists(xpi)) File.Copy(xpi, xpiTmp, true);
            ExtractExtension("ext-chromium.zip", Path.Combine(App.InstallDir, "extensions", "chrome-edge"));
            ExtractExtension("ext-firefox.zip", Path.Combine(App.InstallDir, "extensions", "firefox"));
            if (File.Exists(xpiTmp)) { File.Copy(xpiTmp, xpi, true); File.Delete(xpiTmp); }
            BrowserPolicy.RemoveMachineWide();
            using (var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\MSFamilyControl"))
            {
                if (k.GetValue("BrowserPolicies") == null) k.SetValue("BrowserPolicies", 1, RegistryValueKind.DWord);
                k.SetValue("Version", App.Version);
            }
            using (var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MSFamilyControl")) k.SetValue("DisplayVersion", App.Version);
            Run("sc.exe", "config " + App.ServiceName + " binPath= \"\\\"" + App.InstalledExe + "\\\" --service\" start= auto");
            log("Dienst starten …");
            Run("sc.exe", "start " + App.ServiceName);
            log("Fertig ✓");
        }

        /// <summary>Stilles Update (vom Dienst gestartet): ersetzt das Programm, bei Fehler zurück zur alten Version.</summary>
        public static int SilentUpdate()
        {
            string logFile = Path.Combine(App.DataDir, "update.log");
            Action<string> log = s => { try { File.AppendAllText(logFile, DateTime.Now.ToString("s") + " " + s + "\r\n"); } catch { } };
            if (!Security.IsAdmin()) { log("Abbruch: keine Administratorrechte"); return 1; }
            System.Threading.Thread.Sleep(3000); // Dienst Zeit geben, seinen Stand zu speichern
            string backup = App.InstalledExe + ".bak";
            try { if (File.Exists(App.InstalledExe)) File.Copy(App.InstalledExe, backup, true); } catch (Exception ex) { log("Sicherung: " + ex.Message); }
            log("Update auf " + App.Version + " beginnt");
            try
            {
                Update(log);
            }
            catch (Exception ex)
            {
                log("Fehler: " + ex.Message + " – alte Version wird wiederhergestellt");
                Restore(backup, log);
                return 2;
            }
            for (int i = 0; i < 40; i++)
            {
                try { using (var sc = new ServiceController(App.ServiceName)) { if (sc.Status == ServiceControllerStatus.Running) { log("Update fertig ✓ – Dienst läuft"); return 0; } } } catch { }
                System.Threading.Thread.Sleep(500);
            }
            log("Dienst startet nicht – alte Version wird wiederhergestellt");
            Restore(backup, log);
            return 3;
        }

        static void Restore(string backup, Action<string> log)
        {
            try
            {
                Run("sc.exe", "stop " + App.ServiceName);
                System.Threading.Thread.Sleep(2000);
                foreach (var p in Process.GetProcessesByName("MSFamilyControl")) { try { if (p.Id != Process.GetCurrentProcess().Id) p.Kill(); } catch { } }
                if (File.Exists(backup)) for (int i = 0; i < 10; i++) { try { File.Copy(backup, App.InstalledExe, true); break; } catch { System.Threading.Thread.Sleep(800); } }
            }
            catch (Exception ex) { log("Wiederherstellen: " + ex.Message); }
            Run("sc.exe", "start " + App.ServiceName);
        }

        static void ExtractExtension(string res, string dir)
        {
            var data = Theme.Bytes(res);
            if (data == null) return;
            if (Directory.Exists(dir)) { try { Directory.Delete(dir, true); } catch { } }
            Directory.CreateDirectory(dir);
            using (var z = new ZipArchive(new MemoryStream(data)))
                foreach (var e in z.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    var target = Path.GetFullPath(Path.Combine(dir, e.FullName));
                    if (!target.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var s = e.Open()) using (var f = File.Create(target)) s.CopyTo(f);
                }
        }

        static void Shortcut(string lnk, string target, string args, string desc)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                var st = sc.GetType();
                st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { args });
                st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { desc });
                st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }
        }

        /// <summary>Programmordner löschen, sobald diese EXE beendet ist</summary>
        public static void ScheduleDelete()
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 4 /nobreak >nul & rmdir /s /q \"" + App.InstallDir + "\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = System.IO.Path.GetTempPath() });
            }
            catch { }
        }

        public static void Uninstall(Config cfg, Action<string> log)
        {
            if (cfg != null && cfg.Server != "")
            {
                try
                {
                    Http.Call(cfg.Server, "sync", new Dictionary<string, object> { { "batches", new List<object>() }, { "events", new List<object> { new Dictionary<string, object> { { "type", "uninstall" }, { "title", "MS Family Control wurde auf " + Environment.MachineName + " deinstalliert" }, { "detail", "mit Masterpasswort" } } } }, { "status", new Dictionary<string, object> { { "shutdown", true } } }, { "master_rev", cfg.MasterRev } }, cfg.Token, 8000);
                }
                catch { }
            }
            log("Dienst stoppen …");
            Run("sc.exe", "stop " + App.ServiceName);
            System.Threading.Thread.Sleep(2500);
            Run("sc.exe", "delete " + App.ServiceName);
            foreach (var p in Process.GetProcessesByName("MSFamilyControl")) { try { if (p.Id != Process.GetCurrentProcess().Id) p.Kill(); } catch { } }
            log("Sperren entfernen …");
            Hosts.Remove();
            BrowserPolicy.RemoveMachineWide();
            BrowserPolicy.RemoveAllLoaded();
            try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\MSFamilyControl", false); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MSFamilyControl", false); } catch { }
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "MS Family Control"), true); } catch { }
            try { Directory.Delete(App.DataDir, true); } catch { }
            log("Deinstalliert ✓");
        }
    }
}
