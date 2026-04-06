using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public interface IStopPlatformOrderingSettingsService
{
    string FilePath { get; }
    StopPlatformOrderingSettingsData Load();
    void Save(StopPlatformOrderingSettingsData data);
}
