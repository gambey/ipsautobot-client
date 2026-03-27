using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IpspoolAutomation.Models.Auth;

namespace IpspoolAutomation.Services;

public sealed class ApiClient : IApiClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;
    private string? _token;

    /// <summary>
    /// Paths per api.md. ApiBaseUrl in config should be server root (e.g. http://localhost:3000).
    /// </summary>
    private const string PathPublicKey = "api/public-key";
    private const string PathUserLogin = "api/users/login";
    private const string PathUserPassword = "api/users/password";
    private const string PathUserMe = "api/users/me";
    private const string PathUserMac = "api/users/mac";
    private const string PathUserMacVerify = "api/users/mac/verify";

    public ApiClient(HttpClient http)
    {
        _http = http;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public void SetToken(string? token)
    {
        _token = token;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var url = path.StartsWith("http") ? path : new Uri(_http.BaseAddress!, path).ToString();
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(_token))
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
        if (content != null)
            req.Content = content;
        return req;
    }

    private static string? TryGetMessageFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var msg))
                return msg.GetString();
        }
        catch { /* ignore */ }
        return null;
    }

    /// <summary>
    /// GET /api/public-key - PEM public key for RSA password encryption (api.md).
    /// </summary>
    private async Task<string> GetPublicKeyAsync(CancellationToken cancellationToken)
    {
        var req = CreateRequest(HttpMethod.Get, PathPublicKey);
        var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) ?? "";
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var publicKey = await GetPublicKeyAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(publicKey))
                return new LoginResult(false, null, null, "无法获取加密公钥");

            var passwordEncrypted = RsaHelper.Encrypt(request.Password, publicKey);
            var account = (request.Account ?? "").Trim();
            var isPhone = account.Length > 0 && account.All(char.IsDigit);
            var body = new UserLoginRequest
            {
                Phone = isPhone ? account : null,
                Username = isPhone ? null : (account.Length > 0 ? account : null),
                PasswordEncrypted = passwordEncrypted
            };
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            var req = CreateRequest(HttpMethod.Post, PathUserLogin, new StringContent(json, Encoding.UTF8, "application/json"));
            var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new LoginResult(false, null, null, "账号或密码错误");
            if (res.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var errorBody = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var message = TryGetMessageFromJson(errorBody) ?? (string.IsNullOrWhiteSpace(errorBody) ? "请求参数错误(400)" : errorBody);
                return new LoginResult(false, null, null, message);
            }
            res.EnsureSuccessStatusCode();
            var wrapper = await res.Content.ReadFromJsonAsync<UserLoginApiResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            var data = wrapper?.Data;
            if (data == null || string.IsNullOrEmpty(data.Token))
                return new LoginResult(false, null, null, "Invalid response");
            var userName = data.User?.Username ?? data.User?.Phone ?? request.Account;
            return new LoginResult(
                true,
                data.Token,
                userName,
                null,
                data.User?.MemberExpireAt,
                data.User?.MemberType);
        }
        catch (HttpRequestException ex)
        {
            var msg = ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase)
                ? "接口地址不存在(404)，请检查 appsettings.json 中 ApiBaseUrl 是否为服务根地址（如 http://localhost:3000）"
                : ex.Message;
            return new LoginResult(false, null, null, msg);
        }
        catch (TaskCanceledException)
        {
            return new LoginResult(false, null, null, "请求超时");
        }
        catch (CryptographicException ex)
        {
            return new LoginResult(false, null, null, "加密失败: " + ex.Message);
        }
    }

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var req = CreateRequest(HttpMethod.Post, "auth/register", new StringContent(json, Encoding.UTF8, "application/json"));
            var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new RegisterResult(false, body.Length > 200 ? "注册失败" : body);
            }
            res.EnsureSuccessStatusCode();
            var result = await res.Content.ReadFromJsonAsync<RegisterResult>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            return result ?? new RegisterResult(false, "Invalid response");
        }
        catch (HttpRequestException ex)
        {
            return new RegisterResult(false, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new RegisterResult(false, "请求超时");
        }
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var publicKey = await GetPublicKeyAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(publicKey))
                return new ChangePasswordResult(false, "无法获取加密公钥");

            var newPasswordEncrypted = RsaHelper.Encrypt(request.NewPassword, publicKey);
            // server decrypt middleware expects: newPasswordEncrypted
            var body = new { newPasswordEncrypted };
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            var req = CreateRequest(HttpMethod.Put, PathUserPassword, new StringContent(json, Encoding.UTF8, "application/json"));
            var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                // server: { code: 0, message: 'Password updated' }
                var wrapper = await res.Content.ReadFromJsonAsync<ApiMessageResponse>(_jsonOptions, cancellationToken).ConfigureAwait(false);
                return new ChangePasswordResult(true, wrapper?.Message ?? "Password updated");
            }

            var errorBody = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var message = TryGetMessageFromJson(errorBody) ?? (string.IsNullOrWhiteSpace(errorBody) ? $"请求失败({(int)res.StatusCode})" : errorBody);
            return new ChangePasswordResult(false, message);
        }
        catch (HttpRequestException ex)
        {
            var msg = ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase)
                ? "接口地址不存在(404)，请检查 appsettings.json 中 ApiBaseUrl 是否为服务根地址（如 http://localhost:3000）"
                : ex.Message;
            return new ChangePasswordResult(false, msg);
        }
        catch (TaskCanceledException)
        {
            return new ChangePasswordResult(false, "请求超时");
        }
        catch (CryptographicException ex)
        {
            return new ChangePasswordResult(false, "加密失败: " + ex.Message);
        }
    }

    public async Task<SubscribeOrderResult> CreateSubscribeOrderAsync(SubscribeOrderRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var req = CreateRequest(HttpMethod.Post, "subscribe/order", new StringContent(json, Encoding.UTF8, "application/json"));
            var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new SubscribeOrderResult(false, null, null, "请先登录");
            res.EnsureSuccessStatusCode();
            var result = await res.Content.ReadFromJsonAsync<SubscribeOrderResult>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            return result ?? new SubscribeOrderResult(false, null, null, "Invalid response");
        }
        catch (HttpRequestException ex)
        {
            return new SubscribeOrderResult(false, null, null, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new SubscribeOrderResult(false, null, null, "请求超时");
        }
    }

    public async Task<CurrentUserProfileResult> GetCurrentUserProfileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var req = CreateRequest(HttpMethod.Get, PathUserMe);
            var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new CurrentUserProfileResult(false, null, null, null, null, null, "登录已失效，请重新登录");
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var msg = TryGetMessageFromJson(body) ?? $"请求失败({(int)res.StatusCode})";
                return new CurrentUserProfileResult(false, null, null, null, null, null, msg);
            }
            var wrapper = await res.Content.ReadFromJsonAsync<CurrentUserProfileResponseWrapper>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            var user = wrapper?.Data;
            return new CurrentUserProfileResult(
                true,
                user?.Username,
                user?.Phone,
                user?.MacAddr,
                user?.MemberExpireAt,
                user?.MemberType,
                null);
        }
        catch (HttpRequestException ex)
        {
            return new CurrentUserProfileResult(false, null, null, null, null, null, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new CurrentUserProfileResult(false, null, null, null, null, null, "请求超时");
        }
    }

    public async Task<UserMacQueryResult> GetUserMacAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var req = CreateRequest(HttpMethod.Get, PathUserMac);
            var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new UserMacQueryResult(false, null, "登录已失效，请重新登录");
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var msg = TryGetMessageFromJson(body) ?? $"请求失败({(int)res.StatusCode})";
                return new UserMacQueryResult(false, null, msg);
            }
            var wrapper = await res.Content.ReadFromJsonAsync<UserMacResponseWrapper>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            return new UserMacQueryResult(true, wrapper?.Data?.MacAddr, null);
        }
        catch (HttpRequestException ex)
        {
            return new UserMacQueryResult(false, null, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new UserMacQueryResult(false, null, "请求超时");
        }
    }

    public async Task<UserMacUpsertResult> PutUserMacAsync(string macAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { mac_addr = macAddress }, _jsonOptions);
            var req = CreateRequest(HttpMethod.Put, PathUserMac, new StringContent(body, Encoding.UTF8, "application/json"));
            var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var conflictBody = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var conflictMsg = TryGetMessageFromJson(conflictBody) ?? "MAC 已被其他用户绑定";
                return new UserMacUpsertResult(false, null, conflictMsg, IsConflict: true);
            }
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new UserMacUpsertResult(false, null, "登录已失效，请重新登录");
            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var message = TryGetMessageFromJson(errorBody) ?? $"请求失败({(int)res.StatusCode})";
                return new UserMacUpsertResult(false, null, message);
            }
            var wrapper = await res.Content.ReadFromJsonAsync<UserMacResponseWrapper>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            return new UserMacUpsertResult(true, wrapper?.Data?.MacAddr, null);
        }
        catch (HttpRequestException ex)
        {
            return new UserMacUpsertResult(false, null, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new UserMacUpsertResult(false, null, "请求超时");
        }
    }

    public async Task<UserMacVerifyResult> VerifyUserMacAsync(string macAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { mac_addr = macAddress }, _jsonOptions);
            var req = CreateRequest(HttpMethod.Post, PathUserMacVerify, new StringContent(body, Encoding.UTF8, "application/json"));
            var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new UserMacVerifyResult(false, false, "登录已失效，请重新登录");
            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var msg = TryGetMessageFromJson(errorBody) ?? $"请求失败({(int)res.StatusCode})";
                return new UserMacVerifyResult(false, false, msg);
            }
            var wrapper = await res.Content.ReadFromJsonAsync<UserMacVerifyResponseWrapper>(_jsonOptions, cancellationToken).ConfigureAwait(false);
            return new UserMacVerifyResult(true, wrapper?.Data?.Matched == true, null);
        }
        catch (HttpRequestException ex)
        {
            return new UserMacVerifyResult(false, false, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new UserMacVerifyResult(false, false, "请求超时");
        }
    }

    private sealed class ApiMessageResponse
    {
        public int Code { get; set; }
        public string? Message { get; set; }
    }
}
