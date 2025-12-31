namespace ZapretCLI.Core.Interfaces
{
    public interface ILocalizationService
    {
        string GetString(string key, params object[] args);
        string GetCurrentLanguage();
        Task SetLanguageAsync(string languageCode);
        Dictionary<string, string> GetAvailableLanguages();
        public void Dispose();
    }
}
