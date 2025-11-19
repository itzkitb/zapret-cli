using Microsoft.Extensions.DependencyInjection;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.ServiceProcess;
using System.Text.Json;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Models;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly string _appPath;
        private readonly HttpClient _httpClient;
        private readonly IProfileService _profileManager;
        private readonly IZapretManager _zapretManager;
        private const string GitHubRepo = "https://github.com/Flowseal/zapret-discord-youtube";
        private const string ApiUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
        private const string CliRepo = "https://github.com/itzkitb/zapret-cli";
        private const string CliApiUrl = "https://api.github.com/repos/itzkitb/zapret-cli/releases/latest";

        public UpdateService(string appPath, IProfileService ps, IZapretManager zm)
        {
            _appPath = appPath;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretCLI/1.0");
            _profileManager = ps;
            _zapretManager = zm;
        }

        public async Task CheckForUpdatesAsync()
        {
            try
            {
                var versionFile = Path.Combine(_appPath, "VERSION");
                if (File.Exists(versionFile))
                {
                    var release = await GetLatestRelease();
                    var currentVersion = GetCurrentVersion();
                    var newVersion = new Version(release.TagName.Replace("b", "").Replace("a", ""));

                    if (release != null && currentVersion < newVersion)
                    {

                        ConsoleUI.WriteLine($"[!] New version of Zapret available: {release.TagName}", ConsoleUI.yellow);
                        ConsoleUI.WriteLine($"Current version: {currentVersion}\nNew version: {newVersion}", ConsoleUI.yellow);

                        var shortChangelog = release.Body.Length > 200
                            ? release.Body.Substring(0, 200) + "..."
                            : release.Body;
                        ConsoleUI.WriteLine($"Changelog: {shortChangelog}", ConsoleUI.yellow);


                        var result = ConsoleUI.ReadLineWithPrompt("Update? (Y/N): ");
                        if (result.Contains("Y", StringComparison.InvariantCultureIgnoreCase))
                        {
                            await DownloadLatestReleaseAsync();
                        }
                    }
                }
                else
                {
                    ConsoleUI.WriteLine($"It looks like this is your first launch. Installing the latest version of Zapret...", ConsoleUI.yellow);
                    await DownloadLatestReleaseAsync();
                }
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[!] Failed to check updates: {ex.Message}", ConsoleUI.red);
                await Task.Delay(500);
            }
        }

        public async Task CheckForCliUpdatesAsync()
        {
            try
            {
                var release = await GetLatestCliRelease();
                var currentVersion = GetCliVersion();
                var newVersion = new Version(release.TagName);

                if (release != null && currentVersion < newVersion)
                {
                    ConsoleUI.WriteLine($"[!] New version of Zapret CLI available: {release.TagName}", ConsoleUI.yellow);
                    ConsoleUI.WriteLine($"Current version: {currentVersion}\nNew version: {newVersion}", ConsoleUI.yellow);
                    var shortChangelog = release.Body.Length > 200
                        ? release.Body.Substring(0, 200) + "..."
                        : release.Body;
                    ConsoleUI.WriteLine($"Changelog: {shortChangelog}", ConsoleUI.yellow);
                    ConsoleUI.WriteLine("\n", ConsoleUI.blue);
                    var result = ConsoleUI.ReadLineWithPrompt("Update? (Y/N): ");
                    if (result.Contains("Y", StringComparison.InvariantCultureIgnoreCase))
                    {
                        await UpdateCliAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[!] Failed to check CLI updates: {ex.Message}", ConsoleUI.red);
                await Task.Delay(500);
            }
        }

        private async Task<GithubRelease> GetLatestCliRelease()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(CliApiUrl);
                return JsonSerializer.Deserialize<GithubRelease>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[✗] Error fetching CLI release info: {ex.Message}", ConsoleUI.red);
                return null;
            }
        }

        private Version GetCliVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        }

        public async Task UpdateCliAsync()
        {
            try
            {
                ConsoleUI.WriteLine("Updating Zapret CLI...", ConsoleUI.yellow);
                var release = await GetLatestCliRelease();
                if (release == null || !release.Assets.Any())
                {
                    ConsoleUI.WriteLine("  [✗] No release assets found", ConsoleUI.red);
                    return;
                }

                // Looking for the .exe installer in the release
                var asset = release.Assets.FirstOrDefault(a =>
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    a.Name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0);

                if (asset == null)
                {
                    ConsoleUI.WriteLine("  [✗] No setup executable found in release assets", ConsoleUI.red);
                    return;
                }

                // Create a temporary download folder
                var tempDir = Path.Combine(Path.GetTempPath(), "zapret_cli_update");
                Directory.CreateDirectory(tempDir);
                var downloadPath = Path.Combine(tempDir, asset.Name);

                ConsoleUI.WriteLine($"  Downloading: {asset.Name}", ConsoleUI.blue);

                // Downloading the file
                using (var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    long receivedBytes = 0;

                    using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            receivedBytes += bytesRead;
                            if (totalBytes > 0)
                            {
                                var progress = (int)(receivedBytes * 100 / totalBytes);
                                ConsoleUI.WriteProgress(progress, "  ");
                            }
                        }
                    }
                }

                Console.Write("\n");
                ConsoleUI.WriteLine("  [✓] Download completed!", ConsoleUI.green);

                ConsoleUI.WriteLine("Launching installer...", ConsoleUI.yellow);

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

                Process.Start(processStartInfo);

                ConsoleUI.WriteLine("[✓] Installer launched. Closing application...", ConsoleUI.green);
                await Task.Delay(1000);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[✗] Failed to update CLI: {ex.Message}", ConsoleUI.red);
                ConsoleUI.WriteLine($"Details: {ex.InnerException?.Message ?? ex.StackTrace}", ConsoleUI.red);
            }
        }

        public async Task DownloadLatestReleaseAsync()
        {
            try
            {
                ConsoleUI.WriteLine("Updating...", ConsoleUI.yellow);
                var release = await GetLatestRelease();

                if (release == null || !release.Assets.Any())
                {
                    ConsoleUI.WriteLine("  [✗] No release assets found", ConsoleUI.red);
                    return;
                }

                // Select the first suitable archive (ZIP or RAR)
                var asset = release.Assets.FirstOrDefault(a =>
                    a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    a.Name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase));

                if (asset == null)
                {
                    ConsoleUI.WriteLine("  [✗] No suitable archive found (ZIP/RAR)", ConsoleUI.red);
                    return;
                }

                // Create a temporary download folder
                var tempDir = Path.Combine(Path.GetTempPath(), "zapret_update");
                Directory.CreateDirectory(tempDir);

                var downloadPath = Path.Combine(tempDir, asset.Name);
                ConsoleUI.WriteLine($"  Downloading: {asset.Name}", ConsoleUI.blue);

                // Download the file
                using (var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    long receivedBytes = 0;

                    using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            receivedBytes += bytesRead;

                            if (totalBytes > 0)
                            {
                                var progress = (int)(receivedBytes * 100 / totalBytes);
                                ConsoleUI.WriteProgress(progress, "  ");
                            }
                        }
                    }
                }

                Console.Write("\n");
                ConsoleUI.WriteLine("  [✓] Download completed!", ConsoleUI.green);

                await ExtractArchive(downloadPath, tempDir);
                ConsoleUI.WriteLine("");
                StopServicesAndProcesses();
                await CopyFiles(tempDir);
                await CleanupTempFiles(tempDir);

                ConsoleUI.WriteLine("[✓] Update installed successfully!", ConsoleUI.green);
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"✗ Failed to download update: {ex.Message}", ConsoleUI.red);
                ConsoleUI.WriteLine($"Details: {ex.InnerException?.Message ?? ex.StackTrace}", ConsoleUI.red);
            }
        }

        public void StopServicesAndProcesses()
        {
            try
            {
                ConsoleUI.WriteLine("Stopping services and processes...", ConsoleUI.yellow);

                KillProcessByName("winws.exe");
                StopAndDeleteService("zapret");
                StopAndDeleteService("WinDivert");
                StopAndDeleteService("WinDivert14");

                ConsoleUI.WriteLine("[✓] Services and processes stopped successfully", ConsoleUI.green);
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[!] Warning: Failed to stop some services/processes: {ex.Message}", ConsoleUI.yellow);
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
                        ConsoleUI.WriteLine($"  Stopping service: {serviceName}", ConsoleUI.blue);
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }

                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        ConsoleUI.WriteLine($"  [!] Service {serviceName} could not be stopped, attempting to delete anyway", ConsoleUI.yellow);
                    }

                    ConsoleUI.WriteLine($"  Deleting service: {serviceName}", ConsoleUI.blue);
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
                ConsoleUI.WriteLine($"  [✓] Service {serviceName} not found", ConsoleUI.green);
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"  [✗] Failed to stop/delete service {serviceName}: {ex.Message}", ConsoleUI.red);
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
                    ConsoleUI.WriteLine($"  [✓] Process {processName} not running", ConsoleUI.green);
                    return;
                }

                foreach (var process in processes)
                {
                    try
                    {
                        ConsoleUI.WriteLine($"  Terminating process: {processName} (PID: {process.Id})", ConsoleUI.blue);
                        process.Kill();
                        process.WaitForExit(5000);
                        if (!process.HasExited)
                        {
                            ConsoleUI.WriteLine($"  [✗] Warning: Process {processName} did not exit gracefully", ConsoleUI.yellow);
                        }
                    }
                    catch (Exception ex)
                    {
                        ConsoleUI.WriteLine($"  [✗] Failed to terminate process {processName}: {ex.Message}", ConsoleUI.red);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"  [✗] Error checking processes: {ex.Message}", ConsoleUI.red);
            }
        }

        private async Task<GithubRelease> GetLatestRelease()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(ApiUrl);
                return JsonSerializer.Deserialize<GithubRelease>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[✗] Error fetching release info: {ex.Message}", ConsoleUI.red);
                return null;
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
                ConsoleUI.WriteLine("\nExtracting...", ConsoleUI.yellow);

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

                ConsoleUI.WriteLine("  [✓] Archive extracted successfully", ConsoleUI.green);

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

                ConsoleUI.WriteLine($"  [✓] Found zapret files at: {Path.GetFileName(rootFolder)}", ConsoleUI.green);
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[✗] Failed to extract archive: {ex.Message}", ConsoleUI.red);
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

            ConsoleUI.WriteLine("\nCopying binary files...", ConsoleUI.yellow);
            foreach (var file in Directory.GetFiles(binSource))
            {
                var destFile = Path.Combine(binDest, Path.GetFileName(file));
                File.Copy(file, destFile, true);
                ConsoleUI.WriteLine($"  [✓] {Path.GetFileName(file)}", ConsoleUI.green);
                await Task.Delay(50);
            }

            ConsoleUI.WriteLine("\nUpdating lists...", ConsoleUI.yellow);
            await MergeLists(listsSource, listsDest);

            ConsoleUI.WriteLine("\nProcessing profiles...", ConsoleUI.yellow);
            var profiles = await _profileManager.LoadProfilesFromArchive(sourceDir);
            ConsoleUI.WriteLine($"\n[✓] Found and processed {profiles.Count} profiles", ConsoleUI.green);

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
                        ConsoleUI.WriteLine($"  [✓] Created {file} with {sourceLines.Count} entries", ConsoleUI.green);
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
                        ConsoleUI.WriteLine($"  [✓] Added {newLines.Count} new entries to {file}", ConsoleUI.green);
                    }
                    else
                    {
                        ConsoleUI.WriteLine($"  [✓] {file} is already up to date", ConsoleUI.green);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleUI.WriteLine($"  [✗] Failed to update {file}: {ex.Message}", ConsoleUI.red);
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
                            ConsoleUI.WriteLine($"[✓] Cleaned up temporary files", ConsoleUI.green);
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
                ConsoleUI.WriteLine($"[!] Failed to cleanup temp files: {ex.Message}", ConsoleUI.yellow);
            }
        }
    }
}