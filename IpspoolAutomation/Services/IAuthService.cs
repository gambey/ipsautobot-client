using IpspoolAutomation.Models.Auth;

namespace IpspoolAutomation.Services;

public interface IAuthService
{
    Task<LoginResult> LoginAsync(string account, string password, CancellationToken cancellationToken = default);
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<bool> RefreshCurrentUserProfileAsync(CancellationToken cancellationToken = default);
    void Logout();
    string? BoundMac { get; }
    string? MemberExpireAt { get; }
    int? MemberType { get; }
    bool IsLoggedIn { get; }
    string? Token { get; }
    string? UserName { get; }

    /// <summary>
    /// If <c>targetSettings.json</c> is missing, downloads <c>GET /api/client-settings.zip</c> and extracts beside that path. Requires user JWT.
    /// </summary>
    Task EnsureClientSettingsFromServerIfMissingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 始终请求 <c>GET /api/client-settings.zip</c> 并解压到 <c>client_settings</c> 目录，覆盖已有文件。需要已登录（用户 JWT）。
    /// </summary>
    Task<ClientSettingsRefreshResult> RefreshClientSettingsFromServerAsync(CancellationToken cancellationToken = default);
}
