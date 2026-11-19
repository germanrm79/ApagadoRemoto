using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Apagado.Core.Dtos;

namespace Apagado.Core.Protocol;

public static class FrameReader
{
    public static async Task<(CommandHeader Header, byte[]? ImageData)> ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken = default)
    {
        // Leer longitud de cabecera (Int32)
        byte[] lenBuf = new byte[4];
        await ReadExactAsync(stream, lenBuf, 4, cancellationToken);
        int headerLength = BitConverter.ToInt32(lenBuf, 0);

        // Leer cabecera
        byte[] headerBuf = new byte[headerLength];
        await ReadExactAsync(stream, headerBuf, headerLength, cancellationToken);
        string headerJson = Encoding.UTF8.GetString(headerBuf);
        var header = JsonSerializer.Deserialize<CommandHeader>(headerJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Cabecera inválida");

        byte[]? imageData = null;
        if (header.ImageLength > 0)
        {
            imageData = new byte[header.ImageLength];
            await ReadExactAsync(stream, imageData, (int)header.ImageLength, cancellationToken);
        }

        return (header, imageData);
    }

    public static async Task<CommandResponse> ReadResponseAsync(NetworkStream stream, CancellationToken cancellationToken = default)
    {
        byte[] lenBuf = new byte[4];
        await ReadExactAsync(stream, lenBuf, 4, cancellationToken);
        int length = BitConverter.ToInt32(lenBuf, 0);

        byte[] buf = new byte[length];
        await ReadExactAsync(stream, buf, length, cancellationToken);
        string json = Encoding.UTF8.GetString(buf);
        return JsonSerializer.Deserialize<CommandResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Respuesta inválida");
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, cancellationToken);
            if (read == 0) throw new IOException("Conexión cerrada inesperadamente");
            totalRead += read;
        }
    }
}
