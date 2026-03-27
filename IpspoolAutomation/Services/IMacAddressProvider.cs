namespace IpspoolAutomation.Services;

public interface IMacAddressProvider
{
    IReadOnlyList<string> GetLocalNormalizedMacAddresses();
}
