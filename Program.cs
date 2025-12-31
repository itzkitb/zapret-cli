using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
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
        private static ILoggerService _logger;
        private static CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        static async Task Main(string[] args)
        {
            try
            {
                if (IsAdministrator())
                {
                    ConfigureMinimalLogger();

                    AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    {
                        var exception = e.ExceptionObject as Exception;
                        Log.Fatal(exception, "Unhandled exception occurred");
                    };
                    TaskScheduler.UnobservedTaskException += (s, e) =>
                    {
                        Log.Error(e.Exception, "Unobserved task exception");
                        e.SetObserved();
                    };
                }

                // OS and administrator rights checks
                if (!await InitializeApplicationAsync(args))
                    return;

                // Setting up a DI container
                ServiceProvider = ConfigureServices();

                await RunApplicationAsync(args);
            }
            finally
            {
                DisposeResources();
            }
        }

        private static void ConfigureMinimalLogger()
        {
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var logsPath = Path.Combine(appPath, "logs");
            Directory.CreateDirectory(logsPath);
            var logPath = Path.Combine(logsPath, $"zapret_{DateTime.Now:yyyyMMdd}.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    path: logPath,
                    formatter: new CustomLogFormatter(),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();
        }

        private static void DisposeResources()
        {
            try
            {
                MainMenu.Shutdown();

                var updateService = ServiceProvider.GetRequiredService<IUpdateService>();
                var profileService = ServiceProvider.GetRequiredService<IProfileService>();
                var processService = ServiceProvider.GetRequiredService<IProcessService>();
                var localizationService = ServiceProvider.GetRequiredService<ILocalizationService>();
                var configService = ServiceProvider.GetRequiredService<IConfigService>();

                updateService.Dispose();
                profileService.Dispose();
                processService.Dispose();
                localizationService.Dispose();
                configService.Dispose();

                _statusTimer?.Stop();
                _statusTimer?.Dispose();
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                ServiceProvider?.Dispose();
                Log.CloseAndFlush();
            }
            catch
            {
                // 
            }
        }

        private static ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();
            var appPath = AppDomain.CurrentDomain.BaseDirectory;

            // Registration of services
            services = SetupLogger(services);

            services.AddSingleton<IFileSystemService, FileSystemService>();
            services.AddSingleton<AppConfig>();
            services.AddSingleton<IConfigService, ConfigService>(sp => new ConfigService(appPath, sp.GetRequiredService<ILoggerService>()));
            services.AddSingleton<ILocalizationService, LocalizationService>(sp =>
                new LocalizationService(
                    sp.GetRequiredService<IConfigService>(),
                    sp.GetRequiredService<IFileSystemService>(),
                    sp.GetRequiredService<ILoggerService>(),
                    appPath
            ));

            // HttpClient
            services.AddHttpClient();

            // Basic services
            services.AddSingleton<IProcessService, ProcessService>(sp =>
            {
                var fileSystem = sp.GetRequiredService<IFileSystemService>();
                var configuration = sp.GetRequiredService<IConfigService>().GetConfig();
                var logger = sp.GetRequiredService<ILoggerService>();
                return new ProcessService(fileSystem, configuration, appPath, logger);
            });
            services.AddSingleton<IStatusService, StatusService>(sp =>
            {
                var localizationService = sp.GetRequiredService<ILocalizationService>();
                var logger = sp.GetRequiredService<ILoggerService>();
                return new StatusService(appPath, localizationService, logger);
            });
            services.AddSingleton<IProfileService, ProfileService>(sp =>
            {
                var localizationService = sp.GetRequiredService<ILocalizationService>();
                var logger = sp.GetRequiredService<ILoggerService>();
                return new ProfileService(appPath, localizationService, logger);
            });
            services.AddSingleton<ITestService>(sp =>
            {
                return new TestService(
                    sp.GetRequiredService<IZapretManager>(),
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ILoggerService>(),
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<IProfileService>()
                );
            });
            services.AddSingleton<IZapretManager, ZapretManager>(sp =>
            {
                var processService = sp.GetRequiredService<IProcessService>();
                var statusService = sp.GetRequiredService<IStatusService>();
                var profileService = sp.GetRequiredService<IProfileService>();
                var localizationService = sp.GetRequiredService<ILocalizationService>();
                var configService = sp.GetRequiredService<IConfigService>();
                var logger = sp.GetRequiredService<ILoggerService>();
                return new ZapretManager(appPath, processService, statusService, profileService, localizationService, configService, logger);
            });
            services.AddSingleton<IUpdateService, UpdateService>(sp =>
            {
                return new UpdateService(appPath, sp.GetRequiredService<IProfileService>(), sp.GetRequiredService<IZapretManager>(), sp.GetRequiredService<ILocalizationService>(), sp.GetRequiredService<ILoggerService>());
            });
            services.AddSingleton<IDiagnosticsService, DiagnosticsService>(sp =>
            {
                return new DiagnosticsService(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ILoggerService>(),
                    sp.GetRequiredService<IFileSystemService>(),
                    appPath
                );
            });
            services.AddSingleton<IExportService, ExportService>(sp =>
            {
                var fileSystemService = sp.GetRequiredService<IFileSystemService>();
                var statusService = sp.GetRequiredService<IStatusService>();
                var profileService = sp.GetRequiredService<IProfileService>();
                var configService = sp.GetRequiredService<IConfigService>();
                var zapretManager = sp.GetRequiredService<IZapretManager>();
                var localizationService = sp.GetRequiredService<ILocalizationService>();
                var logger = sp.GetRequiredService<ILoggerService>();
                return new ExportService(appPath, fileSystemService, statusService, profileService, configService, zapretManager, localizationService, logger);
            });

            return services.BuildServiceProvider();
        }

        private static ServiceCollection SetupLogger(ServiceCollection services)
        {
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var logsPath = Path.Combine(appPath, "logs");
            Directory.CreateDirectory(logsPath);

            var logPath = Path.Combine(logsPath, $"zapret_{DateTime.Now:yyyyMMdd}.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: logPath,
                    formatter: new CustomLogFormatter(),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();

            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddSerilog(Log.Logger, dispose: true);
            });

            services.AddSingleton<ILoggerService, LoggerService>();

            return services;
        }

        private static async Task<bool> InitializeApplicationAsync(string[] args)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]Unfortunately, it currently only works on Windows...[/] [{ConsoleUI.greyName}]:([/]");
                return false;
            }

            if (!IsAdministrator())
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]Administrator rights are required! Restarting...[/]");
                await Task.Delay(250);
                RestartAsAdmin(args);
                return false;
            }

            Console.OutputEncoding = Encoding.UTF8;

            // Kill existing processes
            KillProcessByName("winws.exe");

            return true;
        }

        private static async Task RunApplicationAsync(string[] args)
        {
            Stopwatch sp = Stopwatch.StartNew();
            // Initialization
            Console.Title = $"Zapret CLI - v.{MainMenu.version}";

            _logger = ServiceProvider.GetRequiredService<ILoggerService>();
            _logger.LogInformation($"========================================");
            _logger.LogInformation($"Zapret CLI - v.{MainMenu.version}");
            var runId = Guid.NewGuid().ToString("N");
            _logger.LogInformation($"Session: {runId}");
            _logger.LogInformation($"Process ID: {Environment.ProcessId}, Thread ID: {Thread.CurrentThread.ManagedThreadId}");
            _logger.LogInformation($"Launch arguments: {JsonSerializer.Serialize(args)}");
            _logger.LogInformation($"Working directory: {Environment.CurrentDirectory}");
            _logger.LogInformation($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            _logger.LogInformation($"Host: {Environment.MachineName}, User: {Environment.UserName}");
            _logger.LogInformation($".NET runtime: {Environment.Version}");
            _logger.LogInformation($"Initializing...");

            var zapretManager = ServiceProvider.GetRequiredService<IZapretManager>();
            var updateService = ServiceProvider.GetRequiredService<IUpdateService>();

            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{ServiceProvider.GetRequiredService<ILocalizationService>().GetString("checking_for_updates")}[/]");
            _logger.LogInformation($"Initializing ZapretManager...");
            await zapretManager.InitializeAsync();

            // Run update checks
            try
            {
                _logger.LogInformation($"Checking for CLI and Zapret updates...");
                await Task.Run(async () =>
                {
                    await updateService.CheckForUpdatesAsync();
                    await updateService.CheckForCliUpdatesAsync();
                }, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Update checks were canceled");
            }
            catch (Exception ex)
            {
                _logger.LogError("Update checks failed", ex);
            }

            // Title timers setup
            _statusTimer = new System.Timers.Timer(1000);
            _statusTimer.Elapsed += async (sender, e) => await UpdateConsoleTitle(zapretManager);
            _statusTimer.Start();

            // Show main menu
            sp.Stop();
            _logger.LogInformation($"Initialization completed! ({sp.ElapsedMilliseconds}ms)");
            await MainMenu.ShowAsync(ServiceProvider);
        }

        public static async Task UpdateProfiles()
        {
            await ServiceProvider.GetRequiredService<IUpdateService>().DownloadLatestReleaseAsync();
        }

        private static async Task UpdateConsoleTitle(IZapretManager zapretManager)
        {
            var localization = ServiceProvider.GetRequiredService<ILocalizationService>();
            var isRunning = zapretManager.IsRunning();
            var status = isRunning ? localization.GetString("running") : localization.GetString("stopped");
            var stats = await zapretManager.GetStatusStatsAsync();
            var hostsCount = stats.TotalHosts;
            var ipsCount = stats.TotalIPs;
            Console.Title = $"Zapret CLI - {status} • {localization.GetString("hosts")}: {hostsCount} • {localization.GetString("ips")}: {ipsCount}";
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

                // Correctly add arguments using ArgumentList
                if (args != null)
                {
                    foreach (var arg in args)
                    {
                        processStartInfo.ArgumentList.Add(arg);
                    }
                }

                Process.Start(processStartInfo);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                // Fallback to minimal logging if ServiceProvider is not available
                try
                {
                    if (ServiceProvider != null)
                    {
                        var localization = ServiceProvider?.GetRequiredService<ILocalizationService>();
                        AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{localization?.GetString("restart_as_admin_fail") ?? "Failed to restart as administrator"}: {ex.Message}[/]");
                    }
                    else
                    {
                        throw;
                    }
                }
                catch
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]Failed to restart as administrator: {ex.Message}[/]");
                }

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
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Terminate process {processName} failed", ex);
                        AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]Failed to terminate process {{0}}: {{1}}[/]", processName, ex.Message);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Processes check failed", ex);
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]Error checking processes: {{0}}[/]", ex.Message);
            }
        }
    }
}