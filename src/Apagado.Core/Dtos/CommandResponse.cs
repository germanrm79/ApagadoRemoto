namespace Apagado.Core.Dtos;

public class CommandResponse
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? MachineName { get; set; }
    public int? Port { get; set; }
    public string? Version { get; set; }
}
