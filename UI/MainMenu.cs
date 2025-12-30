using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.Collections.Concurrent;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.Core.Managers;
using ZapretCLI.Models;

namespace ZapretCLI.UI
{
    public static class MainMenu
    {
        private static IZapretManager _zapretManager;
        private static IUpdateService _updateService;
        private static IConfigService _configService;
        private static IProfileService _profileService;
        private static ILocalizationService _localizationService;
        private static bool _exitRequested = false;
        private static ILoggerService _logger;

        public static readonly Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        private static readonly string Logo = @" _____                 _   
|__   |___ ___ ___ ___| |_ 
|   __| .'| . |  _| -_|  _|
|_____|__,|  _|_| |___|_|  
          |_|              
";

        private const string GeneralListFileName = "list-general.txt";
        private const string ExcludeListFileName = "list-exclude.txt";

        private static readonly ConcurrentDictionary<string, Action> _menuActions = new ConcurrentDictionary<string, Action>();
        private static readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public static async Task ShowAsync(IServiceProvider serviceProvider)
        {
            InitializeMenuActions();
            _zapretManager = serviceProvider.GetRequiredService<IZapretManager>();
            _updateService = serviceProvider.GetRequiredService<IUpdateService>();
            _configService = serviceProvider.GetRequiredService<IConfigService>();
            _profileService = serviceProvider.GetRequiredService<IProfileService>();
            _localizationService = serviceProvider.GetRequiredService<ILocalizationService>();
            _logger = serviceProvider.GetRequiredService<ILoggerService>();

            // Проверка автозапуска
            var config = _configService.GetConfig();
            if (config.AutoStart && !string.IsNullOrEmpty(config.AutoStartProfile))
            {
                try
                {
                    await _zapretManager.SelectProfileAsync(config.AutoStartProfile);
                    await _zapretManager.StartAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Auto-start failed: {ex.Message}", ex);
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]Auto-start failed: {ex.Message}[/]");
                }
            }

            while (!_exitRequested && !_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    ConsoleUI.Clear();

                    var choice = AnsiConsole.Prompt<string>(
                        new SelectionPrompt<string>()
                            .Title<string>($"[{ConsoleUI.greenName}]{Logo}[/]\n[{ConsoleUI.greyName}]{String.Format(_localizationService.GetString("title"), version)}[/]\n[{ConsoleUI.darkGreyName}]{_localizationService.GetString("navigation")}[/]")
                            .AddChoices(new[] {
                        _zapretManager.IsRunning() ? _localizationService.GetString("menu_stop") : _localizationService.GetString("menu_start"),
                        _localizationService.GetString("menu_status"),
                        _localizationService.GetString("menu_edit"),
                        _localizationService.GetString("menu_update"),
                        _localizationService.GetString("menu_diagnostics"),
                        _localizationService.GetString("menu_test"),
                        _localizationService.GetString("menu_settings"),
                        _localizationService.GetString("menu_exit")
                            })
                            .PageSize(10)
                            .MoreChoicesText($"[{ConsoleUI.greyName}]({_localizationService.GetString("other_options")})[/]")
                            .HighlightStyle(new Style(Color.PaleGreen1))
                            .WrapAround(true)
                    );

                    await HandleChoice(choice);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Menu navigation error: {ex.Message}", ex);
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]An error occurred: {ex.Message}[/]");
                    await Task.Delay(1500);
                }
            }
        }

        private static void InitializeMenuActions()
        {
            _menuActions[_localizationService?.GetString("restart_as_admin_fail") ?? "restart_as_admin_fail"] = () => { };
            _menuActions[_localizationService?.GetString("menu_exit") ?? "menu_exit"] = async () =>
            {
                await HandleExit();
            };
        }

        private static async Task HandleChoice(string choice)
        {
            _logger.LogInformation($"Menu item \"{choice}\" is selected");
            try
            {
                switch (choice)
                {
                    case var c when c == (_zapretManager.IsRunning() ? _localizationService.GetString("menu_stop") : _localizationService.GetString("menu_start")):
                        await HandleStartStopService();
                        break;
                    case var c when c == _localizationService.GetString("menu_status"):
                        await HandleServiceStatus();
                        break;
                    case var c when c == _localizationService.GetString("menu_edit"):
                        await HandleEditLists();
                        break;
                    case var c when c == _localizationService.GetString("menu_update"):
                        await HandleUpdate();
                        break;
                    case var c when c == _localizationService.GetString("menu_test"):
                        await HandleTestProfiles();
                        break;
                    case var c when c == _localizationService.GetString("menu_settings"):
                        await HandleSettings();
                        break;
                    case var c when c == _localizationService.GetString("menu_exit"):
                        await HandleExit();
                        break;
                    case var c when c == _localizationService.GetString("menu_diagnostics"):
                        await HandleDiagnostics();
                        break;
                    default:
                        _logger.LogWarning($"Unknown menu choice: {choice}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling menu choice \"{choice}\": {ex.Message}", ex);
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]Error: {ex.Message}[/]");
                await Task.Delay(1500);
            }
        }

        private static async Task HandleDiagnostics()
        {
            try
            {
                var diagnosticsService = Program.ServiceProvider.GetRequiredService<IDiagnosticsService>();
                await diagnosticsService.RunDiagnosticsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Diagnostics failed: {ex.Message}", ex);
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]Diagnostics failed: {ex.Message}[/]");
                await Task.Delay(1500);
            }
        }

        private static async Task HandleTestProfiles()
        {
            try
            {
                await _zapretManager.TestProfilesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Profile testing failed: {ex.Message}", ex);
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]Testing failed: {ex.Message}[/]");
                await Task.Delay(1500);
            }
        }

        private static async Task HandleStartStopService()
        {
            if (_zapretManager.IsRunning())
            {
                await _zapretManager.StopAsync();
                return;
            }

            await _zapretManager.SelectProfileAsync();
            if (_zapretManager.GetCurrentProfile() == null)
            {
                return;
            }

            await _zapretManager.StartAsync();
        }

        private static async Task HandleServiceStatus()
        {
            await _zapretManager.ShowStatusAsync();
        }

        private static async Task HandleEditLists()
        {
            while (true)
            {
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[{ConsoleUI.greenName}]{_localizationService.GetString("select_list")}[/]\n[gray]{_localizationService.GetString("navigation")}[/]")
                        .AddChoices(new[] {
                _localizationService.GetString("general_list"),
                _localizationService.GetString("exclude_list"),
                _localizationService.GetString("back")
                        })
                        .HighlightStyle(new Style(Color.PaleGreen1))
                        .WrapAround(true)
                );

                if (choice == _localizationService.GetString("back")) return;

                string listFile = choice switch
                {
                    var c when c == _localizationService.GetString("exclude_list") => ExcludeListFileName,
                    _ => GeneralListFileName
                };

                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists", listFile);

                var domains = new List<string>();
                try
                {
                    domains = await ListManager.GetDomainsFromFile(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to load domains from {listFile}: {ex.Message}", ex);
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]Failed to load domains: {ex.Message}[/]");
                    return;
                }

                while (true)
                {
                    var menuItems = new List<string>
                    {
                        $"[{ConsoleUI.greenName}]+ {_localizationService.GetString("add_domain")}[/]",
                        $"[{ConsoleUI.greyName}]← {_localizationService.GetString("back")}[/]"
                    };

                    menuItems.AddRange(domains.Select(d => $"  {d}"));

                    if (domains.Count == 0)
                    {
                        menuItems.Add($"[{ConsoleUI.greyName}]" + _localizationService.GetString("no_domains_in_list") + "[/]");
                    }

                    var selectionPromt = new SelectionPrompt<string>()
                            .Title($"[{ConsoleUI.greenName}]{listFile}[/]\n[gray]" + string.Format(_localizationService.GetString("domains_count"), domains.Count) + "[/]")
                            .AddChoices(menuItems)
                            .HighlightStyle(new Style(Color.PaleGreen1))
                            .WrapAround(true)
                            .PageSize(15)
                            .EnableSearch()
                            .SearchPlaceholderText(_localizationService.GetString("search_placeholder"))
                            .MoreChoicesText($"[{ConsoleUI.greyName}]({_localizationService.GetString("other_options")})[/]");
                    selectionPromt.SearchHighlightStyle = new Style(Color.Grey42);

                    var domainAction = AnsiConsole.Prompt(selectionPromt);

                    if (domainAction.Contains(_localizationService.GetString("back")))
                    {
                        break;
                    }
                    else if (domainAction.Contains(_localizationService.GetString("add_domain")))
                    {
                        var domain = AnsiConsole.Ask<string>($"[{ConsoleUI.greenName}]" + string.Format(_localizationService.GetString("add_new_domain_to"), listFile) + $"[/] [{ConsoleUI.greyName}](example.com)[/]:");
                        if (string.IsNullOrWhiteSpace(domain))
                        {
                            AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("empty_domain")}[/]");
                            AnsiConsole.MarkupLine($"\n[{ConsoleUI.darkGreyName}]{_localizationService.GetString("press_any_key")}[/]");
                            Console.ReadKey(true);
                            ConsoleUI.Clear();
                            continue;
                        }

                        if (domains.Contains(domain))
                        {
                            AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("domain_already_in_list")}[/]");
                            AnsiConsole.MarkupLine($"\n[{ConsoleUI.darkGreyName}]{_localizationService.GetString("press_any_key")}[/]");
                            Console.ReadKey(true);
                            ConsoleUI.Clear();
                            continue;
                        }

                        try
                        {
                            ConsoleUI.Clear();
                            await ListManager.AddDomainToFile(filePath, domain, _localizationService, _logger);
                            domains.Add(domain);
                            _logger.LogInformation($"Added domain to \"{choice}\" list: \"{domain}\"");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Failed to add domain to list: {ex.Message}", ex);
                            AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]" + string.Format(_localizationService.GetString("domain_add_fail"), ex.Message) + "[/]");
                            AnsiConsole.MarkupLine($"\n[{ConsoleUI.darkGreyName}]{_localizationService.GetString("press_any_key")}[/]");
                            Console.ReadKey(true);
                        }
                    }
                    else
                    {
                        var domainToRemove = domainAction.Trim();
                        if (!domains.Contains(domainToRemove))
                        {
                            ConsoleUI.Clear();
                            continue;
                        }

                        if (AnsiConsole.Confirm(string.Format(_localizationService.GetString("confirm_remove_domain"), domainToRemove)))
                        {
                            try
                            {
                                ConsoleUI.Clear();
                                await ListManager.RemoveDomainFromFile(filePath, domainToRemove, _localizationService, _logger);
                                domains.Remove(domainToRemove);
                                _logger.LogInformation($"Removed domain from \"{choice}\" list: \"{domainToRemove}\"");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"Failed to remove domain from list: {ex.Message}", ex);
                                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]" + string.Format(_localizationService.GetString("domain_remove_fail"), ex.Message) + "[/]");
                                AnsiConsole.MarkupLine($"\n[{ConsoleUI.darkGreyName}]{_localizationService.GetString("press_any_key")}[/]");
                                Console.ReadKey(true);
                            }
                        }
                        else
                        {
                            ConsoleUI.Clear();
                        }
                    }
                }
            }
        }

        private static async Task HandleUpdate()
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{ConsoleUI.greenName}]{_localizationService.GetString("update_select")}[/]\n[{ConsoleUI.greyName}]{_localizationService.GetString("navigation")}[/]")
                    .AddChoices(new[] {
                        _localizationService.GetString("update_profiles"),
                        _localizationService.GetString("update_app"),
                        _localizationService.GetString("back")
                    })
                    .HighlightStyle(new Style(Color.PaleGreen1))
                    .WrapAround(true)
            );

            if (choice == _localizationService.GetString("back")) return;

            _logger.LogInformation($"Update item \"{choice}\" is selected");
            switch (choice)
            {
                case var c when c == _localizationService.GetString("update_profiles"):
                    await _updateService.DownloadLatestReleaseAsync();
                    break;
                case var c when c == _localizationService.GetString("update_app"):
                    await _updateService.UpdateCliAsync();
                    break;
            }
        }

        private static async Task HandleSettings()
        {
            var back = false;
            while (!back)
            {
                var config = _configService.GetConfig();

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title($"[{ConsoleUI.greenName}]{_localizationService.GetString("app_settings")}[/]\n[{ConsoleUI.greyName}]{_localizationService.GetString("navigation")}[/]")
                        .AddChoices(new[] {
                        $"{_localizationService.GetString("language_setting")}: {GetLanguageDisplayName(_localizationService.GetCurrentLanguage())}",
                        $"{_localizationService.GetString("auto_run")}: {(config.AutoStart ? $"[{ConsoleUI.greenName}]{_localizationService.GetString("enabled")}[/]" : $"[{ConsoleUI.redName}]{_localizationService.GetString("disabled")}[/]")}",
                        $"{_localizationService.GetString("auto_profile")}: {(string.IsNullOrEmpty(config.AutoStartProfile) ? $"[{ConsoleUI.greyName}]{_localizationService.GetString("not_specified")}[/]" : $"[{ConsoleUI.greenName}]{config.AutoStartProfile}[/]")}",
                        $"{_localizationService.GetString("game_filter")}: {(config.GameFilterEnabled ? $"[{ConsoleUI.greenName}]{_localizationService.GetString("enabled")}[/]" : $"[{ConsoleUI.redName}]{_localizationService.GetString("disabled")}[/]")}",
                        _localizationService.GetString("delete_service"),
                        _localizationService.GetString("back")
                        })
                        .HighlightStyle(new Style(Color.PaleGreen1))
                        .WrapAround(true)
                );

                _logger.LogInformation($"Settings item \"{choice}\" is selected");
                switch (choice)
                {
                    case var c when c.StartsWith(_localizationService.GetString("language_setting")):
                        await HandleLanguageSettings();
                        break;

                    case var c when c.StartsWith($"{_localizationService.GetString("auto_run")}:"):
                        config.AutoStart = !config.AutoStart;
                        _configService.UpdateAutoStart(config.AutoStart, config.AutoStartProfile);
                        break;

                    case var c when c.StartsWith($"{_localizationService.GetString("auto_profile")}:"):
                        if (await SelectDefaultProfile())
                        {
                            config = _configService.GetConfig();
                        }
                        break;

                    case var c when c.StartsWith($"{_localizationService.GetString("game_filter")}:"):
                        config.GameFilterEnabled = !config.GameFilterEnabled;
                        _configService.UpdateGameFilter(config.GameFilterEnabled);
                        _zapretManager.ToggleGameFilter();
                        break;

                    case var c when c == _localizationService.GetString("delete_service"):
                        _updateService.StopServicesAndProcesses();
                        break;

                    case var c when c == _localizationService.GetString("back"):
                        back = true;
                        break;
                }
            }
        }
        private static string GetLanguageDisplayName(string code)
        {
            var languages = _localizationService.GetAvailableLanguages();
            return languages.TryGetValue(code, out var name) ? name : code;
        }
        private static async Task HandleLanguageSettings()
        {
            var currentLang = _localizationService.GetCurrentLanguage();
            var languages = _localizationService.GetAvailableLanguages();

            var selectedLang = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{ConsoleUI.greenName}]Select language[/]\n[{ConsoleUI.greyName}]Movement: ↑↓ arrows\nConfirmation: Enter key[/]")
                    .AddChoices(languages.Values)
                    .HighlightStyle(new Style(Color.PaleGreen1))
                    .WrapAround(true)
            );

            var selectedCode = languages.FirstOrDefault(x => x.Value == selectedLang).Key;
            if (!string.IsNullOrEmpty(selectedCode) && selectedCode != currentLang)
            {
                await _localizationService.SetLanguageAsync(selectedCode);
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("language_changed")}[/]");
                _logger.LogInformation($"Language changed to \"{selectedCode}\"");
                await Task.Delay(1000);
                ConsoleUI.Clear();
            }
        }
        private static async Task<bool> SelectDefaultProfile()
        {
            await _zapretManager.LoadAvailableProfilesAsync();
            var profiles = await _profileService.GetAvailableProfilesAsync();

            if (profiles.Count == 0)
            {
                _logger.LogWarning($"No profiles found");
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("no_profiles")}[/]");
                return false;
            }

            var profileNames = profiles.Select(p => p.Name).ToList();
            var selectedProfile = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{ConsoleUI.greenName}]{_localizationService.GetString("auto_profile_title")}[/]\n[{ConsoleUI.greyName}]{_localizationService.GetString("navigation")}[/]")
                    .AddChoices(profileNames)
                    .HighlightStyle(new Style(Color.PaleGreen1))
                    .WrapAround(true)
            );

            _logger.LogInformation($"Default profile changed to {selectedProfile}");
            _configService.UpdateAutoStart(true, selectedProfile);
            return true;
        }

        private static async Task HandleExit()
        {
            _logger.LogInformation("User requested application exit");
            if (_zapretManager.IsRunning())
            {
                try
                {
                    await _zapretManager.StopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to stop service on exit: {ex.Message}", ex);
                }
            }

            _exitRequested = true;
            _cancellationTokenSource.Cancel();

            await Task.Delay(500);
        }

        public static void Shutdown()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
    }
}
