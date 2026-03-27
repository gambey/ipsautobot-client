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

    [JsonPropertyName("mac_addr")]
    public string? MacAddr { get; set; }

    [JsonPropertyName("member_expire_at")]
    public string? MemberExpireAt { get; set; }

    [JsonPropertyName("member_type")]
    public int? MemberType { get; set; }
}
