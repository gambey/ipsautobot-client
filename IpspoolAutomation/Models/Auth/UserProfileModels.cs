using System.Text.Json.Serialization;

namespace IpspoolAutomation.Models.Auth;

public sealed record CurrentUserProfileResult(
    bool Success,
    string? UserName,
    string? Phone,
    string? MacAddress,
    string? MemberExpireAt,
    int? MemberType,
    string? Message);

internal sealed class CurrentUserProfileResponseWrapper
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public CurrentUserProfileData? Data { get; set; }
}

internal sealed class CurrentUserProfileData
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>旧版顶层；新版见 <see cref="Clients"/>。</summary>
    [JsonPropertyName("mac_addr")]
    public string? MacAddr { get; set; }

    [JsonPropertyName("member_expire_at")]
    public string? MemberExpireAt { get; set; }

    [JsonPropertyName("member_type")]
    public int? MemberType { get; set; }

    [JsonPropertyName("clients")]
    public UserClientsJson? Clients { get; set; }
}
