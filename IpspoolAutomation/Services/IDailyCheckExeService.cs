using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Services;

public interface IDailyCheckExeService
{
    string FilePath { get; }
    CaptureTargetSettings Load();
    void Save(CaptureTargetSettings settings);
}
