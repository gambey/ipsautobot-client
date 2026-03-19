namespace IpspoolAutomation.Models.Auth;

public record RegisterRequest(string UserName, string Password, string? Email = null, string? Phone = null);
