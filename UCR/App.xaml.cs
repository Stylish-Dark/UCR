using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
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
        private Thread _shutdownSplashThread;
        private SplashWindow _shutdownSplashWindow;
        private ManualResetEventSlim _shutdownSplashReady;
        private volatile bool _shutdownSplashCloseRequested;
        private bool _shutdownInProgress;
        private bool _shutdownCleanupComplete;
        private bool _disposed;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            RuntimePathManager.NormalizeWorkingDirectory();
            Logger.InitializeSession();
            AppearanceManager.ApplySavedAccent();
            AppearanceManager.ApplySavedUiScale();
            AppDomain.CurrentDomain.UnhandledException += AppDomain_CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            mutex = new SingleGlobalInstance();
            if (mutex.HasHandle)
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
                    mw.BringToForeground();
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
            RunStartupStage("Initializing device providers and loading profiles...", () =>
            {
                context = Context.Load();
                Logger.SetDiagnosticContextProvider(BuildDiagnosticContextSnapshot);
            });
        }

        private string BuildDiagnosticContextSnapshot()
        {
            var currentContext = context;
            if (currentContext == null) return "Context: unavailable";

            var builder = new StringBuilder();
            builder.AppendLine("Top-level profiles: " + (currentContext.Profiles?.Count ?? 0));
            var active = currentContext.ActiveProfile;
            if (active == null)
            {
                builder.Append("Active profile: none");
                return builder.ToString();
            }

            builder.AppendLine("Active profile: " + active.ProfileBreadCrumbs());
            builder.AppendLine("Mappings: " + (active.Mappings?.Count ?? 0));
            builder.AppendLine("Input device configurations: " + (active.InputDeviceConfigurations?.Count ?? 0));
            builder.Append("Output device configurations: " + (active.OutputDeviceConfigurations?.Count ?? 0));
            return builder.ToString();
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
            var mappingPluginsLoaded = context.GetPlugins().Count != 0;
            var deviceProvidersLoaded = context.DevicesManager.HasLoadedProviderReports();
            if (mappingPluginsLoaded && deviceProvidersLoaded) return;

            Logger.Warn($"Required runtime component check failed. Mapping plugins loaded: {mappingPluginsLoaded}; device provider reports loaded: {deviceProvidersLoaded}.");
            var result = HidWizards.UCR.Utilities.DarkMessageBox.Show("UCR could not load one or more required plugin/provider DLLs. Do you want to unblock the UCR files and retry?", "Unblock files?", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                HidWizards.UCR.Utilities.DarkMessageBox.Show("UCR failed to unblock the required files", "Failed to unblock", MessageBoxButton.OK, MessageBoxImage.Error);
                Current.Shutdown();
            }

            InitializeUcr();
            if (context.GetPlugins().Count == 0 || !context.DevicesManager.HasLoadedProviderReports())
            {
                Logger.Error("Required UCR plugins/providers are still unavailable after the unblock retry.");
            }
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

                // MainWindowHandle can be zero when UCR is hidden to the tray. Enumerate all
                // top-level windows owned by the existing process so a second launch can always
                // reach the hidden WPF main window and ask it to surface itself.
                foreach (var proc in processes)
                {
                    foreach (var handle in GetTopLevelWindowHandles(proc.Id))
                    {
                        NativeMethods.SendMessage(handle, NativeMethods.WM_COPYDATA, IntPtr.Zero, ptrCopyData);
                    }
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

        private static IEnumerable<IntPtr> GetTopLevelWindowHandles(int processId)
        {
            var handles = new List<IntPtr>();
            NativeMethods.EnumWindows((handle, parameter) =>
            {
                uint ownerProcessId;
                NativeMethods.GetWindowThreadProcessId(handle, out ownerProcessId);
                if (ownerProcessId == (uint)processId) handles.Add(handle);
                return true;
            }, IntPtr.Zero);
            return handles.Distinct();
        }

        public void ShutdownWithProgress(Window mainWindow, bool saveContext)
        {
            if (_shutdownInProgress) return;
            _shutdownInProgress = true;
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            Logger.Info("UCR shutdown started");

            try
            {
                StartShutdownSplash();
                mainWindow?.Hide();
                var ucrWindow = mainWindow as MainWindow;
                if (ucrWindow != null)
                {
                    RunShutdownStage("Closing profile windows...", 10, ucrWindow.PrepareForShutdown);
                }
                if (saveContext)
                {
                    RunShutdownStage("Saving configuration...", 15, () => context?.SaveContext());
                }
                else
                {
                    UpdateShutdownProgress(15);
                }
                RunShutdownStage("Saving device state...", 35, () => context?.DevicesManager.UpdateDeviceCache());
                RunShutdownStage("Stopping input and output services...", 70, () =>
                {
                    context?.Dispose();
                    context = null;
                });
                RunShutdownStage("Releasing system hooks...", 88, () =>
                {
                    _hidGuardianClient?.Dispose();
                    _hidGuardianClient = null;
                });
                RunShutdownStage("Finishing...", 96, () =>
                {
                    mutex?.Dispose();
                    mutex = null;
                    Logger.Flush();
                });
                _disposed = true;
                _shutdownCleanupComplete = true;
                Logger.Info("UCR shutdown cleanup completed");
                Logger.Flush();
            }
            finally
            {
                UpdateShutdownSplash("Closing UCR...");
                UpdateShutdownProgress(100);
                Shutdown(0);
            }
        }

        private void RunShutdownStage(string status, double completedProgress, Action action)
        {
            UpdateShutdownSplash(status);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                action();
                stopwatch.Stop();
                Logger.Info($"Shutdown stage '{status}' completed in {stopwatch.ElapsedMilliseconds} ms");
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                Logger.Error($"Shutdown stage '{status}' failed after {stopwatch.ElapsedMilliseconds} ms", exception);
            }
            finally
            {
                UpdateShutdownProgress(completedProgress);
            }
        }

        private void StartShutdownSplash()
        {
            _shutdownSplashCloseRequested = false;
            _shutdownSplashReady = new ManualResetEventSlim(false);
            var accent = AppearanceManager.CurrentAccentColor;
            _shutdownSplashThread = new Thread(() =>
            {
                var splash = new SplashWindow(accent, "Shutting down UCR", string.Empty);
                splash.Topmost = true;
                _shutdownSplashWindow = splash;
                if (_shutdownSplashCloseRequested)
                {
                    _shutdownSplashReady.Set();
                    return;
                }

                splash.SetStatus("Preparing to shut down...");
                splash.SetProgress(5);
                splash.Closed += (sender, args) => System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
                splash.Show();
                _shutdownSplashReady.Set();
                System.Windows.Threading.Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "UCR Shutdown Splash"
            };
            _shutdownSplashThread.SetApartmentState(ApartmentState.STA);
            _shutdownSplashThread.Start();
            _shutdownSplashReady.Wait(2000);
        }

        private void UpdateShutdownSplash(string status)
        {
            if (_shutdownSplashCloseRequested) return;
            var splash = _shutdownSplashWindow;
            if (splash == null) return;

            try
            {
                splash.Dispatcher.BeginInvoke(new Action(() => splash.SetStatus(status)));
            }
            catch (InvalidOperationException)
            {
                // Shutdown splash is already closing.
            }
        }

        private void UpdateShutdownProgress(double value)
        {
            if (_shutdownSplashCloseRequested) return;
            var splash = _shutdownSplashWindow;
            if (splash == null) return;

            try
            {
                splash.Dispatcher.BeginInvoke(new Action(() => splash.SetProgress(value)));
            }
            catch (InvalidOperationException)
            {
                // Shutdown splash is already closing.
            }
        }

        private void CloseShutdownSplash()
        {
            _shutdownSplashCloseRequested = true;
            var splash = _shutdownSplashWindow;
            _shutdownSplashWindow = null;
            if (splash == null) return;

            try
            {
                splash.Dispatcher.Invoke(new Action(splash.Close));
            }
            catch (InvalidOperationException)
            {
                // Dispatcher has already shut down.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CloseSplash();
            CloseShutdownSplash();
            mutex?.Dispose();
            mutex = null;
            context?.Dispose();
            context = null;
            _hidGuardianClient?.Dispose();
            _hidGuardianClient = null;
        }

        private void App_OnExit(object sender, ExitEventArgs e)
        {
            if (!_shutdownCleanupComplete)
            {
                try
                {
                    context?.DevicesManager.UpdateDeviceCache();
                }
                catch (Exception exception)
                {
                    Logger.Error("Updating device cache during application exit failed", exception);
                }

                Dispose();
            }

            CloseShutdownSplash();
            Logger.Flush();
        }

        private static void AppDomain_CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled AppDomain exception");
            Logger.Fatal($"Unhandled AppDomain exception. IsTerminating={e.IsTerminating}", exception);
            Logger.WriteCrashReport("AppDomain.CurrentDomain.UnhandledException", exception);
            Logger.Flush();
        }

        private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Fatal("Unhandled WPF dispatcher exception", e.Exception);
            Logger.WriteCrashReport("Application.DispatcherUnhandledException", e.Exception);
            Logger.Flush();
            // Preserve normal crash semantics. The point of this handler is diagnostics, not swallowing bugs.
            e.Handled = false;
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error("Unobserved task exception", e.Exception);
            Logger.Flush();
        }
    }
}
