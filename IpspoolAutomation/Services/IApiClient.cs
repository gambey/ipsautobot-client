using IpspoolAutomation.Models.Auth;

namespace IpspoolAutomation.Services;

public interface IApiClient
{
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<ChangePasswordResult> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<SubscribeOrderResult> CreateSubscribeOrderAsync(SubscribeOrderRequest request, CancellationToken cancellationToken = default);
}
