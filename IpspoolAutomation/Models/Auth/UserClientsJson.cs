using System.Text.Json.Serialization;

namespace IpspoolAutomation.Models.Auth;

/// <summary>用户多应用切片（api.md：<c>clients.zhiling</c> / <c>clients.yifei</c>）。本客户端固定使用智灵端 <see cref="Zhiling"/>。</summary>
public sealed class UserClientsJson
{
    [JsonPropertyName("zhiling")]
    public UserAppClientSliceJson? Zhiling { get; set; }

    [JsonPropertyName("yifei")]
    public UserAppClientSliceJson? Yifei { get; set; }
}

public sealed class UserAppClientSliceJson
{
    [JsonPropertyName("member_expire_at")]
    public string? MemberExpireAt { get; set; }

    [JsonPropertyName("member_type")]
    public int? MemberType { get; set; }

    [JsonPropertyName("mac_addr")]
    public string? MacAddr { get; set; }
}
