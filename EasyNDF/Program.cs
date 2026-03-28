using System.Globalization;

namespace EasyNDF
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; // Set the default culture to invariant culture for consistent formatting and parsing. Example Use Case: Ensures that date, time, number formats are consistent regardless of the user's locale settings.
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture; // Set the default UI culture to invariant culture for consistent resource lookups. Example Use Case: Ensures that the application uses the same language for UI elements regardless of the user's locale settings, which can be important for applications that are not localized or when you want to enforce a specific language.
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}