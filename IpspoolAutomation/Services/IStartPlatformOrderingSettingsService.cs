using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public interface IStartPlatformOrderingSettingsService
{
    string FilePath { get; }
    StartPlatformOrderingSettingsData Load();
    void Save(StartPlatformOrderingSettingsData data);
}
