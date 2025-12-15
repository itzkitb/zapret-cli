using System.Diagnostics;
using System.Text;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.Models;

namespace ZapretCLI.Core.Services
{
    public class ProcessService : IProcessService, IDisposable
    {
        private readonly IFileSystemService _fileSystem;
        private readonly AppConfig _settings;
        private readonly string _appPath;
        private readonly string _winwsPath;
        private bool _useGameFilter = false;
        private bool _disposed = false;

        public event EventHandler WindivertInitialized;
        public event EventHandler<string> OutputLineReceived;
        public event EventHandler<string> ErrorLineReceived;

        private readonly ILoggerService _logger;

        public ProcessService(IFileSystemService fileSystem, AppConfig settings, string appPath, ILoggerService logger)
        {
            _fileSystem = fileSystem;
            _settings = settings;
            _appPath = appPath;
            _logger = logger;
            _winwsPath = Path.Combine(appPath, settings.BinPath, "winws.exe");

            if (!_fileSystem.FileExists(_winwsPath))
            {
                _logger.LogWarning($"winws.exe not found at {_winwsPath}. Application may not function properly.");
            }

            var binPath = Path.Combine(appPath, settings.BinPath);
            var patternFile = Path.Combine(binPath, "tls_clienthello_www_google_com.bin");
            if (!_fileSystem.FileExists(patternFile))
            {
                _logger.LogWarning($"Pattern file not found at {patternFile}. Some profiles may not work correctly.");
            }
        }

        public async Task<Process> StartZapretAsync(ZapretProfile profile)
        {
            if (profile == null)
            {
                _logger.LogError("Attempted to start Zapret with null profile");
                throw new ArgumentNullException(nameof(profile), "Profile cannot be null");
            }

            if (!_fileSystem.FileExists(_winwsPath))
            {
                _logger.LogError($"winws.exe not found at {_winwsPath}");
                throw new FileNotFoundException("winws.exe not found. Please update the application.", _winwsPath);
            }

            _logger.LogInformation($"Starting Zapret with profile: {profile.Name}");

            var startInfo = new ProcessStartInfo
            {
                FileName = _winwsPath,
                Arguments = BuildArguments(profile),
                WorkingDirectory = Path.Combine(_appPath, _settings.BinPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) => OnOutputLineReceived(e.Data);
            process.ErrorDataReceived += (sender, e) => OnErrorLineReceived(e.Data);

            try
            {
                process.Start();
                _logger.LogInformation($"Process started successfully with ID: {process.Id}");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return process;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start process: {ex.Message}", ex);
                process.Dispose();
                throw;
            }
        }

        public async Task StopZapretAsync(Process process)
        {
            if (process == null)
            {
                _logger.LogWarning("Attempted to stop null process");
                return;
            }

            if (process.HasExited)
            {
                _logger.LogInformation($"Process {process.Id} has already exited");
                process.Dispose();
                return;
            }

            _logger.LogInformation($"Stopping process {process.Id}");

            try
            {
                if (!process.CloseMainWindow())
                {
                    _logger.LogDebug($"MainWindow close failed for process {process.Id}, forcing kill");
                    process.Kill();
                }

                using var timeoutSignal = new CancellationTokenSource(_settings.ProcessStopTimeout);
                var waitForExitTask = process.WaitForExitAsync(timeoutSignal.Token);

                if (await Task.WhenAny(waitForExitTask, Task.Delay(_settings.ProcessStopTimeout.Add(TimeSpan.FromSeconds(5)))) != waitForExitTask)
                {
                    _logger.LogWarning($"Process {process.Id} did not exit within timeout period. Forcing termination.");
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogError($"Failed to force kill process {process.Id}: {killEx.Message}", killEx);
                    }
                }
                else
                {
                    _logger.LogDebug($"Process {process.Id} exited normally");
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogDebug($"Process {process?.Id} already exited: {ex.Message}");
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning($"Timeout waiting for process {process.Id} to exit: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error stopping process {process.Id}: {ex.Message}", ex);
                throw new InvalidOperationException($"Failed to stop zapret process: {ex.Message}", ex);
            }
            finally
            {
                try
                {
                    if (!process.HasExited)
                    {
                        _logger.LogWarning($"Process {process.Id} still running after cleanup attempts. Forcing disposal.");
                        process.Kill();
                    }
                }
                catch
                {
                    // Ignore errors during final cleanup
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private string BuildArguments(ZapretProfile profile)
        {
            if (profile?.Arguments == null || profile.Arguments.Count == 0)
            {
                _logger.LogInformation("Using default arguments for Zapret");
                return GetDefaultArguments();
            }

            var gameFilterPorts = _useGameFilter ? "1024-65535" : "12";
            var finalArguments = new List<string>();

            foreach (var arg in profile.Arguments)
            {
                var processedArg = arg
                    .Replace("%BIN%", $"..\\{_settings.BinPath}\\")
                    .Replace("%LISTS%", $"..\\{_settings.ListsPath}\\")
                    .Replace("%GameFilter%", gameFilterPorts);

                finalArguments.Add(processedArg);
            }

            var result = string.Join(" ", finalArguments);
            _logger.LogDebug($"Built arguments: {result}");
            return result;
        }

        private string GetDefaultArguments()
        {
            var listsPath = Path.Combine(_appPath, _settings.ListsPath);
            var binPath = Path.Combine(_appPath, _settings.BinPath);

            return $"--wf-tcp=80,443 --wf-udp=443 " +
                   $"--hostlist=\"{listsPath}\\list-general.txt\" " +
                   $"--hostlist-exclude=\"{listsPath}\\list-exclude.txt\" " +
                   $"--ipset-exclude=\"{listsPath}\\ipset-exclude.txt\" " +
                   $"--dpi-desync=multisplit " +
                   $"--dpi-desync-split-pos=2,sniext+1 " +
                   $"--dpi-desync-split-seqovl=679 " +
                   $"--dpi-desync-split-seqovl-pattern=\"{binPath}\\tls_clienthello_www_google_com.bin\"";
        }

        private void OnOutputLineReceived(string data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                OutputLineReceived?.Invoke(this, data);

                if (data.Contains("windivert initialized. capture is started."))
                {
                    WindivertInitialized?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void OnErrorLineReceived(string data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                ErrorLineReceived?.Invoke(this, data);
            }
        }

        public void SetGameFilter(bool enabled)
        {
            _useGameFilter = enabled;
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
                    _logger?.LogDebug("Disposing ProcessService resources");
                }
                _disposed = true;
            }
        }
    }
}