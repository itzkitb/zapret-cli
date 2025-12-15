using Spectre.Console;
using System.Collections.Concurrent;
using System.Text;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Managers
{
    public static class ListManager
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

        public static async Task AddDomainToFile(string filePath, string domain, ILocalizationService ls, ILoggerService logger, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(domain))
            {
                logger.LogWarning("Invalid arguments for AddDomainToFile: filePath or domain is null/empty");
                return;
            }

            if (!IsValidDomain(domain))
            {
                logger.LogWarning($"Invalid domain format: {domain}");
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{ls.GetString("invalid_domain_format")}[/]");
                return;
            }

            var fileLock = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
            try
            {
                await fileLock.WaitAsync(cancellationToken);

                logger.LogInformation($"Adding domain '{domain}' to file: {filePath}");

                if (!File.Exists(filePath))
                {
                    logger.LogInformation($"Creating new domain list file: {filePath}");
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    await File.WriteAllTextAsync(filePath, $"{domain}\n", Encoding.UTF8, cancellationToken);
                    return;
                }

                var lines = new List<string>();
                try
                {
                    lines = (await File.ReadAllLinesAsync(filePath, cancellationToken))
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error reading domain list file {filePath}: {ex.Message}", ex);
                    throw;
                }

                if (lines.Contains(domain, StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogInformation($"Domain '{domain}' already exists in the list");
                    return;
                }

                lines.Add(domain);

                try
                {
                    var content = string.Join(Environment.NewLine, lines.Select(l => l.Trim())) + Environment.NewLine;
                    await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
                    logger.LogInformation($"Domain '{domain}' successfully added to {Path.GetFileName(filePath)}");
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error writing to domain list file {filePath}: {ex.Message}", ex);
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation($"Domain addition operation for {domain} was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to add domain to list: {ex.Message}", ex);
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{ls.GetString("domain_add_fail")}: {ex.Message}[/]");
            }
            finally
            {
                if (fileLock != null && fileLock.CurrentCount == 0)
                {
                    fileLock.Release();
                }
            }
        }

        public static async Task RemoveDomainFromFile(string filePath, string domain, ILocalizationService ls, ILoggerService logger, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(domain))
            {
                logger.LogWarning("Invalid arguments for RemoveDomainFromFile: filePath or domain is null/empty");
                return;
            }

            var fileLock = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
            try
            {
                await fileLock.WaitAsync(cancellationToken);

                logger.LogInformation($"Removing domain '{domain}' from file: {filePath}");

                if (!File.Exists(filePath))
                {
                    logger.LogWarning($"Domain list file not found: {filePath}");
                    return;
                }

                var lines = new List<string>();
                try
                {
                    lines = (await File.ReadAllLinesAsync(filePath, cancellationToken))
                        .ToList();
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error reading domain list file {filePath}: {ex.Message}", ex);
                    throw;
                }

                var originalCount = lines.Count;
                var filteredLines = lines
                    .Where(l =>
                        string.IsNullOrWhiteSpace(l) ||
                        l.StartsWith("#") ||
                        !l.Trim().Equals(domain, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (filteredLines.Count == originalCount)
                {
                    logger.LogInformation($"Domain '{domain}' not found in the list");
                    return;
                }

                try
                {
                    var content = string.Join(Environment.NewLine, filteredLines);
                    if (!string.IsNullOrEmpty(content) && !content.EndsWith(Environment.NewLine))
                    {
                        content += Environment.NewLine;
                    }
                    await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
                    logger.LogInformation($"Domain '{domain}' successfully removed from {Path.GetFileName(filePath)}");
                }
                catch (Exception ex)
                {
                    logger.LogError($"Error writing to domain list file {filePath}: {ex.Message}", ex);
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation($"Domain removal operation for {domain} was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to remove domain from list: {ex.Message}", ex);
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{ls.GetString("domain_remove_fail")}: {ex.Message}[/]");
            }
            finally
            {
                if (fileLock != null && fileLock.CurrentCount == 0)
                {
                    fileLock.Release();
                }
            }
        }

        public static async Task<List<string>> GetDomainsFromFile(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new List<string>();
            }

            try
            {
                var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
                return lines
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                // В production коде здесь нужно логгирование
                return new List<string>();
            }
        }

        private static bool IsValidDomain(string domain)
        {
            domain = domain.Trim().ToLowerInvariant();

            if (domain.Length > 253 || domain.Length < 3)
                return false;

            if (!System.Text.RegularExpressions.Regex.IsMatch(domain, @"^[a-z0-9][a-z0-9.-]*[a-z0-9]$"))
                return false;

            if (domain.Contains(".."))
                return false;

            var parts = domain.Split('.');
            foreach (var part in parts)
            {
                if (part.Length == 0 || part.Length > 63)
                    return false;

                if (!System.Text.RegularExpressions.Regex.IsMatch(part, @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$"))
                    return false;
            }

            return true;
        }
    }
}