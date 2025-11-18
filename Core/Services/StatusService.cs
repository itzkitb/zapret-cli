using System.Text.RegularExpressions;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Models;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Services
{
    public class StatusService : IStatusService
    {
        private readonly string _listsPath;

        private Dictionary<string, int> _hostCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _ipCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private int _activeProfiles = 0;
        private int _defaultProfile = 0;

        public StatusService(string appPath)
        {
            _listsPath = Path.Combine(appPath, "lists");
            Directory.CreateDirectory(_listsPath);
        }

        public async Task Initialize()
            => await LoadInitialData();

        private async Task LoadInitialData()
        {
            var listFiles = Directory.GetFiles(_listsPath, "*.txt");
            foreach (var file in listFiles)
            {
                var fileName = Path.GetFileName(file);
                var lines = await File.ReadAllLinesAsync(file);

                if (fileName.Contains("exclude") || fileName.Contains("ipset"))
                {
                    _ipCounts[fileName] = CountValidLines(lines);
                }
                else
                {
                    _hostCounts[fileName] = CountValidLines(lines);
                }
            }
        }

        private int CountValidLines(string[] lines)
        {
            int count = 0;
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line) && !line.Trim().StartsWith("#"))
                {
                    count++;
                }
            }
            return count;
        }

        public void ProcessOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            // Parsing profile information
            var profileMatch = Regex.Match(line, @"we have (\d+) user defined desync profile\(s\) and default low priority profile (\d+)");
            if (profileMatch.Success && profileMatch.Groups.Count >= 3)
            {
                if (int.TryParse(profileMatch.Groups[1].Value, out var activeProfiles))
                {
                    _activeProfiles = activeProfiles;
                }
                if (int.TryParse(profileMatch.Groups[2].Value, out var defaultProfile))
                {
                    _defaultProfile = defaultProfile;
                }
                return;
            }

            // Parsing information about loaded hosts
            var hostMatch = Regex.Match(line, @"Loaded (\d+) hosts from .+\\lists\\([^\\]+\.txt)");
            if (hostMatch.Success && hostMatch.Groups.Count >= 3)
            {
                var fileName = hostMatch.Groups[2].Value;
                if (int.TryParse(hostMatch.Groups[1].Value, out var count))
                {
                    _hostCounts[fileName] = count;
                }
                return;
            }

            // Parsing information about loaded IPs/subnets
            var ipMatch = Regex.Match(line, @"Loaded (\d+) IP/subnet\(s\) from .+\\lists\\([^\\]+\.txt)");
            if (ipMatch.Success && ipMatch.Groups.Count >= 3)
            {
                var fileName = ipMatch.Groups[2].Value;
                if (int.TryParse(ipMatch.Groups[1].Value, out var count))
                {
                    _ipCounts[fileName] = count;
                }
                return;
            }

            // Parsing information about loaded IPs from ipset
            var ipsetMatch = Regex.Match(line, @"Loaded (\d+) IP/subnet\(s\) from ipset file .+\\lists\\([^\\]+\.txt)");
            if (ipsetMatch.Success && ipsetMatch.Groups.Count >= 3)
            {
                var fileName = ipsetMatch.Groups[2].Value;
                if (int.TryParse(ipsetMatch.Groups[1].Value, out var count))
                {
                    _ipCounts[fileName] = count;
                }
                return;
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
            ConsoleUI.WriteLine($"  Hosts: {stats.TotalHosts}", ConsoleUI.blue);
            ConsoleUI.WriteLine($"  IP/subnets: {stats.TotalIPs}", ConsoleUI.blue);
            ConsoleUI.WriteLine($"  Desync profiles: {stats.ActiveProfiles}", ConsoleUI.blue);
            ConsoleUI.WriteLine($"  Low priority profile: {stats.DefaultProfile}", ConsoleUI.blue);
        }
    }
}