using System.Text.Json.Serialization;

namespace IpspoolAutomation.Models.Auth;

/// <summary>
/// Server wraps success in { code: 0, data: { token, user } } (ips_autobot_svr userController.login).
/// </summary>
public sealed class UserLoginApiResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public UserLoginResponse? Data { get; set; }
}

/// <summary>
/// Inner data for POST /api/users/login - token and user info.
/// </summary>
public sealed class UserLoginResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("user")]
    public UserInfo? User { get; set; }
}

public sealed class UserInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("member_expire_at")]
    public string? MemberExpireAt { get; set; }

    [JsonPropertyName("member_type")]
    public int? MemberType { get; set; }
}
