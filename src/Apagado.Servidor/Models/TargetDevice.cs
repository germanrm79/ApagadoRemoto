using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Apagado.Servidor.Models;

public class TargetDevice : INotifyPropertyChanged
{
    private string _machineName = string.Empty;
    private string _ip = string.Empty;
    private int _port = 5007;
    private string _version = "—";
    private string _lastStatus = "Sin acciones";
    private DateTime? _lastUpdated;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MachineName
    {
        get => _machineName;
        set
        {
            if (_machineName == value) return;
            _machineName = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string Ip
    {
        get => _ip;
        set
        {
            if (_ip == value) return;
            _ip = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Address));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            if (_port == value) return;
            _port = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Address));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string Version
    {
        get => _version;
        set
        {
            string next = string.IsNullOrWhiteSpace(value) ? "—" : value;
            if (_version == next) return;
            _version = next;
            OnPropertyChanged();
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        set
        {
            string next = value ?? string.Empty;
            if (_lastStatus == next) return;
            _lastStatus = next;
            OnPropertyChanged();
        }
    }

    public DateTime? LastUpdated
    {
        get => _lastUpdated;
        set
        {
            if (_lastUpdated == value) return;
            _lastUpdated = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastUpdatedDisplay));
        }
    }

    [JsonIgnore]
    public string Address => $"{Ip}:{Port}";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(MachineName) ? Address : $"{MachineName} — {Address}";

    [JsonIgnore]
    public string LastUpdatedDisplay => _lastUpdated?.ToString("dd/MM/yyyy HH:mm:ss") ?? "—";

    public override string ToString() => DisplayName;

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
