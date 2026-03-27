using System.Net.NetworkInformation;
using System.Text;

namespace IpspoolAutomation.Services;

public sealed class MacAddressProvider : IMacAddressProvider
{
    public IReadOnlyList<string> GetLocalNormalizedMacAddresses()
    {
        var macs = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsUsable(nic))
                continue;
            var raw = nic.GetPhysicalAddress()?.ToString();
            var normalized = Normalize(raw);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;
            if (!macs.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                macs.Add(normalized);
        }
        return macs;
    }

    private static bool IsUsable(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
            nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            return false;
        return nic.OperationalStatus == OperationalStatus.Up;
    }

    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var sb = new StringBuilder(12);
        foreach (var ch in input)
        {
            if (Uri.IsHexDigit(ch))
                sb.Append(char.ToUpperInvariant(ch));
        }
        if (sb.Length != 12)
            return null;
        var hex = sb.ToString();
        return $"{hex[..2]}:{hex[2..4]}:{hex[4..6]}:{hex[6..8]}:{hex[8..10]}:{hex[10..12]}";
    }
}
