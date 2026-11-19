using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Apagado.Core.Dtos;
using Apagado.Core.Enums;
using Apagado.Core.Protocol;
using Apagado.Servidor.Models;

namespace Apagado.Servidor.Services;

public class DiscoveryScanner
{
    private const int MaxHostsPerSubnet = 254;
    private const int MaxParallel = 256;
    private static readonly TimeSpan HostTimeout = TimeSpan.FromMilliseconds(250);

    public async Task<List<TargetDevice>> ScanAsync(int port, CancellationToken cancellationToken = default)
    {
        List<IPAddress> addresses = GetScanAddresses();
        var results = new List<TargetDevice>();
        var sync = new object();
        using var semaphore = new SemaphoreSlim(MaxParallel);

        IEnumerable<Task> tasks = addresses.Select(async address =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                TargetDevice? device = await ProbeAsync(address, port, cancellationToken);
                if (device != null)
                {
                    lock (sync)
                    {
                        results.Add(device);
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return results
            .GroupBy(d => string.IsNullOrWhiteSpace(d.MachineName)
                ? d.Address
                : $"{d.MachineName}|{d.Port}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderBy(d => d.Ip == IPAddress.Loopback.ToString() ? 1 : 0)
                .ThenBy(d => d.Ip)
                .First())
            .OrderBy(d => d.MachineName)
            .ThenBy(d => d.Ip)
            .ToList();
    }

    private static async Task<TargetDevice?> ProbeAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        try
        {
            Task connectTask = client.ConnectAsync(address, port);
            await connectTask.WaitAsync(HostTimeout, cancellationToken);

            using NetworkStream stream = client.GetStream();
            var header = new CommandHeader
            {
                Type = CommandType.Identify,
                Token = string.Empty,
                RequestId = Guid.NewGuid().ToString()
            };

            await FrameWriter.WriteFrameAsync(stream, header, null, cancellationToken);
            CommandResponse response = await FrameReader.ReadResponseAsync(stream, cancellationToken);

            if (!response.Success)
            {
                return null;
            }

            return new TargetDevice
            {
                MachineName = response.MachineName ?? string.Empty,
                Ip = address.ToString(),
                Port = response.Port ?? port,
                Version = response.Version ?? "—"
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static List<IPAddress> GetScanAddresses()
    {
        var hosts = new HashSet<IPAddress> { IPAddress.Loopback };

        List<NetworkInterface> nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsUsable)
            .ToList();
        List<NetworkInterface> withGateway = nics.Where(HasIPv4Gateway).ToList();
        List<NetworkInterface> candidates = withGateway.Count > 0 ? withGateway : nics;

        foreach (NetworkInterface nic in candidates)
        {
            foreach (UnicastIPAddressInformation unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                byte[] ipBytes = unicast.Address.GetAddressBytes();
                if (ipBytes[0] == 169 && ipBytes[1] == 254)
                {
                    continue;
                }

                byte[]? maskBytes = unicast.IPv4Mask?.GetAddressBytes();
                if (maskBytes == null || maskBytes.Length != 4)
                {
                    continue;
                }

                AddSubnetHosts(hosts, ipBytes, maskBytes);
            }
        }

        return hosts.ToList();
    }

    private static bool IsUsable(NetworkInterface nic)
    {
        return nic.OperationalStatus == OperationalStatus.Up &&
               nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
               nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel;
    }

    private static bool HasIPv4Gateway(NetworkInterface nic)
    {
        return nic.GetIPProperties().GatewayAddresses.Any(g =>
            g.Address.AddressFamily == AddressFamily.InterNetwork &&
            !g.Address.Equals(IPAddress.Any));
    }

    private static void AddSubnetHosts(HashSet<IPAddress> hosts, byte[] ip, byte[] mask)
    {
        uint ipValue = ToUInt32(ip);
        uint maskValue = ToUInt32(mask);
        uint network = ipValue & maskValue;
        uint broadcast = network | ~maskValue;
        uint hostCount = broadcast - network - 1;

        if (hostCount == 0)
        {
            return;
        }

        if (hostCount > MaxHostsPerSubnet)
        {
            network = ipValue & 0xFFFFFF00u;
            broadcast = network | 0x000000FFu;
        }

        uint first = network + 1;
        uint last = broadcast - 1;
        for (uint value = first; value <= last; value++)
        {
            hosts.Add(new IPAddress(ToBytes(value)));
        }
    }

    private static uint ToUInt32(byte[] bytes)
    {
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static byte[] ToBytes(uint value)
    {
        return new[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        };
    }
}
