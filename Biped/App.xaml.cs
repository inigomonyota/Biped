using System;
using System.Linq;
using System.Windows;

namespace biped
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 1. Check for silent flag immediately
            // We accept both /silent and -silent
            bool silent = e.Args.Any(arg => arg.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                                            arg.Equals("-silent", StringComparison.OrdinalIgnoreCase));

            // 2. Create the window
            // Note: MainWindow constructor now initializes the hardware immediately.
            // This ensures pedals work even if we never call .Show()
            MainWindow wnd = new MainWindow();

            // 3. Process Command Line Key Bindings (if any)
            if (e.Args.Length > 0)
            {
                var cliBindings = ProcessCommandLineArguments(e.Args);

                // Only apply if we got a full set of 3 bindings
                if (cliBindings.Length == 3)
                {
                    wnd.ApplyCommandLineBindings(cliBindings[0], cliBindings[1], cliBindings[2]);
                }
            }

            // 4. Decide whether to show the UI
            if (!silent)
            {
                wnd.Show();
            }
            else
            {
                // If silent, we do NOT show the window.
                // The application keeps running because the MainWindow object exists 
                // (just hidden), and the System Tray icon (initialized in constructor) keeps it accessible.
            }
        }

        private uint[] ProcessCommandLineArguments(string[] args)
        {
            uint leftBinding = uint.MaxValue;
            uint middleBinding = uint.MaxValue;
            uint rightBinding = uint.MaxValue;
            int argIndex = 0;

            try
            {
                while (argIndex < args.Length)
                {
                    string currentArg = args[argIndex].ToLower();

                    switch (currentArg)
                    {
                        case "-left":
                            if (argIndex + 1 < args.Length)
                            {
                                argIndex++;
                                leftBinding = uint.Parse(args[argIndex]);
                            }
                            break;

                        case "-middle":
                            if (argIndex + 1 < args.Length)
                            {
                                argIndex++;
                                middleBinding = uint.Parse(args[argIndex]);
                            }
                            break;

                        case "-right":
                            if (argIndex + 1 < args.Length)
                            {
                                argIndex++;
                                rightBinding = uint.Parse(args[argIndex]);
                            }
                            break;

                        // Explicitly ignore the silent flag here so it doesn't trigger an error
                        // (We handled it in Application_Startup)
                        case "/silent":
                        case "-silent":
                            break;
                    }
                    argIndex++;
                }

                // Check if we successfully parsed all 3 bindings
                if (leftBinding != uint.MaxValue && middleBinding != uint.MaxValue && rightBinding != uint.MaxValue)
                {
                    return new uint[] { leftBinding, middleBinding, rightBinding };
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error parsing command line arguments: " + ex.Message);
            }

            // Return empty if parsing failed or arguments were incomplete
            return new uint[0];
        }
    }
}