using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace StreamNetMon.App.Windows;

/// <summary>The adapter that carries traffic to the default gateway, and what is around it on the LAN.</summary>
public sealed class WindowsNetwork : IAdapterSource, INeighbourSource
{
    private NetworkInterface? _adapter;
    private string? _gateway;
    private DateTimeOffset _foundAt;

    public AdapterReading? Read()
    {
        try
        {
            if (Find() is not { } nic) return null;
            var stats = nic.GetIPStatistics();
            return new AdapterReading(nic.Name, nic.Speed, stats.BytesSent, stats.BytesReceived,
                stats.OutgoingPacketsWithErrors, stats.OutgoingPacketsDiscarded);
        }
        catch (NetworkInformationException)
        {
            _adapter = null;
            return null;
        }
    }

    public string? DefaultGateway()
    {
        Find();
        return _gateway;
    }

    public IReadOnlyList<(string Address, string? Name)> Neighbours()
    {
        var index = Find()?.GetIPProperties().GetIPv4Properties()?.Index;
        return ArpTable.Read()
            .Where(e => index is null || e.InterfaceIndex == index)
            .Select(e => (e.Address.ToString(), (string?)null))
            .Distinct()
            .ToList();
    }

    /// <summary>Re-resolves the adapter every 10 s; the list of interfaces is not free to enumerate.</summary>
    private NetworkInterface? Find()
    {
        var now = DateTimeOffset.UtcNow;
        if (_adapter is not null && now - _foundAt < TimeSpan.FromSeconds(10)) return _adapter;
        _foundAt = now;
        _adapter = null;
        _gateway = null;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var gateway = nic.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
            if (gateway is null) continue;

            // Prefer a physical Ethernet adapter when several have a gateway (VPNs, virtual switches).
            if (_adapter is null || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet && _adapter.NetworkInterfaceType != NetworkInterfaceType.Ethernet)
            {
                _adapter = nic;
                _gateway = gateway.ToString();
            }
        }
        return _adapter;
    }
}

/// <summary>The IPv4 neighbour (ARP) table.</summary>
internal static class ArpTable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MibIpNetRow
    {
        public int Index;
        public int PhysAddrLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] PhysAddr;
        public uint Addr;
        public int Type;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetIpNetTable(IntPtr table, ref int size, bool order);

    private const int ErrorInsufficientBuffer = 122;

    public static IReadOnlyList<(IPAddress Address, int InterfaceIndex)> Read()
    {
        var result = new List<(IPAddress, int)>();
        var size = 0;
        if (GetIpNetTable(IntPtr.Zero, ref size, false) != ErrorInsufficientBuffer || size <= 0) return result;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetIpNetTable(buffer, ref size, false) != 0) return result;
            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibIpNetRow>();
            var row = buffer + sizeof(int);
            for (var i = 0; i < count; i++, row += rowSize)
            {
                var entry = Marshal.PtrToStructure<MibIpNetRow>(row);
                // 3 = dynamic, 4 = static; a real device has a 6-byte hardware address.
                if (entry.Type is not (3 or 4) || entry.PhysAddrLen != 6) continue;
                var address = new IPAddress(entry.Addr);
                var bytes = address.GetAddressBytes();
                if (bytes[0] >= 224 || bytes[3] == 255) continue;   // multicast and broadcast
                result.Add((address, entry.Index));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }
}
