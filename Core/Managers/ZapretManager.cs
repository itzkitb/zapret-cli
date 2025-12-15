using Spectre.Console;
using System.Diagnostics;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
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
                await _processService.StopZapretAsync(_zapretProcess);
                _zapretProcess = null;
                if (showLogs) AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("zapret_stop")}[/]");
            }
            catch (Exception ex)
            {
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
                        .PageSize(10)
                        .MoreChoicesText($"[{ConsoleUI.greyName}]({_localizationService.GetString("other_options")})[/]")
                        .HighlightStyle(new Style(Color.PaleGreen1))
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

        public async Task<bool> StartAsync(bool showLogs = true)
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

                _zapretProcess = await _processService.StartZapretAsync(_currentProfile);

                // Waiting for initialization or timeout
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout));

                // Unsubscribing from the event
                _processService.WindivertInitialized -= handler;

                if (completedTask != tcs.Task)
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{String.Format(_localizationService.GetString("zapret_start_timeout"), timeout.Seconds)}[/]");
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

        public async Task TestProfilesAsync()
        {
            var initialProfile = _currentProfile;
            if (IsRunning()) await StopAsync(false);

            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("profile_testing_title")}[/]");
            AnsiConsole.MarkupLine($"[{ConsoleUI.greyName}]{_localizationService.GetString("close_apps_warning")}[/]\n");

            var domain = AnsiConsole.Prompt(new TextPrompt<string>($"[{ConsoleUI.greenName}]{_localizationService.GetString("test_domain_ask")}[/] [{ConsoleUI.greyName}](example.com)[/]:"));

            if (string.IsNullOrWhiteSpace(domain))
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("empty_domain")}[/]");
            }
            bool isBlocked = await IsDomainBlocked(domain);

            if (!isBlocked)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{_localizationService.GetString("domain_accessible_warning")}[/]", domain);
                if (!AnsiConsole.Confirm(_localizationService.GetString("continue_anyway")))
                    return;
            }

            await LoadAvailableProfilesAsync();
            if (!_availableProfiles.Any())
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("no_profiles_to_test")}[/]");
                return;
            }

            var results = new List<(string ProfileName, bool Success, string Message)>();
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            string testUrl = $"https://{domain}";

            await ListManager.AddDomainToFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists", "list-general.txt"), domain, _localizationService, _logger);

            AnsiConsole.MarkupLine($"\n[{ConsoleUI.greyName}]{_localizationService.GetString("starting_tests")}[/]", $"[/][{ConsoleUI.greenName}]{domain}[/][{ConsoleUI.greyName}]", $"[/][{ConsoleUI.greenName}]{_availableProfiles.Count}[/][{ConsoleUI.greyName}]");
            AnsiConsole.MarkupLine($"[{ConsoleUI.darkGreyName}]--------------------[/]");

            foreach (var profile in _availableProfiles)
            {
                AnsiConsole.MarkupLine($"\n[{ConsoleUI.orangeName}]{_localizationService.GetString("testing_profile")}[/]", profile.Name);
                if (IsRunning()) await StopAsync(false);
                _currentProfile = profile;

                try
                {
                    if (!await StartAsync(false))
                    {
                        results.Add((profile.Name, false, _localizationService.GetString("init_failed")));
                        continue;
                    }

                    await Task.Delay(500);
                    bool success = false;
                    string message = "";

                    try
                    {
                        var response = await httpClient.GetAsync(testUrl, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

                        if (response.IsSuccessStatusCode)
                        {
                            success = true;
                            message = $"{String.Format(_localizationService.GetString("http_success"), response.StatusCode)}";
                            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("success")}: {{0}}[/]", message);
                        }
                        else
                        {
                            message = $"{String.Format(_localizationService.GetString("http_fail"), response.StatusCode)}";
                            AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("failed")}: {{0}}[/]", message);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        message = _localizationService.GetString("conn_timeout");
                        AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("failed")}: {{0}}[/]", message);
                    }
                    catch (Exception ex)
                    {
                        message = $"{_localizationService.GetString("error_occurred")}: {ex.Message}";
                        AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("failed")}: {{0}}[/]", message);
                    }

                    results.Add((profile.Name, success, message));
                }
                finally
                {
                    if (IsRunning()) await StopAsync(false);
                    await Task.Delay(1000);
                }
            }

            AnsiConsole.MarkupLine($"\n[{ConsoleUI.greyName}]{_localizationService.GetString("test_results")}[/]");
            AnsiConsole.MarkupLine($"[{ConsoleUI.darkGreyName}]--------------------[/]");

            var successfulProfiles = results.Where(r => r.Success).ToList();
            if (successfulProfiles.Any())
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("profiles_bypassed")}[/]\n", successfulProfiles.Count);
                foreach (var result in successfulProfiles)
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}] - {result.ProfileName}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("no_profiles_bypassed")}[/]");
            }

            _currentProfile = initialProfile;
            AnsiConsole.MarkupLine($"\n[{ConsoleUI.darkGreyName}]{_localizationService.GetString("press_any_key")}[/]");
            Console.ReadKey(true);
        }

        private async Task<bool> IsDomainBlocked(string domain)
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var testUrl = $"https://{domain}";
                var response = await httpClient.GetAsync(testUrl, new CancellationTokenSource(TimeSpan.FromSeconds(7)).Token);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}