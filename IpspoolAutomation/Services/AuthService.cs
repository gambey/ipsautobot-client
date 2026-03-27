using System.IO;
using System.Text.Json;
using IpspoolAutomation.Models.Auth;

namespace IpspoolAutomation.Services;

public sealed class AuthService : IAuthService
{
    private readonly IApiClient _api;
    private readonly IMacAddressProvider _macAddressProvider;
    private string? _token;
    private string? _userName;
    private string? _boundMac;
    private string? _memberExpireAt;
    private int? _memberType;
    private static readonly string StoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation", "auth.json");

    public AuthService(IApiClient api, IMacAddressProvider macAddressProvider)
    {
        _api = api;
        _macAddressProvider = macAddressProvider;
        LoadStored();
    }

    public bool IsLoggedIn => !string.IsNullOrEmpty(_token);
    public string? Token => _token;
    public string? UserName => _userName;
    public string? BoundMac => _boundMac;
    public string? MemberExpireAt => _memberExpireAt;
    public int? MemberType => _memberType;

    public async Task<LoginResult> LoginAsync(string account, string password, CancellationToken cancellationToken = default)
    {
        var result = await _api.LoginAsync(new LoginRequest(account, password), cancellationToken).ConfigureAwait(false);
        if (result.Success && !string.IsNullOrEmpty(result.Token))
        {
            _token = result.Token;
            _userName = result.UserName ?? account;
            _memberExpireAt = result.MemberExpireAt;
            _memberType = result.MemberType;
            _api.SetToken(_token);
            await RefreshProfileFromMeAsync(cancellationToken).ConfigureAwait(false);
            var bindResult = await EnsureMacBoundAfterLoginAsync(cancellationToken).ConfigureAwait(false);
            if (!bindResult.Success)
            {
                Logout();
                return new LoginResult(false, null, null, bindResult.Message);
            }
            SaveStored();
        }
        return result;
    }

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        return await _api.RegisterAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RefreshCurrentUserProfileAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_token))
            return false;

        var refreshed = await RefreshProfileFromMeAsync(cancellationToken).ConfigureAwait(false);
        if (refreshed)
            SaveStored();
        return refreshed;
    }

    public void Logout()
    {
        _token = null;
        _userName = null;
        _boundMac = null;
        _memberExpireAt = null;
        _memberType = null;
        _api.SetToken(null);
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
            var data = new
            {
                Token = _token,
                UserName = _userName,
                BoundMac = _boundMac,
                MemberExpireAt = _memberExpireAt,
                MemberType = _memberType
            };
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
            if (root.TryGetProperty("BoundMac", out var m))
                _boundMac = m.GetString();
            if (root.TryGetProperty("MemberExpireAt", out var e))
                _memberExpireAt = e.GetString();
            if (root.TryGetProperty("MemberType", out var mt) && mt.ValueKind == JsonValueKind.Number && mt.TryGetInt32(out var memberType))
                _memberType = memberType;
            if (!string.IsNullOrEmpty(_token))
                _api.SetToken(_token);
        }
        catch { /* ignore */ }
    }

    private async Task<(bool Success, string? Message)> EnsureMacBoundAfterLoginAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_boundMac))
        {
            return (true, null);
        }

        var current = await _api.GetUserMacAsync(cancellationToken).ConfigureAwait(false);
        if (!current.Success)
            return (false, current.Message ?? "查询网卡绑定失败");
        if (!string.IsNullOrWhiteSpace(current.MacAddress))
        {
            _boundMac = current.MacAddress;
            return (true, null);
        }

        var localMacs = _macAddressProvider.GetLocalNormalizedMacAddresses();
        if (localMacs.Count == 0)
            return (false, "未找到可用网卡，无法完成绑定");

        var bind = await _api.PutUserMacAsync(localMacs[0], cancellationToken).ConfigureAwait(false);
        if (!bind.Success)
            return (false, bind.Message ?? "网卡绑定失败");

        _boundMac = bind.MacAddress ?? localMacs[0];
        return (true, null);
    }

    private async Task<bool> RefreshProfileFromMeAsync(CancellationToken cancellationToken)
    {
        var profile = await _api.GetCurrentUserProfileAsync(cancellationToken).ConfigureAwait(false);
        if (!profile.Success)
            return false;

        if (!string.IsNullOrWhiteSpace(profile.UserName))
            _userName = profile.UserName;
        else if (string.IsNullOrWhiteSpace(_userName))
            _userName = profile.Phone;

        _boundMac = profile.MacAddress;
        _memberExpireAt = profile.MemberExpireAt;
        _memberType = profile.MemberType;
        return true;
    }
}
