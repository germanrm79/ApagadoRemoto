using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Apagado.Core.Enums;

namespace Apagado.Cliente.Services;

public static class SessionLauncher
{
    private const int WtsActive = 0;
    private const int WtsDisconnected = 4;
    private const uint TokenAllAccess = 0x000F01FF;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint WaitTimeout = 258;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;

    public static bool IsRunningAsSystem()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.IsSystem;
    }

    public static int ApplyWallpaperToAllSessions(string path, WallpaperStyle style, out string? errors, out int attempted)
    {
        string arguments = $"--apply-wallpaper {Quote(path)} {style}";
        return RunInAllSessions(arguments, waitForExit: true, out errors, out attempted);
    }

    public static int RunInAllSessions(string arguments, bool waitForExit, out string? errors, out int attempted)
    {
        var targetSessions = EnumerateSessions()
            .Where(s => s.SessionId > 0 && (s.State == WtsActive || s.State == WtsDisconnected))
            .ToList();

        attempted = targetSessions.Count;
        int launched = 0;
        var sessionErrors = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.ForEach(targetSessions, session =>
        {
            string? error = TryLaunchInSession(session.SessionId, arguments, waitForExit);
            if (error == null)
                Interlocked.Increment(ref launched);
            else
                sessionErrors.Add($"sesión {session.SessionId}: {error}");
        });

        errors = sessionErrors.Count > 0 ? string.Join("; ", sessionErrors) : null;
        return launched;
    }

    public static bool StartLocal(string arguments)
    {
        try
        {
            string exePath = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return false;
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            };
            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Quote(string value)
    {
        string clean = (value ?? string.Empty)
            .Replace("\"", "'")
            .Replace("\r", " ")
            .Replace("\n", " ");
        return $"\"{clean}\"";
    }

    private static List<WtsSessionInfo> EnumerateSessions()
    {
        var sessions = new List<WtsSessionInfo>();
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out IntPtr sessionInfo, out int count))
            return sessions;

        try
        {
            int size = Marshal.SizeOf<WtsSessionInfo>();
            for (int i = 0; i < count; i++)
            {
                IntPtr item = IntPtr.Add(sessionInfo, i * size);
                sessions.Add(Marshal.PtrToStructure<WtsSessionInfo>(item));
            }
        }
        finally
        {
            WTSFreeMemory(sessionInfo);
        }

        return sessions;
    }

    private static string? TryLaunchInSession(int sessionId, string arguments, bool waitForExit)
    {
        IntPtr userToken = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;
        IntPtr desktopName = IntPtr.Zero;

        try
        {
            EnablePrivilege("SeTcbPrivilege");
            EnablePrivilege("SeAssignPrimaryTokenPrivilege");
            EnablePrivilege("SeIncreaseQuotaPrivilege");

            if (!WTSQueryUserToken((uint)sessionId, out userToken))
                return new Win32Exception(Marshal.GetLastWin32Error()).Message;

            if (!DuplicateTokenEx(userToken, TokenAllAccess, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primaryToken))
                return new Win32Exception(Marshal.GetLastWin32Error()).Message;

            bool hasEnvironment = CreateEnvironmentBlock(out environment, primaryToken, false);

            string exePath = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return "no se pudo determinar la ruta del ejecutable";

            string commandLine = $"\"{exePath}\" {arguments}";
            desktopName = Marshal.StringToHGlobalUni(@"winsta0\default");
            var startupInfo = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = desktopName
            };
            uint flags = CreateNoWindow | (hasEnvironment ? CreateUnicodeEnvironment : 0);

            bool created = CreateProcessAsUser(
                primaryToken,
                exePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                flags,
                hasEnvironment ? environment : IntPtr.Zero,
                Path.GetDirectoryName(exePath),
                ref startupInfo,
                out ProcessInfo processInfo);

            if (!created)
                return new Win32Exception(Marshal.GetLastWin32Error()).Message;

            try
            {
                if (!waitForExit)
                {
                    return null;
                }

                uint waitResult = WaitForSingleObject(processInfo.hProcess, 3000);
                GetExitCodeProcess(processInfo.hProcess, out uint exitCode);
                if (waitResult == WaitTimeout)
                    return "el proceso auxiliar no terminó a tiempo";
                return exitCode == 0 ? null : $"el proceso auxiliar terminó con código {exitCode}";
            }
            finally
            {
                CloseHandle(processInfo.hThread);
                CloseHandle(processInfo.hProcess);
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            if (desktopName != IntPtr.Zero) Marshal.FreeHGlobal(desktopName);
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out IntPtr token))
            return;

        try
        {
            if (!LookupPrivilegeValue(null, name, out long luid))
                return;

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                LuidLowPart = (uint)(luid & 0xFFFFFFFF),
                LuidHighPart = (int)(luid >> 32),
                Attributes = SePrivilegeEnabled
            };

            AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Attributes;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr token,
        string? applicationName,
        string commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInfo processInfo);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr token,
        bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);
}
