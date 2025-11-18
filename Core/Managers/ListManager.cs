using ZapretCLI.UI;

namespace ZapretCLI.Core.Managers
{
    public static class ListManager
    {
        public static async Task AddDomainInteractive()
        {
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var listsPath = Path.Combine(appPath, "lists");
            Directory.CreateDirectory(listsPath);

            ConsoleUI.WriteLine("Available lists:", ConsoleUI.yellow);
            ConsoleUI.WriteLine("  1. list-general.txt (main list)");
            ConsoleUI.WriteLine("  2. list-google.txt  (Google services)");
            ConsoleUI.WriteLine("  3. list-exclude.txt (exclusions)");
            ConsoleUI.WriteLine("");

            var choice = ConsoleUI.ReadLineWithPrompt("Select list (1-3): ");
            string listFile = "list-general.txt";

            switch (choice.Trim())
            {
                case "2": listFile = "list-google.txt"; break;
                case "3": listFile = "list-exclude.txt"; break;
            }

            var domain = ConsoleUI.ReadLineWithPrompt("Enter domain to add: ").Trim().ToLower();
            if (string.IsNullOrWhiteSpace(domain) || !IsValidDomain(domain))
            {
                ConsoleUI.WriteLine("[✗] Invalid domain format!", ConsoleUI.red);
                return;
            }

            var filePath = Path.Combine(listsPath, listFile);
            await AddDomainToFile(filePath, domain);
        }

        private static async Task AddDomainToFile(string filePath, string domain)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    await File.WriteAllTextAsync(filePath, $"{domain}\n");
                    ConsoleUI.WriteLine($"[✓] Created new list and added {domain}", ConsoleUI.green);
                    return;
                }

                var lines = (await File.ReadAllLinesAsync(filePath))
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .ToList();

                if (lines.Contains(domain))
                {
                    ConsoleUI.WriteLine($"[✓] Domain {domain} already exists in the list", ConsoleUI.yellow);
                    return;
                }

                await File.AppendAllTextAsync(filePath, $"{domain}\n");
                ConsoleUI.WriteLine($"[✓] Successfully added {domain} to {Path.GetFileName(filePath)}", ConsoleUI.green);
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteLine($"[✗] Failed to add domain: {ex.Message}", ConsoleUI.red);
            }
        }

        private static bool IsValidDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return false;
            if (domain.Length > 253) return false;

            var parts = domain.Split('.');
            if (parts.Length < 2) return false;

            return parts.All(part =>
                !string.IsNullOrWhiteSpace(part) &&
                part.Length <= 63 &&
                part.All(c => char.IsLetterOrDigit(c) || c == '-'));
        }
    }
}