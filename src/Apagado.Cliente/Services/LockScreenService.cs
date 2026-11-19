using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace Apagado.Cliente.Services;

public static class LockScreenService
{
    public const string DefaultMessage = "Ud. ha infringido el reglamento. Para continuar, contacte al administrador.";

    private const string EventName = @"Local\ApagadoRemoto.LockScreen";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string PendingLockFile => Path.Combine(WallpaperService.DataDirectory, "lock.json");

    public static void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            message = DefaultMessage;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var gate = new EventWaitHandle(false, EventResetMode.ManualReset, EventName, out _);
        gate.Reset();

        using var form = new LockOverlayForm(message, gate);
        Application.Run(form);
    }

    public static bool RequestHide()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(EventName, out EventWaitHandle? gate))
            {
                return false;
            }

            using (gate)
            {
                gate.Set();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SavePending(string message)
    {
        try
        {
            Directory.CreateDirectory(WallpaperService.DataDirectory);
            var state = new PendingLock
            {
                Message = string.IsNullOrWhiteSpace(message) ? DefaultMessage : message
            };
            string json = JsonSerializer.Serialize(state);
            string tempPath = PendingLockFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, PendingLockFile, true);
        }
        catch
        {
        }
    }

    public static string? LoadPending()
    {
        try
        {
            if (!File.Exists(PendingLockFile))
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<PendingLock>(File.ReadAllText(PendingLockFile), JsonOptions);
            return string.IsNullOrWhiteSpace(state?.Message) ? DefaultMessage : state!.Message;
        }
        catch
        {
            return null;
        }
    }

    public static void ClearPending()
    {
        try
        {
            if (File.Exists(PendingLockFile))
            {
                File.Delete(PendingLockFile);
            }
        }
        catch
        {
        }
    }
}

public sealed class PendingLock
{
    public string Message { get; set; } = LockScreenService.DefaultMessage;
}

internal sealed class LockOverlayForm : Form
{
    private readonly EventWaitHandle _gate;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly OverlayKeyboardGuard _keyboardGuard = new();
    private bool _allowClose;
    private int _ticks;

    public LockOverlayForm(string message, EventWaitHandle gate)
    {
        _gate = gate;

        Text = $"APR-{Application.ProductVersion}";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(15, 18, 24);
        KeyPreview = true;
        Cursor.Hide();

        Rectangle bounds = Screen.AllScreens[0].Bounds;
        foreach (Screen screen in Screen.AllScreens)
        {
            bounds = Rectangle.Union(bounds, screen.Bounds);
        }
        Bounds = bounds;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BackColor
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));

        var title = new Label
        {
            Text = "EQUIPO BLOQUEADO",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.BottomCenter,
            Font = new Font("Segoe UI", 42f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 196, 0),
            Padding = new Padding(40, 0, 40, 20)
        };

        var detail = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 24f, FontStyle.Regular),
            ForeColor = Color.White,
            Padding = new Padding(80, 10, 80, 10)
        };

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(detail, 0, 1);
        Controls.Add(layout);

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_gate.WaitOne(0))
        {
            _allowClose = true;
            Close();
            return;
        }

        _ticks++;
        if (_ticks % 6 == 0)
        {
            TopMost = false;
            TopMost = true;
            BringToFront();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _keyboardGuard.Attach();
        Activate();
        BringToFront();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _keyboardGuard.Dispose();

        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
