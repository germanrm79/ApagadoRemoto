using Apagado.Core.Enums;
using System.Text.Json.Serialization;

namespace Apagado.Core.Dtos;

public class CommandHeader
{
    public CommandType Type { get; set; }
    public string Token { get; set; } = string.Empty;
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long ImageLength { get; set; }
    public WallpaperStyle Style { get; set; } = WallpaperStyle.Fill;
    public int DelaySeconds { get; set; }
    public string? Message { get; set; }
    public string? Title { get; set; }
}
