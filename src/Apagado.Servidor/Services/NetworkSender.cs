using System.Net.Sockets;
using Apagado.Core.Dtos;
using Apagado.Core.Protocol;

namespace Apagado.Servidor.Services;

public class NetworkSender
{
    public async Task<CommandResponse?> SendAsync(string ip, int port, CommandHeader header, byte[]? imageData = null, int timeoutMs = 5000)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                var result = client.BeginConnect(ip, port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs), true);
                if (!success)
                {
                    client.Close();
                    return new CommandResponse { RequestId = header.RequestId, Success = false, Message = "Tiempo de espera agotado" };
                }
                client.EndConnect(result);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = timeoutMs;
                    stream.WriteTimeout = timeoutMs;
                    await FrameWriter.WriteFrameAsync(stream, header, imageData);
                    return await FrameReader.ReadResponseAsync(stream);
                }
            }
        }
        catch (Exception ex)
        {
            return new CommandResponse { RequestId = header.RequestId, Success = false, Message = ex.Message };
        }
    }
}
