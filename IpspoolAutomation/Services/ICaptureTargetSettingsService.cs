using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Services;

public interface ICaptureTargetSettingsService
{
    string SettingsPath { get; }
    CaptureTargetSettings Load();
    void Save(CaptureTargetSettings settings);
}

