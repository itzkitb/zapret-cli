namespace ZapretCLI.Core.Interfaces
{
    public interface IUpdateService
    {
        Task CheckForUpdatesAsync();
        Task DownloadLatestReleaseAsync();
        Task CheckForCliUpdatesAsync();
        Task UpdateCliAsync();
    }
}