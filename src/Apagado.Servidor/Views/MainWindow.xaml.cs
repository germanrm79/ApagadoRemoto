using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Apagado.Core.Dtos;
using Apagado.Core.Enums;
using Apagado.Servidor.Models;
using Apagado.Servidor.Services;

namespace Apagado.Servidor.Views;

public partial class MainWindow : Window
{
    private const int DefaultPort = 5007;
    private const int MaxDelaySeconds = 604800;
    private const string DefaultLockMessage = "Ud. ha infringido el reglamento. Para continuar, contacte al administrador.";

    private string? _selectedImagePath;
    private string? _selectedUpdatePackagePath;
    private bool _isScanning;
    private readonly NetworkSender _sender = new();
    private readonly DiscoveryScanner _scanner = new();
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static string TargetsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ApagadoRemoto",
        "targets.json");

    public MainWindow()
    {
        InitializeComponent();
        cmbStyle.SelectedIndex = 4; // Fill
        LoadTargets();
    }

    private void LstTargets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int count = lstTargets.SelectedItems.Count;
        txtSelectionCount.Text = count == 1 ? "1 seleccionado" : $"{count} seleccionados";
    }

    private void Log(string msg)
    {
        Dispatcher.Invoke(() => {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            txtLog.ScrollToEnd();
        });
    }

    private void LoadTargets()
    {
        try
        {
            if (File.Exists(TargetsPath))
            {
                AddLoadedTargets(File.ReadAllText(TargetsPath));
                return;
            }

            string nextToExe = Path.Combine(AppContext.BaseDirectory, "targets.json");
            if (File.Exists(nextToExe))
            {
                AddLoadedTargets(File.ReadAllText(nextToExe));
                return;
            }

            string inWorkingDir = Path.Combine(Directory.GetCurrentDirectory(), "targets.json");
            if (File.Exists(inWorkingDir))
            {
                AddLoadedTargets(File.ReadAllText(inWorkingDir));
            }
        }
        catch (Exception ex)
        {
            Log($"No se pudo cargar la lista: {ex.Message}");
        }
    }

    private void AddLoadedTargets(string json)
    {
        List<TargetDevice>? devices = null;
        try
        {
            devices = JsonSerializer.Deserialize<List<TargetDevice>>(json, ReadOptions);
        }
        catch (JsonException)
        {
        }

        if (devices != null)
        {
            foreach (TargetDevice device in devices)
            {
                if (!string.IsNullOrWhiteSpace(device.Ip))
                {
                    lstTargets.Items.Add(device);
                }
            }
            return;
        }

        List<string>? legacy = JsonSerializer.Deserialize<List<string>>(json, ReadOptions);
        if (legacy == null)
        {
            return;
        }

        foreach (string entry in legacy)
        {
            TargetDevice? device = ParseTarget(entry);
            if (device != null)
            {
                lstTargets.Items.Add(device);
            }
        }
    }

    private void SaveTargets()
    {
        try
        {
            string directory = Path.GetDirectoryName(TargetsPath)!;
            Directory.CreateDirectory(directory);
            var list = lstTargets.Items.Cast<TargetDevice>().ToList();
            File.WriteAllText(TargetsPath, JsonSerializer.Serialize(list, WriteOptions));
        }
        catch (Exception ex)
        {
            Log($"No se pudo guardar la lista: {ex.Message}");
        }
    }

    private TargetDevice? FindTarget(string ip, int port)
    {
        return lstTargets.Items.Cast<TargetDevice>()
            .FirstOrDefault(t => string.Equals(t.Ip, ip, StringComparison.OrdinalIgnoreCase) && t.Port == port);
    }

    private static TargetDevice? ParseTarget(string text)
    {
        string value = text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] parts = value.Split(':', 2);
        int port = DefaultPort;
        if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort) && parsedPort > 0 && parsedPort <= 65535)
        {
            port = parsedPort;
        }

        return new TargetDevice { Ip = parts[0].Trim(), Port = port };
    }

    private bool TryGetTargets(out List<TargetDevice> targets)
    {
        targets = lstTargets.SelectedItems.Cast<TargetDevice>().ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("Selecciona al menos un destino de la lista. Usa Ctrl o Shift para seleccionar varios.");
            return false;
        }
        return true;
    }

    private bool TryGetDelay(out int delay)
    {
        if (!int.TryParse(txtDelay.Text.Trim(), out delay) || delay < 0 || delay > MaxDelaySeconds)
        {
            MessageBox.Show($"Retraso inválido. Usa un número de segundos entre 0 y {MaxDelaySeconds}.");
            return false;
        }
        return true;
    }

    private async Task SendCommandToAllAsync(List<TargetDevice> targets, string action, Func<CommandHeader> headerFactory, byte[]? imageData = null, int? timeoutMs = null)
    {
        int effectiveTimeoutMs = timeoutMs ?? (imageData != null ? 30000 : 5000);

        foreach (TargetDevice target in targets)
        {
            CommandHeader header = headerFactory();
            Log($"{action}: enviando a {target.DisplayName}...");

            CommandResponse? resp = await _sender.SendAsync(target.Ip, target.Port, header, imageData, effectiveTimeoutMs);
            string status = resp == null
                ? "ERROR: Sin respuesta"
                : resp.Success ? $"OK: {resp.Message}" : $"ERROR: {resp.Message}";

            target.LastStatus = $"{action}: {status}";
            target.LastUpdated = DateTime.Now;
            Log($"{target.DisplayName} → {status}");
        }

        SaveTargets();
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        string input = Microsoft.VisualBasic.Interaction.InputBox("Introduce IP:Puerto", "Añadir destino", "192.168.1.100:5007");
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        TargetDevice? device = ParseTarget(input);
        if (device == null || string.IsNullOrWhiteSpace(device.Ip))
        {
            MessageBox.Show("Formato inválido. Usa IP:Puerto.");
            return;
        }

        if (FindTarget(device.Ip, device.Port) != null)
        {
            MessageBox.Show("Ese destino ya está en la lista.");
            return;
        }

        device.LastStatus = "Agregado";
        device.LastUpdated = DateTime.Now;
        lstTargets.Items.Add(device);
        SaveTargets();
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (lstTargets.SelectedItems.Count == 0)
        {
            return;
        }

        foreach (TargetDevice device in lstTargets.SelectedItems.Cast<TargetDevice>().ToList())
        {
            lstTargets.Items.Remove(device);
        }

        SaveTargets();
    }

    private async void BtnDiscover_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning)
        {
            return;
        }

        if (!int.TryParse(txtPort.Text.Trim(), out int port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("Puerto inválido.");
            return;
        }

        _isScanning = true;
        btnDiscover.IsEnabled = false;
        object? originalContent = btnDiscover.Content;
        btnDiscover.Content = "Buscando...";

        try
        {
            Log($"Buscando clientes en la red local (puerto {port})...");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            List<TargetDevice> found = await _scanner.ScanAsync(port);
            stopwatch.Stop();

            var foundByEndpoint = found
                .GroupBy(d => EndpointKey(d.Ip, d.Port), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var registered = lstTargets.Items.Cast<TargetDevice>().ToList();
            var pendingProbes = new List<TargetDevice>();

            // Actualiza los ya registrados. Los que no salieron en el escaneo se consultan
            // directamente porque pueden usar otro puerto o estar fuera de la subred escaneada.
            foreach (TargetDevice target in registered)
            {
                if (foundByEndpoint.TryGetValue(EndpointKey(target.Ip, target.Port), out TargetDevice? scanned))
                {
                    if (!string.IsNullOrWhiteSpace(scanned.MachineName))
                    {
                        target.MachineName = scanned.MachineName;
                    }
                    if (!string.IsNullOrWhiteSpace(scanned.Version))
                    {
                        target.Version = scanned.Version;
                    }
                    target.LastStatus = "Activo";
                    target.LastUpdated = DateTime.Now;
                    Log($"Activo: {target.DisplayName}");
                }
                else
                {
                    pendingProbes.Add(target);
                }
            }

            if (pendingProbes.Count > 0)
            {
                using var semaphore = new System.Threading.SemaphoreSlim(32);
                await Task.WhenAll(pendingProbes.Select(async target =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await ProbeTargetAsync(target);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            // Agrega solo los clientes que aún no estaban registrados.
            int added = 0;
            foreach (TargetDevice device in found)
            {
                if (FindTarget(device.Ip, device.Port) != null)
                {
                    continue;
                }

                device.LastStatus = "Activo";
                device.LastUpdated = DateTime.Now;
                lstTargets.Items.Add(device);
                added++;
                Log($"Encontrado: {device.DisplayName}");
            }

            int online = lstTargets.Items.Cast<TargetDevice>().Count(t => t.LastStatus == "Activo");
            int offline = lstTargets.Items.Cast<TargetDevice>().Count(t => t.LastStatus == "Sin respuesta");

            SaveTargets();

            string nuevos = added == 0 ? "sin clientes nuevos" : $"{added} nuevo(s)";
            Log($"Búsqueda finalizada en {stopwatch.Elapsed.TotalSeconds:0.0}s: {nuevos}, {online} activo(s), {offline} sin respuesta.");
        }
        catch (Exception ex)
        {
            Log($"ERROR al buscar clientes: {ex.Message}");
        }
        finally
        {
            btnDiscover.Content = originalContent;
            btnDiscover.IsEnabled = true;
            _isScanning = false;
        }
    }

    private static string EndpointKey(string ip, int port) => $"{ip}:{port}";

    private async Task ProbeTargetAsync(TargetDevice target)
    {
        CommandResponse? response = await Task.Run(() => _sender.SendAsync(
            target.Ip,
            target.Port,
            new CommandHeader
            {
                Type = CommandType.Identify,
                Token = string.Empty,
                RequestId = Guid.NewGuid().ToString()
            },
            null,
            2000));

        if (response?.Success == true)
        {
            if (!string.IsNullOrWhiteSpace(response.MachineName))
            {
                target.MachineName = response.MachineName;
            }
            if (!string.IsNullOrWhiteSpace(response.Version))
            {
                target.Version = response.Version;
            }
            target.LastStatus = "Activo";
            Log($"Activo: {target.DisplayName}");
        }
        else
        {
            target.LastStatus = "Sin respuesta";
            Log($"Sin respuesta: {target.DisplayName}");
        }

        target.LastUpdated = DateTime.Now;
    }

    private async void BtnRefreshVersions_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTargets(out List<TargetDevice> targets))
        {
            return;
        }

        btnRefreshVersions.IsEnabled = false;
        try
        {
            foreach (TargetDevice target in targets)
            {
                await ProbeTargetAsync(target);
            }
            SaveTargets();
            Log("Verificación de versiones finalizada.");
        }
        finally
        {
            btnRefreshVersions.IsEnabled = true;
        }
    }

    private void BtnSelectImage_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dlg = new OpenFileDialog
        {
            Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp",
            Title = "Seleccionar imagen para fondo de escritorio"
        };
        if (dlg.ShowDialog() == true)
        {
            _selectedImagePath = dlg.FileName;
            BitmapImage bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(_selectedImagePath);
            bmp.EndInit();
            imgPreview.Source = bmp;
            Log($"Imagen seleccionada: {Path.GetFileName(_selectedImagePath)}");
        }
    }

    private void BtnSelectUpdatePackage_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dlg = new OpenFileDialog
        {
            Filter = "Instalador de cliente|ApagadoRemotoClienteSetup.exe;*.exe",
            Title = "Seleccionar instalador del cliente"
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        _selectedUpdatePackagePath = dlg.FileName;
        txtUpdatePackage.Text = dlg.FileName;
        string version = (FileVersionInfo.GetVersionInfo(dlg.FileName).ProductVersion ?? string.Empty).Trim();
        txtUpdateVersion.Text = version;
        txtUpdateSha256.Text = ComputeSha256(dlg.FileName);
        Log($"Paquete de actualización seleccionado: {Path.GetFileName(dlg.FileName)} (v{version})");
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private WallpaperStyle GetSelectedStyle()
    {
        string s = ((ComboBoxItem)cmbStyle.SelectedItem).Content.ToString() ?? "Fill";
        return s switch
        {
            "Center" => WallpaperStyle.Center,
            "Tile" => WallpaperStyle.Tile,
            "Stretch" => WallpaperStyle.Stretch,
            "Fit" => WallpaperStyle.Fit,
            "Fill" => WallpaperStyle.Fill,
            "Span" => WallpaperStyle.Span,
            _ => WallpaperStyle.Fill
        };
    }

    private async void BtnShutdown_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTargets(out List<TargetDevice> targets) || !TryGetDelay(out int delay))
        {
            return;
        }

        await SendCommandToAllAsync(targets, "Apagado", () => new CommandHeader
        {
            Type = CommandType.Shutdown,
            Token = txtToken.Text,
            DelaySeconds = delay
        });
    }

    private async void BtnReboot_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTargets(out List<TargetDevice> targets) || !TryGetDelay(out int delay))
        {
            return;
        }

        await SendCommandToAllAsync(targets, "Reinicio", () => new CommandHeader
        {
            Type = CommandType.Reboot,
            Token = txtToken.Text,
            DelaySeconds = delay
        });
    }

    private async void BtnCancelShutdown_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTargets(out List<TargetDevice> targets))
        {
            return;
        }

        await SendCommandToAllAsync(targets, "Cancelar apagado", () => new CommandHeader
        {
            Type = CommandType.CancelShutdown,
            Token = txtToken.Text
        });
    }

    private async void BtnLock_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTargets(out List<TargetDevice> targets))
        {
            return;
        }

        string message = txtLockMessage.Text.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            message = DefaultLockMessage;
        }

        await SendCommandToAllAsync(targets, "Bloqueo", () => new CommandHeader
        {
            Type = CommandType.Lock,
            Token = txtToken.Text,
            Message = message
        });
    }

    private async void BtnUnlock_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTargets(out List<TargetDevice> targets))
        {
            return;
        }

        await SendCommandToAllAsync(targets, "Desbloqueo", () => new CommandHeader
        {
            Type = CommandType.Unlock,
            Token = txtToken.Text
        });
    }

    private async void BtnSendNotice_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTargets(out List<TargetDevice> targets))
        {
            return;
        }

        string title = txtNoticeTitle.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Aviso del administrador";
        }

        string message = txtNoticeMessage.Text.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            MessageBox.Show("Escribe el mensaje del aviso.");
            return;
        }

        if (message.Length > 2000)
        {
            MessageBox.Show("El mensaje no puede superar los 2000 caracteres.");
            return;
        }

        await SendCommandToAllAsync(targets, "Aviso", () => new CommandHeader
        {
            Type = CommandType.Notice,
            Token = txtToken.Text,
            Title = title,
            Message = message
        });
    }

    private async void BtnSetWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTargets(out List<TargetDevice> targets))
        {
            return;
        }

        if (string.IsNullOrEmpty(_selectedImagePath) || !File.Exists(_selectedImagePath))
        {
            MessageBox.Show("Selecciona una imagen.");
            return;
        }

        byte[] imageData = File.ReadAllBytes(_selectedImagePath);
        string fileName = Path.GetFileName(_selectedImagePath);
        string contentType = GetContentType(_selectedImagePath);
        WallpaperStyle style = GetSelectedStyle();

        await SendCommandToAllAsync(targets, "Fondo", () => new CommandHeader
        {
            Type = CommandType.SetWallpaper,
            Token = txtToken.Text,
            FileName = fileName,
            ContentType = contentType,
            Style = style
        }, imageData);
    }

    private async void BtnUpdateClients_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTargets(out List<TargetDevice> targets))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedUpdatePackagePath) || !File.Exists(_selectedUpdatePackagePath))
        {
            MessageBox.Show("Selecciona el instalador del cliente.");
            return;
        }

        string version = txtUpdateVersion.Text.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            MessageBox.Show("Indica la versión del paquete.");
            return;
        }

        string sha256 = txtUpdateSha256.Text.Trim();
        if (string.IsNullOrWhiteSpace(sha256))
        {
            sha256 = ComputeSha256(_selectedUpdatePackagePath);
            txtUpdateSha256.Text = sha256;
        }

        List<TargetDevice> newer = targets.Where(t => IsVersionNewer(t.Version, version)).ToList();
        if (newer.Count > 0)
        {
            string nombres = string.Join(", ", newer.Select(t => t.DisplayName));
            MessageBoxResult result = MessageBox.Show(
                $"Estos equipos tienen una versión mayor: {nombres}. ¿Continuar de todos modos?",
                "Actualizar clientes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        byte[] packageData = File.ReadAllBytes(_selectedUpdatePackagePath);
        string fileName = Path.GetFileName(_selectedUpdatePackagePath);

        await SendCommandToAllAsync(targets, "Actualizar", () => new CommandHeader
        {
            Type = CommandType.Update,
            Token = txtToken.Text,
            FileName = fileName,
            ContentType = "application/octet-stream",
            PackageVersion = version,
            PackageSha256 = sha256
        }, packageData, 600000);
    }

    private static bool IsVersionNewer(string current, string incoming)
    {
        string a = NormalizeVersion(current);
        string b = NormalizeVersion(incoming);
        return Version.TryParse(a, out Version? currentVersion)
               && Version.TryParse(b, out Version? incomingVersion)
               && currentVersion > incomingVersion;
    }

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
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

        return text;
    }

    private string GetContentType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }
}
