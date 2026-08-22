using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Utilities;
using HidWizards.UCR.Utilities;
using HidWizards.UCR.Views;
using Application = System.Windows.Application;

namespace HidWizards.UCR
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application, IDisposable
    {
        private Context context;
        private HidGuardianClient _hidGuardianClient;
        private SingleGlobalInstance mutex;
        private Thread _splashThread;
        private SplashWindow _splashWindow;
        private ManualResetEventSlim _splashReady;
        private volatile bool _splashCloseRequested;
        private readonly Stopwatch _startupStopwatch = new Stopwatch();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AppDomain.CurrentDomain.UnhandledException += AppDomain_CurrentDomain_UnhandledException;

            mutex = new SingleGlobalInstance();
            if (mutex.HasHandle && GetProcesses().Length <= 1)
            {
                Logger.Info("Launching UCR");
                // The splash is a real Window on its own dispatcher. Keep shutdown explicit until the
                // actual MainWindow has been assigned so closing the splash can never terminate UCR.
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                _startupStopwatch.Restart();
                StartSplash();

                try
                {
                    RunStartupStage("Checking HidGuardian...", () =>
                    {
                        _hidGuardianClient = new HidGuardianClient();
                        _hidGuardianClient.WhitelistProcess();
                    });

                    InitializeUcr();

                    RunStartupStage("Checking plugins...", CheckForBlockedDll);

                    RunStartupStage("Processing command line...", () => context.ParseCommandLineArguments(e.Args));

                    UpdateSplash("Opening UCR...");
                    var windowStage = Stopwatch.StartNew();
                    var mw = new MainWindow(context);
                    Current.MainWindow = mw;
                    mw.Show();
                    ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
                    windowStage.Stop();
                    Logger.Info($"Startup stage 'Opening UCR' completed in {windowStage.ElapsedMilliseconds} ms");

                    _startupStopwatch.Stop();
                    Logger.Info($"UCR startup completed in {_startupStopwatch.ElapsedMilliseconds} ms");
                    CloseSplash();
                }
                catch
                {
                    CloseSplash();
                    throw;
                }
            }
            else
            {
                SendArgs(string.Join(";", e.Args));
                Current.Shutdown();
            }
        }

        private void InitializeUcr()
        {
            RunStartupStage("Loading interface resources...", () => new ResourceLoader().Load());
            RunStartupStage("Initializing device providers and loading profiles...", () => context = Context.Load());
        }

        private void RunStartupStage(string status, Action action)
        {
            UpdateSplash(status);
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            Logger.Info($"Startup stage '{status}' completed in {stopwatch.ElapsedMilliseconds} ms");
        }

        private void StartSplash()
        {
            _splashCloseRequested = false;
            _splashReady = new ManualResetEventSlim(false);
            _splashThread = new Thread(() =>
            {
                var splash = new SplashWindow();
                _splashWindow = splash;
                if (_splashCloseRequested)
                {
                    _splashReady.Set();
                    return;
                }

                splash.Closed += (sender, args) => System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                splash.Show();
                _splashReady.Set();
                System.Windows.Threading.Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "UCR Startup Splash"
            };
            _splashThread.SetApartmentState(ApartmentState.STA);
            _splashThread.Start();

            // Do not let a splash-screen failure become a startup blocker of its own.
            _splashReady.Wait(2000);
        }

        private void UpdateSplash(string status)
        {
            if (_splashCloseRequested) return;
            var splash = _splashWindow;
            if (splash == null) return;

            try
            {
                splash.Dispatcher.BeginInvoke(new Action(() => splash.SetStatus(status)));
            }
            catch (InvalidOperationException)
            {
                // Splash is already closing; startup should continue normally.
            }
        }

        private void CloseSplash()
        {
            _splashCloseRequested = true;
            var splash = _splashWindow;
            _splashWindow = null;
            if (splash == null) return;

            try
            {
                splash.Dispatcher.BeginInvoke(new Action(splash.Close));
            }
            catch (InvalidOperationException)
            {
                // Dispatcher has already shut down.
            }
        }

        private void CheckForBlockedDll()
        {
            if (context.GetPlugins().Count != 0) return;

            var result = MessageBox.Show("UCR has detected blocked files which are required, do you want to unblock blocked UCR files?", "Unblock files?", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            UpdateSplash("Unblocking UCR files...");
            var process = new Process
            {
                StartInfo =
                {
                    FileName = "UCR_unblocker.exe",
                    UseShellExecute = true,
                    Arguments = $"\"{Environment.CurrentDirectory}\"",
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(1000 * 60 * 5);

            var exitCode = process.ExitCode;
            if (exitCode != 0)
            {
                MessageBox.Show("UCR failed to unblock the required files", "Failed to unblock", MessageBoxButton.OK, MessageBoxImage.Error);
                Current.Shutdown();
            }

            InitializeUcr();
        }

        private static Process[] GetProcesses()
        {
            return Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName);
        }

        private void SendArgs(string args)
        {
            Logger.Info($"UCR is already running, sending args: {{{args}}}");
            // Find the window with the name of the main form
            var processes = GetProcesses();
            processes = processes.Where(p => p.Id != Process.GetCurrentProcess().Id).ToArray();
            if (processes.Length == 0) return;

            IntPtr ptrCopyData = IntPtr.Zero;
            try
            {
                // Create the data structure and fill with data
                NativeMethods.COPYDATASTRUCT copyData = new NativeMethods.COPYDATASTRUCT
                {
                    dwData = new IntPtr(2),
                    cbData = args.Length + 1,
                    lpData = Marshal.StringToHGlobalAnsi(args)
                };
                // Just a number to identify the data type
                // One extra byte for the \0 character

                // Allocate memory for the data and copy
                ptrCopyData = Marshal.AllocCoTaskMem(Marshal.SizeOf(copyData));
                Marshal.StructureToPtr(copyData, ptrCopyData, false);

                // Send the message
                foreach (var proc in processes)
                {
                    if (proc.MainWindowHandle == IntPtr.Zero) continue;
                    NativeMethods.SendMessage(proc.MainWindowHandle, NativeMethods.WM_COPYDATA, IntPtr.Zero, ptrCopyData);
                }

            }
            catch (Exception e)
            {
                Logger.Error("Unable to send args to existing process", e);
            }
            finally
            {
                // Free the allocated memory after the control has been returned
                if (ptrCopyData != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(ptrCopyData);
            }
        }

        public void Dispose()
        {
            CloseSplash();
            mutex?.Dispose();
            context?.Dispose();
            _hidGuardianClient?.Dispose();
        }

        private void App_OnExit(object sender, ExitEventArgs e)
        {
            context?.DevicesManager.UpdateDeviceCache();

            Dispose();
        }

        private static void AppDomain_CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = (Exception) e.ExceptionObject;
            Logger.Fatal(exception.Message, exception);
        }
    }
}
