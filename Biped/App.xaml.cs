using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace biped
{
    public partial class App : Application
    {
        // Unique IDs
        private const string AppId = "Biped_Unique_Mutex_v2"; // Changed v2 to ensure clean start
        private const string PipeName = "Biped_IPC_Pipe_v2";

        private Mutex _appMutex;
        private MainWindow _mainWindow;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 1. Single Instance Check
            _appMutex = new Mutex(true, AppId, out bool createdNew);

            if (!createdNew)
            {
                // We are the 2nd instance. Send args to the 1st instance and quit.
                SendArgsToRunningInstance(e.Args);
                Shutdown();
                return;
            }

            // --- MAIN INSTANCE STARTUP ---

            // 2. Start Background Listener
            Task.Run(() => StartPipeServer());

            // 3. Create Window (This initializes Hardware + Default Profile)
            _mainWindow = new MainWindow();

            // 4. Process Initial Arguments (if launched via command line)
            ProcessArgs(_mainWindow, e.Args);

            // 5. Show Window (unless /silent)
            bool silent = e.Args.Any(arg => arg.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                                            arg.Equals("-silent", StringComparison.OrdinalIgnoreCase));

            if (!silent)
            {
                _mainWindow.Show();
            }
        }

        // ---------------------------------------------------------
        // LOGIC: Argument Processing & Path Resolution
        // ---------------------------------------------------------
        private void ProcessArgs(MainWindow wnd, string[] args)
        {
            bool shouldShow = false;

            // Check for explicit show request
            if (args.Length == 1 && args[0] == "||SHOW||")
            {
                shouldShow = true;
            }
            // Or if there are NO args at all coming from another instance → treat as show request
            else if (args.Length == 0 || (args.Length == 1 && string.IsNullOrWhiteSpace(args[0])))
            {
                shouldShow = true;
            }

            // Only show if explicitly requested
            if (shouldShow)
            {
                wnd.Dispatcher.Invoke(() =>
                {
                    if (wnd.WindowState == WindowState.Minimized)
                        wnd.WindowState = WindowState.Normal;

                    wnd.Show();
                    wnd.Activate();
                    wnd.Topmost = true;
                    wnd.Topmost = false;
                    wnd.Focus();
                });
            }

            // Always process real arguments (even if we didn't show)
            string cfgArg = GetArgValue(args, "-cfg");
            if (!string.IsNullOrEmpty(cfgArg))
            {
                string finalPath = ResolveConfigPath(cfgArg);
                if (!string.IsNullOrEmpty(finalPath))
                {
                    wnd.Dispatcher.Invoke(() => wnd.ApplyGameProfile(finalPath));
                }
            }

            // Respect /silent (even if combined with -cfg)
            bool silent = args.Any(a => a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                                        a.Equals("-silent", StringComparison.OrdinalIgnoreCase));
            if (silent && shouldShow)
            {
                // If /silent was passed, override the show request
                wnd.Dispatcher.Invoke(() =>
                {
                    wnd.Hide(); // or just don't show if already hidden
                });
            }
        }
        private string ResolveConfigPath(string input)
        {
            // 1. If absolute path and exists, use it.
            if (Path.IsPathRooted(input) && File.Exists(input)) return input;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string cfgDir = Path.Combine(baseDir, "cfg");

            // 2. Check: "input" in base dir
            string check1 = Path.Combine(baseDir, input);
            if (File.Exists(check1)) return check1;

            // 3. Check: "input.cfg" in base dir
            string check2 = Path.Combine(baseDir, input + ".cfg");
            if (File.Exists(check2)) return check2;

            // 4. Check: "input" in cfg subdir (Most common)
            string check3 = Path.Combine(cfgDir, input);
            if (File.Exists(check3)) return check3;

            // 5. Check: "input.cfg" in cfg subdir
            string check4 = Path.Combine(cfgDir, input + ".cfg");
            if (File.Exists(check4)) return check4;

            return null; // Not found
        }

        private string GetArgValue(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        // ---------------------------------------------------------
        // IPC SERVER (Listens for commands)
        // ---------------------------------------------------------
        private async void StartPipeServer()
        {
            while (true)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In))
                    {
                        await server.WaitForConnectionAsync();

                        using (var reader = new StreamReader(server))
                        {
                            string argsLine = await reader.ReadToEndAsync();
                            string[] args = argsLine.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);

                            if (_mainWindow != null)
                            {
                                await _mainWindow.Dispatcher.InvokeAsync(() => ProcessArgs(_mainWindow, args));
                            }
                        }
                    }
                }
                catch
                {
                    await Task.Delay(1000); // Retry on crash
                }
            }
        }

        // ---------------------------------------------------------
        // IPC CLIENT (Sends commands)
        // ---------------------------------------------------------
        private void SendArgsToRunningInstance(string[] args)
        {
            // Retry a few times in case the pipe server is still starting
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        client.Connect(200);

                        using (var writer = new StreamWriter(client))
                        {
                            // Decide what to send
                            bool hasRealArgs = args.Any(a =>
                                a.StartsWith("-cfg", StringComparison.OrdinalIgnoreCase) ||
                                a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                                a.Equals("-silent", StringComparison.OrdinalIgnoreCase));

                            if (!hasRealArgs && args.Length > 0)
                            {
                                // Only bare launch (or unknown args) → request show
                                writer.Write("||SHOW||");
                            }
                            else
                            {
                                // Real command-line work → just forward args, no show
                                writer.Write(string.Join("|", args));
                            }
                            writer.Flush();
                        }
                        return; // Success
                    }
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}