namespace IpspoolAutomation.Services;

public enum NetworkBindingFailureReason
{
    None = 0,
    NotBound = 1,
    NotMatched = 2,
    RequestFailed = 3
}

public sealed record NetworkBindingCheckResult(bool Allowed, NetworkBindingFailureReason FailureReason, string? Message);

public interface INetworkBindingGuard
{
    Task<NetworkBindingCheckResult> CheckCurrentMachineAsync(CancellationToken cancellationToken = default);
}
