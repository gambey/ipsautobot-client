using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Services;

public interface ICaptureTargetSettingsService : ICaptureTargetListPersistence
{
    string SettingsPath { get; }
}

