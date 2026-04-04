using System.IO;

namespace IpspoolAutomation.Services;

internal static class ClientSettingsPaths
{
    /// <summary>Legacy folder (files before <c>client_settings</c> subfolder).</summary>
    internal static string LegacyAppDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IpspoolAutomation");

    internal static string ClientSettingsDirectory { get; } = Path.Combine(LegacyAppDirectory, "client_settings");

    internal static string? ResolveExistingPath(string primary, string legacy)
    {
        if (File.Exists(primary))
            return primary;
        if (File.Exists(legacy))
            return legacy;
        return null;
    }
}
