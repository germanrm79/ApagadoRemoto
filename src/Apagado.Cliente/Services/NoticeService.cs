using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Apagado.Cliente.Services;

public static class NoticeService
{
    public const string DefaultTitle = "Aviso del administrador";

    private const string MutexName = @"Local\ApagadoRemoto.NoticeOverlay";

    public static void Show(string? title, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            message = "El administrador ha enviado un aviso.";
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = DefaultTitle;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var mutex = new Mutex(false, MutexName);
        try
        {
            try
            {
                mutex.WaitOne(TimeSpan.FromMinutes(5));
            }
            catch (AbandonedMutexException)
            {
            }

            using var form = new NoticeOverlayForm(title!, message);
            Application.Run(form);
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch { }
        }
    }

    public static string EncodePayload(string? title, string message)
    {
        var payload = new NoticePayload
        {
            Title = title ?? string.Empty,
            Message = message
        };
        string json = JsonSerializer.Serialize(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryDecodePayload(string encoded, out string title, out string message)
    {
        title = DefaultTitle;
        message = string.Empty;

        try
        {
            byte[] bytes = Convert.FromBase64String(encoded);
            var payload = JsonSerializer.Deserialize<NoticePayload>(Encoding.UTF8.GetString(bytes));
            if (payload == null || string.IsNullOrWhiteSpace(payload.Message))
            {
                return false;
            }

            title = string.IsNullOrWhiteSpace(payload.Title) ? DefaultTitle : payload.Title;
            message = payload.Message;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class NoticePayload
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

internal sealed class NoticeOverlayForm : Form
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly OverlayKeyboardGuard _keyboardGuard = new();
    private readonly Button _btnAccept;
    private bool _allowClose;
    private int _ticks;

    public NoticeOverlayForm(string title, string message)
    {
        Text = $"Aviso - {Application.ProductVersion}";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        TopMost = true;
        BackColor = Color.FromArgb(15, 18, 24);
        KeyPreview = true;

        Rectangle bounds = Screen.AllScreens[0].Bounds;
        foreach (Screen screen in Screen.AllScreens)
        {
            bounds = Rectangle.Union(bounds, screen.Bounds);
        }
        Bounds = bounds;

        Color cardColor = Color.FromArgb(28, 32, 40);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 76));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 14));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 14));

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = cardColor,
            Padding = new Padding(40)
        };

        var cardLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = cardColor
        };
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        var lblTitle = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 64,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 26f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 196, 0),
            Margin = new Padding(0, 0, 0, 12)
        };

        var txtMessage = new TextBox
        {
            Text = message,
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 16f, FontStyle.Regular),
            ForeColor = Color.White,
            BackColor = cardColor,
            TabStop = false,
            Margin = new Padding(0, 0, 0, 16)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = cardColor,
            AutoSize = true,
            Margin = new Padding(0)
        };

        _btnAccept = new Button
        {
            Text = "Aceptar",
            Width = 170,
            Height = 46,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold)
        };
        _btnAccept.Click += OnAcceptClick;
        buttonPanel.Controls.Add(_btnAccept);

        cardLayout.Controls.Add(lblTitle, 0, 0);
        cardLayout.Controls.Add(txtMessage, 0, 1);
        cardLayout.Controls.Add(buttonPanel, 0, 2);
        card.Controls.Add(cardLayout);

        layout.Controls.Add(card, 1, 1);
        Controls.Add(layout);

        AcceptButton = _btnAccept;

        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnAcceptClick(object? sender, EventArgs e)
    {
        _allowClose = true;
        Close();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
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
        _btnAccept.Focus();
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
