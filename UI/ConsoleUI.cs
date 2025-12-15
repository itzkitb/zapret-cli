using Pastel;

namespace ZapretCLI.UI
{
    public static class ConsoleUI
    {
        public const string redName = "indianred1";
        public const string greenName = "palegreen1";
        public const string orangeName = "lightsalmon3_1";
        public const string greyName = "grey62";
        public const string darkGreyName = "grey42";

        public static void Clear()
        {
            Console.Clear();
            Console.Write("\x1b[3J"); // Clearing the scrollback buffer
            Console.Write("\x1b[2J"); // Cleaning the screen
            Console.Write("\x1b[H");  // Move cursor to beginning
            Console.Clear();
        }
    }
}