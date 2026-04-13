namespace IpspoolAutomation.Models.Auth;

/// <summary>
/// Request body for POST /api/users/login。Server accepts phone or username（api.md）。
/// </summary>
public sealed class UserLoginRequest
{
    public string? Phone { get; set; }
    public string? Username { get; set; }
    public string PasswordEncrypted { get; set; } = "";
}
