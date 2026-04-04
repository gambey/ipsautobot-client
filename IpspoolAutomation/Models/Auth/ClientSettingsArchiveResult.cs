namespace IpspoolAutomation.Models.Auth;

public sealed record ClientSettingsArchiveResult(bool Success, string? Message, byte[]? Data);
