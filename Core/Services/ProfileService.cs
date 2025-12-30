using Spectre.Console;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.Models;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Services
{
    public class ProfileService : IProfileService, IDisposable
    {
        private readonly string _profilesPath;
        private readonly string _batProfilesPath;

        private readonly ILoggerService _logger;
        private readonly ILocalizationService _localizationService;

        private readonly SemaphoreSlim _fileAccessLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, ZapretProfile> _profileCache = new ConcurrentDictionary<string, ZapretProfile>();
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public ProfileService(string appPath, ILocalizationService ls, ILoggerService logs)
        {
            _logger = logs;
            _localizationService = ls;
            _profilesPath = Path.Combine(appPath, "profiles");
            _batProfilesPath = Path.Combine(appPath, "bat_profiles");
            Directory.CreateDirectory(_profilesPath);
            Directory.CreateDirectory(_batProfilesPath);
        }

        public async Task<List<ZapretProfile>> LoadProfilesFromArchive(string extractedPath)
        {
            _logger.LogInformation($"Loading profiles from archive: {extractedPath}");

            await _fileAccessLock.WaitAsync();
            try
            {
                var profiles = new List<ZapretProfile>();
                var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Search for .bat files in different possible locations
                var searchPaths = new[]
                {
                    extractedPath,
                    Path.Combine(extractedPath, "bat_profiles"),
                    Path.Combine(extractedPath, "profiles"),
                    Path.Combine(extractedPath, "strategies")
                };

                foreach (var searchPath in searchPaths)
                {
                    if (!Directory.Exists(searchPath)) continue;

                    var batFiles = Directory.GetFiles(searchPath, "*.bat", SearchOption.TopDirectoryOnly);
                    foreach (var batFile in batFiles)
                    {
                        var fileName = Path.GetFileName(batFile);
                        if (processedFiles.Contains(fileName)) continue;

                        if (fileName.Equals("service.bat", StringComparison.OrdinalIgnoreCase)) continue;

                        try
                        {
                            var destBatFile = Path.Combine(_batProfilesPath, fileName);
                            File.Copy(batFile, destBatFile, true);
                            processedFiles.Add(fileName);

                            // Parsing profile
                            var profile = ParseBatFile(batFile);
                            if (profile != null && IsValidProfile(profile))
                            {
                                profiles.Add(profile);
                                await SaveProfile(profile);
                                AnsiConsole.MarkupLine($"  {_localizationService.GetString("parsed_profile")}: [{ConsoleUI.greenName}]{{0}}[/]", profile.Name);
                                _profileCache[profile.Name] = profile;
                            }
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("profile_parse_fail")} {{0}}: {{1}}[/]", fileName, ex.Message);
                            _logger.LogError($"Error parsing profile from {fileName}: {ex.Message}", ex);
                        }
                    }
                }

                _logger.LogInformation($"Loaded {profiles.Count} profiles from archive");
                _lastCacheUpdate = DateTime.Now;
                return profiles;
            }
            finally
            {
                _fileAccessLock.Release();
            }
        }

        private bool IsValidProfile(ZapretProfile profile)
        {
            return profile != null &&
                   !string.IsNullOrWhiteSpace(profile.Name) &&
                   profile.Arguments != null &&
                   profile.Arguments.Count > 0;
        }

        public ZapretProfile ParseBatFile(string filePath)
        {
            _logger.LogInformation($"Parsing profile: {filePath}");
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to read file {filePath}: {ex.Message}", ex);
                return null;
            }

            // Determine the profile name from the file name
            var profileName = fileName.Replace("general", "", StringComparison.OrdinalIgnoreCase)
                                      .Replace("(", "")
                                      .Replace(")", "")
                                      .Replace("_", " ")
                                      .Trim();

            if (string.IsNullOrWhiteSpace(profileName) || profileName.Equals("general", StringComparison.OrdinalIgnoreCase))
            {
                profileName = "Default";
            }

            var profile = new ZapretProfile
            {
                Name = profileName,
                Description = $"Profile from '{fileName}.bat'",
                Arguments = new List<string>(),
                Id = Guid.NewGuid().ToString()
            };

            var fullCommand = new StringBuilder();
            bool inCommand = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.Contains("winws.exe", StringComparison.OrdinalIgnoreCase))
                {
                    inCommand = true;
                    var startIndex = trimmedLine.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase) + "winws.exe".Length;
                    if (startIndex < trimmedLine.Length)
                    {
                        fullCommand.Append(trimmedLine.Substring(startIndex).Trim());
                    }
                }
                else if (inCommand && (trimmedLine.EndsWith("^") || trimmedLine.Contains("winws.exe", StringComparison.OrdinalIgnoreCase)))
                {
                    var lineToAdd = trimmedLine.TrimEnd('^').Trim();
                    if (!string.IsNullOrWhiteSpace(lineToAdd))
                    {
                        fullCommand.Append(" ").Append(lineToAdd);
                    }
                }
                else if (inCommand && !string.IsNullOrWhiteSpace(trimmedLine) &&
                        !trimmedLine.StartsWith("call", StringComparison.OrdinalIgnoreCase) &&
                        !trimmedLine.StartsWith("set", StringComparison.OrdinalIgnoreCase) &&
                        !trimmedLine.StartsWith("cd", StringComparison.OrdinalIgnoreCase) &&
                        !trimmedLine.StartsWith("echo", StringComparison.OrdinalIgnoreCase) &&
                        !trimmedLine.StartsWith("@", StringComparison.OrdinalIgnoreCase))
                {
                    fullCommand.Append(" ").Append(trimmedLine);
                }
                else if (inCommand && string.IsNullOrWhiteSpace(trimmedLine))
                {
                    break;
                }
            }

            if (fullCommand.Length > 0)
            {
                profile.Arguments = ParseArguments(fullCommand.ToString());
                _logger.LogInformation($"Profile {profile.Name} parsed");
                return profile;
            }

            _logger.LogInformation($"The profile was skipped because the fullCommand length was 0: {filePath}");
            return null;
        }

        private List<string> ParseArguments(string commandLine)
        {
            var args = new List<string>();
            var currentArg = new StringBuilder();
            bool inQuotes = false;
            bool escapeNext = false;

            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];

                if (escapeNext)
                {
                    currentArg.Append(c);
                    escapeNext = false;
                    continue;
                }

                if (c == '^' && !inQuotes)
                {
                    escapeNext = true;
                    continue;
                }

                if (c == '"' && (i == 0 || commandLine[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (currentArg.Length > 0)
                    {
                        var arg = currentArg.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(arg) && IsValidArgument(arg))
                        {
                            args.Add(arg);
                        }
                        currentArg.Clear();
                    }
                    continue;
                }

                currentArg.Append(c);
            }

            if (currentArg.Length > 0)
            {
                var arg = currentArg.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(arg) && IsValidArgument(arg))
                {
                    args.Add(arg);
                }
            }

            return args;
        }

        private bool IsValidArgument(string arg)
        {
            // Save all arguments
            return !arg.Equals("--new", StringComparison.OrdinalIgnoreCase) &&
                   !arg.Equals("/min", StringComparison.OrdinalIgnoreCase) &&
                   !arg.StartsWith("start ", StringComparison.OrdinalIgnoreCase) &&
                   !arg.StartsWith("\"zapret:", StringComparison.OrdinalIgnoreCase) &&
                   !arg.StartsWith("call ", StringComparison.OrdinalIgnoreCase) &&
                   !arg.StartsWith("chcp ", StringComparison.OrdinalIgnoreCase) &&
                   !arg.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) &&
                   !arg.StartsWith("set ", StringComparison.OrdinalIgnoreCase) &&
                   !arg.StartsWith("echo", StringComparison.OrdinalIgnoreCase) &&
                   !arg.StartsWith("@echo", StringComparison.OrdinalIgnoreCase);
        }

        public async Task SaveProfile(ZapretProfile profile)
        {
            profile.UpdatedAt = DateTime.Now;
            var fileName = SanitizeFileName(profile.Name) + ".json";
            var profilePath = Path.Combine(_profilesPath, fileName);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(profile, options);
            await File.WriteAllTextAsync(profilePath, json);
            _logger.LogInformation($"Saved profile {profile.Name}");
        }

        public async Task<List<ZapretProfile>> GetAvailableProfilesAsync()
        {
            if (_profileCache.Count > 0 && (DateTime.Now - _lastCacheUpdate) < CacheDuration)
            {
                return _profileCache.Values.OrderBy(p => p.Name).ToList();
            }

            await _fileAccessLock.WaitAsync();
            try
            {
                var profiles = new List<ZapretProfile>();
                var profileFiles = Directory.GetFiles(_profilesPath, "*.json");

                foreach (var file in profileFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var profile = JsonSerializer.Deserialize<ZapretProfile>(json);
                        if (profile != null)
                        {
                            profiles.Add(profile);
                            _profileCache[profile.Name] = profile;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to load profile from {file}: {ex.Message}", ex);
                        AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("profile_load_fail")} {{0}}: {{1}}[/]", Path.GetFileName(file), ex.Message);
                    }
                }

                _lastCacheUpdate = DateTime.Now;
                return profiles.OrderBy(p => p.Name).ToList();
            }
            finally
            {
                _fileAccessLock.Release();
            }
        }

        public async Task<ZapretProfile> GetProfileByName(string name)
        {
            var profileFiles = Directory.GetFiles(_profilesPath, "*.json");
            foreach (var file in profileFiles)
            {
                if (Path.GetFileNameWithoutExtension(file).Equals(SanitizeFileName(name), StringComparison.OrdinalIgnoreCase))
                {
                    var json = await File.ReadAllTextAsync(file);
                    return JsonSerializer.Deserialize<ZapretProfile>(json);
                }
            }
            return null;
        }

        private string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Concat(name.Split(invalidChars));
            return string.IsNullOrWhiteSpace(sanitized) ? "unnamed_profile" : sanitized;
        }

        public void ClearCache()
        {
            _profileCache.Clear();
            _lastCacheUpdate = DateTime.MinValue;
        }

        public void Dispose()
        {
            _fileAccessLock.Dispose();
        }
    }
}