// TaguchiBench.Common/Logger.cs

using System;
using System.IO;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
// ReSharper disable once RedundantUsingDirective (Serilog.Sinks.XUnit is used in InitializeForTesting)
using Serilog.Sinks.XUnit;

namespace TaguchiBench.Common {
    /// <summary>
    /// Centralized logging system for TaguchiBench applications.
    /// A simple, yet effective, facade over Serilog.
    /// </summary>
    public static class Logger {
        private static ILogger _logger;
        private static readonly object _lockObject = new();
        private static readonly LoggingLevelSwitch _levelSwitch = new(LogEventLevel.Information);

        /// <summary>
        /// Indicates whether the logger has been initialized.
        /// </summary>
        public static bool IsInitialized => _logger != null;

        /// <summary>
        /// Initializes the logging system with the specified configuration.
        /// </summary>
        /// <param name="logDirectory">Directory to store log files.</param>
        /// <param name="verbose">Whether to enable verbose (Debug) logging.</param>
        public static void Initialize(string logDirectory = "logs", bool verbose = false, bool allToError = false) {
            lock (_lockObject) {
                if (_logger != null) {
                    return; // Already initialized, a commendable state.
                }
                _levelSwitch.MinimumLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

                if (!Directory.Exists(logDirectory)) {
                    Directory.CreateDirectory(logDirectory);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string logFilePath = Path.Combine(logDirectory, $"taguchibench-{timestamp}.log");

                _logger = new LoggerConfiguration().Enrich.FromLogContext()
                    .MinimumLevel.ControlledBy(_levelSwitch)
                    .WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Module}] {Message:lj}{NewLine}{Exception}",
                        standardErrorFromLevel: allToError ? LogEventLevel.Verbose : LogEventLevel.Error,
                        theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code,
                        applyThemeToRedirectedOutput: true
                    )
                    .WriteTo.File(logFilePath,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Module}] {Message:lj}{NewLine}{Exception}",
                        rollingInterval: RollingInterval.Day)
                    .CreateLogger();

                Info("CORE", "Logging system initialized. Log file: {LogFilePath}", logFilePath);
                if (verbose) {
                    Info("CORE", "Verbose logging has been enabled. Discretion is advised.");
                }
            }
        }

        /// <summary>
        /// Initializes the logging system for unit testing, directing output to the xUnit test output helper.
        /// </summary>
        /// <param name="testOutputHelper">xUnit test output helper to capture logs.</param>
        /// <param name="verbose">Whether to enable verbose (Debug) logging.</param>
        public static void InitializeForTesting(Xunit.Abstractions.ITestOutputHelper testOutputHelper, bool verbose = false) {
            lock (_lockObject) {
                if (_logger != null) {
                    return; // One initialization is quite sufficient.
                }

                _levelSwitch.MinimumLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

                _logger = new LoggerConfiguration().Enrich.FromLogContext()
                    .MinimumLevel.ControlledBy(_levelSwitch)
                    .WriteTo.TestOutput(testOutputHelper, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Module}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                Info("CORE", "Logging system initialized for testing conduit.");
                if (verbose) {
                    Info("CORE", "Verbose logging enabled for test execution.");
                }
            }
        }

        /// <summary>
        /// Adjusts the logging verbosity at runtime.
        /// </summary>
        /// <param name="verbose">True to enable Debug level logging; false for Information level.</param>
        public static void SetVerbosity(bool verbose) {
            _levelSwitch.MinimumLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;
            Info("CORE", "Logging verbosity set to: {Level}", _levelSwitch.MinimumLevel);
        }

        /// <summary>
        /// Logs a message at the Debug level.
        /// </summary>
        /// <param name="module">The originating module of the log event.</param>
        /// <param name="messageTemplate">The message template, with Serilog-style placeholders.</param>
        /// <param name="propertyValues">Values for the placeholders in the message template.</param>
        public static void Debug(string module, string messageTemplate, params object[] propertyValues) {
            EnsureInitialized();
            using (LogContext.PushProperty("Module", module)) {
                _logger.Debug(messageTemplate, propertyValues);
            }
        }

        /// <summary>
        /// Logs a message at the Information level.
        /// </summary>
        /// <param name="module">The originating module of the log event.</param>
        /// <param name="messageTemplate">The message template, with Serilog-style placeholders.</param>
        /// <param name="propertyValues">Values for the placeholders in the message template.</param>
        public static void Info(string module, string messageTemplate, params object[] propertyValues) {
            EnsureInitialized();
            using (LogContext.PushProperty("Module", module)) {
                _logger.Information(messageTemplate, propertyValues);
            }
        }

        /// <summary>
        /// Logs a message at the Warning level.
        /// </summary>
        /// <param name="module">The originating module of the log event.</param>
        /// <param name="messageTemplate">The message template, with Serilog-style placeholders.</param>
        /// <param name="propertyValues">Values for the placeholders in the message template.</param>
        public static void Warning(string module, string messageTemplate, params object[] propertyValues) {
            EnsureInitialized();
            using (LogContext.PushProperty("Module", module)) {
                _logger.Warning(messageTemplate, propertyValues);
            }
        }

        /// <summary>
        /// Logs a message at the Error level.
        /// </summary>
        /// <param name="module">The originating module of the log event.</param>
        /// <param name="messageTemplate">The message template, with Serilog-style placeholders.</param>
        /// <param name="propertyValues">Values for the placeholders in the message template.</param>
        public static void Error(string module, string messageTemplate, params object[] propertyValues) {
            EnsureInitialized();
            using (LogContext.PushProperty("Module", module)) {
                _logger.Error(messageTemplate, propertyValues);
            }
        }

        /// <summary>
        /// Logs an exception with a message at the Error level.
        /// </summary>
        /// <param name="module">The originating module of the log event.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="messageTemplate">The message template, with Serilog-style placeholders.</param>
        /// <param name="propertyValues">Values for the placeholders in the message template.</param>
        public static void Exception(string module, System.Exception exception, string messageTemplate, params object[] propertyValues) {
            EnsureInitialized();
            using (LogContext.PushProperty("Module", module)) {
                _logger.Error(exception, messageTemplate, propertyValues);
            }
        }

        /// <summary>
        /// Logs raw text that may contain ANSI escape codes at the Information level.
        /// The text is passed through as-is without Serilog's message template processing.
        /// </summary>
        /// <param name="module">The originating module of the log event.</param>
        /// <param name="rawText">Raw text that may contain ANSI escape codes.</param>
        public static void RawText(string module, string rawText) {
            EnsureInitialized();
            using (LogContext.PushProperty("Module", module)) {
                // Use a format specifier that renders strings verbatim
                _logger.Information("{@RawText}", rawText);
            }
        }

        private static void EnsureInitialized() {
            if (_logger == null) {
                // A fallback, though ideally Initialize is called at application startup.
                // This is an anti-pattern, but guards against total failure if misused.
                lock (_lockObject) {
                    if (_logger == null) { // Double-check lock
                        Initialize("logs_uninitialized", true); // Default to verbose if uninitialized
                        Warning("CORE", "Logger was used before explicit initialization. Defaulting to console/file logger in 'logs_uninitialized'.");
                        // throw new InvalidOperationException("Logger is not initialized. Call Logger.Initialize() first.");
                        // Decided against throwing an exception to allow for more resilient, albeit improperly configured, execution.
                        // The warning should suffice to alert the developer.
                    }
                }
            }
        }
    }
}