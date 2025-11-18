using System.Text.Json;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Models;
using ZapretCLI.UI;

namespace ZapretCLI.Configuration
{
    public class ConfigurationService
    {
        private readonly IFileSystemService _fileSystem;
        private readonly string _appPath;

        public ConfigurationService(IFileSystemService fileSystem, string appPath)
        {
            _fileSystem = fileSystem;
            _appPath = appPath;
        }

        public AppSettings GetAppSettings()
        {
            var settingsFile = Path.Combine(_appPath, "appsettings.json");
            if (_fileSystem.FileExists(settingsFile))
            {
                try
                {
                    var json = _fileSystem.ReadAllLinesAsync(settingsFile).GetAwaiter().GetResult();
                    return JsonSerializer.Deserialize<AppSettings>(string.Join("", json)) ?? new AppSettings();
                }
                catch
                {
                    ConsoleUI.WriteLine("[!] The settings file 'appsettings.json' is corrupted. The default configuration has been loaded", ConsoleUI.yellow);
                    return new AppSettings();
                }
            }
            else
            {
                _fileSystem.WriteAllTextAsync(settingsFile, JsonSerializer.Serialize(new AppSettings()));
            }

            return new AppSettings();
        }
    }
}