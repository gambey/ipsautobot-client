using IpspoolAutomation.Models.Auth;

namespace IpspoolAutomation.Services;

public interface IAuthService
{
    Task<LoginResult> LoginAsync(string account, string password, CancellationToken cancellationToken = default);
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    void Logout();
    bool IsLoggedIn { get; }
    string? Token { get; }
    string? UserName { get; }
}
