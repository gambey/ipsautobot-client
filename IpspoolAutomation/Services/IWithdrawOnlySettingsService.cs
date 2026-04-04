namespace IpspoolAutomation.Services;

public interface IWithdrawOnlySettingsService : ICaptureTargetListPersistence
{
    string FilePath { get; }
}
