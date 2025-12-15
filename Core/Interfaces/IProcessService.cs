using System.Diagnostics;
using ZapretCLI.Models;

namespace ZapretCLI.Core.Interfaces
{
    public interface IProcessService
    {
        Task<Process> StartZapretAsync(ZapretProfile profile);
        Task StopZapretAsync(Process process);
        void SetGameFilter(bool enabled);
        event EventHandler WindivertInitialized;
        event EventHandler<string> OutputLineReceived;
        event EventHandler<string> ErrorLineReceived;
        void Dispose();
    }
}
