using Microsoft.Win32;
using System.Globalization;
using System.Text.Json;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.Models;

namespace ZapretCLI.Core.Services
{
    public class ConfigService : IConfigService, IDisposable
    {
        private readonly string _configPath;
        private AppConfig _config;
        private readonly ILoggerService _logger;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
        private FileSystemWatcher _watcher;
        private bool _disposed = false;
        private const int MaxRetryAttempts = 3;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        public event EventHandler OnConfigChanged;

        public ConfigService(string appPath, ILoggerService logs) : this(appPath, logs, true) { }

        private ConfigService(string appPath, ILoggerService logs, bool enableFileWatcher)
        {
            _logger = logs;
            _configPath = Path.Combine(appPath, "appconfig.json");

            // Ensure config directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));

            LoadConfig();

            if (enableFileWatcher)
            {
                InitializeFileWatcher(appPath);
            }
        }

        private void InitializeFileWatcher(string appPath)
        {
            try
            {
                var directory = Path.GetDirectoryName(_configPath);
                _watcher = new FileSystemWatcher(directory, Path.GetFileName(_configPath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _watcher.Changed += OnConfigFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to initialize file watcher: {ex.Message}");
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            _logger.LogDebug("Config file changed externally, reloading...");
            LoadConfig(true);
        }

        private void LoadConfig(bool isExternalChange = false)
        {
            if (!File.Exists(_configPath))
            {
                _logger.LogInformation($"Config file not found at {_configPath}, creating default config");
                _config = new AppConfig();
                SaveConfig();
                return;
            }

            try
            {
                var fileContent = File.ReadAllText(_configPath);
                if (string.IsNullOrWhiteSpace(fileContent))
                {
                    throw new InvalidOperationException("Config file is empty");
                }

                var config = JsonSerializer.Deserialize<AppConfig>(fileContent, _jsonOptions);
                if (config == null)
                {
                    throw new InvalidOperationException("Failed to deserialize config");
                }

                // Validate loaded configuration
                ValidateConfig(config);

                _config = config;
                _logger.LogInformation($"Config loaded successfully from {_configPath}");

                if (isExternalChange)
                {
                    OnConfigChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load config from {_configPath}: {ex.Message}. Using default config.", ex);
                _config = new AppConfig();
                // Don't recursively call SaveConfig here to avoid infinite loop
            }
        }

        private void ValidateConfig(AppConfig config)
        {
            // Ensure valid language code
            if (!string.IsNullOrEmpty(config.Language) &&
                !new[] { "en", "ru" }.Contains(config.Language.ToLower()))
            {
                config.Language = null;
            }

            // Ensure valid timeouts
            if (config.ProcessStopTimeout.TotalSeconds <= 0)
            {
                config.ProcessStopTimeout = TimeSpan.FromSeconds(10);
            }

            if (config.ServiceStopTimeout.TotalSeconds <= 0)
            {
                config.ServiceStopTimeout = TimeSpan.FromSeconds(10);
            }
        }

        public AppConfig GetConfig() => _config;

        public async Task SaveConfigAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
                await SaveConfigInternalAsync();
                OnConfigChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public void SaveConfig()
        {
            _fileLock.Wait();
            try
            {
                SaveConfigInternal();
                OnConfigChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private void SaveConfigInternal()
        {
            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    // Create backup before overwriting
                    if (File.Exists(_configPath))
                    {
                        var backupPath = $"{_configPath}.bak";
                        File.Copy(_configPath, backupPath, true);
                    }

                    var json = JsonSerializer.Serialize(_config, _jsonOptions);
                    File.WriteAllText(_configPath, json);
                    _logger.LogInformation($"Config saved successfully to {_configPath}");
                    return;
                }
                catch (IOException ex) when (attempt < MaxRetryAttempts)
                {
                    _logger.LogWarning($"Attempt {attempt} failed to save config: {ex.Message}");
                    Thread.Sleep(100 * attempt);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to save config after {attempt} attempts: {ex.Message}", ex);
                    throw;
                }
            }
        }

        private async Task SaveConfigInternalAsync()
        {
            for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    // Create backup before overwriting
                    if (File.Exists(_configPath))
                    {
                        var backupPath = $"{_configPath}.bak";
                        await File.WriteAllTextAsync(backupPath, await File.ReadAllTextAsync(_configPath));
                    }

                    var json = JsonSerializer.Serialize(_config, _jsonOptions);
                    await File.WriteAllTextAsync(_configPath, json);
                    _logger.LogInformation($"Config saved successfully to {_configPath}");
                    return;
                }
                catch (IOException ex) when (attempt < MaxRetryAttempts)
                {
                    _logger.LogWarning($"Attempt {attempt} failed to save config: {ex.Message}");
                    await Task.Delay(100 * attempt);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to save config after {attempt} attempts: {ex.Message}", ex);
                    throw;
                }
            }
        }

        private void UpdateRegistryAutoStart(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    var appName = "ZapretCLI";
                    if (enabled)
                    {
                        var exePath = Environment.ProcessPath;
                        var arguments = "--autostart-service";
                        key.SetValue(appName, $"\"{exePath}\" {arguments}");
                        _logger.LogInformation($"Added application to Windows startup registry at path: {exePath}");
                    }
                    else
                    {
                        if (key.GetValue(appName) != null)
                        {
                            key.DeleteValue(appName);
                            _logger.LogInformation($"Removed application from Windows startup registry");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating registry auto-start: {ex.Message}", ex);
            }
        }

        private bool IsAutoStartEnabledInRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", false))
                {
                    var appName = "ZapretCLI";
                    return key?.GetValue(appName) != null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking registry auto-start: {ex.Message}", ex);
                return false;
            }
        }

        public void SyncAutoStartWithRegistry()
        {
            var configAutoStart = _config.AutoStart;
            var registryAutoStart = IsAutoStartEnabledInRegistry();

            if (configAutoStart != registryAutoStart)
            {
                _logger.LogInformation($"Auto-start mismatch detected. Config: {configAutoStart}, Registry: {registryAutoStart}. Syncing...");
                UpdateRegistryAutoStart(configAutoStart);
            }
        }

        public async Task UpdateAutoStartAsync(bool enabled, string profileName = null)
        {
            _config.AutoStart = enabled;
            _config.AutoStartProfile = profileName;
            await SaveConfigAsync();
            UpdateRegistryAutoStart(enabled);
        }

        public async Task UpdateGameFilterAsync(bool enabled)
        {
            _config.GameFilterEnabled = enabled;
            await SaveConfigAsync();
        }

        public async Task SetLanguageAsync(string languageCode)
        {
            if (!string.IsNullOrEmpty(languageCode) &&
                !new[] { "en", "ru" }.Contains(languageCode.ToLower()))
            {
                _logger.LogWarning($"Attempted to set invalid language code: {languageCode}");
                throw new ArgumentException($"Invalid language code: {languageCode}");
            }

            _config.Language = languageCode;
            await SaveConfigAsync();
        }

        // Synchronous versions for backward compatibility
        public void UpdateAutoStart(bool enabled, string profileName = null)
        {
            _config.AutoStart = enabled;
            _config.AutoStartProfile = profileName;
            SaveConfig();
            UpdateRegistryAutoStart(enabled);
        }

        public void UpdateGameFilter(bool enabled)
        {
            _config.GameFilterEnabled = enabled;
            SaveConfig();
        }

        public void SetLanguage(string languageCode)
        {
            if (!string.IsNullOrEmpty(languageCode) &&
                !new[] { "en", "ru" }.Contains(languageCode.ToLower()))
            {
                _logger.LogWarning($"Attempted to set invalid language code: {languageCode}");
                throw new ArgumentException($"Invalid language code: {languageCode}");
            }

            _config.Language = languageCode;
            SaveConfig();
        }

        public string GetLanguage()
        {
            return string.IsNullOrEmpty(_config.Language)
                ? (CultureInfo.CurrentCulture.TwoLetterISOLanguageName.ToLower() == "ru" ? "ru" : "en")
                : _config.Language;
        }

        public void ReloadConfig()
        {
            LoadConfig();
        }

        public async Task ReloadConfigAsync()
        {
            await _fileLock.WaitAsync();
            try
            {
                LoadConfig();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _fileLock?.Dispose();
                    _watcher?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
