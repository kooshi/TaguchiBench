// TaguchiBench.Engine/Program.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaguchiBench.Common; // For Logger, TimingUtilities
using TaguchiBench.Engine.Configuration;
using TaguchiBench.Engine.Core;
using TaguchiBench.Engine.Interfaces;
using TaguchiBench.Engine.Reporting;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TaguchiBench.Engine {
    public static class Program {
        // In TaguchiBench.Engine.Program.cs
        public static readonly ISerializer YamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
            .WithTypeConverter(new ExperimentStateYamlConverter())
            .WithTypeConverter(new OrthogonalArrayDesignYamlConverter())
            .WithTypeConverter(new SignalToNoiseTypeYamlConverter())
            .WithTypeConverter(new FactorYamlConverter())
            .WithTypeConverter(new ParameterLevelSetYamlConverter())
            .WithTypeConverter(new LevelYamlConverter()) // Handles Level when it's a direct value
            .WithTypeConverter(new OALevelYamlConverter())
            .WithTypeConverter(new ParameterMainEffectYamlConverter()) // New
            .WithTypeConverter(new ParameterInteractionEffectYamlConverter()) // New
            .WithTypeConverter(new OptimalConfigurationYamlConverter()) // New
            .WithTypeConverter(new ParameterSettingsYamlConverter()) // New
            .Build();

        public static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new ExperimentStateYamlConverter())
            .WithTypeConverter(new OrthogonalArrayDesignYamlConverter())
            .WithTypeConverter(new SignalToNoiseTypeYamlConverter())
            .WithTypeConverter(new FactorYamlConverter())
            .WithTypeConverter(new ParameterLevelSetYamlConverter())
            .WithTypeConverter(new LevelYamlConverter()) // Problematic for read if scalar
            .WithTypeConverter(new OALevelYamlConverter())
            .WithTypeConverter(new ParameterMainEffectYamlConverter()) // New
            .WithTypeConverter(new ParameterInteractionEffectYamlConverter()) // New
            .WithTypeConverter(new OptimalConfigurationYamlConverter()) // New
            .WithTypeConverter(new ParameterSettingsYamlConverter()) // New
            .Build();

        private static string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        public static async Task<int> Main(string[] args) {
            CommandLineOptions options = null;
            EngineConfiguration engineConfig = null; // Used for --config mode
            ExperimentState loadedState = null;    // Used for --recover or --report-* modes
            string effectiveOutputDirectory = null;
            bool verboseLogging = false; // Determined by options or config

            try {
                options = ParseCommandLineArguments(args);
                if (options == null) { // Parsing error, usage already printed
                    return ExitCodes.ArgumentError;
                }

                if (options.ShowHelp) {
                    PrintUsage();
                    return ExitCodes.Success;
                }

                // Determine initial verbosity for logging setup
                // If config/state is loaded later, verbosity might be updated.
                verboseLogging = options.GlobalVerboseOverride ?? false; // Initial verbosity from CLI if present

                // Determine operation mode and load necessary data
                string loadedStatePath = null;
                if (!string.IsNullOrEmpty(options.ConfigPath)) { // Normal run
                    engineConfig = EngineConfiguration.LoadFromFile(options.ConfigPath);
                    verboseLogging = options.GlobalVerboseOverride ?? engineConfig.Verbose;
                    effectiveOutputDirectory = options.OutputDirectoryOverride ?? engineConfig.OutputDirectory;
                } else if (!string.IsNullOrEmpty(options.RecoverStatePath)) { // Recovery run
                    loadedState = LoadExperimentState(options.RecoverStatePath);
                    loadedStatePath = options.RecoverStatePath;
                    engineConfig = loadedState.Configuration; // Use config from state for recovery context
                    verboseLogging = options.GlobalVerboseOverride ?? engineConfig.Verbose;
                    effectiveOutputDirectory = options.OutputDirectoryOverride ?? engineConfig.OutputDirectory;
                } else if (!string.IsNullOrEmpty(options.ReportHtmlFromStatePath) || !string.IsNullOrEmpty(options.ReportMdFromStatePath)) { // Report-only
                    string reportStatePath = options.ReportHtmlFromStatePath ?? options.ReportMdFromStatePath;
                    loadedState = LoadExperimentState(reportStatePath);
                    loadedStatePath = reportStatePath;
                    // For report-only, verbosity comes from CLI or defaults to false if state's config is minimal
                    verboseLogging = options.GlobalVerboseOverride ?? loadedState.Configuration?.Verbose ?? false;
                    effectiveOutputDirectory = options.OutputDirectoryOverride ?? loadedState.Configuration?.OutputDirectory ?? "./taguchi_results_report_only";
                } else {
                    // This case should be caught by argument validation in ParseCommandLineArguments
                    // or by defaulting to a config file if present.
                    HandleError("Operational Error", new InvalidOperationException("No valid operation mode determined."), ExitCodes.OperationError, options.ShowHelp);
                    return ExitCodes.OperationError;
                }

                InitializeLogging(effectiveOutputDirectory, verboseLogging);
                if (!string.IsNullOrEmpty(loadedStatePath)) {
                    Logger.Info("ENGINE_PROG", "Loaded experiment state from: {StatePath}", loadedStatePath);
                }
                Logger.Info("ENGINE_PROG", "TaguchiBench.Engine ({Version}) starting.", AppVersion);
                LogOperationMode(options, engineConfig, loadedState);


                if (!string.IsNullOrEmpty(options.ReportHtmlFromStatePath) || !string.IsNullOrEmpty(options.ReportMdFromStatePath)) {
                    // Report-only mode
                    await GenerateReportsFromStateAsync(loadedState, effectiveOutputDirectory, options);
                } else {
                    // Full experiment run (new or recovered)
                    ITargetRunner targetRunner = new TargetRunner(engineConfig.TargetExecutablePath);
                    var taguchiEngine = new TaguchiBenchmarkEngine(engineConfig, targetRunner);

                    if (loadedState != null) { // Recovery mode
                        loadedState = await taguchiEngine.RecoverExperimentAsync(loadedState);
                    } else { // New experiment mode
                        loadedState = await taguchiEngine.RunFullExperimentAsync();
                    }

                    // Generate reports after the run
                    await GenerateReportsFromStateAsync(loadedState, effectiveOutputDirectory, options, isPostRun: true);
                }

                Logger.Info("ENGINE_PROG", "TaguchiBench.Engine operation completed successfully!");
                return ExitCodes.Success;

            } catch (ConfigurationException ex) {
                HandleError("Configuration Error", ex, ExitCodes.ConfigError, options?.ShowHelp ?? false);
                return ExitCodes.ConfigError;
            } catch (DesignException ex) {
                HandleError("Experiment Design Error", ex, ExitCodes.DesignError, options?.ShowHelp ?? false);
                return ExitCodes.DesignError;
            } catch (FileNotFoundException ex) {
                HandleError("File Not Found Error", ex, ExitCodes.FileError, options?.ShowHelp ?? false, $"File: {ex.FileName}");
                return ExitCodes.FileError;
            } catch (InvalidOperationException ex) { // Catch broader operational errors
                HandleError("Operation Error", ex, ExitCodes.OperationError, options?.ShowHelp ?? false);
                return ExitCodes.OperationError;
            } catch (Exception ex) {
                HandleError("An unexpected critical error occurred", ex, ExitCodes.UnexpectedError, options?.ShowHelp ?? false);
                return ExitCodes.UnexpectedError;
            }
        }

        private static async Task GenerateReportsFromStateAsync(ExperimentState state, string outputDir, CommandLineOptions options, bool isPostRun = false) {
            if (state == null) {
                Logger.Error("REPORTER", "Cannot generate reports: Experiment state is null.");
                throw new InvalidOperationException("Experiment state is null, cannot generate reports.");
            }
            if (state.AnalysisResults == null || !state.AnalysisResults.Any()) {
                Logger.Warning("REPORTER", "No analysis results found in the experiment state. Reports may be empty or incomplete.");
                // Allow report generation to proceed, it might just report config.
            }

            string outputTimestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string modelNameSanitized = SanitizeFileName(state.Configuration?.TargetExecutablePath ?? "unknown_target");

            Directory.CreateDirectory(outputDir); // Ensure output directory exists

            // Generate HTML report if requested or if it's a post-run scenario and no specific report type was denied
            if (!string.IsNullOrEmpty(options.ReportHtmlFromStatePath) || (isPostRun && string.IsNullOrEmpty(options.ReportMdFromStatePath))) {
                Logger.Info("REPORTER", "Generating HTML report...");
                var htmlReporter = new HtmlReportGenerator(state.Configuration, state.AnalysisResults, state.RawMetricsPerRun, state.ArrayDesign);
                string htmlReportFileName = Path.Combine(outputDir, $"{modelNameSanitized}_{outputTimestamp}_TaguchiAnalysisReport.html");
                htmlReporter.SaveReportToFile(htmlReportFileName);
                state.HtmlReportPath = htmlReportFileName; // Store path in state
            }

            // Generate Markdown report if requested or if it's a post-run scenario and no specific report type was denied
            if (!string.IsNullOrEmpty(options.ReportMdFromStatePath) || (isPostRun && string.IsNullOrEmpty(options.ReportHtmlFromStatePath))) {
                Logger.Info("REPORTER", "Generating Markdown report...");
                var markdownReporter = new MarkdownReportGenerator(state.Configuration, state.AnalysisResults, state.RawMetricsPerRun, state.HtmlReportPath, AppVersion, state.ArrayDesign);
                string markdownReportFileName = Path.Combine(outputDir, $"{modelNameSanitized}_{outputTimestamp}_analysis_report.md");
                await markdownReporter.SaveReportToFileAsync(markdownReportFileName);
                state.MarkdownReportPath = markdownReportFileName; // Store path in state
            }

            // Save final summary YAML (which is essentially the state file with report paths)
            // This might overwrite an intermediate state file if names collide, so ensure unique naming or careful handling.
            // The PersistExperimentState in TaguchiBenchmarkEngine already saves the state.
            // Here, we might save a "final_summary" version if needed, or just rely on the last persisted state.
            // For simplicity, let's assume the engine's final PersistExperimentState is sufficient as the summary.
            // If report paths need to be in that persisted state, the engine should update its state object before its final save.
            // Alternative: Save a separate _summary.yaml here.
            string finalSummaryFileName = Path.Combine(outputDir, $"{modelNameSanitized}_{outputTimestamp}_experiment_summary.yaml");
            SaveExperimentState(state, finalSummaryFileName); // Save the state, which now includes report paths
            Logger.Info("REPORTER", "Final experiment summary (state with report paths) saved to: {File}", finalSummaryFileName);
        }


        private static void HandleError(string errorType, Exception ex, int exitCode, bool showHelpHint, string additionalContext = null) {
            if (Logger.IsInitialized) {
                string format = string.IsNullOrEmpty(additionalContext) ? "{ErrorType}: {ErrorMessage}" : "{ErrorType}: {ErrorMessage}. Context: {AdditionalContext}";
                Logger.Exception("ENGINE_PROG", ex, format, errorType, ex.Message, additionalContext);
            } else {
                // Fallback if logger isn't even up
                Console.Error.WriteLine($"--- UNHANDLED EXCEPTION (Logger not initialized) ---\n{ex}\n--- END ---");
            }
            if (showHelpHint) {
                Console.WriteLine("\nFor command-line help, run: TaguchiBench.Engine --help");
            }
            Environment.ExitCode = exitCode; // Set exit code for the process
        }

        private static ExperimentState LoadExperimentState(string stateFilePath) {
            if (string.IsNullOrEmpty(stateFilePath) || !File.Exists(stateFilePath)) {
                throw new FileNotFoundException($"Experiment state file not found: {stateFilePath}", stateFilePath);
            }
            string yamlState = File.ReadAllText(stateFilePath);
            ExperimentState state = YamlDeserializer.Deserialize<ExperimentState>(yamlState);
            if (state == null) {
                throw new InvalidOperationException($"Failed to deserialize experiment state from '{stateFilePath}'. File may be corrupt or not a valid state file.");
            }
            if (state.Configuration == null || state.ArrayDesign == null) {
                throw new InvalidOperationException($"Experiment state file '{stateFilePath}' is incomplete (missing Configuration or ArrayDesign).");
            }
            return state;
        }

        private static void SaveExperimentState(ExperimentState state, string filePath) {
            // This is a utility to save state, e.g., the final summary.
            // The main persistence during runs is handled by TaguchiBenchmarkEngine.
            try {
                string yamlState = YamlSerializer.Serialize(state);
                File.WriteAllText(filePath, yamlState);
            } catch (Exception ex) {
                Logger.Exception("ENGINE_PROG", ex, "Failed to save final experiment summary state to {FilePath}", filePath);
            }
        }


        private static void InitializeLogging(string outputDirectory, bool verbose) {
            string logDirectory = Path.Combine(outputDirectory, "logs_engine");
            Directory.CreateDirectory(logDirectory); // Ensure it exists
            Logger.Initialize(logDirectory, verbose);
        }

        private static void LogOperationMode(CommandLineOptions options, EngineConfiguration config, ExperimentState state) {
            if (!string.IsNullOrEmpty(options.ConfigPath)) {
                Logger.Info("ENGINE_PROG", "Mode: New experiment run from config: {ConfigPath}", options.ConfigPath);
                Logger.Info("ENGINE_PROG", "Target: {Target}, Output: {Output}", config.TargetExecutablePath, options.OutputDirectoryOverride ?? config.OutputDirectory);
            } else if (!string.IsNullOrEmpty(options.RecoverStatePath)) {
                Logger.Info("ENGINE_PROG", "Mode: Recover experiment from state: {StatePath}", options.RecoverStatePath);
                Logger.Info("ENGINE_PROG", "Target: {Target}, Output: {Output}", state.Configuration.TargetExecutablePath, options.OutputDirectoryOverride ?? state.Configuration.OutputDirectory);
            } else if (!string.IsNullOrEmpty(options.ReportHtmlFromStatePath)) {
                Logger.Info("ENGINE_PROG", "Mode: Generate HTML report from state: {StatePath}", options.ReportHtmlFromStatePath);
                Logger.Info("ENGINE_PROG", "Output: {Output}", options.OutputDirectoryOverride ?? state.Configuration.OutputDirectory);
            } else if (!string.IsNullOrEmpty(options.ReportMdFromStatePath)) {
                Logger.Info("ENGINE_PROG", "Mode: Generate Markdown report from state: {StatePath}", options.ReportMdFromStatePath);
                Logger.Info("ENGINE_PROG", "Output: {Output}", options.OutputDirectoryOverride ?? state.Configuration.OutputDirectory);
            }
        }


        private static CommandLineOptions ParseCommandLineArguments(string[] args) {
            var options = new CommandLineOptions();
            if (args == null || args.Length == 0) {
                // Default behavior: look for a default config file if no args provided
                string defaultConfigName = "config.yaml";
                string localConfigPath = Path.Combine(Directory.GetCurrentDirectory(), defaultConfigName);
                // string scriptDirConfigPath = Path.Combine(AppContext.BaseDirectory, defaultConfigName); // If executable is elsewhere

                if (File.Exists(localConfigPath)) {
                    options.ConfigPath = localConfigPath;
                    Console.WriteLine($"No arguments provided. Defaulting to --config {localConfigPath}");
                }
                // else if (File.Exists(scriptDirConfigPath)) { // Check executable's directory
                //    options.ConfigPath = scriptDirConfigPath;
                //    Console.WriteLine($"No arguments provided. Defaulting to --config {scriptDirConfigPath}");
                // } 
                else {
                    PrintUsage(); // No default found, print usage
                    return null; // Indicates to caller that usage was printed due to no args/default
                }
                return options;
            }

            try {
                for (int i = 0; i < args.Length; i++) {
                    string currentArg = args[i];
                    string NextArgValue() {
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) {
                            return args[++i];
                        }
                        throw new ArgumentException($"Missing value for option {currentArg}");
                    }

                    switch (currentArg.ToLowerInvariant()) {
                        case "-c": case "--config": options.ConfigPath = NextArgValue(); break;
                        case "-r": case "--recover": options.RecoverStatePath = NextArgValue(); break;
                        case "--report-html": options.ReportHtmlFromStatePath = NextArgValue(); break;
                        case "--report-md": options.ReportMdFromStatePath = NextArgValue(); break;
                        case "-o": case "--output-dir": options.OutputDirectoryOverride = NextArgValue(); break;
                        case "-v": case "--verbose": options.GlobalVerboseOverride = true; break; // Global verbosity flag
                        case "-h": case "--help": options.ShowHelp = true; return options; // Early exit for help
                        default: throw new ArgumentException($"Unknown option: {currentArg}");
                    }
                }
            } catch (ArgumentException ex) {
                Console.Error.WriteLine($"Argument Error: {ex.Message}");
                PrintUsage();
                return null; // Indicate parsing failure
            }

            // Validate mutually exclusive modes
            int modeCount = new[] { options.ConfigPath, options.RecoverStatePath, options.ReportHtmlFromStatePath, options.ReportMdFromStatePath }
                            .Count(path => !string.IsNullOrEmpty(path));
            if (modeCount > 1) {
                Console.Error.WriteLine("Error: --config, --recover, --report-html, --report-md are mutually exclusive.");
                PrintUsage();
                return null;
            }
            if (modeCount == 0 && !options.ShowHelp) { // Should be caught by initial args check, but defensive
                Console.Error.WriteLine("Error: No operation mode specified (--config, --recover, or report generation).");
                PrintUsage();
                return null;
            }

            return options;
        }

        private static void PrintUsage() {
            var sb = new StringBuilder();
            sb.AppendLine($"TaguchiBench.Engine - LLM Parameter Optimization Framework (Version {AppVersion})");
            sb.AppendLine("Usage: TaguchiBench.Engine [mode_option] [other_options]");
            sb.AppendLine("\nOperation Modes (Mutually Exclusive):");
            sb.AppendLine("  -c, --config <path>          Run a new experiment using the specified YAML configuration file.");
            sb.AppendLine("  -r, --recover <state_path>   Recover and continue an experiment from a .yaml state file.");
            sb.AppendLine("  --report-html <state_path>   Generate only an HTML report from an experiment state file.");
            sb.AppendLine("  --report-md <state_path>     Generate only a Markdown report from an experiment state file.");
            sb.AppendLine("\nOther Options:");
            sb.AppendLine("  -o, --output-dir <dir>       Override the output directory specified in the config/state file.");
            sb.AppendLine("  -v, --verbose                Enable verbose logging globally, overriding config/state settings.");
            sb.AppendLine("  -h, --help                   Show this help message and exit.");
            sb.AppendLine("\nIf no arguments are provided, the program will look for 'config.yaml' in the current directory.");
            Console.WriteLine(sb.ToString());
        }

        private static string SanitizeFileName(string name) {
            if (string.IsNullOrWhiteSpace(name)) { return "unnamed_target"; }
            string fileName = Path.GetFileNameWithoutExtension(name); // Get just the file name part
            var invalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());
            invalidChars.UnionWith(new[] { ':', '/', '\\', '?', '*', '"', '<', '>', '|', ' ' }); // Add common problematic chars

            var sb = new StringBuilder(fileName.Length);
            foreach (char c in fileName) {
                sb.Append(invalidChars.Contains(c) ? '_' : c);
            }
            string sanitized = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
            return string.IsNullOrEmpty(sanitized) ? "sanitized_target_name" : sanitized;
        }
    }

    internal static class ExitCodes {
        public const int Success = 0;
        public const int UnexpectedError = 1;
        public const int ArgumentError = 2; // Specific error for CLI parsing
        public const int ConfigError = 3;
        public const int DesignError = 4;
        public const int FileError = 5;
        public const int OperationError = 6; // General operational issues
    }

    internal class CommandLineOptions {
        public string ConfigPath { get; set; }
        public string RecoverStatePath { get; set; }
        public string ReportHtmlFromStatePath { get; set; }
        public string ReportMdFromStatePath { get; set; }
        public string OutputDirectoryOverride { get; set; }
        public bool? GlobalVerboseOverride { get; set; } // Nullable to distinguish not set vs set to false
        public bool ShowHelp { get; set; } = false;
    }
}