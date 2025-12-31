using Spectre.Console;
using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.Core.Services;
using ZapretCLI.Models;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Managers
{
    public class ZapretManager : IZapretManager
    {
        private readonly string _appPath;
        private Process _zapretProcess;
        private readonly IProcessService _processService;
        private readonly IStatusService _statusService;
        private readonly IProfileService _profileService;
        private readonly ILocalizationService _localizationService;
        private readonly IConfigService _configService;
        private readonly ILoggerService _logger;

        private ZapretProfile _currentProfile;
        private List<ZapretProfile> _availableProfiles = new List<ZapretProfile>();
        private bool _gameFilterEnabled = false;

        public ZapretManager(string appPath, IProcessService prs, IStatusService ss, IProfileService ps, ILocalizationService ls, IConfigService cs, ILoggerService logs)
        {
            _appPath = appPath;

            _processService = prs;
            _statusService = ss;
            _profileService = ps;
            _localizationService = ls;
            _configService = cs;
            _logger = logs;

            _processService.OutputLineReceived += (sender, line) => _statusService.ProcessOutputLine(line);
            _processService.ErrorLineReceived += (sender, line) => _statusService.ProcessOutputLine(line);

            _gameFilterEnabled = _configService.GetConfig().GameFilterEnabled;
        }

        public async Task InitializeAsync()
        {
            await _statusService.Initialize();
        }

        public async Task StopAsync(bool showLogs = true)
        {
            if (!IsRunning())
            {
                return;
            }

            try
            {
                _logger.LogError("Stopping Zapret...");
                await _processService.StopZapretAsync(_zapretProcess);
                _zapretProcess = null;
                if (showLogs) AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("zapret_stop")}[/]");
            }
            catch (Exception ex)
            {
                _logger.LogError("Zapret stop failed", ex);
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{String.Format(_localizationService.GetString("zapret_stop_fail"), ex.Message)}[/]");
            }
        }

        public async Task ShowStatusAsync()
        {
            var status = IsRunning() ? _localizationService.GetString("running") : _localizationService.GetString("stopped");
            var color = IsRunning() ? ConsoleUI.greenName : ConsoleUI.redName;

            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("service_status")}[/]\n");
            AnsiConsole.MarkupLine($"  {_localizationService.GetString("status")}: [{color}]{status}[/] [{ConsoleUI.greyName}](PID: {_zapretProcess?.Id ?? 0})[/]");
            AnsiConsole.MarkupLine($"  {_localizationService.GetString("working_directory")}: [{ConsoleUI.greenName}]'{_appPath}'[/]");

            await _statusService.DisplayStatusAsync();
            ShowProfileInfo();

            AnsiConsole.MarkupLine($"\n[{ConsoleUI.darkGreyName}]{_localizationService.GetString("press_any_key")}[/]");
            Console.ReadKey(true);
        }

        public async Task LoadAvailableProfilesAsync()
        {
            if (_availableProfiles.Count == 0)
            {
                _availableProfiles = await _profileService.GetAvailableProfilesAsync();
                if (_availableProfiles.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("no_profiles")}[/]");
                }
            }

            return;
        }

        public async Task SelectProfileAsync(string profileName = null)
        {
            _logger.LogError($"Selecting profile... profileName=\"{profileName}\"");
            if (_availableProfiles.Count == 0)
                await LoadAvailableProfilesAsync();

            if (_availableProfiles.Count == 0)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("updating_no_profiles")}[/]");
                await Program.UpdateProfiles();
                await LoadAvailableProfilesAsync();
            }

            if (_availableProfiles.Count == 0)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("no_profiles_after_update")}[/]");
                return;
            }

            if (string.IsNullOrWhiteSpace(profileName))
            {
                ConsoleUI.Clear();

                var profileNames = _availableProfiles.Select(p => p.Name).ToList();
                var selectedProfile = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[{ConsoleUI.greenName}]{_localizationService.GetString("available_profiles")}[/]\n[{ConsoleUI.darkGreyName}]{_localizationService.GetString("navigation")}[/]")
                        .AddChoices(profileNames)
                        .MoreChoicesText($"[{ConsoleUI.greyName}]({_localizationService.GetString("other_options")})[/]")
                        .HighlightStyle(new Style(Color.PaleGreen1))
                        .WrapAround(true)
                        .PageSize(15)
                        .EnableSearch()
                        .SearchPlaceholderText(_localizationService.GetString("search_placeholder"))
                );

                _currentProfile = _availableProfiles.FirstOrDefault(p =>
                    p.Name.Equals(selectedProfile, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                _currentProfile = _availableProfiles.FirstOrDefault(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

                if (_currentProfile == null)
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{String.Format(_localizationService.GetString("profile_not_found"), profileName)}[/]");
                    return;
                }
            }
        }

        public async Task<bool> StartAsync(bool showLogs = true, bool filterAllIp = false)
        {
            if (_currentProfile == null)
            {
                await SelectProfileAsync();
                if (_currentProfile == null) return false;
            }

            if (IsRunning())
            {
                return true;
            }

            try
            {
                var tcs = new TaskCompletionSource<bool>();
                var timeout = TimeSpan.FromSeconds(5);
                var cancellationTokenSource = new CancellationTokenSource(timeout);

                EventHandler handler = (sender, args) => tcs.TrySetResult(true);

                // Subscribing to the initialization event
                _processService.WindivertInitialized += handler;

                _zapretProcess = await _processService.StartZapretAsync(_currentProfile, filterAllIp);

                // Waiting for initialization or timeout
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout));

                // Unsubscribing from the event
                _processService.WindivertInitialized -= handler;

                if (completedTask != tcs.Task)
                {
                    _logger.LogWarning($"Profile start failed: Timeout");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]<{_currentProfile.Name}> {String.Format(_localizationService.GetString("zapret_start_timeout"), timeout.Seconds)}[/]");
                    await StopAsync();
                    return false;
                }

                if (showLogs) {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{String.Format(_localizationService.GetString("zapret_start"), _currentProfile.Name)}[/]");
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Zapret start failed", ex);
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{String.Format(_localizationService.GetString("zapret_start_fail"), ex.Message)}[/]");
                return false;
            }
        }

        public void ShowProfileInfo()
        {
            if (_currentProfile == null)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("profile_not_selected")}[/]");
                return;
            }

            AnsiConsole.MarkupLine($"  {_localizationService.GetString("profile")}: [{ConsoleUI.greenName}]'{_currentProfile.Name}'[/]");
            AnsiConsole.MarkupLine($"  {_localizationService.GetString("description")}: [{ConsoleUI.greyName}]'{_currentProfile.Description}'[/]");
        }

        public void ToggleGameFilter()
        {
            _logger.LogError($"GAME FILTER TOGGLED gameFilterEnabled={_gameFilterEnabled}");
            _gameFilterEnabled = !_gameFilterEnabled;
            _processService.SetGameFilter(_gameFilterEnabled);
            _configService.UpdateGameFilter(_gameFilterEnabled);
        }

        public bool IsRunning()
        {
            return _zapretProcess != null && !_zapretProcess.HasExited;
        }

        public async Task<ListStats> GetStatusStatsAsync()
        {
            return await _statusService.GetStatusStatsAsync();
        }

        public bool IsGameFilterEnabled() => _gameFilterEnabled;

        public ZapretProfile GetCurrentProfile()
        {
            return _currentProfile;
        }
    }
}