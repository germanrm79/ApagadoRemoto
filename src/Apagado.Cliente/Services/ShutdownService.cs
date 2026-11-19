using System.Diagnostics;
using System.Text;

namespace Apagado.Cliente.Services;

public class ShutdownService
{
    public void Shutdown(int delaySeconds = 60)
    {
        Run("/s", delaySeconds);
    }

    public void Reboot(int delaySeconds = 60)
    {
        Run("/r", delaySeconds);
    }

    public bool CancelShutdown()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/a");

            using Process? process = Process.Start(startInfo);
            if (process == null) return false;

            if (!process.WaitForExit(5000))
            {
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Run(string mode, int delaySeconds)
    {
        if (delaySeconds < 0) delaySeconds = 0;

        var startInfo = new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add("/t");
        startInfo.ArgumentList.Add(delaySeconds.ToString());
        Process.Start(startInfo);
    }
}
