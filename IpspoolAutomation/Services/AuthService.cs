using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models.Auth;

namespace IpspoolAutomation.Services;

public sealed class AuthService : IAuthService
{
    private readonly IApiClient _api;
    private string? _token;
    private string? _userName;
    private static readonly string StoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation", "auth.json");

    public AuthService(IApiClient api)
    {
        _api = api;
        LoadStored();
    }

    public bool IsLoggedIn => !string.IsNullOrEmpty(_token);
    public string? Token => _token;
    public string? UserName => _userName;

    public async Task<LoginResult> LoginAsync(string account, string password, CancellationToken cancellationToken = default)
    {
        var result = await _api.LoginAsync(new LoginRequest(account, password), cancellationToken).ConfigureAwait(false);
        if (result.Success && !string.IsNullOrEmpty(result.Token))
        {
            _token = result.Token;
            _userName = result.UserName ?? account;
            if (_api is ApiClient client)
                client.SetToken(_token);
            SaveStored();
        }
        return result;
    }

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        return await _api.RegisterAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public void Logout()
    {
        _token = null;
        _userName = null;
        if (_api is ApiClient client)
            client.SetToken(null);
        try
        {
            if (File.Exists(StoragePath))
                File.Delete(StoragePath);
        }
        catch { /* ignore */ }
    }

    private void SaveStored()
    {
        try
        {
            var dir = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var data = new { Token = _token, UserName = _userName };
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(data));
        }
        catch { /* ignore */ }
    }

    private void LoadStored()
    {
        try
        {
            if (!File.Exists(StoragePath))
                return;
            var json = File.ReadAllText(StoragePath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Token", out var t))
                _token = t.GetString();
            if (root.TryGetProperty("UserName", out var u))
                _userName = u.GetString();
            if (!string.IsNullOrEmpty(_token) && _api is ApiClient client)
                client.SetToken(_token);
        }
        catch { /* ignore */ }
    }
}
