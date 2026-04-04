using System.Text.Json;

namespace IpspoolAutomation.Services;

internal static class ClientSettingsJson
{
    internal static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    internal static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = true
    };
}
