using System;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace MSFamilyControl
{
    /// <summary>
    /// Browser-Richtlinien NUR für die Windows-Konten der Kinder (HKEY_USERS\&lt;SID&gt;\Software\Policies).
    /// Eltern-Konten und der restliche PC bleiben unberührt. Kinder (Standardkonto) können diese Schlüssel nicht ändern.
    /// </summary>
    internal static class BrowserPolicy
    {
        const string Marker = "MSFamilyControl";
        static readonly string[] ChromiumKeys = { @"Software\Policies\Google\Chrome", @"Software\Policies\Microsoft\Edge" };
        const string FirefoxKey = @"Software\Policies\Mozilla\Firefox";

        public static string SidOfSession(uint session)
        {
            IntPtr tok;
            if (!Native.WTSQueryUserToken(session, out tok)) return null;
            try { using (var id = new WindowsIdentity(tok)) return id.User != null ? id.User.Value : null; }
            finally { Native.CloseHandle(tok); }
        }

        static RegistryKey Users { get { return RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default); } }

        /// <summary>Gesperrte Schlüssel anlegen: SYSTEM + Administratoren Vollzugriff, Benutzer nur lesen</summary>
        static RegistryKey Protected(string sid, string path)
        {
            var root = Users.OpenSubKey(sid, true);
            if (root == null) return null; // Profil noch nicht geladen
            RegistryKey k = root;
            var parts = path.Split('\\');
            for (int i = 0; i < parts.Length; i++)
            {
                bool existed;
                using (var t = k.OpenSubKey(parts[i])) existed = t != null;
                var next = k.CreateSubKey(parts[i]);
                if (k != root) k.Dispose();
                k = next;
                bool leaf = i == parts.Length - 1;
                if (k.Name.IndexOf(@"\Software\Policies", StringComparison.OrdinalIgnoreCase) >= 0 && (!existed || leaf))
                {
                    try
                    {
                        var sec = new RegistrySecurity();
                        sec.SetAccessRuleProtection(true, false);
                        var inh = InheritanceFlags.ContainerInherit;
                        sec.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), RegistryRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                        sec.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), RegistryRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
                        sec.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(sid), RegistryRights.ReadKey, inh, PropagationFlags.None, AccessControlType.Allow));
                        sec.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(WellKnownSidType.RestrictedCodeSid, null), RegistryRights.ReadKey, inh, PropagationFlags.None, AccessControlType.Allow));
                        k.SetAccessControl(sec);
                    }
                    catch { }
                }
            }
            root.Dispose();
            return k;
        }

        public static bool Apply(string sid, bool safeSearch, int youtube)
        {
            if (string.IsNullOrEmpty(sid)) return false;
            using (var test = Users.OpenSubKey(sid)) if (test == null) return false;
            foreach (var path in ChromiumKeys)
            {
                bool edge = path.Contains("Edge");
                using (var k = Protected(sid, path))
                {
                    if (k == null) return false;
                    k.SetValue(Marker, 1, RegistryValueKind.DWord);
                    k.SetValue(edge ? "InPrivateModeAvailability" : "IncognitoModeAvailability", 1, RegistryValueKind.DWord);
                    k.SetValue("BrowserGuestModeEnabled", 0, RegistryValueKind.DWord);
                    k.SetValue(edge ? "BrowserAddProfileEnabled" : "BrowserAddPersonEnabled", 0, RegistryValueKind.DWord);
                    k.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
                    if (safeSearch) { k.SetValue("ForceGoogleSafeSearch", 1, RegistryValueKind.DWord); if (edge) k.SetValue("ForceBingSafeSearch", 2, RegistryValueKind.DWord); }
                    else { k.DeleteValue("ForceGoogleSafeSearch", false); k.DeleteValue("ForceBingSafeSearch", false); }
                    if (youtube > 0) k.SetValue("ForceYouTubeRestrict", youtube, RegistryValueKind.DWord); else k.DeleteValue("ForceYouTubeRestrict", false);
                    // Alte Versionen (1.0.0/1.0.1) haben fremde Erweiterungen gesperrt – das blockierte auch die eigene
                    try { k.DeleteSubKeyTree("ExtensionInstallBlocklist", false); k.DeleteSubKeyTree("ExtensionInstallAllowlist", false); } catch { }
                }
            }
            using (var k = Protected(sid, FirefoxKey))
            {
                if (k == null) return false;
                k.SetValue(Marker, 1, RegistryValueKind.DWord);
                k.SetValue("DisablePrivateBrowsing", 1, RegistryValueKind.DWord);
                using (var d = k.CreateSubKey("DNSOverHTTPS")) { d.SetValue("Enabled", 0, RegistryValueKind.DWord); d.SetValue("Locked", 1, RegistryValueKind.DWord); }
                var xpi = System.IO.Path.Combine(App.InstallDir, "extensions", "firefox-signed.xpi");
                if (System.IO.File.Exists(xpi))
                    k.SetValue("ExtensionSettings", "{\"" + App.FirefoxId + "\":{\"installation_mode\":\"force_installed\",\"install_url\":\"" + new Uri(xpi).AbsoluteUri + "\"}}", RegistryValueKind.String);
            }
            return true;
        }

        /// <summary>Entfernt unsere Richtlinien aus einem Benutzerprofil (z. B. bei Deinstallation)</summary>
        public static void Remove(string sid)
        {
            try
            {
                foreach (var path in ChromiumKeys)
                    using (var k = Users.OpenSubKey(sid + "\\" + path, true))
                    {
                        if (k == null || k.GetValue(Marker) == null) continue;
                        foreach (var v in new[] { Marker, "InPrivateModeAvailability", "IncognitoModeAvailability", "BrowserGuestModeEnabled", "BrowserAddProfileEnabled", "BrowserAddPersonEnabled", "DnsOverHttpsMode", "ForceGoogleSafeSearch", "ForceBingSafeSearch", "ForceYouTubeRestrict" }) k.DeleteValue(v, false);
                        k.DeleteSubKeyTree("ExtensionInstallBlocklist", false); k.DeleteSubKeyTree("ExtensionInstallAllowlist", false);
                    }
                using (var k = Users.OpenSubKey(sid + "\\" + FirefoxKey, true))
                    if (k != null && k.GetValue(Marker) != null) { foreach (var v in new[] { Marker, "DisablePrivateBrowsing", "ExtensionSettings" }) k.DeleteValue(v, false); k.DeleteSubKeyTree("DNSOverHTTPS", false); }
            }
            catch { }
        }

        public static void RemoveAllLoaded()
        {
            try { foreach (var sid in Users.GetSubKeyNames()) if (sid.StartsWith("S-1-5-21-") && !sid.EndsWith("_Classes")) Remove(sid); } catch { }
        }

        /// <summary>Rechnerweite Richtlinien aus Version 1.0.0/1.0.1 entfernen (haben auch Eltern-Browser eingeschränkt)</summary>
        public static void RemoveMachineWide()
        {
            foreach (var path in new[] { @"SOFTWARE\Policies\Google\Chrome", @"SOFTWARE\Policies\Microsoft\Edge" })
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(path, true))
                        if (k != null) foreach (var v in new[] { "InPrivateModeAvailability", "IncognitoModeAvailability", "BrowserGuestModeEnabled", "BrowserAddProfileEnabled", "BrowserAddPersonEnabled", "DnsOverHttpsMode" }) k.DeleteValue(v, false);
                    Registry.LocalMachine.DeleteSubKeyTree(path + @"\ExtensionInstallBlocklist", false);
                    Registry.LocalMachine.DeleteSubKeyTree(path + @"\ExtensionInstallAllowlist", false);
                    using (var k = Registry.LocalMachine.OpenSubKey(path)) if (k != null && k.ValueCount == 0 && k.SubKeyCount == 0) { k.Close(); Registry.LocalMachine.DeleteSubKey(path, false); }
                }
                catch { }
            }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Mozilla\Firefox", true))
                    if (k != null) { k.DeleteValue("DisablePrivateBrowsing", false); k.DeleteValue("ExtensionSettings", false); k.DeleteSubKeyTree("DNSOverHTTPS", false); }
            }
            catch { }
        }
    }
}
