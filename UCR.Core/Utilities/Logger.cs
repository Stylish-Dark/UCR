using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace HidWizards.UCR.Core.Utilities
{
    public static class Logger
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly object BreadcrumbLock = new object();
        private static readonly Queue<string> Breadcrumbs = new Queue<string>();
        private const int MaximumBreadcrumbs = 250;
        private static int _crashReportWritten;
        private static string _logDirectory;
        private static Func<string> _diagnosticContextProvider;

        private enum LogLevel { Trace, Debug, Info, Warn, Error, Fatal }

        public static string SessionId { get; private set; }
        public static string LastCrashReportPath { get; private set; }

        public static string GetLogDirectory()
        {
            return string.IsNullOrWhiteSpace(_logDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")
                : _logDirectory;
        }

        public static void SetDiagnosticContextProvider(Func<string> provider)
        {
            _diagnosticContextProvider = provider;
        }

        public static void InitializeSession()
        {
            if (!string.IsNullOrWhiteSpace(SessionId)) return;

            SessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-P" + Process.GetCurrentProcess().Id;
            _logDirectory = ResolveWritableLogDirectory();
            NLog.GlobalDiagnosticsContext.Set("SessionId", SessionId);
            NLog.GlobalDiagnosticsContext.Set("LogDirectory", _logDirectory);

            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            Info("Diagnostic session started. session=" + SessionId +
                 "; version=" + version +
                 "; os=" + Environment.OSVersion +
                 "; clr=" + Environment.Version +
                 "; process64=" + Environment.Is64BitProcess +
                 "; os64=" + Environment.Is64BitOperatingSystem +
                 "; cwd=" + Environment.CurrentDirectory);
        }

        private static string ResolveWritableLogDirectory()
        {
            var preferred = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (CanWriteDirectory(preferred)) return preferred;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var fallback = Path.Combine(localAppData, "HidWizards", "UCR", "logs");
            if (CanWriteDirectory(fallback)) return fallback;

            // NLog is configured not to throw. Returning the preferred location keeps diagnostics
            // best-effort without turning a logging permission problem into an application crash.
            return preferred;
        }

        private static bool CanWriteDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                var probe = Path.Combine(path, ".ucr-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (File.Create(probe)) { }
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Trace(string message, Exception e = null)
        {
            Log(LogLevel.Trace, message, e);
        }

        public static void Debug(string message, Exception e = null)
        {
            Log(LogLevel.Debug, message, e);
        }

        public static void Info(string message, Exception e = null)
        {
            Log(LogLevel.Info, message, e);
        }

        public static void Warn(string message, Exception e = null)
        {
            Log(LogLevel.Warn, message, e);
        }

        public static void Error(string message, Exception e = null)
        {
            Log(LogLevel.Error, message, e);
        }

        public static void Fatal(string message, Exception e = null)
        {
            Log(LogLevel.Fatal, message, e);
        }

        public static string WriteCrashReport(string source, Exception exception)
        {
            try
            {
                if (Interlocked.Exchange(ref _crashReportWritten, 1) != 0) return LastCrashReportPath;
                InitializeSession();

                var directory = GetLogDirectory();
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory,
                    "CRASH-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-P" + Process.GetCurrentProcess().Id + ".txt");

                var process = Process.GetCurrentProcess();
                var assembly = Assembly.GetEntryAssembly();
                var builder = new StringBuilder();
                builder.AppendLine("UCR crash diagnostics");
                builder.AppendLine("=====================");
                builder.AppendLine("Session: " + SessionId);
                builder.AppendLine("Captured: " + DateTime.Now.ToString("O"));
                builder.AppendLine("Source: " + (source ?? "unknown"));
                builder.AppendLine("Version: " + (assembly?.GetName().Version?.ToString() ?? "unknown"));
                builder.AppendLine("Process: " + process.ProcessName + " (PID " + process.Id + ")");
                builder.AppendLine("Working set: " + process.WorkingSet64 + " bytes");
                builder.AppendLine("OS: " + Environment.OSVersion);
                builder.AppendLine("CLR: " + Environment.Version);
                builder.AppendLine("64-bit process: " + Environment.Is64BitProcess);
                builder.AppendLine("64-bit OS: " + Environment.Is64BitOperatingSystem);
                builder.AppendLine("Command line: " + Environment.CommandLine);
                builder.AppendLine("Working directory: " + Environment.CurrentDirectory);
                builder.AppendLine("Base directory: " + AppDomain.CurrentDomain.BaseDirectory);
                builder.AppendLine("Managed thread: " + Thread.CurrentThread.ManagedThreadId);
                builder.AppendLine("Session log: " + Path.Combine(directory, "UCR-" + SessionId + ".log"));
                builder.AppendLine("Persistent error log: " + Path.Combine(directory, "UCR-errors.log"));

                var contextProvider = _diagnosticContextProvider;
                if (contextProvider != null)
                {
                    builder.AppendLine();
                    builder.AppendLine("Application state");
                    builder.AppendLine("-----------------");
                    try
                    {
                        builder.AppendLine(contextProvider() ?? "No application state was returned.");
                    }
                    catch (Exception contextFailure)
                    {
                        builder.AppendLine("Unable to collect application state: " + contextFailure);
                    }
                }

                builder.AppendLine();
                builder.AppendLine("Exception");
                builder.AppendLine("---------");
                builder.AppendLine(exception?.ToString() ?? "No Exception object was supplied.");
                builder.AppendLine();
                builder.AppendLine("Recent diagnostic breadcrumbs");
                builder.AppendLine("-----------------------------");

                lock (BreadcrumbLock)
                {
                    foreach (var breadcrumb in Breadcrumbs) builder.AppendLine(breadcrumb);
                }

                File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
                LastCrashReportPath = path;
                logger.Fatal("Crash diagnostics written to " + path);
                Flush();
                return path;
            }
            catch (Exception diagnosticFailure)
            {
                try
                {
                    logger.Error(diagnosticFailure, "Failed to write independent crash diagnostics");
                    Flush();
                }
                catch
                {
                    // A diagnostics failure must never mask the original crash.
                }
                return null;
            }
        }

        public static void Flush()
        {
            NLog.LogManager.Flush();
        }

        private static void Log(LogLevel logLevel, string message, Exception e)
        {
            AddBreadcrumb(logLevel, message, e);
            switch (logLevel)
            {
                case LogLevel.Trace:
                    logger.Trace(e, message);
                    break;
                case LogLevel.Debug:
                    logger.Debug(e, message);
                    break;
                case LogLevel.Info:
                    logger.Info(e, message);
                    break;
                case LogLevel.Warn:
                    logger.Warn(e, message);
                    break;
                case LogLevel.Error:
                    logger.Error(e, message);
                    break;
                case LogLevel.Fatal:
                    logger.Fatal(e, message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
            }
        }

        private static void AddBreadcrumb(LogLevel level, string message, Exception exception)
        {
            var line = DateTime.Now.ToString("O") + " " + level.ToString().ToUpperInvariant() +
                       " [T" + Thread.CurrentThread.ManagedThreadId + "] " + (message ?? string.Empty);
            if (exception != null) line += " | " + exception.GetType().FullName + ": " + exception.Message;

            lock (BreadcrumbLock)
            {
                Breadcrumbs.Enqueue(line);
                while (Breadcrumbs.Count > MaximumBreadcrumbs) Breadcrumbs.Dequeue();
            }
        }
    }
}
