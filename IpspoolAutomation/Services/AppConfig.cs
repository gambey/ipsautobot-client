using System.Text.Json;
using System.IO; 
namespace IpspoolAutomation.Services;

public sealed class AppConfig : IAppConfig
{
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
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                _baseUrl = root.TryGetProperty("ApiBaseUrl", out var u) ? u.GetString() ?? "https://your-server.com/api" : "https://your-server.com/api";
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
            }
        }
        else
        {
            _baseUrl = "https://your-server.com/api";
            _timeout = 30;
            _helperPath = "";
            _merchantPath = "";
        }
    }

    public string ApiBaseUrl => _baseUrl;
    public int ApiTimeoutSeconds => _timeout;
    public string XunjieHelperPath => _helperPath;
    public string XunjieMerchantPath => _merchantPath;
}
