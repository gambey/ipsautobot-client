namespace IpspoolAutomation.Services;

public interface IAppConfig
{
    string ApiBaseUrl { get; }
    int ApiTimeoutSeconds { get; }
    string XunjieHelperPath { get; }
    string XunjieMerchantPath { get; }
}
