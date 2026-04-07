using IpspoolAutomation.Models;

namespace IpspoolAutomation.Services;

public interface IAutoOrderingDataService
{
    string FilePath { get; }

    AutoOrderingDataFile Load();
    void Save(AutoOrderingDataFile data);
}
