namespace IpspoolAutomation.Services;

public interface IExchangeScoreSettingsService : ICaptureTargetListPersistence
{
    string FilePath { get; }
}
