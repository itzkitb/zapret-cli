using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using ZapretCLI.Configuration;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Managers;
using ZapretCLI.Core.Services;
using ZapretCLI.Models;
using ZapretCLI.UI;

namespace ZapretCLI
{
    class Program
    {
        public static ServiceProvider ServiceProvider;
        private static System.Timers.Timer _statusTimer;

        static async Task Main(string[] args)
        {
            // OS and administrator rights checks
            if (!await InitializeApplicationAsync(args))
                return;

            // Setting up a DI container
            ServiceProvider = ConfigureServices();

            try
            {
                await RunApplicationAsync();
            }
            finally
            {
                ServiceProvider?.Dispose();
                _statusTimer?.Stop();
                _statusTimer?.Dispose();
            }
        }

        private static ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();
            var appPath = AppDomain.CurrentDomain.BaseDirectory;

            // Registration of services
            services.AddSingleton<IFileSystemService, FileSystemService>();
            services.AddSingleton<AppSettings>();
            services.AddSingleton<ConfigurationService>(sp =>
            {
                return new ConfigurationService(sp.GetRequiredService<IFileSystemService>(), appPath);
            });

            // HttpClient
            services.AddHttpClient();

            // Basic services
            services.AddSingleton<IProcessService, ProcessService>(sp =>
            {
                var fileSystem = sp.GetRequiredService<IFileSystemService>();
                var configuration = sp.GetRequiredService<ConfigurationService>().GetAppSettings();
                return new ProcessService(fileSystem, configuration, appPath);
            });
            services.AddSingleton<IStatusService, StatusService>(sp =>
            {
                return new StatusService(appPath);
            });
            services.AddSingleton<IProfileService, ProfileService>(sp =>
            {
                return new ProfileService(appPath);
            });
            services.AddSingleton<IZapretManager, ZapretManager>(sp =>
            {
                var processService = sp.GetRequiredService<IProcessService>();
                var statusService = sp.GetRequiredService<IStatusService>();
                var profileService = sp.GetRequiredService<IProfileService>();
                return new ZapretManager(appPath, processService, statusService, profileService);
            });
            services.AddSingleton<IUpdateService, UpdateService>(sp =>
            {
                return new UpdateService(appPath, sp.GetRequiredService<IProfileService>(), sp.GetRequiredService<IZapretManager>());
            });

            return services.BuildServiceProvider();
        }

        private static async Task<bool> InitializeApplicationAsync(string[] args)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                ConsoleUI.WriteLine("[✗] Sorry, but Zapret only works on Windows... :(", ConsoleUI.red);
                return false;
            }

            if (!IsAdministrator())
            {
                ConsoleUI.WriteLine("[!] Administrator rights are required! Restarting...", ConsoleUI.yellow);
                await Task.Delay(250);
                RestartAsAdmin(args);
                return false;
            }

            Console.OutputEncoding = Encoding.UTF8;

            // Kill existing processes
            KillProcessByName("winws.exe");

            return true;
        }

        private static async Task RunApplicationAsync()
        {
            // Services init
            Console.Title = "Zapret CLI";
            var zapretManager = ServiceProvider.GetRequiredService<IZapretManager>();
            var updateService = ServiceProvider.GetRequiredService<IUpdateService>();

            ConsoleUI.WriteLine("Checking for updates...", ConsoleUI.blue);

            await zapretManager.InitializeAsync();
            await updateService.CheckForUpdatesAsync();
            await updateService.CheckForCliUpdatesAsync();

            // Timer for title update
            _statusTimer = new System.Timers.Timer(1000);
            _statusTimer.Elapsed += async (sender, e) => await UpdateConsoleTitle(zapretManager);
            _statusTimer.Start();

            ConsoleUI.ShowHeader();
            await MainLoop(zapretManager, updateService);
        }

        public static async Task UpdateProfiles()
        {
            await ServiceProvider.GetRequiredService<IUpdateService>().DownloadLatestReleaseAsync();
        }

        private static async Task UpdateConsoleTitle(IZapretManager zapretManager)
        {
            var isRunning = zapretManager.IsRunning();
            var status = isRunning ? "Running" : "Stopped";
            var stats = await zapretManager.GetStatusStatsAsync();
            var hostsCount = stats.TotalHosts;
            var ipsCount = stats.TotalIPs;
            Console.Title = $"Zapret CLI - {status} • Hosts: {hostsCount} • IPs: {ipsCount}";
        }

        private static async Task MainLoop(IZapretManager zapretManager, IUpdateService updateService)
        {
            while (true)
            {
                var command = ConsoleUI.ReadLineWithPrompt("zapret-cli> ") ?? "";
                if (string.IsNullOrWhiteSpace(command))
                {
                    ConsoleUI.WriteLine("Unknown command. Type 'help' for available commands.\n", ConsoleUI.yellow);
                    continue;
                }

                var commandParts = command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var cmd = commandParts[0].ToLower().Trim();
                var args = commandParts.Length > 1 ? commandParts[1].Trim() : null;

                switch (cmd)
                {
                    case "start":
                        await zapretManager.StartAsync();
                        break;
                    case "stop":
                        await zapretManager.StopAsync();
                        break;
                    case "status":
                        await zapretManager.ShowStatusAsync();
                        break;
                    case "add":
                        await ListManager.AddDomainInteractive();
                        break;
                    case "update":
                        await updateService.DownloadLatestReleaseAsync();
                        break;
                    case "exit":
                        await zapretManager.StopAsync();
                        return;
                    case "help":
                        ConsoleUI.ShowHelp();
                        break;
                    case "select":
                        await zapretManager.SelectProfileAsync(args);
                        break;
                    case "info":
                        zapretManager.ShowProfileInfo();
                        break;
                    case "list":
                        await ShowProfileList(zapretManager);
                        break;
                    case "test":
                        await zapretManager.TestProfilesAsync();
                        break;
                    case "toggle-game-filter":
                        zapretManager.ToggleGameFilter();
                        break;
                    case "game-filter-status":
                        ConsoleUI.WriteLine($"Game filter is currently {(zapretManager.IsGameFilterEnabled() ? "ENABLED" : "DISABLED")}",
                            zapretManager.IsGameFilterEnabled() ? ConsoleUI.green : ConsoleUI.yellow);
                        break;
                    case "del-service":
                        updateService.StopServicesAndProcesses();
                        break;
                    case "restart":
                        await zapretManager.StopAsync();
                        await Task.Delay(1000);
                        await zapretManager.StartAsync();
                        break;
                    default:
                        ConsoleUI.WriteLine("Unknown command. Type 'help' for available commands.", ConsoleUI.yellow);
                        break;
                }

                ConsoleUI.WriteLine("");
            }
        }

        private static async Task ShowProfileList(IZapretManager zapretManager)
        {
            await zapretManager.LoadAvailableProfilesAsync();
            var profiles = await ServiceProvider.GetRequiredService<IProfileService>().GetAvailableProfilesAsync();
            var currentProfile = zapretManager.GetCurrentProfile() ?? new Models.ZapretProfile();

            ConsoleUI.WriteLine("Profiles:", ConsoleUI.yellow);
            foreach (var profile in profiles)
            {
                if (currentProfile.Id == profile.Id)
                {
                    ConsoleUI.WriteLine($"► {profile.Name}", ConsoleUI.green);
                }
                else
                {
                    ConsoleUI.WriteLine($"• {profile.Name}", ConsoleUI.blue);
                }
                ConsoleUI.WriteLine($"  Description: {profile.Description}", ConsoleUI.white);
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void RestartAsAdmin(string[] args = null)
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = currentProcess.MainModule.FileName,
                    Verb = "runas",
                    UseShellExecute = true
                };

                if (args != null && args.Length > 0)
                {
                    processStartInfo.Arguments = string.Join(" ", args.Select(arg =>
                        arg.Contains(" ") ? $"\"{arg}\"" : arg));
                }

                Process.Start(processStartInfo);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[✗] Failed to restart as administrator: {ex.Message}", ConsoleUI.red);
                throw;
            }
        }

        private static void KillProcessByName(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
                if (processes.Length == 0)
                {
                    return;
                }

                foreach (var process in processes)
                {
                    try
                    {
                        ConsoleUI.WriteLine($"Terminating process: {processName} (PID: {process.Id})", ConsoleUI.blue);
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        ConsoleUI.WriteLine($"[✗] Failed to terminate process {processName}: {ex.Message}", ConsoleUI.red);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[✗] Error checking processes: {ex.Message}", ConsoleUI.red);
            }
        }
    }
}