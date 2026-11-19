using System.Net;
using System.Net.Sockets;
using Apagado.Core.Dtos;
using Apagado.Core.Enums;
using Apagado.Core.Protocol;

namespace Apagado.Cliente.Services;

public class TcpListenerService
{
    private const int MaxDelaySeconds = 604800;
    private const string DefaultLockMessage = LockScreenService.DefaultMessage;

    private readonly string _token;
    private readonly int _port;
    private readonly ShutdownService _shutdownService = new();
    private readonly WallpaperService _wallpaperService = new();

    public TcpListenerService(string token, int port = 5007)
    {
        _token = token;
        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        TcpListener listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        Console.WriteLine($"[Apagado.Cliente] Escuchando en 0.0.0.0:{_port}...");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                var (header, imageData) = await FrameReader.ReadFrameAsync(stream, cancellationToken);

                if (header.Type == CommandType.Identify)
                {
                    var identify = new CommandResponse
                    {
                        RequestId = header.RequestId,
                        Success = true,
                        Message = "Cliente activo",
                        MachineName = Environment.MachineName,
                        Port = _port,
                        Version = UpdateService.CurrentVersion
                    };
                    await FrameWriter.WriteResponseAsync(stream, identify, cancellationToken);
                    Console.WriteLine($"[Apagado.Cliente] Identificado por {client.Client.RemoteEndPoint}");
                    return;
                }

                if (header.Token != _token)
                {
                    await RespondAsync(stream, header, false, "Token inválido", cancellationToken);
                    return;
                }

                switch (header.Type)
                {
                    case CommandType.Shutdown:
                        await HandleShutdownAsync(stream, header, reboot: false, cancellationToken);
                        return;
                    case CommandType.Reboot:
                        await HandleShutdownAsync(stream, header, reboot: true, cancellationToken);
                        return;
                    case CommandType.CancelShutdown:
                        await HandleCancelShutdownAsync(stream, header, cancellationToken);
                        return;
                    case CommandType.Lock:
                        await HandleLockAsync(stream, header, cancellationToken);
                        return;
                    case CommandType.Unlock:
                        await HandleUnlockAsync(stream, header, cancellationToken);
                        return;
                    case CommandType.Notice:
                        await HandleNoticeAsync(stream, header, cancellationToken);
                        return;
                    case CommandType.SetWallpaper:
                        await HandleWallpaperAsync(stream, header, imageData, cancellationToken);
                        return;
                    case CommandType.Update:
                        await HandleUpdateAsync(stream, header, imageData, cancellationToken);
                        return;
                    default:
                        await RespondAsync(stream, header, false, "Tipo de comando no soportado", cancellationToken);
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error manejando cliente: {ex.Message}");
        }
    }

    private async Task HandleShutdownAsync(NetworkStream stream, CommandHeader header, bool reboot, CancellationToken cancellationToken)
    {
        int delay = Math.Clamp(header.DelaySeconds, 0, MaxDelaySeconds);

        if (reboot)
            _shutdownService.Reboot(delay);
        else
            _shutdownService.Shutdown(delay);

        string action = reboot ? "Reinicio" : "Apagado";
        string message = delay == 0
            ? $"{action} inmediato en curso."
            : $"{action} programado en {delay} segundos.";

        Console.WriteLine($"[Apagado.Cliente] {message}");
        await RespondAsync(stream, header, true, message, cancellationToken);
    }

    private async Task HandleCancelShutdownAsync(NetworkStream stream, CommandHeader header, CancellationToken cancellationToken)
    {
        bool cancelled = _shutdownService.CancelShutdown();
        string message = cancelled
            ? "Apagado o reinicio cancelado."
            : "No hay un apagado o reinicio programado.";

        Console.WriteLine($"[Apagado.Cliente] {message}");
        await RespondAsync(stream, header, cancelled, message, cancellationToken);
    }

    private async Task HandleLockAsync(NetworkStream stream, CommandHeader header, CancellationToken cancellationToken)
    {
        string message = string.IsNullOrWhiteSpace(header.Message) ? DefaultLockMessage : header.Message!;
        LockScreenService.SavePending(message);

        bool success = true;
        string result;

        if (SessionLauncher.IsRunningAsSystem())
        {
            int launched = SessionLauncher.RunInAllSessions(
                $"--show-lock {SessionLauncher.Quote(message)}",
                waitForExit: false,
                out string? errors,
                out int attempted);

            if (attempted == 0)
            {
                result = "Equipo bloqueado. El aviso se mostrará cuando un usuario inicie sesión.";
            }
            else if (launched == attempted)
            {
                result = $"Equipo bloqueado en {launched} sesión(es) activa(s).";
            }
            else if (launched > 0)
            {
                result = $"Bloqueado en {launched} de {attempted} sesión(es). {errors}";
            }
            else
            {
                success = false;
                result = $"No se pudo mostrar el bloqueo en las sesiones activas ({errors}). Se mostrará al iniciar sesión.";
            }
        }
        else if (SessionLauncher.StartLocal($"--show-lock {SessionLauncher.Quote(message)}"))
        {
            result = "Equipo bloqueado.";
        }
        else
        {
            success = false;
            result = "No se pudo iniciar el bloqueo local.";
        }

        Console.WriteLine($"[Apagado.Cliente] {result}");
        await RespondAsync(stream, header, success, result, cancellationToken);
    }

    private async Task HandleUnlockAsync(NetworkStream stream, CommandHeader header, CancellationToken cancellationToken)
    {
        LockScreenService.ClearPending();

        bool success;
        string result;

        if (SessionLauncher.IsRunningAsSystem())
        {
            int launched = SessionLauncher.RunInAllSessions(
                "--hide-lock",
                waitForExit: true,
                out string? errors,
                out int attempted);

            if (attempted == 0)
            {
                success = true;
                result = "Equipo desbloqueado. No hay sesiones activas.";
            }
            else if (launched > 0)
            {
                success = true;
                result = $"Equipo desbloqueado en {launched} de {attempted} sesión(es).";
            }
            else
            {
                success = false;
                result = $"El equipo no estaba bloqueado en ninguna sesión activa. {errors}";
            }
        }
        else
        {
            success = LockScreenService.RequestHide();
            result = success ? "Equipo desbloqueado." : "El equipo no estaba bloqueado.";
        }

        Console.WriteLine($"[Apagado.Cliente] {result}");
        await RespondAsync(stream, header, success, result, cancellationToken);
    }

    private async Task HandleNoticeAsync(NetworkStream stream, CommandHeader header, CancellationToken cancellationToken)
    {
        string message = string.IsNullOrWhiteSpace(header.Message)
            ? "El administrador ha enviado un aviso."
            : header.Message!;
        string title = string.IsNullOrWhiteSpace(header.Title) ? NoticeService.DefaultTitle : header.Title!;

        string payload = NoticeService.EncodePayload(title, message);
        string arguments = $"--show-notice {payload}";

        bool success;
        string result;

        if (SessionLauncher.IsRunningAsSystem())
        {
            int launched = SessionLauncher.RunInAllSessions(
                arguments,
                waitForExit: false,
                out string? errors,
                out int attempted);

            if (attempted == 0)
            {
                success = false;
                result = "No hay sesiones activas. El aviso no se mostró.";
            }
            else if (launched == attempted)
            {
                success = true;
                result = $"Aviso mostrado en {launched} sesión(es) activa(s).";
            }
            else if (launched > 0)
            {
                success = true;
                result = $"Aviso mostrado en {launched} de {attempted} sesión(es). {errors}";
            }
            else
            {
                success = false;
                result = $"No se pudo mostrar el aviso en las sesiones activas ({errors}).";
            }
        }
        else if (SessionLauncher.StartLocal(arguments))
        {
            success = true;
            result = "Aviso mostrado.";
        }
        else
        {
            success = false;
            result = "No se pudo iniciar el aviso local.";
        }

        Console.WriteLine($"[Apagado.Cliente] {result}");
        await RespondAsync(stream, header, success, result, cancellationToken);
    }

    private async Task HandleWallpaperAsync(NetworkStream stream, CommandHeader header, byte[]? imageData, CancellationToken cancellationToken)
    {
        if (imageData == null || imageData.Length == 0)
        {
            await RespondAsync(stream, header, false, "No se recibió imagen", cancellationToken);
            return;
        }

        try
        {
            string path = _wallpaperService.SaveWallpaper(imageData, header.FileName ?? "wallpaper.jpg");
            _wallpaperService.SavePendingWallpaper(path, header.Style);

            string message;
            bool success = true;

            if (SessionLauncher.IsRunningAsSystem())
            {
                int applied = SessionLauncher.ApplyWallpaperToAllSessions(path, header.Style, out string? sessionErrors, out int attempted);
                if (!string.IsNullOrEmpty(sessionErrors))
                    Console.WriteLine($"[Apagado.Cliente] Avisos al aplicar el fondo: {sessionErrors}");

                if (applied > 0)
                {
                    message = $"Fondo aplicado a {applied} de {attempted} sesión(es). Se aplicará a los demás usuarios al iniciar sesión.";
                }
                else if (attempted == 0)
                {
                    message = "Fondo guardado. Se aplicará a cada usuario al iniciar sesión.";
                }
                else
                {
                    success = false;
                    message = $"No se pudo aplicar el fondo en las sesiones activas ({sessionErrors}). Se aplicará al iniciar sesión.";
                }
            }
            else
            {
                _wallpaperService.ApplyWallpaper(path, header.Style);
                message = "Fondo de escritorio aplicado correctamente.";
            }

            _wallpaperService.CleanupOldWallpapers();
            await RespondAsync(stream, header, success, message, cancellationToken);
        }
        catch (Exception ex)
        {
            await RespondAsync(stream, header, false, ex.Message, cancellationToken);
        }
    }

    private async Task HandleUpdateAsync(NetworkStream stream, CommandHeader header, byte[]? imageData, CancellationToken cancellationToken)
    {
        if (imageData == null || imageData.Length == 0)
        {
            await RespondAsync(stream, header, false, "No se recibió el paquete de actualización.", cancellationToken);
            return;
        }

        try
        {
            var updateService = new UpdateService();
            bool started = updateService.TryStartUpdate(
                imageData,
                header.FileName,
                header.PackageVersion,
                header.PackageSha256,
                header.AllowDowngrade,
                out string message);

            Console.WriteLine($"[Apagado.Cliente] {message}");
            await RespondAsync(stream, header, started, message, cancellationToken);
        }
        catch (Exception ex)
        {
            await RespondAsync(stream, header, false, $"No se pudo iniciar la actualización: {ex.Message}", cancellationToken);
        }
    }

    private static async Task RespondAsync(NetworkStream stream, CommandHeader header, bool success, string message, CancellationToken cancellationToken)
    {
        var response = new CommandResponse
        {
            RequestId = header.RequestId,
            Success = success,
            Message = message,
            Version = UpdateService.CurrentVersion
        };
        await FrameWriter.WriteResponseAsync(stream, response, cancellationToken);
    }
}
