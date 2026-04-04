using IpspoolAutomation.Models.Capture;

namespace IpspoolAutomation.Services;

/// <summary>Load/save of <see cref="CaptureTargetSettings"/> (shared by capture / 仅兑换不提现 editors).</summary>
public interface ICaptureTargetListPersistence
{
    CaptureTargetSettings Load();
    void Save(CaptureTargetSettings settings);
}
