using Microsoft.Win32;
using Spectre.Console;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Services
{
    public class ExportService : IExportService, IDisposable
    {
        private readonly string _appPath;
        private readonly IFileSystemService _fileSystemService;
        private readonly IStatusService _statusService;
        private readonly IProfileService _profileService;
        private readonly IConfigService _configService;
        private readonly IZapretManager _zapretManager;
        private readonly ILocalizationService _localizationService;
        private readonly ILoggerService _logger;
        private bool _disposed = false;

        public ExportService(
            string appPath,
            IFileSystemService fileSystemService,
            IStatusService statusService,
            IProfileService profileService,
            IConfigService configService,
            IZapretManager zapretManager,
            ILocalizationService localizationService,
            ILoggerService logger)
        {
            _appPath = appPath;
            _fileSystemService = fileSystemService;
            _statusService = statusService;
            _profileService = profileService;
            _configService = configService;
            _zapretManager = zapretManager;
            _localizationService = localizationService;
            _logger = logger;
        }

        public async Task<bool> ExportDataAsync()
        {
            _logger.LogInformation("Starting data export process");
            try
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{string.Format(_localizationService.GetString("exporting"))}[/]");
                var exportPath = GetExportPath();
                var tempExportDir = Path.Combine(Path.GetTempPath(), $"zapret_export_{Guid.NewGuid().ToString("N")}");
                Directory.CreateDirectory(tempExportDir);

                // Create directory structure
                var logsTempDir = Path.Combine(tempExportDir, "logs");
                var listsTempDir = Path.Combine(tempExportDir, "lists");
                var profilesTempDir = Path.Combine(tempExportDir, "profiles");
                var binTempDir = Path.Combine(tempExportDir, "bin");
                var configTempDir = Path.Combine(tempExportDir, "config");

                Directory.CreateDirectory(logsTempDir);
                Directory.CreateDirectory(listsTempDir);
                Directory.CreateDirectory(profilesTempDir);
                Directory.CreateDirectory(binTempDir);
                Directory.CreateDirectory(configTempDir);

                // 1. Copy logs
                await CopyDirectoryAsync(Path.Combine(_appPath, "logs"), logsTempDir);

                // 2. Copy lists
                await CopyDirectoryAsync(Path.Combine(_appPath, "lists"), listsTempDir);

                // 3. Copy profiles
                await CopyDirectoryAsync(Path.Combine(_appPath, "profiles"), profilesTempDir);

                // 4. Copy bin directory (excluding large log files if any)
                var binSource = Path.Combine(_appPath, "bin");
                if (Directory.Exists(binSource))
                {
                    foreach (var file in Directory.GetFiles(binSource))
                    {
                        // Skip large log files that might be in bin directory
                        if (Path.GetFileName(file).EndsWith(".log", StringComparison.OrdinalIgnoreCase) && new FileInfo(file).Length > 10 * 1024 * 1024)
                        {
                            _logger.LogInformation($"Skipping large log file: {Path.GetFileName(file)}");
                            continue;
                        }
                        File.Copy(file, Path.Combine(binTempDir, Path.GetFileName(file)), true);
                    }
                }

                // 5. Copy appconfig.json
                var appConfigPath = Path.Combine(_appPath, "appconfig.json");
                if (File.Exists(appConfigPath))
                {
                    var destConfigPath = Path.Combine(configTempDir, "appconfig.json");
                    File.Copy(appConfigPath, destConfigPath, true);
                    _logger.LogInformation($"Copied appconfig.json to export");
                }

                // 6. Generate debug info
                var debugInfo = await GenerateDebugInfoAsync();
                var debugFilePath = Path.Combine(tempExportDir, "DEBUG.txt");
                await File.WriteAllTextAsync(debugFilePath, debugInfo);

                // Create zip archive
                var zipPath = Path.Combine(exportPath, $"ZapretExport_{DateTime.Now:yyyyMMdd_HHmmss}.EZ");
                await CreateZipArchiveAsync(tempExportDir, zipPath);

                // Cleanup temp directory
                Directory.Delete(tempExportDir, true);

                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{string.Format(_localizationService.GetString("export_success"), zipPath)}[/]");
                _logger.LogInformation($"Export completed successfully: {zipPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Export failed", ex);
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{string.Format(_localizationService.GetString("export_fail"), ex.Message)}[/]");
                return false;
            }
        }

        private async Task CopyDirectoryAsync(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir))
            {
                _logger.LogWarning($"Source directory does not exist: {sourceDir}");
                return;
            }

            _logger.LogInformation($"Copying directory: {sourceDir} -> {targetDir}");

            // Create target directory
            Directory.CreateDirectory(targetDir);

            // Copy files
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(targetDir, Path.GetFileName(file));

                try
                {
                    File.Copy(file, destFile, true);
                    await Task.Delay(1); // Prevent CPU spike on many files
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to copy file {file}: {ex.Message}");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]Warning: Failed to copy {Path.GetFileName(file)}[/]");
                }
            }

            // Copy subdirectories recursively
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                await CopyDirectoryAsync(subDir, destSubDir);
            }
        }

        private string GetExportPath()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        private async Task<string> GenerateDebugInfoAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Zapret CLI Export Data");
            sb.AppendLine($"# Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"# Application Version: {MainMenu.version}");
            sb.AppendLine();
            sb.AppendLine("## System Information");
            sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");
            sb.AppendLine($".NET Runtime: {Environment.Version}");
            sb.AppendLine($"Machine Name: {Environment.MachineName}");
            sb.AppendLine($"User Name: {Environment.UserName}");
            sb.AppendLine($"Uptime: {GetSystemUptime()}");
            sb.AppendLine();

            sb.AppendLine("## Hardware Information");
            sb.AppendLine($"Processor: {GetProcessorInfo()}");
            sb.AppendLine($"RAM: {GetMemoryInfo()}");
            sb.AppendLine($"Disk Space (App Directory): {GetDiskSpaceInfo(_appPath)}");
            sb.AppendLine();

            sb.AppendLine("## Network Configuration");
            sb.AppendLine($"BFE Service Status: {await GetBfeServiceStatusAsync()}");
            sb.AppendLine($"TCP Timestamps: {await GetTcpTimestampsStatusAsync()}");
            sb.AppendLine($"VPN Services: {await GetVpnServicesStatusAsync()}");
            sb.AppendLine($"System Proxy: {GetProxySettings()}");
            sb.AppendLine($"Secure DNS (DoH): {await GetSecureDnsStatusAsync()}");
            sb.AppendLine();

            sb.AppendLine("## Application Configuration");
            var config = _configService.GetConfig();
            sb.AppendLine($"AutoStart: {config.AutoStart}");
            sb.AppendLine($"AutoStart Profile: {config.AutoStartProfile ?? "Not set"}");
            sb.AppendLine($"Game Filter Enabled: {config.GameFilterEnabled}");
            sb.AppendLine($"Current Language: {config.Language ?? "System default"}");
            sb.AppendLine();

            sb.AppendLine("## Application Status");
            var isRunning = _zapretManager.IsRunning();
            sb.AppendLine($"Service Status: {(isRunning ? "Running" : "Stopped")}");
            if (isRunning)
            {
                var currentProfile = _zapretManager.GetCurrentProfile();
                sb.AppendLine($"Active Profile: {currentProfile?.Name ?? "Unknown"}");
                sb.AppendLine($"Profile Description: {currentProfile?.Description ?? "N/A"}");
            }

            var stats = await _statusService.GetStatusStatsAsync();
            sb.AppendLine($"Total Hosts: {stats.TotalHosts}");
            sb.AppendLine($"Total IP/Subnets: {stats.TotalIPs}");
            sb.AppendLine($"Active Desync Profiles: {stats.ActiveProfiles}");
            sb.AppendLine($"Default Low Priority Profile: {stats.DefaultProfile}");
            sb.AppendLine();

            sb.AppendLine("## Installed Version");
            var versionFile = Path.Combine(_appPath, "VERSION");
            if (File.Exists(versionFile))
            {
                sb.AppendLine($"Zapret Version: {await File.ReadAllTextAsync(versionFile)}");
            }
            else
            {
                sb.AppendLine("Zapret Version: Not found");
            }
            sb.AppendLine();

            sb.AppendLine("## Conflict Detection");
            sb.AppendLine($"AdGuard: {await CheckAdguardAsync()}");
            sb.AppendLine($"Killer Network Services: {await CheckServicePatternAsync("Killer", false)}");
            sb.AppendLine($"Intel Connectivity Services: {await CheckServicePatternAsync("Intel.*Connectivity.*Network", false)}");
            sb.AppendLine($"Checkpoint VPN: {await CheckServicePatternAsync("(TracSrvWrapper|EPWD)", false)}");
            sb.AppendLine($"SmartByte: {await CheckServicePatternAsync("SmartByte", false)}");
            sb.AppendLine($"WinDivert Driver: {CheckWinDivertFileStatus()}");
            sb.AppendLine();

            sb.AppendLine("## Available Profiles");
            var profiles = await _profileService.GetAvailableProfilesAsync();
            sb.AppendLine($"Total Profiles: {profiles.Count}");
            foreach (var profile in profiles)
            {
                sb.AppendLine($"- {profile.Name}: {profile.Description}");
            }

            return sb.ToString();
        }

        private string GetSystemUptime()
        {
            try
            {
                using var uptime = new PerformanceCounter("System", "System Up Time", true);
                uptime.NextValue(); // First call returns 0
                var uptimeSpan = TimeSpan.FromSeconds(uptime.NextValue());
                return $"{(int)uptimeSpan.TotalDays} days, {uptimeSpan.Hours} hours, {uptimeSpan.Minutes} minutes";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetProcessorInfo()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key != null)
                {
                    var processorName = key.GetValue("ProcessorNameString")?.ToString() ?? "Unknown";
                    var numCores = Environment.ProcessorCount;
                    return $"{processorName} ({numCores} cores)";
                }
            }
            catch
            {
                // Fall back to basic info
            }
            return $"{Environment.ProcessorCount} cores";
        }

        private string GetMemoryInfo()
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                memStatus.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    var totalMB = memStatus.ullTotalPhys / 1024 / 1024;
                    var availMB = memStatus.ullAvailPhys / 1024 / 1024;
                    return $"{totalMB:N0} MB total, {availMB:N0} MB available";
                }
            }
            catch
            {
                // Fall back
            }
            return "Memory information unavailable";
        }

        private string GetDiskSpaceInfo(string path)
        {
            try
            {
                var drive = Path.GetPathRoot(path);
                var driveInfo = new DriveInfo(drive);
                var totalGB = driveInfo.TotalSize / 1024.0 / 1024.0 / 1024.0;
                var freeGB = driveInfo.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                return $"{freeGB:F2} GB free of {totalGB:F2} GB total on {drive}";
            }
            catch
            {
                return "Disk information unavailable";
            }
        }

        private async Task<string> GetBfeServiceStatusAsync()
        {
            try
            {
                using var service = new ServiceController("BFE");
                return service.Status.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"BFE service check failed: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        private async Task<string> GetTcpTimestampsStatusAsync()
        {
            try
            {
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

                var match = Regex.Match(output, "timestamps\\s+([^\\s]+)", RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count >= 2)
                {
                    return match.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"TCP timestamps check failed: {ex.Message}");
                return $"Error: {ex.Message}";
            }
            return "Unknown";
        }

        private async Task<string> GetVpnServicesStatusAsync()
        {
            try
            {
                var vpnServices = ServiceController.GetServices()
                    .Where(s => s.ServiceName.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               s.DisplayName.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(s => $"{s.ServiceName} ({s.Status})")
                    .ToList();

                return vpnServices.Count > 0
                    ? string.Join(", ", vpnServices)
                    : "No VPN services detected";
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"VPN services check failed: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        private string GetProxySettings()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                if (key != null)
                {
                    var proxyEnable = key.GetValue("ProxyEnable") as int?;
                    var proxyServer = key.GetValue("ProxyServer") as string;

                    if (proxyEnable == 1 && !string.IsNullOrEmpty(proxyServer))
                    {
                        return $"Enabled: {proxyServer}";
                    }
                    return "Disabled";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Proxy settings check failed: {ex.Message}");
                return $"Error: {ex.Message}";
            }
            return "Unknown";
        }

        private async Task<string> GetSecureDnsStatusAsync()
        {
            try
            {
                // Check if Secure DNS is enabled on Windows 11
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-Command \"Get-DnsClientDohServerAddress | Measure-Object | Select-Object -ExpandProperty Count\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (int.TryParse(output?.Trim(), out var count) && count > 0)
                {
                    return "Enabled";
                }
                return "Disabled";
            }
            catch
            {
                return "Unknown";
            }
        }

        private async Task<string> CheckAdguardAsync()
        {
            try
            {
                var processes = Process.GetProcessesByName("AdguardSvc");
                return processes.Length > 0 ? "Detected" : "Not detected";
            }
            catch
            {
                return "Unknown";
            }
        }

        private async Task<string> CheckServicePatternAsync(string pattern, bool detailed = true)
        {
            try
            {
                var matchingServices = ServiceController.GetServices()
                    .Where(s => Regex.IsMatch(s.ServiceName, pattern, RegexOptions.IgnoreCase) ||
                               Regex.IsMatch(s.DisplayName, pattern, RegexOptions.IgnoreCase))
                    .ToList();

                if (matchingServices.Count == 0)
                {
                    return "None detected";
                }

                if (detailed)
                {
                    return string.Join(", ", matchingServices.Select(s => $"{s.ServiceName} ({s.Status})"));
                }
                return $"Detected ({matchingServices.Count})";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string CheckWinDivertFileStatus()
        {
            var binPath = Path.Combine(_appPath, "bin");
            if (!Directory.Exists(binPath))
            {
                return "Bin directory missing";
            }

            var sysFiles = Directory.GetFiles(binPath, "*.sys");
            return sysFiles.Length > 0
                ? $"Found: {Path.GetFileName(sysFiles[0])}"
                : "Not found";
        }

        private async Task CreateZipArchiveAsync(string sourceDirectory, string zipPath)
        {
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"temp_export_{Guid.NewGuid().ToString("N")}.zip");

            try
            {
                // Create zip archive using System.IO.Compression
                ZipFile.CreateFromDirectory(sourceDirectory, tempZipPath);

                // Move to final location with .EZ extension
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                File.Move(tempZipPath, zipPath);
            }
            finally
            {
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
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
                    _logger?.LogDebug("Disposing ExportService");
                }
                _disposed = true;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}
