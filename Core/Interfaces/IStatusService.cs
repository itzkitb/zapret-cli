using ZapretCLI.Models;

namespace ZapretCLI.Core.Interfaces
{
    public interface IStatusService
    {
        Task<ListStats> GetStatusStatsAsync();
        Task DisplayStatusAsync();
        void ProcessOutputLine(string line);
        Task Initialize();
    }
}