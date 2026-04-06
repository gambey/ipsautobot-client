using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public interface IAutoAcceptOrderSettingsService
{
    string FilePath { get; }
    AutoAcceptOrderSettingsData Load();
    void Save(AutoAcceptOrderSettingsData data);
}
