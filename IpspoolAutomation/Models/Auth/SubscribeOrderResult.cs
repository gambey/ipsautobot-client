namespace IpspoolAutomation.Models.Auth;

public record SubscribeOrderResult(bool Success, string? OrderId, string? QrCodeUrl, string? Message);
