using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HidWizards.UCR.Core;
using HidWizards.UCR.Core.Models;
using HidWizards.UCR.Core.Persistence;
using System.ComponentModel.Composition;
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

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Startup can await without a main window, including during version handover.
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            RuntimePathManager.NormalizeWorkingDirectory();
            Logger.InitializeSession();
            AppDomain.CurrentDomain.UnhandledException += AppDomain_CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // User-customisable appearance files and preferences must not make UCR unlaunchable.
            try { DeviceOutlineColors.LoadFromIni(); }
            catch (Exception exception) { Logger.Warn("Could not load device outline colours; using defaults.", exception); }
            try { AppearanceManager.LoadPaletteOverrides(); }
            catch (Exception exception) { Logger.Warn("Could not load editable accent colours; using defaults.", exception); }
            try { AppearanceManager.LoadGeometryOverrides(); }
            catch (Exception exception) { Logger.Warn("Could not load editable layout values; using defaults.", exception); }
            try { AppearanceManager.ApplySavedAccent(); AppearanceManager.ApplySavedUiScale(); }
            catch (Exception exception) { Logger.Warn("Could not restore appearance preferences; using defaults.", exception); }

            mutex = new SingleGlobalInstance();
            if (!mutex.HasHandle)
            {
                try { await HandleExistingInstanceAsync(e.Args); }
                catch (Exception exception)
                {
                    Logger.Error("Could not inspect the running UCR instance.", exception);
                }
            }
            if (mutex.HasHandle)
            {
                Logger.Info("Launching UCR");
                _startupStopwatch.Restart();
                StartSplash();

                try
                {
                    RunStartupStage("Checking HidGuardian...", () =>
                    {
                        _hidGuardianClient = new HidGuardianClient();
                        _hidGuardianClient.WhitelistProcess();
                    });

                    UpdateSplash("Checking plugins and device providers...");
                    if (!await EnsureRequiredRuntimeComponentsAsync())
                    {
                        CloseSplash();
                        Shutdown(1);
                        return;
                    }

                    RunStartupStage("Loading interface resources...", () => new ResourceLoader().Load());
                    try { CreatePortableVersionBackup(); }
                    catch (Exception exception) { Logger.Error("Portable version backup failed; UCR will continue to open.", exception); }

                    UpdateSplash("Checking Exclusive Device Mode...");
                    await context.SubscriptionsManager.RecoverExclusiveModeAsync();
                    UpdateSplash("Processing command line...");
                    await context.ParseCommandLineArgumentsAsync(e.Args);

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
                Shutdown();
            }
        }

        private async Task HandleExistingInstanceAsync(string[] args)
        {
            if (mutex.TryAcquire(0)) return;

            var assembly = Assembly.GetExecutingAssembly();
            var identity = assembly.GetName();
            var metadata = FileVersionInfo.GetVersionInfo(assembly.Location);
            var ourVersion = new Version(metadata.FileVersion);
            using (var current = Process.GetCurrentProcess())
            {
                var candidates = Process.GetProcessesByName(current.ProcessName);
                try
                {
                    foreach (var process in candidates)
                    {
                        try
                        {
                            if (process.Id == current.Id || process.SessionId != current.SessionId ||
                                !TryReadUcrProcessVersion(process.MainModule.FileName, identity, metadata,
                                    out var existingVersion)) continue;

                            if (process.HasExited) continue;
                            var processId = process.Id;
                            if (existingVersion == ourVersion)
                            {
                                var deadline = Stopwatch.StartNew();
                                do
                                {
                                    if (await Task.Run(() => SendCopyData(processId, string.Join(";", args))))
                                        return;
                                    if (process.HasExited) break;
                                    await Task.Delay(150);
                                } while (deadline.Elapsed < TimeSpan.FromSeconds(5));
                                continue;
                            }

                            if (existingVersion > ourVersion)
                            {
                                Logger.Warn("A newer UCR build is already running.");
                                return;
                            }

                            // Only a UCR main window can acknowledge the version replacement message.
                            if (!await Task.Run(() => SendCopyData(processId, "UCR_INTERNAL_VERSION_REPLACE")))
                                continue;

                            Logger.Info("Waiting for UCR " + processId + " to finish its normal shutdown.");
                            var exited = await Task.Run(() => process.WaitForExit(15000));
                            if (exited && mutex.TryAcquire(0))
                            {
                                Logger.Info("Previous UCR build exited; starting this version.");
                                return;
                            }

                            Logger.Warn("Previous UCR build did not complete version handover in time.");
                            DarkMessageBox.Show("Close the running UCR instance, then open this build again.",
                                "UCR version handover", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        catch (Exception exception)
                        {
                            Logger.Warn("Could not verify or contact a UCR instance.", exception);
                        }
                    }
                }
                finally
                {
                    foreach (var candidate in candidates) candidate.Dispose();
                }
            }

            if (mutex.TryAcquire(0)) return;
            Logger.Warn("Could not contact the UCR instance holding the single-instance mutex.");
            DarkMessageBox.Show("Another UCR instance is still running. Close it before starting this build.",
                "UCR is already running", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static bool TryReadUcrProcessVersion(string candidatePath, AssemblyName identity,
            FileVersionInfo metadata, out Version version)
        {
            version = null;
            var candidateIdentity = AssemblyName.GetAssemblyName(candidatePath);
            if (!string.Equals(candidateIdentity.Name, identity.Name, StringComparison.OrdinalIgnoreCase) ||
                !(candidateIdentity.GetPublicKeyToken() ?? new byte[0]).SequenceEqual(
                    identity.GetPublicKeyToken() ?? new byte[0])) return false;

            var candidateVersion = FileVersionInfo.GetVersionInfo(candidatePath);
            return !string.IsNullOrWhiteSpace(metadata.ProductName) &&
                   !string.IsNullOrWhiteSpace(metadata.CompanyName) &&
                   string.Equals(candidateVersion.ProductName, metadata.ProductName, StringComparison.Ordinal) &&
                   string.Equals(candidateVersion.CompanyName, metadata.CompanyName, StringComparison.Ordinal) &&
                   Version.TryParse(candidateVersion.FileVersion, out version);
        }

        private static bool SendCopyData(int processId, string message)
        {
            var data = new NativeMethods.COPYDATASTRUCT
            {
                dwData = new IntPtr(2),
                cbData = Encoding.Default.GetByteCount(message) + 1,
                lpData = Marshal.StringToHGlobalAnsi(message)
            };
            IntPtr memory = IntPtr.Zero;
            try
            {
                memory = Marshal.AllocCoTaskMem(Marshal.SizeOf(data));
                Marshal.StructureToPtr(data, memory, false);
                foreach (var handle in GetTopLevelWindowHandles(processId))
                {
                    NativeMethods.GetWindowThreadProcessId(handle, out var ownerProcessId);
                    if (ownerProcessId != (uint)processId) continue;
                    var delivered = SendMessageTimeout(handle, NativeMethods.WM_COPYDATA, IntPtr.Zero,
                        memory, 0x0002, 1500, out var result) != IntPtr.Zero;
                    if (delivered && result == new IntPtr(1)) return true;
                }
                return false;
            }
            finally
            {
                if (memory != IntPtr.Zero) Marshal.FreeCoTaskMem(memory);
                Marshal.FreeHGlobal(data.lpData);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
            IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        private void PreserveOriginalProfileFilesForVersion()
        {
            // Make a byte-for-byte safety snapshot before JSON deserialization, migrations,
            // device loading or the first new-version save can modify the data.
            var version = "v" + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "UCR");
            var backupRoot = Path.Combine(root, "VersionBackups");
            var marker = Path.Combine(backupRoot, "last-version.txt");
            if (File.Exists(marker) && string.Equals(File.ReadAllText(marker).Trim(), version, StringComparison.Ordinal))
                return;

            var snapshots = Path.Combine(backupRoot, version, "OriginalJson");
            Directory.CreateDirectory(snapshots);
            var profiles = Path.Combine(root, "Profiles");
            if (Directory.Exists(profiles))
            {
                foreach (var source in Directory.GetFiles(profiles, "*.json"))
                {
                    var destination = Path.Combine(snapshots, Path.GetFileName(source));
                    if (!File.Exists(destination)) File.Copy(source, destination);
                }
            }
            foreach (var name in new[] { "state.json", "devices.json" })
            {
                var source = Path.Combine(root, name);
                var destination = Path.Combine(snapshots, name);
                if (File.Exists(source) && !File.Exists(destination))
                    File.Copy(source, destination);
            }
            Logger.Info("Preserved original pre-load profile JSON under " + snapshots);
        }

        private void CreatePortableVersionBackup()
        {
            if (context == null) return;
            var version = "v" + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
            var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "UCR", "VersionBackups");
            var marker = Path.Combine(backupRoot, "last-version.txt");
            if (File.Exists(marker) && string.Equals(File.ReadAllText(marker).Trim(), version, StringComparison.Ordinal))
                return;
            var directory = Path.Combine(backupRoot, version);
            Directory.CreateDirectory(directory);
            var archive = Path.Combine(directory, "AllProfiles.ucrprofiles");
            if (!File.Exists(archive))
            {
                var pending = archive + ".pending";
                context.ProfilesManager.ExportProfileList(pending);
                File.Move(pending, archive);
            }
            foreach (var profile in context.Profiles)
            {
                var safeName = new string((profile.Title ?? "Profile")
                    .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
                if (safeName.Length > 55) safeName = safeName.Substring(0, 55);
                var file = Path.Combine(directory, safeName + " - " + profile.Guid.ToString("D") + ".ucrprofile");
                if (File.Exists(file)) continue;
                var pendingProfile = file + ".pending";
                context.ProfilesManager.ExportProfile(profile, pendingProfile);
                File.Move(pendingProfile, file);
            }
            var tempMarker = marker + ".tmp";
            File.WriteAllText(tempMarker, version);
            if (File.Exists(marker)) File.Replace(tempMarker, marker, null);
            else File.Move(tempMarker, marker);
            Logger.Info("Importable version backup created: " + directory);
        }

        private void InitializeUcr()
        {
            RunStartupStage("Initializing device providers and loading profiles...", () =>
            {
                // Backups are important, but locked Documents folders, invalid profile titles or
                // interrupted earlier backups must not prevent the editor from opening.
                try { PreserveOriginalProfileFilesForVersion(); }
                catch (Exception exception) { Logger.Error("Pre-load backup failed; original JSON files were not modified by the backup.", exception); }
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
            if (currentContext.ActiveProfiles.Count == 0)
            {
                builder.Append("Active profiles: none");
                return builder.ToString();
            }

            builder.AppendLine("Active profiles: " + string.Join(" + ", currentContext.ActiveProfiles.Select(profile => profile.Title)));
            foreach (var active in currentContext.ActiveProfiles)
            {
                builder.AppendLine("- " + active.Title + ": mappings=" + active.GetRuntimeMappings().Count() +
                                   "; input configs=" + (active.InputDeviceConfigurations?.Count ?? 0) +
                                   "; output configs=" + (active.OutputDeviceConfigurations?.Count ?? 0));
            }
            return builder.ToString().TrimEnd();
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

        private bool HasRequiredRuntimeComponents()
        {
            var mappingPluginsLoaded = context.GetPlugins().Count != 0;
            var deviceProvidersLoaded = context.DevicesManager.HasLoadedProviderReports(requireBothDirections: true);
            if (mappingPluginsLoaded && deviceProvidersLoaded) return true;

            Logger.Warn($"Required runtime component check failed. Mapping plugins loaded: {mappingPluginsLoaded}; device provider reports loaded: {deviceProvidersLoaded}.");
            return false;
        }

        private bool TryInitializeRuntime()
        {
            try
            {
                InitializeUcr();
                return HasRequiredRuntimeComponents();
            }
            catch (Exception exception) when (exception is MissingPersistedPluginException ||
                exception is CompositionException || exception is ReflectionTypeLoadException ||
                exception is FileLoadException || exception is BadImageFormatException)
            {
                Logger.Warn("Unable to load required UCR runtime components.", exception);
                return false;
            }
        }

        private async Task<bool> EnsureRequiredRuntimeComponentsAsync()
        {
            if (TryInitializeRuntime()) return true;

            var result = DarkMessageBox.Show(
                "UCR could not load the required mapping plugins or device providers. Unblock the UCR files and retry?",
                "Unblock files?", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return false;

            UpdateSplash("Unblocking UCR files...");
            var directory = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                var exitCode = await Task.Run(() => RunUnblocker(directory, 5 * 60 * 1000));
                if (!exitCode.HasValue)
                    return ReportUnblockFailure("The file unblocker timed out.");
                if (exitCode.Value != 0)
                    return ReportUnblockFailure("The file unblocker failed (exit code " + exitCode.Value + ").");

                // The first attempt may have successfully loaded only some providers.
                // Dispose that context before composing a replacement.
                var previousContext = context;
                context = null;
                previousContext?.Dispose();
                if (TryInitializeRuntime()) return true;

                return ReportUnblockFailure("The required mapping plugins or device providers are still unavailable after unblocking.");
            }
            catch (Exception exception)
            {
                Logger.Error("Unable to complete UCR file unblocking and runtime recovery.", exception);
                return ReportUnblockFailure("UCR could not reload its required components. See the UCR log for details.");
            }
        }

        private static int? RunUnblocker(string directory, int timeoutMilliseconds)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(directory, "UCR_unblocker.exe"),
                WorkingDirectory = directory,
                // Appending "." avoids a trailing backslash escaping the closing quote.
                Arguments = $"\"{Path.Combine(directory, ".")}\" --non-interactive",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var process = Process.Start(startInfo))
            {
                if (process == null) throw new InvalidOperationException("Could not start the UCR file unblocker.");
                return process.WaitForExit(timeoutMilliseconds) ? (int?)process.ExitCode : null;
            }
        }

        private static bool ReportUnblockFailure(string message)
        {
            Logger.Error(message);
            DarkMessageBox.Show(message, "UCR could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
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

        public async void ShutdownWithProgress(Window mainWindow, bool saveContext)
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
                // All configuration saving is performed by the user's Save command or
                // by a confirmed Yes response in MainWindow's close dialog. Never
                // perform a second implicit write during the shutdown sequence.
                UpdateShutdownProgress(15);
                UpdateShutdownProgress(35);
                UpdateShutdownSplash("Stopping input and output services...");
                try
                {
                    if (context != null) await context.DisposeAsync();
                }
                catch (Exception exception)
                {
                    Logger.Error("Stopping input and output services during shutdown failed.", exception);
                }
                finally
                {
                    context = null;
                }
                UpdateShutdownProgress(70);
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
            try
            {
                context?.Dispose();
            }
            finally
            {
                context = null;
                try { _hidGuardianClient?.Dispose(); }
                finally
                {
                    _hidGuardianClient = null;
                    mutex?.Dispose();
                    mutex = null;
                }
            }
        }

        private void App_OnExit(object sender, ExitEventArgs e)
        {
            if (!_shutdownCleanupComplete)
                Dispose();

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
