using System.Diagnostics;
using System.Text;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Models;

namespace ZapretCLI.Core.Services
{
    public class ProcessService : IProcessService, IDisposable
    {
        private readonly IFileSystemService _fileSystem;
        private readonly AppSettings _settings;
        private readonly string _appPath;
        private readonly string _winwsPath;
        private bool _useGameFilter = false;
        private bool _disposed = false;

        public event EventHandler WindivertInitialized;
        public event EventHandler<string> OutputLineReceived;
        public event EventHandler<string> ErrorLineReceived;

        public ProcessService(IFileSystemService fileSystem, AppSettings settings, string appPath)
        {
            _fileSystem = fileSystem;
            _settings = settings;
            _appPath = appPath;
            _winwsPath = Path.Combine(appPath, settings.BinPath, "winws.exe");
        }

        public async Task<Process> StartZapretAsync(ZapretProfile profile)
        {
            if (!_fileSystem.FileExists(_winwsPath))
                throw new FileNotFoundException("winws.exe not found. Please update the application.");

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

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            //await Task.Delay(1000).ConfigureAwait(false);
            return process;
        }

        public async Task StopZapretAsync(Process process)
        {
            if (process == null || process.HasExited) return;

            try
            {
                if (!process.CloseMainWindow())
                {
                    process.Kill();
                }

                var timeoutSignal = new CancellationTokenSource(_settings.ProcessStopTimeout);
                await process.WaitForExitAsync(timeoutSignal.Token).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The process has already been completed
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to stop zapret process: {ex.Message}", ex);
            }
            finally
            {
                process.Dispose();
            }
        }

        private string BuildArguments(ZapretProfile profile)
        {
            if (profile?.Arguments == null || profile.Arguments.Count == 0)
                return GetDefaultArguments();

            var binPath = Path.Combine(_appPath, _settings.BinPath).TrimEnd('\\') + "\\";
            var listsPath = Path.Combine(_appPath, _settings.ListsPath).TrimEnd('\\') + "\\";
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

            return string.Join(" ", finalArguments);
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
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}