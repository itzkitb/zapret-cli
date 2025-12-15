using ZapretCLI.Models;

namespace ZapretCLI.Core.Interfaces
{
    public interface IConfigService
    {
        AppConfig GetConfig();
        void SaveConfig();
        Task SaveConfigAsync();
        void UpdateAutoStart(bool enabled, string profileName = null);
        Task UpdateAutoStartAsync(bool enabled, string profileName = null);
        void UpdateGameFilter(bool enabled);
        Task UpdateGameFilterAsync(bool enabled);
        void SetLanguage(string languageCode);
        Task SetLanguageAsync(string languageCode);
        string GetLanguage();
        event EventHandler OnConfigChanged;
        void ReloadConfig();
        Task ReloadConfigAsync();
        void Dispose();
        void SyncAutoStartWithRegistry();
    }
}