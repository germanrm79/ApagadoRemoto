using Apagado.Cliente.Services;
using Apagado.Core.Enums;

namespace Apagado.Cliente;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var wallpaperService = new WallpaperService();
        string command = args.Length > 0 ? args[0] : string.Empty;

        if (command.Equals("--show-lock", StringComparison.OrdinalIgnoreCase))
        {
            string message = args.Length > 1 ? args[1] : LockScreenService.DefaultMessage;
            LockScreenService.Show(message);
            return;
        }

        if (command.Equals("--show-notice", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2 || !NoticeService.TryDecodePayload(args[1], out string noticeTitle, out string noticeMessage))
            {
                Console.Error.WriteLine("Aviso inválido: falta el contenido o la codificación no es válida.");
                Environment.ExitCode = 1;
                return;
            }

            NoticeService.Show(noticeTitle, noticeMessage);
            return;
        }

        if (command.Equals("--hide-lock", StringComparison.OrdinalIgnoreCase))
        {
            if (!LockScreenService.RequestHide())
            {
                Environment.ExitCode = 1;
            }
            return;
        }

        if (command.Equals("--apply-pending-wallpaper", StringComparison.OrdinalIgnoreCase))
        {
            bool wallpaperFailed = false;
            try
            {
                wallpaperService.ApplyPendingWallpaper();
            }
            catch (Exception ex)
            {
                wallpaperFailed = true;
                Console.Error.WriteLine($"No se pudo aplicar el fondo pendiente: {ex.Message}");
            }

            string? pendingLock = LockScreenService.LoadPending();
            if (pendingLock != null)
            {
                LockScreenService.Show(pendingLock);
                return;
            }

            if (wallpaperFailed)
            {
                Environment.ExitCode = 1;
            }
            return;
        }

        if (args.Length >= 3 && command.Equals("--apply-wallpaper", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                WallpaperStyle style = Enum.TryParse(args[2], true, out WallpaperStyle parsed) ? parsed : WallpaperStyle.Fill;
                wallpaperService.ApplyWallpaper(args[1], style);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"No se pudo aplicar el fondo: {ex.Message}");
                Environment.ExitCode = 1;
            }
            return;
        }

        Console.WriteLine("ApagadoRemoto - Cliente (Escucha)");
        Console.WriteLine("Presiona Ctrl+C para salir");

        string token = Environment.GetEnvironmentVariable("APAGADO_TOKEN") ?? "clave-compartida-aqui";
        int port = int.TryParse(Environment.GetEnvironmentVariable("APAGADO_PORT"), out int p) ? p : 5007;

        var service = new TcpListenerService(token, port);
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
        service.StartAsync(cts.Token).GetAwaiter().GetResult();
    }
}
