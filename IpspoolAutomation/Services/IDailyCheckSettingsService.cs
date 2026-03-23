using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public interface IDailyCheckSettingsService
{
    string FilePath { get; }
    DailyCheckSettingsData Load();
    void Save(DailyCheckSettingsData data);
}
