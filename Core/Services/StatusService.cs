using Spectre.Console;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.Models;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Services
{
    public class StatusService : IStatusService
    {
        private readonly string _listsPath;

        private readonly ConcurrentDictionary<string, int> _hostCounts = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _ipCounts = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private int _activeProfiles = 0;
        private int _defaultProfile = 0;

        private readonly ILocalizationService _localizationService;
        private readonly ILoggerService _logger;

        private static readonly Regex _profileRegex = new Regex(@"we have (\d+) user defined desync profile\(s\) and default low priority profile (\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _hostRegex = new Regex(@"Loaded (\d+) hosts from .+\\lists\\([^\\]+\.txt)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _ipRegex = new Regex(@"Loaded (\d+) IP/subnet\(s\) from .+\\lists\\([^\\]+\.txt)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _ipsetRegex = new Regex(@"Loaded (\d+) IP/subnet\(s\) from ipset file .+\\lists\\([^\\]+\.txt)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public StatusService(string appPath, ILocalizationService ls, ILoggerService logs)
        {
            _listsPath = Path.Combine(appPath, "lists");
            _localizationService = ls;
            _logger = logs;
            Directory.CreateDirectory(_listsPath);
        }

        public async Task Initialize()
            => await LoadInitialData();

        private async Task LoadInitialData()
        {
            try
            {
                var ipsets = 0;
                var hosts = 0;

                _logger.LogInformation("Loading whitelists...");
                if (!Directory.Exists(_listsPath))
                {
                    _logger.LogWarning($"Lists directory not found: {_listsPath}");
                    Directory.CreateDirectory(_listsPath);
                    return;
                }

                var listFiles = Directory.GetFiles(_listsPath, "*.txt");
                foreach (var file in listFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        var lines = await File.ReadAllLinesAsync(file);

                        if (fileName.Contains("ipset", StringComparison.OrdinalIgnoreCase))
                        {
                            var count = CountValidLines(lines);
                            _ipCounts[fileName] = count;
                            ipsets++;
                            _logger.LogDebug($"Loaded {count} IP/subnets from {fileName}");
                        }
                        else
                        {
                            var count = CountValidLines(lines);
                            _hostCounts[fileName] = count;
                            hosts++;
                            _logger.LogDebug($"Loaded {count} hosts from {fileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error loading whitelist file {file}", ex);
                    }
                }

                _logger.LogInformation($"Loaded {ipsets} ipsets and {hosts} hosts");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initializing status service", ex);
                throw;
            }
        }

        private int CountValidLines(string[] lines)
        {
            return lines.Count(line =>
                !string.IsNullOrWhiteSpace(line) &&
                !line.TrimStart().StartsWith("#", StringComparison.OrdinalIgnoreCase));
        }

        public void ProcessOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            try
            {
                // Parsing profile information
                var profileMatch = _profileRegex.Match(line);
                if (profileMatch.Success && profileMatch.Groups.Count >= 3)
                {
                    if (int.TryParse(profileMatch.Groups[1].Value, out var activeProfiles))
                    {
                        Interlocked.Exchange(ref _activeProfiles, activeProfiles);
                    }
                    if (int.TryParse(profileMatch.Groups[2].Value, out var defaultProfile))
                    {
                        Interlocked.Exchange(ref _defaultProfile, defaultProfile);
                    }
                    return;
                }

                // Parsing information about loaded hosts
                var hostMatch = _hostRegex.Match(line);
                if (hostMatch.Success && hostMatch.Groups.Count >= 3)
                {
                    var fileName = hostMatch.Groups[2].Value;
                    if (int.TryParse(hostMatch.Groups[1].Value, out var count))
                    {
                        _hostCounts.AddOrUpdate(fileName, count, (key, oldValue) => count);
                    }
                    return;
                }

                // Parsing information about loaded IPs/subnets
                var ipMatch = _ipRegex.Match(line);
                if (ipMatch.Success && ipMatch.Groups.Count >= 3)
                {
                    var fileName = ipMatch.Groups[2].Value;
                    if (int.TryParse(ipMatch.Groups[1].Value, out var count))
                    {
                        _ipCounts.AddOrUpdate(fileName, count, (key, oldValue) => count);
                    }
                    return;
                }

                // Parsing information about loaded IPs from ipset
                var ipsetMatch = _ipsetRegex.Match(line);
                if (ipsetMatch.Success && ipsetMatch.Groups.Count >= 3)
                {
                    var fileName = ipsetMatch.Groups[2].Value;
                    if (int.TryParse(ipsetMatch.Groups[1].Value, out var count))
                    {
                        _ipCounts.AddOrUpdate(fileName, count, (key, oldValue) => count);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing log line: {line}", ex);
            }
        }

        public async Task<ListStats> GetStatusStatsAsync()
        {
            var stats = new ListStats
            {
                TotalHosts = 0,
                TotalIPs = 0,
                ActiveProfiles = _activeProfiles,
                DefaultProfile = _defaultProfile.ToString()
            };

            // Summarize all hosts from all files
            foreach (var count in _hostCounts.Values)
            {
                stats.TotalHosts += count;
            }

            // Summarize all IP addresses from all files
            foreach (var count in _ipCounts.Values)
            {
                stats.TotalIPs += count;
            }

            return stats;
        }

        public async Task DisplayStatusAsync()
        {
            var stats = await GetStatusStatsAsync();
            AnsiConsole.MarkupLine($"  {_localizationService.GetString("hosts")}: [{ConsoleUI.greenName}]{stats.TotalHosts}[/]");
            AnsiConsole.MarkupLine($"  {_localizationService.GetString("ip_subnets")}: [{ConsoleUI.greenName}]{stats.TotalIPs}[/]");
            AnsiConsole.MarkupLine($"  {_localizationService.GetString("desync_profiles")}: [{ConsoleUI.greenName}]{stats.ActiveProfiles}[/]");
            AnsiConsole.MarkupLine($"  {_localizationService.GetString("low_priority_profile")}: [{ConsoleUI.greenName}]{stats.DefaultProfile}[/]");
        }
    }
}