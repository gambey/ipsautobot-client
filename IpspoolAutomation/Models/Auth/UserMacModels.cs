using System.Text.Json.Serialization;

namespace IpspoolAutomation.Models.Auth;

public sealed record UserMacQueryResult(bool Success, string? MacAddress, string? Message);
public sealed record UserMacUpsertResult(bool Success, string? MacAddress, string? Message, bool IsConflict = false);
public sealed record UserMacVerifyResult(bool Success, bool Matched, string? Message);

internal sealed class UserMacResponseWrapper
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public UserMacResponseData? Data { get; set; }
}

internal sealed class UserMacResponseData
{
    [JsonPropertyName("mac_addr")]
    public string? MacAddr { get; set; }
}

internal sealed class UserMacVerifyResponseWrapper
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public UserMacVerifyData? Data { get; set; }
}

internal sealed class UserMacVerifyData
{
    [JsonPropertyName("matched")]
    public bool Matched { get; set; }
}
