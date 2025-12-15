using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZapretCLI.Models
{
    public class AppConfig
    {
        public string GitHubRepositoryUrl { get; set; } = "https://github.com/Flowseal/zapret-discord-youtube";
        public string GitHubApiUrl { get; set; } = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
        public string BinPath { get; set; } = "bin";
        public string ListsPath { get; set; } = "lists";
        public string ProfilesPath { get; set; } = "profiles";
        public string Language { get; set; } = null;
        public TimeSpan ProcessStopTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan ServiceStopTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public bool AutoStart { get; set; } = false;
        public string AutoStartProfile { get; set; } = null;
        public bool GameFilterEnabled { get; set; } = false;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
