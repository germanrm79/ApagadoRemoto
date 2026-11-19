using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Apagado.Core.Dtos;

namespace Apagado.Core.Protocol;

public static class FrameWriter
{
    public static async Task WriteFrameAsync(NetworkStream stream, CommandHeader header, byte[]? imageData = null, CancellationToken cancellationToken = default)
    {
        if (imageData != null && imageData.Length > 0)
        {
            header.ImageLength = imageData.Length;
        }
        else
        {
            header.ImageLength = 0;
        }

        string headerJson = JsonSerializer.Serialize(header);
        byte[] headerBytes = Encoding.UTF8.GetBytes(headerJson);
        byte[] lenBytes = BitConverter.GetBytes(headerBytes.Length);

        await stream.WriteAsync(lenBytes, 0, lenBytes.Length, cancellationToken);
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length, cancellationToken);

        if (imageData != null && imageData.Length > 0)
        {
            await stream.WriteAsync(imageData, 0, imageData.Length, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    public static async Task WriteResponseAsync(NetworkStream stream, CommandResponse response, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(response);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        byte[] lenBytes = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(lenBytes, 0, lenBytes.Length, cancellationToken);
        await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
