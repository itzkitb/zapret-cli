using Pastel;

namespace ZapretCLI.UI
{
    public static class ConsoleUI
    {
        private static List<string> history = [];
        private static readonly string Logo = @" _____                 _   
|__   |___ ___ ___ ___| |_ 
|   __| .'| . |  _| -_|  _|
|_____|__,|  _|_| |___|_|  
          |_|              
";

        public const string red = "#FF8F8F";
        public const string yellow = "#FFF1CB";
        public const string blue = "#C2E2FA";
        public const string purple = "#B7A3E3";
        public const string white = "#E3E3E3";
        public const string green = "#CBF3BB";
        public static readonly Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        public static void WriteLine(string text, string color = white, bool save = true)
        {
            if (save)
            {
                string newText = text;
                if (newText.StartsWith("\r"))
                {
                    newText = newText.Substring(1);
                    history.RemoveAt(history.Count - 1);
                }

                history.Add(newText.Pastel(color));
            }

            Console.WriteLine(text.Pastel(color));
        }

        public static void HistoryRestore()
        {
            Clear(false);

            foreach (var item in history)
            {
                Console.WriteLine(item);
            }
        }

        public static void WriteProgress(int percentage, string prefix = "")
        {
            if (history[history.Count - 1].Contains($"{prefix}Downloading: [", StringComparison.OrdinalIgnoreCase))
            {
                history.RemoveAt(history.Count - 1);
            }
            history.Add($"{prefix}Downloading: [{new string('#', percentage / 5)}{new string(' ', 20 - percentage / 5)}] {percentage}%");
            Console.Write($"\r{prefix}Downloading: [{new string('#', percentage / 5)}{new string(' ', 20 - percentage / 5)}] {percentage}%");
        }

        public static string ReadLineWithPrompt(string prompt)
        {
            Console.Write(prompt.Pastel("#B7A3E3"));
            string result = Console.ReadLine() ?? string.Empty;
            history.Add($"{prompt.Pastel("#B7A3E3")}{result}");

            return result;
        }

        public static async Task ShowWelcome()
        {
            Clear();
            WriteLine(Logo, purple);
            WriteLine("Welcome to Zapret CLI! • DPI bypass tool", blue);
            WriteLine("This is the first launch. Downloading the latest version...", yellow);
            await Task.Delay(1000);
        }

        public static void ShowHeader()
        {
            Clear();
            WriteLine(Logo, purple);
            WriteLine($"Zapret CLI v{version} by SillyApps", blue);
            WriteLine("Type 'help' for available commands", yellow);
        }

        public static void Clear(bool clearHistory = true)
        {
            if (clearHistory) history.Clear();
            Console.Clear();
            Console.Write("\x1b[3J"); // Clearing the scrollback buffer
            Console.Write("\x1b[2J"); // Cleaning the screen
            Console.Write("\x1b[H");  // Move cursor to beginning
            Console.Clear();
        }

        public static void ShowHelp()
        {
            WriteLine("Commands:", yellow);
            WriteLine("  start          - Start the DPI bypass service with selected profile", white);
            WriteLine("  stop           - Stop the running service", white);
            WriteLine("  restart        - Restart the service with current profile", white);
            WriteLine("  status         - Display current service status", white);
            WriteLine("  add            - Add new domain to blocklists", white);
            WriteLine("  update         - Check for and install the latest version", white);
            WriteLine("  select [name]  - Select profile to use (interactive if no name provided)", white);
            WriteLine("  info           - Show details of currently selected profile", white);
            WriteLine("  list           - List all available profiles", white);
            WriteLine("  test           - Launches a check of profiles suitable for bypassing blocking", white);
            WriteLine("  del-service    - Stop the service and remove the drivers. Especially useful when deleting 'WinDivert64.sys'", white);
            WriteLine("  toggle-game-filter - Toggle game filter mode (ports 1024-65535 vs port 12)", white);
            WriteLine("  game-filter-status - Show current game filter status", white);
            WriteLine("  exit           - Stop the service and exit", white);
        }
    }
}