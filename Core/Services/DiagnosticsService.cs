using Microsoft.Win32;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Services
{
    public class DiagnosticsService : IDiagnosticsService, IDisposable
    {
        private readonly ILocalizationService _localizationService;
        private readonly ILoggerService _logger;
        private readonly IFileSystemService _fileSystemService;
        private readonly string _appPath;
        private bool _disposed = false;

        public DiagnosticsService(
            ILocalizationService localizationService,
            ILoggerService logger,
            IFileSystemService fileSystemService,
            string appPath)
        {
            _localizationService = localizationService;
            _logger = logger;
            _fileSystemService = fileSystemService;
            _appPath = appPath;
        }

        public async Task RunDiagnosticsAsync()
        {
            _logger.LogInformation("=== Starting comprehensive diagnostics ===");
            ConsoleUI.Clear();
            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("diagnostics_title")}[/]");
            AnsiConsole.WriteLine();

            await CheckBaseFilteringEngineAsync();
            await CheckProxySettingsAsync();
            await CheckNetshAvailabilityAsync();
            await CheckTcpTimestampsAsync();
            await CheckAdguardAsync();
            await CheckKillerServicesAsync();
            await CheckIntelConnectivityServiceAsync();
            await CheckCheckpointServicesAsync();
            await CheckSmartByteServicesAsync();
            await CheckWinDivertFileAsync();
            await CheckVpnServicesAsync();
            await CheckSecureDnsAsync();
            await CheckWinDivertConflictsAsync();
            await CheckConflictingBypassesAsync();
            await ClearDiscordCacheAsync();

            _logger.LogInformation($"Diagnostics completed. Total checks performed: 15");
            AnsiConsole.MarkupLine($"\n[{ConsoleUI.darkGreyName}]{_localizationService.GetString("press_any_key")}[/]");
            Console.ReadKey(true);
        }

        private async Task CheckBaseFilteringEngineAsync()
        {
            try
            {
                using var service = new ServiceController("BFE");
                _logger.LogDebug($"BFE service status: {service.Status}");
                if (service.Status == ServiceControllerStatus.Running)
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("bfe_check_passed")}[/]");
                    _logger.LogInformation("BFE check passed");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("bfe_not_running")}[/]");
                    _logger.LogWarning("BFE is not running");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("bfe_check_failed")}[/]");
                _logger.LogError($"BFE check failed: {ex.Message}", ex);
            }
        }

        private async Task CheckProxySettingsAsync()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                if (key != null)
                {
                    var proxyEnable = key.GetValue("ProxyEnable") as int?;
                    var proxyServer = key.GetValue("ProxyServer") as string;

                    _logger.LogDebug($"ProxyEnable value: {proxyEnable?.ToString() ?? "null"}");
                    _logger.LogDebug($"ProxyServer value: {proxyServer ?? "null"}");

                    if (proxyEnable == 1 && !string.IsNullOrEmpty(proxyServer))
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{string.Format(_localizationService.GetString("proxy_enabled"), proxyServer)}[/]");
                        _logger.LogWarning($"Proxy is enabled: {proxyServer}");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("proxy_check_passed")}[/]");
                        _logger.LogInformation("Proxy check passed");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("proxy_check_passed")}[/]");
                    _logger.LogInformation("Proxy check passed");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("proxy_check_failed")}[/]");
                _logger.LogError($"Proxy check failed: {ex.Message}", ex);
            }
        }

        private async Task CheckNetshAvailabilityAsync()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "netsh",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("netsh_check_passed")}[/]");
                    _logger.LogInformation("netsh check passed");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("netsh_not_found")}[/]");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greyName}]PATH = \"{Environment.GetEnvironmentVariable("PATH")}\"[/]");
                    _logger.LogError("netsh not found");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("netsh_check_failed")}[/]");
                _logger.LogError($"netsh check failed: {ex.Message}", ex);
            }
        }

        private async Task CheckTcpTimestampsAsync()
        {
            try
            {
                _logger.LogInformation("Verifying TCP timestamps configuration");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = "interface tcp show global",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (Regex.IsMatch(output, "timestamps.*enabled", RegexOptions.IgnoreCase))
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("tcp_timestamps_passed")}[/]");
                    _logger.LogInformation("TCP timestamps check passed");
                }
                else
                {
                    _logger.LogWarning("TCP timestamps are disabled. Attempting to enable...");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{_localizationService.GetString("tcp_timestamps_disabled")}[/]");

                    var enableProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = "interface tcp set global timestamps=enabled",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    enableProcess.Start();
                    await enableProcess.WaitForExitAsync();

                    if (enableProcess.ExitCode == 0)
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("tcp_timestamps_enabled")}[/]");
                        _logger.LogInformation("TCP timestamps enabled successfully");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("tcp_timestamps_failed")}[/]");
                        _logger.LogWarning("Failed to enable TCP timestamps");
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("tcp_timestamps_check_failed")}[/]");
                _logger.LogError($"TCP timestamps check failed: {ex.Message}", ex);
            }
        }

        private async Task CheckAdguardAsync()
        {
            try
            {
                var processes = Process.GetProcessesByName("AdguardSvc");
                if (processes.Length > 0)
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("adguard_found")}[/]");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("adguard_issue_link")}[/]");
                    _logger.LogError("Adguard process found");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("adguard_check_passed")}[/]");
                    _logger.LogInformation("Adguard check passed");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("adguard_check_failed")}[/]");
                _logger.LogError($"Adguard check failed: {ex.Message}", ex);
            }
        }

        private async Task CheckKillerServicesAsync()
        {
            await CheckServicePatternAsync("Killer", _localizationService.GetString("killer_found"),
                _localizationService.GetString("killer_issue_link"),
                _localizationService.GetString("killer_check_passed"));
        }

        private async Task CheckIntelConnectivityServiceAsync()
        {
            await CheckServicePatternAsync("Intel.*Connectivity.*Network",
                _localizationService.GetString("intel_connectivity_found"),
                _localizationService.GetString("intel_connectivity_issue_link"),
                _localizationService.GetString("intel_connectivity_check_passed"));
        }

        private async Task CheckCheckpointServicesAsync()
        {
            var checkpointFound = false;

            try
            {
                checkpointFound = await ServiceExistsAsync("TracSrvWrapper") ||
                                  await ServiceExistsAsync("EPWD");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Checkpoint services check failed: {ex.Message}", ex);
            }

            if (checkpointFound)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("checkpoint_found")}[/]");
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("checkpoint_uninstall")}[/]");
                _logger.LogError("Checkpoint services found");
            }
            else
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("checkpoint_check_passed")}[/]");
                _logger.LogInformation("Checkpoint check passed");
            }
        }

        private async Task CheckSmartByteServicesAsync()
        {
            await CheckServicePatternAsync("SmartByte",
                _localizationService.GetString("smartbyte_found"),
                _localizationService.GetString("smartbyte_disable"),
                _localizationService.GetString("smartbyte_check_passed"));
        }

        private async Task CheckWinDivertFileAsync()
        {
            var binPath = Path.Combine(_appPath, "bin");

            _logger.LogDebug($"Checking WinDivert files in: {binPath}");

            if (!Directory.Exists(binPath))
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("windivert_file_missing")}[/]");
                _logger.LogError($"Bin directory not found: {binPath}");
                return;
            }

            var sysFiles = Directory.GetFiles(binPath, "*.sys");
            _logger.LogDebug($"Found {sysFiles.Length} .sys files: {string.Join(", ", sysFiles)}");

            if (sysFiles.Length == 0)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("windivert_file_missing")}[/]");
                _logger.LogError("WinDivert sys file not found");
            }
            else
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{string.Format(_localizationService.GetString("windivert_file_found"), Path.GetFileName(sysFiles[0]))}[/]");
                _logger.LogInformation($"WinDivert file found: {sysFiles[0]}");
            }
        }

        private async Task CheckVpnServicesAsync()
        {
            var vpnServices = new List<string>();

            try
            {
                var services = ServiceController.GetServices()
                    .Where(s => s.ServiceName.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                s.DisplayName.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                vpnServices.AddRange(services.Select(s => s.ServiceName));
            }
            catch (Exception ex)
            {
                _logger.LogError($"VPN services check failed: {ex.Message}", ex);
            }

            if (vpnServices.Count > 0)
            {
                var servicesList = string.Join(", ", vpnServices);
                AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{string.Format(_localizationService.GetString("vpn_services_found"), servicesList)}[/]");
                AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{_localizationService.GetString("vpn_disable_warning")}[/]");
                _logger.LogWarning($"VPN services found: {servicesList}");
            }
            else
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("vpn_check_passed")}[/]");
                _logger.LogInformation("VPN check passed");
            }
        }

        private async Task CheckSecureDnsAsync()
        {
            try
            {
                // Windows 11
                var output = await RunPowerShellCommandAsync(
                    "Get-ChildItem -Recurse -Path 'HKLM:System\\CurrentControlSet\\Services\\Dnscache\\InterfaceSpecificParameters\\' | " +
                    "Get-ItemProperty | Where-Object { $_.DohFlags -gt 0 } | Measure-Object | Select-Object -ExpandProperty Count"
                );

                if (int.TryParse(output?.Trim(), out var count) && count > 0)
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("secure_dns_passed")}[/]");
                    _logger.LogInformation("Secure DNS check passed");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{_localizationService.GetString("secure_dns_warning")}[/]");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{_localizationService.GetString("secure_dns_windows11")}[/]");
                    _logger.LogWarning("Secure DNS not configured");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Secure DNS check failed: {ex.Message}", ex);
            }
        }

        private async Task CheckWinDivertConflictsAsync()
        {
            try
            {
                var winwsRunning = Process.GetProcessesByName("winws").Length > 0;
                var windivertRunning = await IsServiceRunningAsync("WinDivert");
                var windivert14Running = await IsServiceRunningAsync("WinDivert14");

                if (!winwsRunning && (windivertRunning || windivert14Running))
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{_localizationService.GetString("windivert_conflict")}[/]");

                    if (windivertRunning)
                    {
                        await TryDeleteServiceAsync("WinDivert");
                    }

                    if (windivert14Running)
                    {
                        await TryDeleteServiceAsync("WinDivert14");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("windivert_check_passed")}[/]");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"WinDivert conflict check failed: {ex.Message}", ex);
            }
        }

        private async Task CheckConflictingBypassesAsync()
        {
            var conflictingServices = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2" };
            var foundConflicts = new List<string>();

            _logger.LogDebug($"Checking for conflicting services: {string.Join(", ", conflictingServices)}");

            foreach (var service in conflictingServices)
            {
                bool exists = await ServiceExistsAsync(service);
                _logger.LogDebug($"Service '{service}' exists: {exists}");

                if (exists) foundConflicts.Add(service);
            }

            if (foundConflicts.Count > 0)
            {
                _logger.LogWarning($"Conflicting services detected: {string.Join(", ", foundConflicts)}");
                var conflictsList = string.Join(", ", foundConflicts);
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{string.Format(_localizationService.GetString("conflicting_services_found"), conflictsList)}[/]");

                if (AnsiConsole.Confirm(_localizationService.GetString("remove_conflicting_services")))
                {
                    foreach (var service in foundConflicts)
                    {
                        await RemoveServiceAsync(service);
                    }

                    await TryDeleteServiceAsync("WinDivert");
                    await TryDeleteServiceAsync("WinDivert14");
                }
            }
            else
            {
                _logger.LogInformation("No conflicting services found");
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("conflict_check_passed")}[/]");
            }
        }

        private async Task ClearDiscordCacheAsync()
        {
            if (!AnsiConsole.Confirm(_localizationService.GetString("clear_discord_cache")))
            {
                return;
            }

            var discordProcesses = Process.GetProcessesByName("Discord");
            if (discordProcesses.Length > 0)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{_localizationService.GetString("closing_discord")}[/]");

                foreach (var process in discordProcesses)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                        AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("discord_closed")}[/]");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("discord_close_failed")}[/]");
                        _logger.LogError($"Failed to close Discord: {ex.Message}", ex);
                    }
                }
            }

            var discordCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord");
            var cacheDirs = new[] { "Cache", "Code Cache", "GPUCache" };

            foreach (var cacheDir in cacheDirs)
            {
                var dirPath = Path.Combine(discordCacheDir, cacheDir);
                if (Directory.Exists(dirPath))
                {
                    try
                    {
                        Directory.Delete(dirPath, true);
                        AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{string.Format(_localizationService.GetString("cache_deleted"), dirPath)}[/]");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{string.Format(_localizationService.GetString("cache_delete_failed"), dirPath)}[/]");
                        _logger.LogError($"Failed to delete cache {dirPath}: {ex.Message}", ex);
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{string.Format(_localizationService.GetString("cache_not_found"), dirPath)}[/]");
                }
            }
        }

        private async Task<bool> IsServiceRunningAsync(string serviceName)
        {
            try
            {
                using var service = new ServiceController(serviceName);
                return service.Status == ServiceControllerStatus.Running ||
                       service.Status == ServiceControllerStatus.StopPending;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> ServiceExistsAsync(string serviceName)
        {
            try
            {
                var vpnServices = new List<string>();
                var services = ServiceController.GetServices()
                    .Where(s => s.ServiceName == serviceName)
                    .ToList();

                vpnServices.AddRange(services.Select(s => s.ServiceName));

                return vpnServices.Count > 0;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private async Task<string> RunPowerShellCommandAsync(string command)
        {
            _logger.LogDebug($"Executing PowerShell command: {command}");

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-Command \"{command}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                var output = await outputTask;
                var errors = await errorTask;

                _logger.LogDebug($"PowerShell exit code: {process.ExitCode}");
                _logger.LogDebug($"Command output: {output.Trim()}");

                if (!string.IsNullOrEmpty(errors))
                {
                    _logger.LogWarning($"PowerShell errors: {errors.Trim()}");
                }

                return output;
            }
            catch
            {
                return null;
            }
        }

        private async Task CheckServicePatternAsync(string pattern, string foundMessage, string linkMessage, string passedMessage)
        {
            try
            {
                var matchingServices = ServiceController.GetServices()
                    .Where(s => Regex.IsMatch(s.ServiceName, pattern, RegexOptions.IgnoreCase) ||
                               Regex.IsMatch(s.DisplayName, pattern, RegexOptions.IgnoreCase))
                    .ToList();

                if (matchingServices.Count > 0)
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{foundMessage}[/]");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{linkMessage}[/]");
                    _logger.LogError($"{pattern} services found: {string.Join(", ", matchingServices.Select(s => s.ServiceName))}");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{passedMessage}[/]");
                    _logger.LogInformation($"{pattern} check passed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{pattern} check failed: {ex.Message}", ex);
            }
        }

        private async Task RemoveServiceAsync(string serviceName)
        {
            try
            {
                using var service = new ServiceController(serviceName);
                if (service.Status == ServiceControllerStatus.Running)
                {
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }

                var deleteProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = $"delete \"{serviceName}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    }
                };

                deleteProcess.Start();
                await deleteProcess.WaitForExitAsync();

                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{string.Format(_localizationService.GetString("service_removed"), serviceName)}[/]");
                _logger.LogInformation($"Service {serviceName} removed successfully");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{string.Format(_localizationService.GetString("service_removal_failed"), serviceName)}[/]");
                _logger.LogError($"Failed to remove service {serviceName}: {ex.Message}", ex);
            }
        }

        private async Task TryDeleteServiceAsync(string serviceName)
        {
            try
            {
                _logger.LogWarning($"ATTEMPTING TO DELETE SERVICE: {serviceName}");
                _logger.LogDebug($"Stopping service {serviceName} before deletion");

                var stopProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "net",
                        Arguments = $"stop \"{serviceName}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                stopProcess.Start();
                await stopProcess.WaitForExitAsync();

                _logger.LogDebug($"Executing: sc delete \"{serviceName}\"");

                var deleteProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sc",
                        Arguments = $"delete \"{serviceName}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                deleteProcess.Start();
                await deleteProcess.WaitForExitAsync();

                if (await ServiceExistsAsync(serviceName))
                {
                    _logger.LogError($"Service {serviceName} STILL EXISTS after deletion attempt");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{string.Format(_localizationService.GetString("service_delete_failed"), serviceName)}[/]");

                    var conflictingServices = new[] { "GoodbyeDPI" };
                    var foundConflicts = new List<string>();

                    foreach (var service in conflictingServices)
                    {
                        if (await ServiceExistsAsync(service))
                        {
                            foundConflicts.Add(service);
                        }
                    }

                    if (foundConflicts.Count > 0)
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{_localizationService.GetString("checking_conflicts")}[/]");
                        foreach (var service in foundConflicts)
                        {
                            await RemoveServiceAsync(service);
                        }

                        deleteProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "sc",
                                Arguments = $"delete \"{serviceName}\"",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            }
                        };

                        deleteProcess.Start();
                        await deleteProcess.WaitForExitAsync();

                        if (!await ServiceExistsAsync(serviceName))
                        {
                            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{string.Format(_localizationService.GetString("service_deleted_conflicts"), serviceName)}[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{string.Format(_localizationService.GetString("service_still_active"), serviceName)}[/]");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("no_conflicts_found")}[/]");
                    }
                }
                else
                {
                    _logger.LogInformation($"Service {serviceName} successfully deleted");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{string.Format(_localizationService.GetString("service_deleted"), serviceName)}[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{string.Format(_localizationService.GetString("service_delete_error"), serviceName, ex.Message)}[/]");
                _logger.LogError($"Error deleting service {serviceName}: {ex.Message}", ex);
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
                    _logger?.LogDebug("Disposing DiagnosticsService");
                }
                _disposed = true;
            }
        }
    }
}
