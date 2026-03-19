namespace IpspoolAutomation.Models.Auth;

public record LoginResult(bool Success, string? Token, string? UserName, string? Message);
