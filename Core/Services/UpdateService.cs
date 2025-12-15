using SharpCompress.Archives;
using SharpCompress.Common;
using Spectre.Console;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.ServiceProcess;
using System.Text.Json;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.Models;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Services
{
    public class UpdateService : IUpdateService, IDisposable
    {
        private readonly string _appPath;
        private readonly HttpClient _httpClient;
        private readonly IProfileService _profileManager;
        private readonly IZapretManager _zapretManager;
        private readonly ILocalizationService _localizationService;
        private readonly ILoggerService _logger;
        private const string GitHubRepo = "https://github.com/Flowseal/zapret-discord-youtube";
        private const string ApiUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
        private const string CliRepo = "https://github.com/itzkitb/zapret-cli";
        private const string CliApiUrl = "https://api.github.com/repos/itzkitb/zapret-cli/releases/latest";

        public UpdateService(string appPath, IProfileService ps, IZapretManager zm, ILocalizationService ls, ILoggerService logs)
        {
            _appPath = appPath;
            _profileManager = ps;
            _zapretManager = zm;
            _localizationService = ls;
            _logger = logs;

            _httpClient = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true });
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Zapret-CLI/{GetCliVersion()}");
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                var versionFile = Path.Combine(_appPath, "VERSION");

                if (File.Exists(versionFile))
                {
                    var currentVersion = GetCurrentVersion();
                    _logger.LogInformation($"Local Zapret version: {currentVersion}");

                    var release = await GetLatestRelease();
                    var newVersion = new Version(release.TagName.Replace("b", "").Replace("a", ""));
                    _logger.LogInformation($"Latest Zapret version: {newVersion}");

                    if (release != null && currentVersion < newVersion)
                    {

                        AnsiConsole.MarkupLine($"{_localizationService.GetString("new_version_title")}: [{ConsoleUI.greenName}]{{0}}[/]", release.TagName);
                        AnsiConsole.MarkupLine($"{_localizationService.GetString("current_version")}: [{ConsoleUI.greenName}]{{0}}[/]\nNew version: [{ConsoleUI.greenName}]{{1}}[/]", currentVersion, newVersion);

                        var shortChangelog = release.Body.Length > 200
                            ? release.Body.Substring(0, 200) + "..."
                            : release.Body;
                        AnsiConsole.MarkupLine($"{_localizationService.GetString("changelog")}: [{ConsoleUI.greyName}]{{0}}[/]", shortChangelog);


                        var result = AnsiConsole.Confirm(_localizationService.GetString("update_ask"));
                        if (result)
                        {
                            await DownloadLatestReleaseAsync();
                        }
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("first_launch")}[/]");
                    await DownloadLatestReleaseAsync();
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("updates_check_fail")}: {{0}}[/]", ex.Message);
                _logger.LogInformation("Something went wrong1", ex);
                await Task.Delay(500);
            }
        }

        public async Task CheckForCliUpdatesAsync()
        {
            try
            {
                var currentVersion = GetCliVersion();
                _logger.LogInformation($"Local CLI version: {currentVersion}");

                var release = await GetLatestCliRelease();
                var newVersion = new Version(release.TagName);
                _logger.LogInformation($"Latest CLI version: {newVersion}");

                if (release != null && currentVersion < newVersion)
                {
                    AnsiConsole.MarkupLine($"{_localizationService.GetString("new_cli_version_title")}: [{ConsoleUI.greenName}]{{0}}[/]", release.TagName);
                    AnsiConsole.MarkupLine($"{_localizationService.GetString("current_version")}: [{ConsoleUI.greenName}]{{0}}[/]\nNew version: [{ConsoleUI.greenName}]{{1}}[/]", currentVersion, newVersion);

                    var shortChangelog = release.Body.Length > 200
                        ? release.Body.Substring(0, 200) + "..."
                        : release.Body;
                    AnsiConsole.MarkupLine($"{_localizationService.GetString("changelog")}: [{ConsoleUI.greyName}]{{0}}[/]", shortChangelog);
                    var result = AnsiConsole.Confirm(_localizationService.GetString("update_ask"));
                    if (result)
                    {
                        await UpdateCliAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("cli_updates_check_fail")}: {{0}}[/]", ex.Message);
                _logger.LogInformation("Something went wrong1", ex);
                await Task.Delay(500);
            }
        }

        private async Task<GithubRelease> GetLatestCliRelease()
        {
            var response = await _httpClient.GetStringAsync(CliApiUrl);
            return JsonSerializer.Deserialize<GithubRelease>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        private Version GetCliVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        }

        public async Task UpdateCliAsync()
        {
            try
            {
                _logger.LogInformation($"Downloading new CLI version...");
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("downloading")}[/]");

                var release = await GetLatestCliRelease();
                if (release == null || !release.Assets.Any())
                {
                    throw new Exception("No release assets found");
                }

                // Looking for the .exe installer in the release
                var asset = release.Assets.FirstOrDefault(a =>
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    a.Name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0);

                if (asset == null)
                {
                    throw new Exception("No setup executable found in release assets");
                }

                // Create a temporary download folder
                var tempDir = Path.Combine(Path.GetTempPath(), "zapret_cli_update");
                Directory.CreateDirectory(tempDir);
                var downloadPath = Path.Combine(tempDir, asset.Name);

                // Downloading the file
                using (var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                        }
                    }
                }

                _logger.LogInformation($"Launching installer ({downloadPath})...");
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("launching_installer")}[/]");

                if (_zapretManager.IsRunning())
                {
                    await _zapretManager.StopAsync();
                    await Task.Delay(1000);
                }

                // Run the installer
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = downloadPath,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                var installProcess = Process.Start(processStartInfo);
                _logger.LogInformation($"Installer process started with ID: {installProcess?.Id}");

                _logger.LogInformation("Closing app...");
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("close_app")}[/]");
                await Task.Delay(500);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("update_fail")}: {{0}}[/]", ex.Message);
                AnsiConsole.WriteLine(ex.InnerException?.Message ?? ex.StackTrace);
                _logger.LogInformation("Something went wrong1", ex);
            }
        }

        public async Task DownloadLatestReleaseAsync()
        {
            try
            {
                _logger.LogInformation("Getting latest Zapret version...");
                var release = await GetLatestRelease();

                if (release == null || !release.Assets.Any())
                {
                    throw new Exception("No release assets found");
                }

                // Select the first suitable archive (ZIP or RAR)
                var asset = release.Assets.FirstOrDefault(a =>
                    a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    a.Name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase));

                if (asset == null)
                {
                    throw new Exception("No suitable archive found");
                }

                // Create a temporary download folder
                var tempDir = Path.Combine(Path.GetTempPath(), "zapret_update");
                Directory.CreateDirectory(tempDir);

                var downloadPath = Path.Combine(tempDir, asset.Name);

                _logger.LogInformation("Downloading new Zapret version...");
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("downloading")}[/]");

                // Download the file
                _logger.LogInformation($"Downloading archive: {asset.Name} ({asset.Size / 1024 / 1024:F1} MB)");
                using (var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                        }
                    }
                }

                _logger.LogInformation("Extracting archive...");
                await ExtractArchive(downloadPath, tempDir);
                _logger.LogInformation("Stopping services...");
                StopServicesAndProcesses();
                _logger.LogInformation("Copying files...");
                await CopyFiles(tempDir);
                _logger.LogInformation("Cleaning up temp...");
                await CleanupTempFiles(tempDir);

                _logger.LogInformation($"Zapret updated. New version: {release.TagName}");
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("zapret_updated")}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("update_fail")}: {{0}}[/]", ex.Message);
                AnsiConsole.WriteLine(ex.InnerException?.Message ?? ex.StackTrace);
                _logger.LogInformation("Something went wrong1", ex);
            }
        }

        public void StopServicesAndProcesses()
        {
            try
            {
                KillProcessByName("winws.exe");
                StopAndDeleteService("zapret");
                StopAndDeleteService("WinDivert");
                StopAndDeleteService("WinDivert14");

                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("services_stop")}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("services_stop_fail")}: {{0}}[/]", ex.Message);
                _logger.LogInformation("Something went wrong1", ex);
            }
        }

        private void StopAndDeleteService(string serviceName)
        {
            try
            {
                using (var service = new ServiceController(serviceName))
                {
                    if (service.Status == ServiceControllerStatus.Running)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }

                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{_localizationService.GetString("service_stop_fail")}[/]", serviceName);
                    }

                    service.Dispose();

                    var process = new Process
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

                    process.Start();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
            {
                //ConsoleUI.WriteLine($"  [✓] Service {serviceName} not found", ConsoleUI.green);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("service_delete_fail")} {{0}}: {{1}}[/]", serviceName, ex.Message);
                _logger.LogInformation($"Something went wrong while stopping/deleting {serviceName}1", ex);
                throw;
            }
        }

        private void KillProcessByName(string processName)
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
                        if (!process.HasExited)
                        {
                            AnsiConsole.MarkupLine($"[{ConsoleUI.orangeName}]{_localizationService.GetString("process_exited_with_fail")}[/]", processName);
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("process_terminate_fail")} {{0}}: {{1}}[/]", processName, ex.Message);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("processes_terminate_fail")} {{0}}: {{1}}[/]", processName, ex.Message);
                _logger.LogInformation($"Something went wrong while stopping/deleting {processName}1", ex);
            }
        }

        private async Task<GithubRelease> GetLatestRelease()
        {
            try
            {
                _logger.LogInformation($"Fetching latest release from {ApiUrl}");
                var response = await _httpClient.GetAsync(ApiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"GitHub API returned status code: {response.StatusCode}");
                    throw new HttpRequestException($"Failed to fetch release information: {response.StatusCode}");
                }

                var content = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<GithubRelease>(content, options);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching latest release: {ex.Message}");
                throw;
            }
        }

        private Version GetCurrentVersion()
        {
            var versionFile = Path.Combine(_appPath, "VERSION");
            return File.Exists(versionFile) ? new Version(File.ReadAllText(versionFile).Trim().Replace("b", "").Replace("a", "")) : new Version("1.0.0");
        }

        private async Task ExtractArchive(string archivePath, string tempDir)
        {
            try
            {
                var extractDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractDir);

                var fileExtension = Path.GetExtension(archivePath).ToLower();

                switch (fileExtension)
                {
                    case ".zip":
                        ZipFile.ExtractToDirectory(archivePath, extractDir);
                        break;
                    case ".rar":
                        using (var archive = ArchiveFactory.Open(archivePath))
                        {
                            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                            {
                                entry.WriteToDirectory(extractDir, new ExtractionOptions
                                {
                                    ExtractFullPath = true,
                                    Overwrite = true
                                });
                            }
                        }
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported archive format: {fileExtension}");
                }

                var rootFolder = FindZapretRootFolder(extractDir);
                if (rootFolder == null)
                {
                    throw new DirectoryNotFoundException("Could not find zapret root folder in extracted archive");
                }

                // Move the found folder to zapret_files
                var targetDir = Path.Combine(tempDir, "zapret_files");
                if (Directory.Exists(targetDir))
                    Directory.Delete(targetDir, true);

                Directory.Move(rootFolder, targetDir);

                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("extract_success")}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("extract_fail")}: {{0}}[/]", ex.Message);
                _logger.LogInformation($"Something went wrong1", ex);
                throw;
            }
        }

        private string FindZapretRootFolder(string searchDir)
        {
            // Looking for folders containing bin and lists
            var allDirs = Directory.GetDirectories(searchDir, "*", SearchOption.AllDirectories)
                .Union(new[] { searchDir })
                .ToList();

            foreach (var dir in allDirs)
            {
                var binPath = Path.Combine(dir, "bin");
                var listsPath = Path.Combine(dir, "lists");

                if (Directory.Exists(binPath) && Directory.Exists(listsPath))
                {
                    // Checking for critical files
                    var winwsPath = Path.Combine(binPath, "winws.exe");
                    if (File.Exists(winwsPath))
                    {
                        return dir;
                    }
                }
            }

            // If don't find the standard structure, search for winws.exe directly
            var winwsFiles = Directory.GetFiles(searchDir, "winws.exe", SearchOption.AllDirectories);
            if (winwsFiles.Length > 0)
            {
                return Path.GetDirectoryName(Path.GetDirectoryName(winwsFiles[0]));
            }

            return null;
        }

        private async Task CopyFiles(string tempDir)
        {
            var sourceDir = Path.Combine(tempDir, "zapret_files");
            if (!Directory.Exists(sourceDir))
            {
                // Let's try to find the files in another location.
                var possibleDirs = Directory.GetDirectories(tempDir, "*", SearchOption.AllDirectories)
                    .Where(d => Directory.Exists(Path.Combine(d, "bin")) && Directory.Exists(Path.Combine(d, "lists")))
                    .ToList();

                if (possibleDirs.Count > 0)
                {
                    sourceDir = possibleDirs.First();
                }
                else
                {
                    throw new DirectoryNotFoundException("Extracted files not found in expected location");
                }
            }

            var binSource = Path.Combine(sourceDir, "bin");
            var listsSource = Path.Combine(sourceDir, "lists");
            var binDest = Path.Combine(_appPath, "bin");
            var listsDest = Path.Combine(_appPath, "lists");

            Directory.CreateDirectory(binDest);
            Directory.CreateDirectory(listsDest);

            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("files_copy")}[/]");
            foreach (var file in Directory.GetFiles(binSource))
            {
                var destFile = Path.Combine(binDest, Path.GetFileName(file));
                File.Copy(file, destFile, true);
                await Task.Delay(50);
            }

            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("lists_update")}[/]");
            await MergeLists(listsSource, listsDest);

            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("profiles_proccess")}[/]");
            var profiles = await _profileManager.LoadProfilesFromArchive(sourceDir);
            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("profiles_proccessed")}[/]", profiles.Count);

            var versionFile = Path.Combine(_appPath, "VERSION");
            var releaseInfo = await GetLatestRelease();
            File.WriteAllText(versionFile, releaseInfo?.TagName ?? DateTime.Now.ToString("yyyyMMdd"));
        }

        private async Task MergeLists(string sourceDir, string destDir)
        {
            var listFiles = new[] {
                "ipset-all.txt",
                "ipset-exclude.txt",
                "list-exclude.txt",
                "list-general.txt",
                "list-google.txt"
            };

            foreach (var file in listFiles)
            {
                var sourceFile = Path.Combine(sourceDir, file);
                var destFile = Path.Combine(destDir, file);

                if (!File.Exists(sourceFile)) continue;

                try
                {
                    var sourceLines = (await File.ReadAllLinesAsync(sourceFile))
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                        .Distinct()
                        .ToList();

                    if (!File.Exists(destFile))
                    {
                        await File.WriteAllLinesAsync(destFile, sourceLines);
                        continue;
                    }

                    var destLines = (await File.ReadAllLinesAsync(destFile))
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                        .ToList();

                    var newLines = sourceLines.Except(destLines).ToList();
                    if (newLines.Count > 0)
                    {
                        await File.AppendAllLinesAsync(destFile, newLines.Select(l => l + Environment.NewLine));
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("update_fail")} {{0}}: {{1}}[/]", file, ex.Message);
                    _logger.LogInformation($"Something went wrong1", ex);
                }
            }
        }

        private async Task CleanupTempFiles(string tempDir)
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            Directory.Delete(tempDir, true);
                            return;
                        }
                        catch
                        {
                            await Task.Delay(500);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("temp_cleanup_fail")}: {{0}}[/]", ex.Message);
                _logger.LogInformation($"Something went wrong1", ex);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
            }
        }
    }
}