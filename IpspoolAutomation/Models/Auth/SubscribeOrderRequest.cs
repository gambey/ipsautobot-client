namespace IpspoolAutomation.Models.Auth;

public record SubscribeOrderRequest(string PlanId, decimal Amount, string? ReturnUrl = null);
