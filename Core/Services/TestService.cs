using Spectre.Console;
using System.Net.NetworkInformation;
using System.Security.Authentication;
using System.Text;
using ZapretCLI.Core.Interfaces;
using ZapretCLI.Core.Logging;
using ZapretCLI.Core.Managers;
using ZapretCLI.Models;
using ZapretCLI.UI;

namespace ZapretCLI.Core.Services
{
    public class TestService : ITestService, IDisposable
    {
        private readonly IZapretManager _zapretManager;
        private readonly IProfileService _profileService;
        private readonly ILocalizationService _localizationService;
        private readonly ILoggerService _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly List<DpiTarget> _dpiSuite;
        private HttpClient _httpClient;
        private HttpClient _httpClientTls12;
        private HttpClient _httpClientTls13;
        private bool _disposed = false;
        private readonly bool _isTls13Supported;

        public TestService(
            IZapretManager zapretManager,
            ILocalizationService localizationService,
            ILoggerService logger,
            IHttpClientFactory httpClientFactory,
            IProfileService profileService)
        {
            _zapretManager = zapretManager;
            _profileService = profileService;
            _localizationService = localizationService;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _isTls13Supported = IsTls13Supported();

            ConfigureHttpClients();
            _dpiSuite = InitializeDpiSuite();
        }

        private bool IsTls13Supported()
        {
            try
            {
                var handler = new HttpClientHandler { SslProtocols = SslProtocols.Tls13 };
                using var testClient = new HttpClient(handler);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogInformation("TLS1.3 is not supported", ex);
                return false;
            }
        }

        private void ConfigureHttpClients()
        {
            _httpClient = _httpClientFactory.CreateClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);

            var handlerTls12 = new HttpClientHandler();
            handlerTls12.SslProtocols = SslProtocols.Tls12;
            _httpClientTls12 = new HttpClient(handlerTls12) { Timeout = TimeSpan.FromSeconds(10) };

            var handlerTls13 = new HttpClientHandler();
            handlerTls13.SslProtocols = SslProtocols.Tls13;
            _httpClientTls13 = new HttpClient(handlerTls13) { Timeout = TimeSpan.FromSeconds(10) };
        }

        public async Task RunTestsAsync(TestType testType)
        {
            _logger.LogInformation($"Starting test run. Type: {testType}");
            var initialProfile = _zapretManager.GetCurrentProfile();
            _logger.LogInformation($"Initial profile: {(initialProfile != null ? initialProfile.Name : "None")}");

            if (_zapretManager.IsRunning())
            {
                _logger.LogInformation("Stopping currently running profile before tests");
                await _zapretManager.StopAsync(false);
            }

            // Selecting a testing mode (all profiles or selected ones)
            ConsoleUI.Clear();

            var testModeChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[{ConsoleUI.greenName}]{_localizationService.GetString("select_test_mode")}[/]")
                    .AddChoices(new[]
                    {
                        _localizationService.GetString("all_profiles"),
                        _localizationService.GetString("selected_profiles"),
                        _localizationService.GetString("back")
                    })
                    .HighlightStyle(new Style(Color.PaleGreen1))
                    .WrapAround(true)
            );

            if (testModeChoice == _localizationService.GetString("back"))
            {
                return;
            }

            var testAllProfiles = testModeChoice == _localizationService.GetString("all_profiles");

            // Loading profiles
            await _zapretManager.LoadAvailableProfilesAsync();
            var profiles = await _profileService.GetAvailableProfilesAsync();

            if (!profiles.Any())
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("no_profiles_to_test")}[/]");
                return;
            }

            List<ZapretProfile> profilesToTest = profiles;

            if (!testAllProfiles)
            {
                // Selecting Specific Profiles
                var selectedProfileNames = AnsiConsole.Prompt(
                    new MultiSelectionPrompt<string>()
                        .Title($"[{ConsoleUI.greenName}]{_localizationService.GetString("select_profiles")}[/]")
                        .InstructionsText($"[{ConsoleUI.greyName}]{_localizationService.GetString("profile_selection_instructions")}[/]")
                        .MoreChoicesText($"[{ConsoleUI.greyName}]({_localizationService.GetString("other_options")})[/]")
                        .PageSize(10)
                        .AddChoices(profiles.Select(p => p.Name))
                        .HighlightStyle(new Style(Color.PaleGreen1))
                        .WrapAround(true)
                );

                profilesToTest = profiles.Where(p => selectedProfileNames.Contains(p.Name)).ToList();
            }

            _logger.LogInformation($"Test mode selected: {(testAllProfiles ? "All profiles" : "Selected profiles")}");
            _logger.LogInformation($"Profiles to test: {profilesToTest.Count}. Total available: {profiles.Count}");

            List<TestResult> testResults = new List<TestResult>();

            if (testType == TestType.Standard)
            {
                // Request a domain for testing
                AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("test_domain_ask")}[/] [{ConsoleUI.greyName}](example.com)[/]:");
                var domainInput = Console.ReadLine();

                _logger.LogInformation($"Starting standard tests for domain: {domainInput} on {profilesToTest.Count} profiles");

                if (string.IsNullOrWhiteSpace(domainInput))
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("empty_domain")}[/]");
                    return;
                }

                var domainListPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists", "list-general.txt");
                await ListManager.AddDomainToFile(domainListPath, domainInput, _localizationService, _logger);

                // Defining domains for testing
                var domains = new List<string> { $"https://{domainInput}" };

                AnsiConsole.MarkupLine($"[{ConsoleUI.greyName}]{_localizationService.GetString("starting_standard_tests", domainInput, profilesToTest.Count)}[/]");

                // Running tests
                testResults = await RunStandardTestsAsync(profilesToTest, domains);

                _logger.LogInformation($"Standard tests completed. Total results: {testResults.Count}");
            }
            else // DPI tests
            {
                AnsiConsole.MarkupLine($"[{ConsoleUI.greyName}]{_localizationService.GetString("starting_dpi_tests", profilesToTest.Count)}[/]");

                // Getting a list of targets for DPI tests
                var dpiTargets = GetDpiTargets();
                _logger.LogInformation($"Starting DPI tests on {profilesToTest.Count} profiles with {dpiTargets.Count} targets");

                // Running tests
                testResults = await RunDpiTestsAsync(profilesToTest, dpiTargets);
                _logger.LogInformation($"DPI tests completed. Total results: {testResults.Count}");
            }

            // Analysis and display of results
            DisplayTestResults(testResults, testType);

            // Ask user if they want to save the results
            AnsiConsole.MarkupLine($"");
            var saveResults = AnsiConsole.Confirm($"[{ConsoleUI.greenName}]{_localizationService.GetString("save_results_prompt")}[/]");

            if (saveResults)
            {
                try
                {
                    var filePath = ExportResultsToFile(testResults, testType);
                    _logger.LogInformation($"Test results saved to: {filePath}");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("results_saved")} {Path.GetFileName(filePath)}[/]");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to save results: {ex.Message}");
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("save_failed")} {ex.Message}[/]");
                }
            }

            // Restore the original profile
            if (initialProfile != null)
            {
                await _zapretManager.SelectProfileAsync(initialProfile.Name);
            }

            AnsiConsole.MarkupLine($"[{ConsoleUI.darkGreyName}]{_localizationService.GetString("press_any_key")}[/]");
            Console.ReadKey(true);
        }

        private void DisplayTestResults(List<TestResult> results, TestType testType)
        {
            _logger.LogInformation($"Displaying test results. Type: {testType}, Total results: {results.Count}");
            ConsoleUI.Clear();
            AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("test_results")}[/]");
            AnsiConsole.MarkupLine($"[{ConsoleUI.darkGreyName}]--------------------[/]");

            if (testType == TestType.Standard)
            {
                _logger.LogDebug($"Processing {results.Count} standard test results for display");
                // Analysis of successful profiles
                var successfulProfiles = results
                    .Where(r => r.TestType != "Ping" && r.Success)
                    .GroupBy(r => r.ProfileName)
                    .Select(g => new
                    {
                        Profile = g.Key,
                        SuccessCount = g.Count(r => r.Success),
                        TotalTests = 3
                    })
                    .Where(g => g.SuccessCount > 0)
                    .OrderByDescending(g => g.SuccessCount)
                    .ToList();

                if (successfulProfiles.Any())
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("profiles_bypassed", successfulProfiles.Count)}[/]");
                    foreach (var profile in successfulProfiles)
                    {
                        AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}] - {profile.Profile} ({profile.SuccessCount}/{profile.TotalTests})[/]");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("no_profiles_bypassed")}[/]");
                }

                // Detailed results
                AnsiConsole.MarkupLine($"\n[{ConsoleUI.greyName}]{_localizationService.GetString("detailed_results")}[/]");

                // Grouping results by profile
                var profiles = results.Select(r => r.ProfileName).Distinct().ToList();

                foreach (var profile in profiles)
                {
                    var profileResults = results.Where(r => r.ProfileName == profile).ToList();

                    // Group by target URL/domain
                    var domainGroups = new Dictionary<string, List<TestResult>>(StringComparer.OrdinalIgnoreCase);

                    foreach (var result in profileResults)
                    {
                        if (result.TestType == "Init") continue;

                        string key;
                        if (result.TestType == "Ping")
                        {
                            // For Ping, we use the domain as is.
                            key = result.TargetName;
                        }
                        else
                        {
                            // For HTTP tests, we extract the domain from the URL
                            if (Uri.TryCreate(result.TargetName, UriKind.Absolute, out var uri))
                            {
                                key = uri.Host;
                            }
                            else
                            {
                                key = result.TargetName.Split('/')[0];
                            }
                        }

                        if (!domainGroups.ContainsKey(key))
                            domainGroups[key] = new List<TestResult>();

                        domainGroups[key].Add(result);
                    }

                    foreach (var domainGroup in domainGroups)
                    {
                        var domain = domainGroup.Key;
                        var tests = domainGroup.Value;

                        // Find the original URL
                        var httpTest = tests.FirstOrDefault(t => t.TestType != "Ping");
                        string displayUrl = httpTest?.TargetName ?? domain;

                        // Collect results by test type
                        var httpResult = tests.FirstOrDefault(t => t.TestType == "HTTP");
                        var tls12Result = tests.FirstOrDefault(t => t.TestType == "TLS1.2");
                        var tls13Result = tests.FirstOrDefault(t => t.TestType == "TLS1.3");
                        var pingResult = tests.FirstOrDefault(t => t.TestType == "Ping");

                        var parts = new List<string>();

                        // HTTP
                        if (httpResult != null)
                        {
                            var color = httpResult.Success ? ConsoleUI.greenName : ConsoleUI.redName;
                            parts.Add($"<HTTP>: [{color}]{httpResult.Message}[/]");
                        }

                        // TLS 1.2
                        if (tls12Result != null)
                        {
                            var color = tls12Result.Success ? ConsoleUI.greenName : ConsoleUI.redName;
                            parts.Add($"<TLS1.2>: [{color}]{tls12Result.Message}[/]");
                        }

                        // TLS 1.3
                        if (tls13Result != null)
                        {
                            string tls13Part;
                            if (!_isTls13Supported)
                            {
                                tls13Part = "<TLS1.3>: UNSUP";
                            }
                            else
                            {
                                var color = tls13Result.Success ? ConsoleUI.greenName : ConsoleUI.redName;
                                tls13Part = $"<TLS1.3>: [{color}]{tls13Result.Message}[/]";
                            }
                            parts.Add(tls13Part);
                        }

                        // Ping
                        if (pingResult != null)
                        {
                            var color = pingResult.Success ? ConsoleUI.greyName : ConsoleUI.redName;
                            string pingMessage = pingResult.Success
                                ? $"{pingResult.PingTimeMs} ms"
                                : pingResult.Message;
                            parts.Add($"<Ping>: [{color}]{pingMessage}[/]");
                        }

                        // Forming the final line
                        string profileTag = $"[{ConsoleUI.orangeName}]{profile.Substring(0, Math.Min(3, profile.Length))}[/]";
                        string line = $"{profileTag} {displayUrl} - {string.Join(" | ", parts)}";
                        AnsiConsole.MarkupLine(line);
                    }
                }
            }
            else // DPI tests
            {
                _logger.LogDebug($"Processing {results.Count} DPI test results for display");
                // Analysis of successful profiles
                var successfulProfiles = results
                    .Where(r => !r.IsLikelyBlocked && r.Success)
                    .GroupBy(r => r.ProfileName)
                    .Select(g => new
                    {
                        Profile = g.Key,
                        SuccessCount = g.Count(r => r.Success && !r.IsLikelyBlocked),
                        TotalTests = _dpiSuite.Count(),
                        BlockedCount = g.Count(r => r.IsLikelyBlocked)
                    })
                    .OrderByDescending(g => g.SuccessCount)
                    .ToList();

                if (successfulProfiles.Any())
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}]{_localizationService.GetString("dpi_profiles_passed", successfulProfiles.Count)}[/]");
                    foreach (var profile in successfulProfiles)
                    {
                        var warning = profile.BlockedCount > 0
                            ? $" [{ConsoleUI.orangeName}]({_localizationService.GetString("dpi_blocked_count")} {profile.BlockedCount})[/]"
                            : "";
                        AnsiConsole.MarkupLine($"[{ConsoleUI.greenName}] - {profile.Profile} ({profile.SuccessCount}/{profile.TotalTests}){warning}[/]");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"[{ConsoleUI.redName}]{_localizationService.GetString("no_dpi_profiles_passed")}[/]");
                }

                // Detailed results
                AnsiConsole.MarkupLine($"\n[{ConsoleUI.greyName}]{_localizationService.GetString("detailed_results")}[/]");
                foreach (var profile in results.Select(r => r.ProfileName).Distinct())
                {
                    AnsiConsole.MarkupLine($"\n[{ConsoleUI.orangeName}]{profile}[/]");

                    var profileResults = results.Where(r => r.ProfileName == profile).ToList();
                    foreach (var result in profileResults)
                    {
                        string color;
                        string status;

                        if (result.IsLikelyBlocked)
                        {
                            color = ConsoleUI.orangeName;
                            status = _localizationService.GetString("likely_blocked");
                        }
                        else if (result.Success)
                        {
                            color = ConsoleUI.greenName;
                            status = _localizationService.GetString("success");
                        }
                        else
                        {
                            color = ConsoleUI.redName;
                            status = _localizationService.GetString("failed");
                        }

                        AnsiConsole.MarkupLine($"  {result.TargetName}: [{color}]{status} - {result.Message}[/]");
                    }
                }
            }
            _logger.LogInformation("Results display completed");
        }

        private string ExportResultsToFile(List<TestResult> results, TestType testType)
        {
            // Get desktop path
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = testType == TestType.Standard
                ? $"ZapretCLI_StandardTest_{timestamp}.txt"
                : $"ZapretCLI_DPITest_{timestamp}.txt";
            var filePath = Path.Combine(desktopPath, filename);

            var sb = new StringBuilder();

            // Header
            sb.AppendLine($"ZapretCLI Test Results - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Test Type: {(testType == TestType.Standard ? "Standard" : "DPI")}");
            sb.AppendLine($"Total Profiles Tested: {results.Select(r => r.ProfileName).Distinct().Count()}");
            sb.AppendLine($"Total Tests Executed: {results.Count}");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();

            if (testType == TestType.Standard)
            {
                // Group results by profile and domain for standard tests
                var profiles = results.Select(r => r.ProfileName).Distinct().ToList();

                foreach (var profile in profiles)
                {
                    sb.AppendLine($"Profile: {profile}");
                    sb.AppendLine(new string('-', 40));

                    var profileResults = results.Where(r => r.ProfileName == profile).ToList();

                    // Group by target URL/domain
                    var domainGroups = new Dictionary<string, List<TestResult>>(StringComparer.OrdinalIgnoreCase);

                    foreach (var result in profileResults)
                    {
                        if (result.TestType == "Init") continue;

                        string key;
                        if (result.TestType == "Ping")
                        {
                            key = result.TargetName;
                        }
                        else
                        {
                            if (Uri.TryCreate(result.TargetName, UriKind.Absolute, out var uri))
                            {
                                key = uri.Host;
                            }
                            else
                            {
                                key = result.TargetName.Split('/')[0];
                            }
                        }

                        if (!domainGroups.ContainsKey(key))
                            domainGroups[key] = new List<TestResult>();

                        domainGroups[key].Add(result);
                    }

                    foreach (var domainGroup in domainGroups)
                    {
                        var domain = domainGroup.Key;
                        var tests = domainGroup.Value;

                        // Find the original URL
                        var httpTest = tests.FirstOrDefault(t => t.TestType != "Ping");
                        string displayUrl = httpTest?.TargetName ?? domain;

                        sb.AppendLine($"Target: {displayUrl}");

                        // HTTP
                        var httpResult = tests.FirstOrDefault(t => t.TestType == "HTTP");
                        if (httpResult != null)
                        {
                            sb.AppendLine($"  HTTP: {(httpResult.Success ? "SUCCESS" : "FAILED")} - {httpResult.Message}");
                        }

                        // TLS 1.2
                        var tls12Result = tests.FirstOrDefault(t => t.TestType == "TLS1.2");
                        if (tls12Result != null)
                        {
                            sb.AppendLine($"  TLS1.2: {(tls12Result.Success ? "SUCCESS" : "FAILED")} - {tls12Result.Message}");
                        }

                        // TLS 1.3
                        var tls13Result = tests.FirstOrDefault(t => t.TestType == "TLS1.3");
                        if (tls13Result != null)
                        {
                            string status = !_isTls13Supported ? "UNSUPPORTED" :
                                           (tls13Result.Success ? "SUCCESS" : "FAILED");
                            sb.AppendLine($"  TLS1.3: {status} - {tls13Result.Message}");
                        }

                        // Ping
                        var pingResult = tests.FirstOrDefault(t => t.TestType == "Ping");
                        if (pingResult != null)
                        {
                            string pingMessage = pingResult.Success
                                ? $"{pingResult.PingTimeMs} ms"
                                : pingResult.Message;
                            sb.AppendLine($"  Ping: {(pingResult.Success ? "SUCCESS" : "FAILED")} - {pingMessage}");
                        }

                        sb.AppendLine();
                    }
                    sb.AppendLine();
                }
            }
            else // DPI tests
            {
                // Group by profile for DPI tests
                var profiles = results.Select(r => r.ProfileName).Distinct().ToList();

                foreach (var profile in profiles)
                {
                    sb.AppendLine($"Profile: {profile}");
                    sb.AppendLine(new string('-', 40));

                    var profileResults = results.Where(r => r.ProfileName == profile).ToList();

                    foreach (var result in profileResults)
                    {
                        if (result.TestType == "Init") continue;

                        string status;
                        if (result.IsLikelyBlocked)
                        {
                            status = "LIKELY BLOCKED";
                        }
                        else if (result.Success)
                        {
                            status = "SUCCESS";
                        }
                        else
                        {
                            status = "FAILED";
                        }

                        sb.AppendLine($"Target: {result.TargetName}");
                        sb.AppendLine($"Status: {status}");
                        sb.AppendLine($"Message: {result.Message}");
                        if (result.StatusCode.HasValue)
                        {
                            sb.AppendLine($"HTTP Status: {result.StatusCode}");
                        }
                        if (result.ContentLength.HasValue)
                        {
                            sb.AppendLine($"Content Length: {result.ContentLength} bytes ({result.ContentLength.Value / 1024.0:F1} KB)");
                        }
                        sb.AppendLine();
                    }
                    sb.AppendLine();
                }
            }

            // Write to file
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            return filePath;
        }

        private List<DpiTarget> InitializeDpiSuite()
        {
            return new List<DpiTarget>
            {
                new DpiTarget { Id = "US.CF-01", Provider = "Cloudflare", Url = "https://cdn.cookielaw.org/scripttemplates/202501.2.0/otBannerSdk.js" },
                new DpiTarget { Id = "US.CF-02", Provider = "Cloudflare", Url = "https://genshin.jmp.blue/characters/all#" },
                new DpiTarget { Id = "US.CF-03", Provider = "Cloudflare", Url = "https://api.frankfurter.dev/v1/2000-01-01..2002-12-31" },
                new DpiTarget { Id = "US.DO-01", Provider = "DigitalOcean", Url = "https://genderize.io/", Times = 2 },
                new DpiTarget { Id = "DE.HE-01", Provider = "Hetzner", Url = "https://j.dejure.org/jcg/doctrine/doctrine_banner.webp" },
                new DpiTarget { Id = "FI.HE-01", Provider = "Hetzner", Url = "https://tcp1620-01.dubybot.live/1MB.bin" },
                new DpiTarget { Id = "FI.HE-02", Provider = "Hetzner", Url = "https://tcp1620-02.dubybot.live/1MB.bin" },
                new DpiTarget { Id = "FI.HE-03", Provider = "Hetzner", Url = "https://tcp1620-05.dubybot.live/1MB.bin" },
                new DpiTarget { Id = "FI.HE-04", Provider = "Hetzner", Url = "https://tcp1620-06.dubybot.live/1MB.bin" },
                new DpiTarget { Id = "FR.OVH-01", Provider = "OVH", Url = "https://eu.api.ovh.com/console/rapidoc-min.js" },
                new DpiTarget { Id = "FR.OVH-02", Provider = "OVH", Url = "https://ovh.sfx.ovh/10M.bin" },
                new DpiTarget { Id = "SE.OR-01", Provider = "Oracle", Url = "https://oracle.sfx.ovh/10M.bin" },
                new DpiTarget { Id = "DE.AWS-01", Provider = "AWS", Url = "https://tms.delta.com/delta/dl_anderson/Bootstrap.js" },
                new DpiTarget { Id = "US.AWS-01", Provider = "AWS", Url = "https://corp.kaltura.com/wp-content/cache/min/1/wp-content/themes/airfleet/dist/styles/theme.css" },
                new DpiTarget { Id = "US.GC-01", Provider = "Google Cloud", Url = "https://api.usercentrics.eu/gvl/v3/en.json" },
                new DpiTarget { Id = "US.FST-01", Provider = "Fastly", Url = "https://openoffice.apache.org/images/blog/rejected.png" },
                new DpiTarget { Id = "US.FST-02", Provider = "Fastly", Url = "https://www.juniper.net/etc.clientlibs/juniper/clientlibs/clientlib-site/resources/fonts/lato/Lato-Regular.woff2" },
                new DpiTarget { Id = "PL.AKM-01", Provider = "Akamai", Url = "https://www.lg.com/lg5-common-gp/library/jquery.min.js" },
                new DpiTarget { Id = "PL.AKM-02", Provider = "Akamai", Url = "https://media-assets.stryker.com/is/image/stryker/gateway_1?$max_width_1410$" },
                new DpiTarget { Id = "US.CDN77-01", Provider = "CDN77", Url = "https://cdn.eso.org/images/banner1920/eso2520a.jpg" },
                new DpiTarget { Id = "DE.CNTB-01", Provider = "Contabo", Url = "https://cloudlets.io/wp-content/themes/Avada/includes/lib/assets/fonts/fontawesome/webfonts/fa-solid-900.woff2" },
                new DpiTarget { Id = "FR.SW-01", Provider = "Scaleway", Url = "https://renklisigorta.com.tr/teklif-al" },
                new DpiTarget { Id = "US.CNST-01", Provider = "Constant", Url = "https://cdn.xuansiwei.com/common/lib/font-awesome/4.7.0/fontawesome-webfont.woff2?v=4.7.0" }
            };
        }

        public List<DpiTarget> GetDpiTargets(string customUrl = null)
        {
            if (!string.IsNullOrEmpty(customUrl))
            {
                return new List<DpiTarget> { new DpiTarget { Id = "CUSTOM", Provider = "Custom", Url = customUrl } };
            }

            var targets = new List<DpiTarget>();
            foreach (var entry in _dpiSuite)
            {
                var repeat = entry.Times;
                if (repeat < 1) repeat = 1;

                for (int i = 0; i < repeat; i++)
                {
                    string suffix = repeat > 1 ? $"@{i}" : "";
                    targets.Add(new DpiTarget
                    {
                        Id = $"{entry.Id}{suffix}",
                        Provider = entry.Provider,
                        Url = entry.Url
                    });
                }
            }

            return targets;
        }

        public List<string> GetStandardTargets()
        {
            return new List<string>
            {
                "https://discord.com",
                "https://gateway.discord.gg",
                "https://cdn.discordapp.com",
                "https://updates.discord.com",
                "https://www.youtube.com",
                "https://youtu.be",
                "https://i.ytimg.com",
                "https://redirector.googlevideo.com",
                "https://www.google.com",
                "https://www.gstatic.com",
                "https://www.cloudflare.com",
                "https://cdnjs.cloudflare.com"
            };
        }

        public async Task<bool> IsDomainBlocked(string domain)
        {
            try
            {
                // Delete the protocol if there is one
                if (domain.StartsWith("http://") || domain.StartsWith("https://"))
                {
                    domain = domain.Split("://")[1];
                }

                // Delete path if there is one
                if (domain.Contains("/"))
                {
                    domain = domain.Split("/")[0];
                }

                var testUrl = $"https://{domain}";
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));
                var response = await httpClient.GetAsync(testUrl, cts.Token);
                return false;
            }
            catch
            {
                return true;
            }
        }

        public async Task<List<TestResult>> RunStandardTestsAsync(List<ZapretProfile> profiles, List<string> domains)
        {
            _logger.LogInformation($"Running standard tests on {profiles.Count} profiles for {domains.Count} domains");
            var results = new List<TestResult>();
            var pingTargets = domains.Select(domain =>
            {
                var cleanDomain = domain;
                if (cleanDomain.StartsWith("http://") || cleanDomain.StartsWith("https://"))
                {
                    cleanDomain = cleanDomain.Split("://")[1];
                }
                if (cleanDomain.Contains("/"))
                {
                    cleanDomain = cleanDomain.Split("/")[0];
                }
                return cleanDomain;
            }).ToList();

            foreach (var profile in profiles)
            {
                var profileStart = DateTime.UtcNow;
                _logger.LogInformation($"=== Starting tests for profile: {profile.Name} ===");

                _logger.LogDebug($"Switching to profile: {profile.Name}");
                await _zapretManager.SelectProfileAsync(profile.Name);

                if (!await _zapretManager.StartAsync(false, true))
                {
                    _logger.LogError($"Failed to start profile: {profile.Name}");
                    results.Add(new TestResult
                    {
                        ProfileName = profile.Name,
                        TargetName = "INIT",
                        TestType = "Init",
                        Success = false,
                        Message = _localizationService.GetString("init_failed")
                    });
                    continue;
                }

                await Task.Delay(500);

                _logger.LogInformation($"Profile {profile.Name} started successfully. Running {domains.Count * 3} HTTP tests and {pingTargets.Count} ping tests");

                // HTTP tests in parallel
                var httpTasks = new List<Task<TestResult>>();
                foreach (var domain in domains)
                {
                    httpTasks.Add(TestHttpVersionAsync(profile.Name, domain, _httpClient, "HTTP"));
                    httpTasks.Add(TestHttpVersionAsync(profile.Name, domain, _httpClientTls12, "TLS1.2"));
                    httpTasks.Add(TestHttpVersionAsync(profile.Name, domain, _httpClientTls13, "TLS1.3"));
                }
                _logger.LogDebug($"Starting {httpTasks.Count} HTTP tests in parallel");
                results.AddRange(await Task.WhenAll(httpTasks));
                _logger.LogDebug($"HTTP tests completed for profile {profile.Name}");

                // Ping tests in parallel
                var pingTasks = pingTargets.Select(target => TestPingAsync(profile.Name, target));
                _logger.LogDebug($"Starting {pingTasks.Count()} ping tests in parallel");
                results.AddRange(await Task.WhenAll(pingTasks));
                _logger.LogDebug($"Ping tests completed for profile {profile.Name}");

                if (_zapretManager.IsRunning())
                {
                    _logger.LogDebug($"Stopping profile {profile.Name} after tests");
                    await _zapretManager.StopAsync(false);
                }

                var profileDuration = DateTime.UtcNow - profileStart;
                _logger.LogInformation($"=== Profile {profile.Name} testing completed in {profileDuration.TotalSeconds:F1} seconds. Total results: {results.Count(r => r.ProfileName == profile.Name)} ===");
                await Task.Delay(1000);
            }

            return results;
        }

        private async Task<TestResult> TestHttpVersionAsync(string profileName, string url, HttpClient client, string version)
        {
            _logger.LogDebug($"Testing {version} connection to {url} for profile {profileName}");
            try
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = await client.SendAsync(request, cts.Token);

                return new TestResult
                {
                    ProfileName = profileName,
                    TargetName = url,
                    TestType = version,
                    Success = response.IsSuccessStatusCode,
                    Message = $"{_localizationService.GetString("http_success", response.StatusCode)}",
                    StatusCode = (int)response.StatusCode,
                    ContentLength = response.Content.Headers.ContentLength
                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"Timeout ({version}) when connecting to {url} for profile {profileName}");
                return new TestResult
                {
                    ProfileName = profileName,
                    TargetName = url,
                    TestType = version,
                    Success = false,
                    Message = _localizationService.GetString("conn_timeout")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in {version} test to {url} for profile {profileName}: {ex.Message}");
                return new TestResult
                {
                    ProfileName = profileName,
                    TargetName = url,
                    TestType = version,
                    Success = false,
                    Message = $"{_localizationService.GetString("error_occurred")}: {ex.Message}"
                };
            }
        }

        private async Task<TestResult> TestPingAsync(string profileName, string target)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, 3000);
                return new TestResult
                {
                    ProfileName = profileName,
                    TargetName = target,
                    TestType = "Ping",
                    Success = reply.Status == IPStatus.Success,
                    Message = reply.Status == IPStatus.Success
                        ? $"{reply.RoundtripTime} ms"
                        : reply.Status.ToString(),
                    PingTimeMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : null
                };
            }
            catch (PingException ex)
            {
                return new TestResult
                {
                    ProfileName = profileName,
                    TargetName = target,
                    TestType = "Ping",
                    Success = false,
                    Message = $"Ping error: {ex.Message}"
                };
            }
        }

        public async Task<List<TestResult>> RunDpiTestsAsync(List<ZapretProfile> profiles, List<DpiTarget> targets)
        {
            _logger.LogInformation($"Running DPI tests on {profiles.Count} profiles with {targets.Count} targets");
            var results = new List<TestResult>();
            var dpiTimeoutSeconds = 5;
            var dpiRangeBytes = 262144; // 256KB
            var dpiWarnMinKB = 14;
            var dpiWarnMaxKB = 22;

            foreach (var profile in profiles)
            {
                var profileStart = DateTime.UtcNow;
                _logger.LogInformation($"=== Starting DPI tests for profile: {profile.Name} ===");
                await _zapretManager.SelectProfileAsync(profile.Name);

                if (!await _zapretManager.StartAsync(false, true))
                {
                    _logger.LogError($"Failed to start profile for DPI testing: {profile.Name}");
                    results.Add(new TestResult
                    {
                        ProfileName = profile.Name,
                        TargetName = "INIT",
                        TestType = "Init",
                        Success = false,
                        Message = _localizationService.GetString("init_failed")
                    });
                    continue;
                }

                await Task.Delay(500);

                // Run DPI tests in parallel for all targets
                var dpiTasks = targets.Select(target =>
                    TestDpiAsync(profile.Name, target, dpiRangeBytes, dpiTimeoutSeconds, dpiWarnMinKB, dpiWarnMaxKB)
                );
                _logger.LogDebug($"Running {targets.Count} DPI tests in parallel for profile {profile.Name}");
                results.AddRange(await Task.WhenAll(dpiTasks));
                _logger.LogDebug($"DPI tests completed for profile {profile.Name}");

                if (_zapretManager.IsRunning())
                {
                    await _zapretManager.StopAsync(false);
                }

                var profileDuration = DateTime.UtcNow - profileStart;
                _logger.LogInformation($"=== Profile {profile.Name} DPI testing completed in {profileDuration.TotalSeconds:F1} seconds ===");
                await Task.Delay(1000);
            }

            return results;
        }

        private async Task<TestResult> TestDpiAsync(string profileName, DpiTarget target, int rangeBytes, int timeoutSeconds, int warnMinKB, int warnMaxKB)
        {
            try
            {
                _logger.LogDebug($"Testing DPI bypass for {target.Id} ({target.Provider}) at {target.Url} for profile {profileName}");
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 2));
                var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, rangeBytes - 1);

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                long contentLength = 0;
                if (response.Content != null)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                    var buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                    {
                        contentLength += bytesRead;
                    }
                }

                var sizeKB = contentLength / 1024.0;
                bool isLikelyBlocked = false;
                string status = "OK";

                if (!response.IsSuccessStatusCode || contentLength == 0)
                {
                    if (IsLikelyDpiBlocked((int)response.StatusCode, contentLength, warnMinKB, warnMaxKB))
                    {
                        isLikelyBlocked = true;
                        status = "LIKELY_BLOCKED";
                    }
                    else
                    {
                        status = "FAIL";
                    }
                }

                _logger.LogInformation($"DPI bypass for {target.Url}: code={response.StatusCode} size={contentLength} kilobytes={sizeKB:F1} status={status}");
                return new TestResult
                {
                    ProfileName = profileName,
                    TargetName = $"{target.Id} <{target.Provider}>",
                    TestType = "DPI",
                    Success = response.IsSuccessStatusCode,
                    Message = $"{_localizationService.GetString("http_success", response.StatusCode)} size={contentLength} KB={sizeKB:F1} {status}",
                    IsLikelyBlocked = isLikelyBlocked,
                    StatusCode = (int)response.StatusCode,
                    ContentLength = contentLength
                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"Timeout during DPI test for {target.Id} ({target.Provider}) in profile {profileName}");
                return new TestResult
                {
                    ProfileName = profileName,
                    TargetName = $"{target.Id} <{target.Provider}>",
                    TestType = "DPI",
                    Success = false,
                    Message = _localizationService.GetString("conn_timeout"),
                    IsLikelyBlocked = false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"DPI test failed for {target.Id} ({target.Provider}) in profile {profileName}: {ex.Message}");
                return new TestResult
                {
                    ProfileName = profileName,
                    TargetName = $"{target.Id} <{target.Provider}>",
                    TestType = "DPI",
                    Success = false,
                    Message = $"{_localizationService.GetString("error_occurred")}: {ex.Message}",
                    IsLikelyBlocked = false
                };
            }
        }

        private bool IsLikelyDpiBlocked(int statusCode, long contentLength, int warnMinKB, int warnMaxKB)
        {
            var sizeKB = contentLength / 1024.0;

            return (statusCode >= 400 || statusCode == 0) &&
                   sizeKB >= warnMinKB && sizeKB <= warnMaxKB;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                    _httpClientTls12?.Dispose();
                    _httpClientTls13?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
