using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Apagado.Core.Enums;

namespace Apagado.Cliente.Services;

public class WallpaperService
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        [return: MarshalAs(UnmanagedType.U4)] uint GetMonitorDevicePathCount();
        [return: MarshalAs(UnmanagedType.Struct)] Rect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor([MarshalAs(UnmanagedType.U4)] uint color);
        [return: MarshalAs(UnmanagedType.U4)] uint GetBackgroundColor();
        void SetPosition([MarshalAs(UnmanagedType.I4)] DesktopWallpaperPosition position);
        [return: MarshalAs(UnmanagedType.I4)] DesktopWallpaperPosition GetPosition();
        void SetSlideshow(IntPtr items);
        IntPtr GetSlideshow();
        void SetSlideshowOptions([MarshalAs(UnmanagedType.I4)] DesktopSlideshowDirection options, uint slideshowTick);
        [return: MarshalAs(UnmanagedType.U4)] uint GetSlideshowOptions(out DesktopSlideshowDirection options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.I4)] DesktopSlideshowDirection direction);
        DesktopSlideshowDirection GetStatus();
        bool Enable();
    }

    [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    private enum DesktopWallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5
    }

    private enum DesktopSlideshowDirection
    {
        Forward = 0,
        Backward = 1
    }

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ApagadoRemoto");

    public static string LocalDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ApagadoRemoto");

    public static string WallpapersDirectory => Path.Combine(DataDirectory, "Wallpapers");

    public static string LocalWallpapersDirectory => Path.Combine(LocalDataDirectory, "Wallpapers");

    public static string PendingWallpaperFile => Path.Combine(DataDirectory, "wallpaper.json");

    public static string LocalPendingWallpaperFile => Path.Combine(LocalDataDirectory, "wallpaper.json");

    private static bool UseSharedDirectory => SessionLauncher.IsRunningAsSystem();

    private static readonly object SharedAclLock = new();
    private static bool _sharedAclReady;

    private static void EnsureSharedReadAccess()
    {
        if (!UseSharedDirectory)
        {
            return;
        }

        lock (SharedAclLock)
        {
            if (_sharedAclReady)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(DataDirectory);
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                var directory = new DirectoryInfo(DataDirectory);
                DirectorySecurity security = directory.GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(
                    users,
                    FileSystemRights.ReadAndExecute,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                directory.SetAccessControl(security);
            }
            catch
            {
            }

            _sharedAclReady = true;
        }
    }

    public string SaveWallpaper(byte[] imageData, string fileName)
    {
        string ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
        ext = ext.ToLowerInvariant();

        if (UseSharedDirectory)
        {
            EnsureSharedReadAccess();
        }

        string wallpapersDir = UseSharedDirectory ? WallpapersDirectory : LocalWallpapersDirectory;
        Directory.CreateDirectory(wallpapersDir);

        string uniqueName = "wallpaper_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N")[..8] + ext;
        string targetPath = Path.Combine(wallpapersDir, uniqueName);
        File.WriteAllBytes(targetPath, imageData);

        try
        {
            using (var image = System.Drawing.Image.FromFile(targetPath)) { }
        }
        catch
        {
            try { File.Delete(targetPath); } catch { }
            throw new Exception("La imagen recibida no es válida o está corrupta.");
        }

        return targetPath;
    }

    public void SavePendingWallpaper(string path, WallpaperStyle style)
    {
        if (UseSharedDirectory)
        {
            EnsureSharedReadAccess();
        }

        string pendingFile = UseSharedDirectory ? PendingWallpaperFile : LocalPendingWallpaperFile;
        Directory.CreateDirectory(Path.GetDirectoryName(pendingFile)!);
        var request = new PendingWallpaper { Path = path, Style = style };
        string json = JsonSerializer.Serialize(request);
        string tempPath = pendingFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, pendingFile, true);
    }

    public PendingWallpaper? LoadPendingWallpaper()
    {
        PendingWallpaper? shared = TryLoadPendingWallpaper(PendingWallpaperFile);
        PendingWallpaper? local = TryLoadPendingWallpaper(LocalPendingWallpaperFile);

        if (shared == null) return local;
        if (local == null) return shared;

        DateTime sharedTime = File.GetLastWriteTimeUtc(PendingWallpaperFile);
        DateTime localTime = File.GetLastWriteTimeUtc(LocalPendingWallpaperFile);
        return localTime > sharedTime ? local : shared;
    }

    private static PendingWallpaper? TryLoadPendingWallpaper(string pendingFile)
    {
        try
        {
            if (!File.Exists(pendingFile)) return null;
            var request = JsonSerializer.Deserialize<PendingWallpaper>(File.ReadAllText(pendingFile));
            if (request == null || string.IsNullOrWhiteSpace(request.Path) || !File.Exists(request.Path)) return null;
            return request;
        }
        catch
        {
            return null;
        }
    }

    public void CleanupOldWallpapers(int keep = 10)
    {
        CleanupOldWallpapers(WallpapersDirectory, keep);
        CleanupOldWallpapers(LocalWallpapersDirectory, keep);
    }

    private static void CleanupOldWallpapers(string directory, int keep)
    {
        try
        {
            if (!Directory.Exists(directory)) return;

            var files = Directory.GetFiles(directory, "wallpaper_*")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            for (int i = keep; i < files.Count; i++)
            {
                try { File.Delete(files[i]); } catch { }
            }
        }
        catch { }
    }

    public bool ApplyPendingWallpaper(int attempts = 4, int delayMilliseconds = 1500)
    {
        var request = LoadPendingWallpaper();
        if (request == null) return false;

        Exception? lastError = null;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                ApplyWallpaper(request.Path, request.Style);
                return true;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < attempts - 1)
                    Thread.Sleep(delayMilliseconds);
            }
        }

        throw new InvalidOperationException($"No se pudo aplicar el fondo: {lastError?.Message}", lastError);
    }

    public void ApplyWallpaper(string path, WallpaperStyle style)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("No se encontró el archivo de fondo de escritorio.", path);

        string localPath = PrepareLocalWallpaper(path);

        WriteUserStyle(style);

        // SystemParametersInfo fuerza a Explorer a releer el archivo. Si el fondo
        // compartido no es accesible desde la sesión del usuario, Windows muestra
        // un fondo negro aunque la operación se reporte como correcta.
        bool applied = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, localPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

        if (!applied && !TryApplyWithDesktopWallpaper(localPath, style))
            throw new InvalidOperationException("Windows no pudo aplicar el fondo de escritorio.");
    }

    private static string PrepareLocalWallpaper(string sourcePath)
    {
        string sourceFullPath = Path.GetFullPath(sourcePath);
        string localFullPath = Path.GetFullPath(LocalWallpapersDirectory);
        if (sourceFullPath.StartsWith(localFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return sourceFullPath;

        Directory.CreateDirectory(LocalWallpapersDirectory);

        string ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
        string uniqueName = "wallpaper_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N")[..8] + ext;
        string targetPath = Path.Combine(LocalWallpapersDirectory, uniqueName);

        try
        {
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"No se pudo leer la imagen del fondo compartido: acceso denegado a '{sourcePath}'.", ex);
        }

        return targetPath;
    }

    private static bool TryApplyWithDesktopWallpaper(string path, WallpaperStyle style)
    {
        object? wallpaperObject = null;
        try
        {
            wallpaperObject = new DesktopWallpaperClass();
            var wallpaper = (IDesktopWallpaper)wallpaperObject;
            wallpaper.SetPosition((DesktopWallpaperPosition)(int)style);

            uint count = wallpaper.GetMonitorDevicePathCount();
            if (count == 0) return false;

            for (uint i = 0; i < count; i++)
            {
                string monitorId = wallpaper.GetMonitorDevicePathAt(i);
                if (!string.IsNullOrEmpty(monitorId))
                    wallpaper.SetWallpaper(monitorId, path);
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (wallpaperObject != null)
            {
                try { Marshal.ReleaseComObject(wallpaperObject); } catch { }
            }
        }
    }

    private static void WriteUserStyle(WallpaperStyle style)
    {
        string wallpaperStyle;
        string tileWallpaper;

        switch (style)
        {
            case WallpaperStyle.Tile:
                wallpaperStyle = "0";
                tileWallpaper = "1";
                break;
            case WallpaperStyle.Center:
            case WallpaperStyle.Span:
                wallpaperStyle = "0";
                tileWallpaper = "0";
                break;
            case WallpaperStyle.Stretch:
                wallpaperStyle = "2";
                tileWallpaper = "0";
                break;
            case WallpaperStyle.Fit:
                wallpaperStyle = "6";
                tileWallpaper = "0";
                break;
            default:
                wallpaperStyle = "10";
                tileWallpaper = "0";
                break;
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
        if (key == null) return;

        key.SetValue("WallpaperStyle", wallpaperStyle);
        key.SetValue("TileWallpaper", tileWallpaper);
    }
}

public sealed class PendingWallpaper
{
    public string Path { get; set; } = string.Empty;
    public WallpaperStyle Style { get; set; } = WallpaperStyle.Fill;
}
