namespace IpspoolAutomation.Models.Auth;

/// <summary>
/// PUT /api/users/password request (client side uses plain password; ApiClient handles RSA encryption).
/// </summary>
public sealed class ChangePasswordRequest
{
    public string NewPassword { get; set; } = "";
}

