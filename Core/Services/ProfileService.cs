using System.Text;
using System.Text.Json;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Models;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Services
{
    public class ProfileService : IProfileService
    {
        private readonly string _profilesPath;
        private readonly string _batProfilesPath;

        public ProfileService(string appPath)
        {
            _profilesPath = Path.Combine(appPath, "profiles");
            _batProfilesPath = Path.Combine(appPath, "bat_profiles");
            Directory.CreateDirectory(_profilesPath);
            Directory.CreateDirectory(_batProfilesPath);
        }

        public async Task<List<ZapretProfile>> LoadProfilesFromArchive(string extractedPath)
        {
            var profiles = new List<ZapretProfile>();
            var processedFiles = new List<string>();

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
                    if (processedFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase)) continue;

                    if (fileName.Equals("service.bat", StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        var destBatFile = Path.Combine(_batProfilesPath, fileName);
                        File.Copy(batFile, destBatFile, true);
                        processedFiles.Add(fileName);

                        // Parsing profile
                        var profile = ParseBatFile(batFile);
                        if (profile != null && profile.Arguments?.Count > 0)
                        {
                            profiles.Add(profile);
                            await SaveProfile(profile);
                            ConsoleUI.WriteLine($"  [✓] Parsed profile: {profile.Name}", ConsoleUI.green);
                        }
                    }
                    catch (Exception ex)
                    {
                        ConsoleUI.WriteLine($"[✗] Failed to parse profile from {fileName}: {ex.Message}", ConsoleUI.red);
                    }
                }
            }

            return profiles;
        }

        public ZapretProfile ParseBatFile(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var lines = File.ReadAllLines(filePath);

            // Determine the profile name from the file name
            var profileName = fileName.Replace("general", "")
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
                Arguments = new List<string>()
            };

            var fullCommand = new StringBuilder();
            bool inCommand = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.Contains("winws.exe"))
                {
                    inCommand = true;
                    var startIndex = trimmedLine.IndexOf("winws.exe") + "winws.exe".Length;
                    if (startIndex < trimmedLine.Length)
                    {
                        fullCommand.Append(trimmedLine.Substring(startIndex).Trim());
                    }
                }
                else if (inCommand && (trimmedLine.EndsWith("^") || trimmedLine.Contains("winws.exe")))
                {
                    var lineToAdd = trimmedLine.TrimEnd('^').Trim();
                    if (!string.IsNullOrWhiteSpace(lineToAdd))
                    {
                        fullCommand.Append(" ").Append(lineToAdd);
                    }
                }
                else if (inCommand && !string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("call") && !trimmedLine.StartsWith("set") && !trimmedLine.StartsWith("cd") && !trimmedLine.StartsWith("echo") && !trimmedLine.StartsWith("@"))
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
                return profile;
            }

            return null;
        }

        private List<string> ParseArguments(string commandLine)
        {
            var args = new List<string>();
            var currentArg = new StringBuilder();
            bool inQuotes = false;
            bool escapeNext = false;
            bool isStartArg = true;

            foreach (char c in commandLine)
            {
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

                if (c == '"' && !escapeNext)
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (currentArg.Length > 0 || isStartArg)
                    {
                        var arg = currentArg.ToString();
                        if (!string.IsNullOrWhiteSpace(arg) && IsValidArgument(arg))
                        {
                            args.Add(arg);
                        }
                        currentArg.Clear();
                        isStartArg = false;
                    }
                    continue;
                }

                currentArg.Append(c);
            }

            if (currentArg.Length > 0)
            {
                var arg = currentArg.ToString();
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
        }

        public async Task<List<ZapretProfile>> GetAvailableProfilesAsync()
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
                    }
                }
                catch (Exception ex)
                {
                    ConsoleUI.WriteLine($"[✗] Failed to load profile {Path.GetFileName(file)}: {ex.Message}", ConsoleUI.red);
                }
            }

            return profiles.OrderBy(p => p.Name).ToList();
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
    }
}