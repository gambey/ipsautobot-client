using System.Text.Json;
using System.IO; 
namespace IpspoolAutomation.Services;

public sealed class AppConfig : IAppConfig
{
    private readonly string _appVersion;
    private readonly string _baseUrl;
    private readonly int _timeout;
    private readonly string _helperPath;
    private readonly string _merchantPath;

    public AppConfig()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                var root = doc.RootElement;
                _appVersion = root.TryGetProperty("AppVersion", out var av) ? (av.GetString() ?? "v0.0.1") : "v0.0.1";
                var env = root.TryGetProperty("ApiEnvironment", out var envValue)
                    ? (envValue.GetString() ?? "").Trim().ToLowerInvariant()
                    : "";
                var localUrl = root.TryGetProperty("ApiBaseUrlLocal", out var localValue) ? localValue.GetString() : null;
                var prodUrl = root.TryGetProperty("ApiBaseUrlProd", out var prodValue) ? prodValue.GetString() : null;
                var legacyUrl = root.TryGetProperty("ApiBaseUrl", out var u) ? u.GetString() : null;
                _baseUrl = env switch
                {
                    "local" when !string.IsNullOrWhiteSpace(localUrl) => localUrl!,
                    "prod" or "production" when !string.IsNullOrWhiteSpace(prodUrl) => prodUrl!,
                    _ => !string.IsNullOrWhiteSpace(legacyUrl) ? legacyUrl! : "https://your-server.com/api"
                };
                _timeout = root.TryGetProperty("ApiTimeoutSeconds", out var t) ? t.GetInt32() : 30;
                _helperPath = root.TryGetProperty("XunjieHelperPath", out var h) ? h.GetString() ?? "" : "";
                _merchantPath = root.TryGetProperty("XunjieMerchantPath", out var m) ? m.GetString() ?? "" : "";
            }
            catch
            {
                _baseUrl = "https://your-server.com/api";
                _timeout = 30;
                _helperPath = "";
                _merchantPath = "";
                _appVersion = "v0.0.1";
            }
        }
        else
        {
            _baseUrl = "https://your-server.com/api";
            _timeout = 30;
            _helperPath = "";
            _merchantPath = "";
            _appVersion = "v0.0.1";
        }
    }

    public string AppVersion => _appVersion;
    public string ApiBaseUrl => _baseUrl;
    public int ApiTimeoutSeconds => _timeout;
    public string XunjieHelperPath => _helperPath;
    public string XunjieMerchantPath => _merchantPath;
}
