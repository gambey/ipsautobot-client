using IpspoolAutomation.Models.Auth;

namespace IpspoolAutomation.Services;

public interface IApiClient
{
    void SetToken(string? token);
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<ChangePasswordResult> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<SubscribeOrderResult> CreateSubscribeOrderAsync(SubscribeOrderRequest request, CancellationToken cancellationToken = default);
    Task<CurrentUserProfileResult> GetCurrentUserProfileAsync(CancellationToken cancellationToken = default);
    Task<UserMacQueryResult> GetUserMacAsync(CancellationToken cancellationToken = default);
    Task<UserMacUpsertResult> PutUserMacAsync(string macAddress, CancellationToken cancellationToken = default);
    Task<UserMacVerifyResult> VerifyUserMacAsync(string macAddress, CancellationToken cancellationToken = default);
    Task<ClientSettingsArchiveResult> DownloadClientSettingsZipAsync(CancellationToken cancellationToken = default);
}
