using System.Diagnostics;
using System.Text.Json;
using ZapretCLI.Core.Interfaces;
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
        private ZapretProfile _currentProfile;
        private List<ZapretProfile> _availableProfiles = new List<ZapretProfile>();
        private bool _gameFilterEnabled = false;

        public ZapretManager(string appPath, IProcessService processService, IStatusService statusService, IProfileService profileService)
        {
            _appPath = appPath;

            _processService = processService;
            _statusService = statusService;
            _profileService = profileService;

            _processService.OutputLineReceived += (sender, line) => _statusService.ProcessOutputLine(line);
            _processService.ErrorLineReceived += (sender, line) => _statusService.ProcessOutputLine(line);

            _gameFilterEnabled = LoadGameFilterSetting();
        }

        public async Task InitializeAsync()
        {
            await _statusService.Initialize();
        }

        public async Task StopAsync(bool showLogs = true)
        {
            if (!IsRunning())
            {
                if (showLogs) ConsoleUI.WriteLine("[!] Zapret is not running!", ConsoleUI.yellow);
                return;
            }

            try
            {
                await _processService.StopZapretAsync(_zapretProcess);
                _zapretProcess = null;
                if (showLogs) ConsoleUI.WriteLine("[✓] Zapret stopped successfully!", ConsoleUI.green);
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[✗] Failed to stop zapret: {ex.Message}", ConsoleUI.red);
            }
        }

        public async Task ShowStatusAsync()
        {
            var status = IsRunning() ? "Running" : "Stopped";
            var color = IsRunning() ? ConsoleUI.green : ConsoleUI.red;

            ConsoleUI.WriteLine("Status:", ConsoleUI.yellow);
            ConsoleUI.WriteLine($"  Status: {status} (PID: {_zapretProcess?.Id ?? 0})", color);
            ConsoleUI.WriteLine($"  Working directory: '{_appPath}'");

            await _statusService.DisplayStatusAsync();
        }

        public async Task LoadAvailableProfilesAsync()
        {
            if (_availableProfiles.Count == 0)
            {
                _availableProfiles = await _profileService.GetAvailableProfilesAsync();
                if (_availableProfiles.Count == 0)
                {
                    ConsoleUI.WriteLine("[!] No profiles available. Please update the application.", ConsoleUI.yellow);
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
                ConsoleUI.WriteLine("[!] No profiles available. Updating...", ConsoleUI.yellow);
                await Program.UpdateProfiles();
                await LoadAvailableProfilesAsync();
            }

            if (_availableProfiles.Count == 0)
            {
                ConsoleUI.WriteLine("[✗] No profiles available after update. Something went wrong?", ConsoleUI.red);
                return;
            }

            if (string.IsNullOrWhiteSpace(profileName))
            {
                ConsoleUI.Clear(false);
                ConsoleUI.WriteLine("Available profiles:", ConsoleUI.yellow, false);

                int selectedIndex = _currentProfile != null
                    ? _availableProfiles.IndexOf(_currentProfile)
                    : 0;

                if (selectedIndex < 0) selectedIndex = 0;

                DrawMenu(selectedIndex);

                while (true)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.UpArrow && selectedIndex > 0)
                    {
                        selectedIndex--;
                        DrawMenu(selectedIndex);
                    }
                    else if (key.Key == ConsoleKey.DownArrow && selectedIndex < _availableProfiles.Count - 1)
                    {
                        selectedIndex++;
                        DrawMenu(selectedIndex);
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        break;
                    }
                }

                // Clearing the menu area
                int menuHeight = _availableProfiles.Count + 2;

                for (int i = 0; i < menuHeight; i++)
                {
                    Console.SetCursorPosition(0, 1 + i);
                    Console.Write(new string(' ', Console.WindowWidth));
                }

                Console.SetCursorPosition(0, 1);
                _currentProfile = _availableProfiles[selectedIndex];
            }
            else
            {
                _currentProfile = _availableProfiles.FirstOrDefault(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

                if (_currentProfile == null)
                {
                    ConsoleUI.WriteLine($"[✗] Profile '{profileName}' not found.", ConsoleUI.red);
                    return;
                }
            }

            ConsoleUI.Clear(false);
            ConsoleUI.HistoryRestore();

            /*if (_currentProfile != null)
            {
                ConsoleUI.WriteLine($"[✓] Selected profile: '{_currentProfile.Name}'", ConsoleUI.green);
            }*/
        }

        private void DrawMenu(int index)
        {
            Console.SetCursorPosition(0, 1);

            for (int i = 0; i < _availableProfiles.Count; i++)
            {
                var profile = _availableProfiles[i];
                string prefix = i == index ? "► " : "  ";
                string color = i == index ? ConsoleUI.green : ConsoleUI.white;
                ConsoleUI.WriteLine($"{prefix}{$"{i + 1}.",-3} {profile.Name}", color, false);
            }

            // Instructions
            Console.SetCursorPosition(0, 1 + _availableProfiles.Count + 1);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Use arrow keys to navigate, press Enter to select", ConsoleUI.blue);
            Console.ResetColor();
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
                if (showLogs) ConsoleUI.WriteLine("[!] Zapret is already running!", ConsoleUI.yellow);
                return false;
            }

            try
            {
                var tcs = new TaskCompletionSource<bool>();
                var timeout = TimeSpan.FromSeconds(15);
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
                    ConsoleUI.WriteLine($"[!] Timeout waiting for windivert initialization ({timeout.Seconds} seconds)", ConsoleUI.yellow);
                    await StopAsync();
                    return false;
                }

                if (showLogs) { 
                    ConsoleUI.WriteLine($"[✓] Zapret started successfully with profile '{_currentProfile.Name}'!", ConsoleUI.green);
                    await ShowStatusAsync();
                }
                return true;
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[✗] Failed to start zapret: {ex.Message}", ConsoleUI.red);
                return false;
            }
        }

        public void ShowProfileInfo()
        {
            if (_currentProfile == null)
            {
                ConsoleUI.WriteLine("[!] No profile selected", ConsoleUI.yellow);
                return;
            }

            ConsoleUI.WriteLine($"Profile: '{_currentProfile.Name}'", ConsoleUI.blue);
            ConsoleUI.WriteLine($"Description: '{_currentProfile.Description}'", ConsoleUI.blue);
            ConsoleUI.WriteLine($"Arguments:", ConsoleUI.blue);

            foreach (var arg in _currentProfile.Arguments)
            {
                ConsoleUI.WriteLine($"  {arg}", ConsoleUI.white);
            }
        }

        private bool LoadGameFilterSetting()
        {
            var settingsFile = Path.Combine(_appPath, "settings.json");
            if (File.Exists(settingsFile))
            {
                try
                {
                    var settings = JsonSerializer.Deserialize<Dictionary<string, bool>>(
                        File.ReadAllText(settingsFile));
                    return settings != null && settings.TryGetValue("gameFilter", out var enabled) && enabled;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        private void SaveGameFilterSetting(bool enabled)
        {
            var settingsFile = Path.Combine(_appPath, "settings.json");
            var settings = new Dictionary<string, bool> { { "gameFilter", enabled } };
            File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }

        public void ToggleGameFilter()
        {
            _gameFilterEnabled = !_gameFilterEnabled;
            _processService.SetGameFilter(_gameFilterEnabled);
            SaveGameFilterSetting(_gameFilterEnabled);

            ConsoleUI.WriteLine($"Game filter is now {(_gameFilterEnabled ? "enabled" : "disabled")}",
                _gameFilterEnabled ? ConsoleUI.green : ConsoleUI.yellow);
            ConsoleUI.WriteLine("[!] Service restart is required for changes to take effect", ConsoleUI.yellow);
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

            ConsoleUI.WriteLine("Profile Testing", ConsoleUI.yellow);
            ConsoleUI.WriteLine("[!] Close all third-party applications for the best testing experience (e.g. Discord, VPN, etc.)", ConsoleUI.yellow);
            ConsoleUI.WriteLine("---------------", ConsoleUI.yellow);

            var domain = ConsoleUI.ReadLineWithPrompt("Enter a blocked domain for testing (e.g., example.com): ").Trim();
            if (string.IsNullOrWhiteSpace(domain))
            {
                ConsoleUI.WriteLine("[!] Domain cannot be empty", ConsoleUI.red);
                return;
            }

            // Checking if a domain is really blocked (without bypassing it)
            ConsoleUI.WriteLine("\nChecking if the domain is actually blocked...", ConsoleUI.blue);
            bool isBlocked = await IsDomainBlocked(domain);
            if (!isBlocked)
            {
                ConsoleUI.WriteLine($"[!] Warning: {domain} appears to be accessible without bypass. Testing may not be accurate.", ConsoleUI.yellow);
                ConsoleUI.WriteLine("Continue anyway? (y/n): ", ConsoleUI.yellow);
                var response = Console.ReadLine()?.Trim().ToLower();
                if (response != "y" && response != "yes")
                {
                    return;
                }
            }

            await LoadAvailableProfilesAsync();
            if (_availableProfiles.Count == 0)
            {
                ConsoleUI.WriteLine("[!] No profiles available to test", ConsoleUI.red);
                return;
            }

            var results = new List<(string ProfileName, bool Success, string Message)>();
            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            string testUrl = $"https://{domain}";

            ConsoleUI.WriteLine($"\nStarting tests for {domain} with {_availableProfiles.Count} profiles...", ConsoleUI.blue);
            ConsoleUI.WriteLine("-------------------------------------------------------------", ConsoleUI.blue);

            foreach (var profile in _availableProfiles)
            {
                ConsoleUI.WriteLine($"\nTesting profile: {profile.Name}", ConsoleUI.yellow);

                if (IsRunning()) await StopAsync(false);
                _currentProfile = profile;

                try
                {
                    // Launch the profile and wait for initialization.
                    if (!await StartAsync(false))
                    {
                        results.Add((profile.Name, false, "Failed to initialize"));
                        continue;
                    }

                    // Additional waiting for stabilization
                    await Task.Delay(500);

                    // Testing domain access
                    bool success = false;
                    string message = "";
                    try
                    {
                        ConsoleUI.WriteLine($"  Attempting to connect to {domain}...", ConsoleUI.blue);
                        var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
                        var response = await httpClient.GetAsync(testUrl, cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            success = true;
                            message = $"HTTP {response.StatusCode}: Connection successful";
                            ConsoleUI.WriteLine($"  [✓] SUCCESS: {message}", ConsoleUI.green);
                        }
                        else
                        {
                            message = $"HTTP {response.StatusCode}: Connection failed";
                            ConsoleUI.WriteLine($"  [✗] FAILED: {message}", ConsoleUI.red);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        message = "Connection timed out";
                        ConsoleUI.WriteLine($"  [✗] FAILED: {message}", ConsoleUI.red);
                    }
                    catch (Exception ex)
                    {
                        message = $"Error: {ex.Message}";
                        ConsoleUI.WriteLine($"  [✗] FAILED: {message}", ConsoleUI.red);
                    }

                    results.Add((profile.Name, success, message));
                }
                finally
                {
                    // Stop the profile after the test
                    if (IsRunning()) await StopAsync(false);
                    await Task.Delay(1000);
                }
            }

            ConsoleUI.WriteLine("\n\nTest Results Summary", ConsoleUI.yellow);
            ConsoleUI.WriteLine("=====================", ConsoleUI.yellow);

            var successfulProfiles = results.Where(r => r.Success).ToList();

            if (successfulProfiles.Count > 0)
            {
                ConsoleUI.WriteLine($"[✓] {successfulProfiles.Count} profile(s) successfully bypassed blocking:", ConsoleUI.green);
                foreach (var result in successfulProfiles)
                {
                    ConsoleUI.WriteLine($"  • {result.ProfileName}", ConsoleUI.green);
                }
            }
            else
            {
                ConsoleUI.WriteLine("[!] No profiles successfully bypassed the blocking. May God save us.", ConsoleUI.red);
            }

            _currentProfile = initialProfile;
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