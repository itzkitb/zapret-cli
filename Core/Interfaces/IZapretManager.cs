using ZapretCLI.Models;

namespace ZapretCLI.Core.Interfaces
{
    public interface IZapretManager
    {
        Task InitializeAsync();
        Task<bool> StartAsync(bool showLogs = true);
        Task StopAsync(bool showLogs = true);
        Task ShowStatusAsync();
        Task SelectProfileAsync(string profileName = null);
        void ShowProfileInfo();
        Task LoadAvailableProfilesAsync();
        ZapretProfile GetCurrentProfile();
        void ToggleGameFilter();
        bool IsGameFilterEnabled();
        bool IsRunning();
        Task<ListStats> GetStatusStatsAsync();
        Task TestProfilesAsync();
    }
}