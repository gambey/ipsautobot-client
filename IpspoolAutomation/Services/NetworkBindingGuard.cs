namespace IpspoolAutomation.Services;

public sealed class NetworkBindingGuard : INetworkBindingGuard
{
    public const string NotBoundPlatformMessage = "本软件不能运行在非绑定平台！";

    private readonly IApiClient _apiClient;
    private readonly IMacAddressProvider _macAddressProvider;

    public NetworkBindingGuard(IApiClient apiClient, IMacAddressProvider macAddressProvider)
    {
        _apiClient = apiClient;
        _macAddressProvider = macAddressProvider;
    }

    public async Task<NetworkBindingCheckResult> CheckCurrentMachineAsync(CancellationToken cancellationToken = default)
    {
        var current = await _apiClient.GetUserMacAsync(cancellationToken).ConfigureAwait(false);
        if (!current.Success)
            return new NetworkBindingCheckResult(false, NetworkBindingFailureReason.RequestFailed, current.Message ?? "查询绑定网卡失败");

        if (string.IsNullOrWhiteSpace(current.MacAddress))
            return new NetworkBindingCheckResult(false, NetworkBindingFailureReason.NotBound, "当前账号未绑定网卡，请重新登录完成绑定。");

        var localMacs = _macAddressProvider.GetLocalNormalizedMacAddresses();
        var matched = localMacs.Any(x => string.Equals(x, current.MacAddress, StringComparison.OrdinalIgnoreCase));
        if (!matched)
            return new NetworkBindingCheckResult(false, NetworkBindingFailureReason.NotMatched, NotBoundPlatformMessage);

        return new NetworkBindingCheckResult(true, NetworkBindingFailureReason.None, null);
    }
}
