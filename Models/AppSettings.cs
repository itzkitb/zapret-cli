namespace ZapretCLI.Models
{
    public class AppSettings
    {
        public string GitHubRepositoryUrl { get; set; } = "https://github.com/Flowseal/zapret-discord-youtube";
        public string GitHubApiUrl { get; set; } = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
        public string BinPath { get; set; } = "bin";
        public string ListsPath { get; set; } = "lists";
        public string ProfilesPath { get; set; } = "profiles";
        public TimeSpan ProcessStopTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan ServiceStopTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }
}