using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MSFamilyControl
{
    /// <summary>Windows-API-Aufrufe (Sitzungen, Prozessstart im Benutzerkontext, Eingabe-Leerlauf, Prozessschutz)</summary>
    internal static class Native
    {
        public const int WTS_CURRENT_SERVER = 0;

        [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);
        [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytes);
        [DllImport("wtsapi32.dll")] public static extern void WTSFreeMemory(IntPtr p);
        [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSDisconnectSession(IntPtr server, uint sessionId, bool wait);
        [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSLogoffSession(IntPtr server, uint sessionId, bool wait);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attrs, int impLevel, int tokenType, out IntPtr newToken);
        [DllImport("userenv.dll", SetLastError = true)] public static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);
        [DllImport("userenv.dll", SetLastError = true)] public static extern bool DestroyEnvironmentBlock(IntPtr env);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool ProcessIdToSessionId(uint pid, out uint sessionId);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessAsUser(IntPtr token, string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint rev, out IntPtr sd, out uint size);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool SetKernelObjectSecurity(IntPtr handle, uint info, IntPtr sd);
        [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr p);

        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern bool ExitWindowsEx(uint flags, uint reason);

        public const uint NORMAL_PRIORITY_CLASS = 0x20, CREATE_UNICODE_ENVIRONMENT = 0x400, CREATE_NO_WINDOW = 0x08000000;

        /// <summary>Leerlaufzeit (Sekunden) in der eigenen Sitzung</summary>
        public static int IdleSeconds()
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref lii)) return 0;
            uint ticks = unchecked((uint)Environment.TickCount);
            return (int)(unchecked(ticks - lii.dwTime) / 1000);
        }

        /// <summary>Benutzername einer Sitzung (WTSUserName = 5)</summary>
        public static string SessionUser(uint sessionId)
        {
            IntPtr buf; uint bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, 5, out buf, out bytes)) return "";
            try { return Marshal.PtrToStringUni(buf) ?? ""; } finally { WTSFreeMemory(buf); }
        }

        /// <summary>Ist die Sitzung von Windows gesperrt? (WTSSessionInfoEx = 25, ab Windows 8)</summary>
        public static bool SessionLocked(uint sessionId)
        {
            IntPtr buf; uint bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, 25, out buf, out bytes)) return false;
            try
            {
                // WTSINFOEX: Level(4) + WTSINFOEX_LEVEL1: SessionId(4), SessionState(4), SessionFlags(4)
                int flags = Marshal.ReadInt32(buf, 12);
                return flags == 0; // WTS_SESSIONSTATE_LOCK = 0 (Windows 10/11)
            }
            catch { return false; }
            finally { WTSFreeMemory(buf); }
        }

        /// <summary>Startet ein Programm in der Benutzersitzung (aus dem Dienst heraus) und schützt den Prozess gegen Beenden durch den Benutzer.</summary>
        public static int StartInSession(uint sessionId, string exe, string args, bool protect)
        {
            IntPtr userToken = IntPtr.Zero, dup = IntPtr.Zero, env = IntPtr.Zero;
            try
            {
                if (!WTSQueryUserToken(sessionId, out userToken)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!DuplicateTokenEx(userToken, 0x02000000 /*MAXIMUM_ALLOWED*/, IntPtr.Zero, 2 /*Impersonation*/, 1 /*Primary*/, out dup)) throw new Win32Exception(Marshal.GetLastWin32Error());
                CreateEnvironmentBlock(out env, dup, false);
                var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), lpDesktop = "winsta0\\default" };
                PROCESS_INFORMATION pi;
                string cmd = "\"" + exe + "\" " + args;
                if (!CreateProcessAsUser(dup, exe, cmd, IntPtr.Zero, IntPtr.Zero, false, NORMAL_PRIORITY_CLASS | CREATE_UNICODE_ENVIRONMENT, env, System.IO.Path.GetDirectoryName(exe), ref si, out pi))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (protect) Protect(pi.hProcess);
                CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
                return pi.dwProcessId;
            }
            finally
            {
                if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
                if (dup != IntPtr.Zero) CloseHandle(dup);
                if (userToken != IntPtr.Zero) CloseHandle(userToken);
            }
        }

        /// <summary>
        /// Besitzer = SYSTEM, Benutzer dürfen den Prozess nur abfragen – nicht beenden (Task-Manager → „Zugriff verweigert“).
        /// Administratoren können das bewusst weiterhin aufheben.
        /// </summary>
        public static void Protect(IntPtr hProcess)
        {
            IntPtr sd; uint size;
            // SY: Vollzugriff · BA: Vollzugriff · IU (interaktive Benutzer): Abfragen + Synchronisieren
            const string sddl = "O:SYG:SYD:(A;;0x1fffff;;;SY)(A;;0x1fffff;;;BA)(A;;0x101410;;;IU)";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out sd, out size)) return;
            try { SetKernelObjectSecurity(hProcess, 0x1 | 0x4 /*OWNER|DACL*/, sd); }
            finally { LocalFree(sd); }
        }

        public static uint SessionOf(Process p)
        {
            uint s;
            return ProcessIdToSessionId((uint)p.Id, out s) ? s : uint.MaxValue;
        }

        public static string WindowTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
