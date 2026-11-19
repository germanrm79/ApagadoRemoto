using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Apagado.Cliente.Services;

internal sealed class UpdateService
{
    private const string UpdateMutexName = @"Local\ApagadoRemoto.ClientUpdate";
    private const long MaxPackageBytes = 256L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly bool _isSystem;
    private readonly string _updatesDirectory;
    private readonly string _packagePath;
    private readonly string _manifestPath;
    private readonly string _resultPath;
    private readonly string _logPath;
    private readonly string _installScriptPath;

    public static string CurrentVersion { get; } = GetCurrentVersion();

    public UpdateService()
    {
        _isSystem = SessionLauncher.IsRunningAsSystem();
        string root = _isSystem
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ApagadoRemoto")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApagadoRemoto");
        _updatesDirectory = Path.Combine(root, "Updates");
        _packagePath = Path.Combine(_updatesDirectory, "ApagadoRemotoClienteSetup.exe");
        _manifestPath = Path.Combine(_updatesDirectory, "update.json");
        _resultPath = Path.Combine(_updatesDirectory, "update-result.json");
        _logPath = Path.Combine(_updatesDirectory, "install.log");

        string? exeDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        _installScriptPath = Path.Combine(exeDirectory ?? string.Empty, "installupdate.ps1");
    }

    public bool TryStartUpdate(
        byte[] payload,
        string? fileName,
        string? packageVersion,
        string? expectedSha256,
        bool allowDowngrade,
        out string message)
    {
        message = string.Empty;

        if (payload == null || payload.Length == 0)
        {
            message = "No se recibió el paquete de actualización.";
            return false;
        }

        if (payload.Length > MaxPackageBytes)
        {
            message = $"El paquete supera el tamaño máximo permitido de {MaxPackageBytes / (1024 * 1024)} MB.";
            return false;
        }

        string expected = (expectedSha256 ?? string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .Trim();
        if (expected.Length == 0)
        {
            message = "Falta el hash SHA-256 del paquete.";
            return false;
        }

        string actualHash = Convert.ToHexString(SHA256.HashData(payload));
        if (!string.Equals(actualHash, expected, StringComparison.OrdinalIgnoreCase))
        {
            message = "El hash SHA-256 del paquete no coincide.";
            return false;
        }

        string safeFileName = Path.GetFileName(fileName ?? string.Empty);
        if (!safeFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            message = "El paquete de actualización debe ser un archivo .exe.";
            return false;
        }

        Version? incoming = ParseVersion(packageVersion);
        Version? current = ParseVersion(CurrentVersion);
        if (!allowDowngrade && incoming != null && current != null && incoming <= current)
        {
            message = $"El paquete {incoming} no es mayor que la versión instalada {current}.";
            return false;
        }

        bool ownsMutex = false;
        using var mutex = new Mutex(false, UpdateMutexName);
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                message = "Ya hay una actualización en curso.";
                return false;
            }

            Directory.CreateDirectory(_updatesDirectory);
            string tempPath = Path.Combine(_updatesDirectory, $"setup-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(tempPath, payload);
            File.Move(tempPath, _packagePath, true);

            WriteJsonAtomic(_manifestPath, new
            {
                FileName = safeFileName,
                Version = packageVersion,
                Sha256 = actualHash,
                StagedUtc = DateTime.UtcNow,
                State = "Preparado"
            });
            WriteJsonAtomic(_resultPath, new
            {
                Status = "Preparado",
                Message = "Paquete verificado.",
                ExitCode = 0,
                FinishedUtc = DateTime.UtcNow
            });

            string versionText = packageVersion ?? safeFileName;
            if (_isSystem)
            {
                LaunchInstaller();
                message = $"Paquete {versionText} verificado; instalación silenciosa iniciada.";
            }
            else
            {
                message = $"Paquete {versionText} verificado; modo local, instalador no iniciado.";
            }

            return true;
        }
        catch (Exception ex)
        {
            message = $"No se pudo preparar la actualización: {ex.Message}";
            return false;
        }
        finally
        {
            if (ownsMutex)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private void LaunchInstaller()
    {
        if (!File.Exists(_installScriptPath))
        {
            throw new FileNotFoundException("No se encontró el script installupdate.ps1.", _installScriptPath);
        }

        string arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{_installScriptPath}\" " +
                           $"-Package \"{_packagePath}\" -Log \"{_logPath}\" -Result \"{_resultPath}\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_installScriptPath) ?? Environment.SystemDirectory
        };
        Process.Start(startInfo);
    }

    private static void WriteJsonAtomic(string path, object value)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(tempPath, path, true);
    }

    private static string GetCurrentVersion()
    {
        string? informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            string cleaned = informational.Split('+')[0];
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }
        }

        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string text = value.Trim();
        int plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        int dash = text.IndexOf('-');
        if (dash >= 0)
        {
            text = text[..dash];
        }

        return Version.TryParse(text, out Version? version) ? version : null;
    }
}
