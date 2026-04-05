namespace IpspoolAutomation.Models.Auth;

/// <summary>从服务器拉取并解压 <c>client_settings.zip</c> 的结果。</summary>
public sealed record ClientSettingsRefreshResult(bool Success, string? Message);
