// TaguchiBench.LiveBenchRunner/Program.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using TaguchiBench.Common; // For Logger, TimingUtilities

namespace TaguchiBench.LiveBenchRunner {
    public static class Program {
        private const string ResultSentinel = "v^v^v^RESULT^v^v^v";

        public static async Task<int> Main(string[] args) {
            // Initialize logger early. Verbosity might be a CLI argument.
            // For now, let's make verbosity a simple flag.
            bool verbose = args.Contains("--verbose-runner");
            string logDir = Path.Combine(Directory.GetCurrentDirectory(), "log");
            Logger.Initialize(logDir, verbose, allToError: true);
            Logger.Info("LB_PROG", "TaguchiBench.LiveBenchRunner starting...");

            try {
                RunnerArguments runnerArgs = ParseRunnerArguments(args);
                if (runnerArgs.ShowHelp) {
                    PrintUsage();
                    return 0;
                }
                LogRunnerArguments(runnerArgs);

                ServerManager serverManager = null;
                bool localServerManaged = !string.IsNullOrWhiteSpace(runnerArgs.LlamaServerExecutablePath) &&
                                          !string.IsNullOrWhiteSpace(runnerArgs.LlamaModelPath);

                if (localServerManaged) {
                    Logger.Info("LB_PROG", "Local llama-server management is enabled.");
                    serverManager = new ServerManager(
                        runnerArgs.LlamaServerExecutablePath!,
                        runnerArgs.LlamaModelPath!,
                        runnerArgs.LlamaServerHost!,
                        runnerArgs.LlamaServerPort,
                        runnerArgs.LlamaEnvironmentVariables,
                        runnerArgs.LiveBenchMaxTokens, // Used for server's context calculation
                        runnerArgs.LiveBenchParallelRequests, // Used for server's context calculation
                        runnerArgs.EnableLlamaServerLogs,
                        runnerArgs.LlamaServerLogVerbosity
                    );
                } else {
                    Logger.Info("LB_PROG", "Targeting external API endpoint: {ApiBaseUrl}", runnerArgs.ApiBaseUrl);
                }

                var liveBenchRunner = new LiveBenchRunner(
                    runnerArgs.LiveBenchScriptsPath!,
                    runnerArgs.ModelNameForLiveBench!,
                    localServerManaged ? serverManager!.ServerUrl : runnerArgs.ApiBaseUrl!, // Use managed server URL or provided one
                    runnerArgs.ApiKey!,
                    runnerArgs.LiveBenchBenchName!,
                    runnerArgs.LiveBenchReleaseOption!,
                    runnerArgs.LiveBenchParallelRequests,
                    runnerArgs.LiveBenchMaxTokens,
                    runnerArgs.LiveBenchNumberOfQuestions,
                    runnerArgs.LiveBenchSystemPrompt!,
                    verbose, // Pass runner's verbosity to LiveBenchRunner's internal logging
                    runnerArgs.LiveBenchForceTemperature
                );

                await liveBenchRunner.InitializeAsync();

                Dictionary<string, double> metrics;
                if (serverManager != null) { // Managing local server
                    bool serverStarted = await serverManager.StartServerAsync(runnerArgs.LlamaParameters);
                    if (!serverStarted) {
                        Logger.Error("LB_PROG", "Failed to start local llama-server. Aborting evaluation.");
                        return 1; // Indicate error
                    }
                    try {
                        metrics = await liveBenchRunner.EvaluateAsync();
                    } finally {
                        await serverManager.StopServerAsync();
                    }
                } else { // Targeting external server
                    metrics = await liveBenchRunner.EvaluateAsync();
                }

                OutputResults(metrics);
                Logger.Info("LB_PROG", "TaguchiBench.LiveBenchRunner finished successfully.");
                return 0;

            } catch (ArgumentException argEx) {
                Logger.Error("LB_PROG", "Configuration error: {ErrorMessage}", argEx.Message);
                Console.Error.WriteLine($"Error: {argEx.Message}");
                PrintUsage();
                return 2;
            } catch (FileNotFoundException fnfEx) {
                Logger.Error("LB_PROG", "File not found: {ErrorMessage}", fnfEx.Message);
                Console.Error.WriteLine($"Error: File not found - {fnfEx.FileName} - {fnfEx.Message}");
                return 3;
            } catch (Exception ex) {
                Logger.Exception("LB_PROG", ex, "An unexpected error occurred in LiveBenchRunner Program.");
                Console.Error.WriteLine($"An unexpected error occurred: {ex.Message}");
                return 1;
            }
        }

        private static void OutputResults(Dictionary<string, double> metrics) {
            Console.WriteLine(ResultSentinel);
            // Ensure consistent serialization options if TaguchiBench.Engine expects specific casing etc.
            // Default is camelCase for keys if no attribute is on the Dictionary.
            string jsonResult = JsonSerializer.Serialize(new { result = metrics },
                new JsonSerializerOptions { WriteIndented = false }); // No indent for STDOUT parsing
            Console.WriteLine(jsonResult);
        }

        private static void LogRunnerArguments(RunnerArguments args) {
            if (!Logger.IsInitialized || !args.VerboseRunner) { return; } // Only log if verbose enabled for runner

            Logger.Debug("LB_PROG_ARGS", "LiveBenchRunner Program Arguments:");
            Logger.Debug("LB_PROG_ARGS", "  LiveBench Scripts Path: {Path}", args.LiveBenchScriptsPath);
            Logger.Debug("LB_PROG_ARGS", "  Model Name (for LB): {Name}", args.ModelNameForLiveBench);
            Logger.Debug("LB_PROG_ARGS", "  API Base URL (target): {Url}", args.ApiBaseUrl);
            Logger.Debug("LB_PROG_ARGS", "  API Key: {Key}", string.IsNullOrEmpty(args.ApiKey) ? "Not set" : "****"); // Mask key
            Logger.Debug("LB_PROG_ARGS", "  LB Bench Name: {Name}", args.LiveBenchBenchName);
            Logger.Debug("LB_PROG_ARGS", "  LB Release: {Release}", args.LiveBenchReleaseOption);
            Logger.Debug("LB_PROG_ARGS", "  LB Parallel Requests: {Count}", args.LiveBenchParallelRequests);
            Logger.Debug("LB_PROG_ARGS", "  LB Max Tokens: {Count}", args.LiveBenchMaxTokens);
            Logger.Debug("LB_PROG_ARGS", "  LB Num Questions: {Count}", args.LiveBenchNumberOfQuestions == -1 ? "All" : args.LiveBenchNumberOfQuestions.ToString());
            Logger.Debug("LB_PROG_ARGS", "  LB System Prompt: {Prompt}", string.IsNullOrEmpty(args.LiveBenchSystemPrompt) ? "None" : "Provided");
            Logger.Debug("LB_PROG_ARGS", "  LB Force Temperature: {Temp}", args.LiveBenchForceTemperature?.ToString("F2", CultureInfo.InvariantCulture) ?? "Not set");

            Logger.Debug("LB_PROG_ARGS", "  Llama Server Executable: {Path}", string.IsNullOrEmpty(args.LlamaServerExecutablePath) ? "N/A (External Server)" : args.LlamaServerExecutablePath);
            if (!string.IsNullOrEmpty(args.LlamaServerExecutablePath)) {
                Logger.Debug("LB_PROG_ARGS", "  Llama Model Path: {Path}", args.LlamaModelPath);
                Logger.Debug("LB_PROG_ARGS", "  Llama Server Host: {Host}", args.LlamaServerHost);
                Logger.Debug("LB_PROG_ARGS", "  Llama Server Port: {Port}", args.LlamaServerPort);
                Logger.Debug("LB_PROG_ARGS", "  Llama Server Logs: {Enabled}, Verbosity: {Verbosity}", args.EnableLlamaServerLogs, args.LlamaServerLogVerbosity);
                if (args.LlamaParameters.Any()) {
                    Logger.Debug("LB_PROG_ARGS", "  Llama Params: {Params}", string.Join(", ", args.LlamaParameters.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                }
                if (args.LlamaEnvironmentVariables.Any()) {
                    Logger.Debug("LB_PROG_ARGS", "  Llama Env Vars: {Vars}", string.Join(", ", args.LlamaEnvironmentVariables.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                }
            }
        }


        // A simple argument parsing mechanism. For a production tool, System.CommandLine is recommended.
        private static RunnerArguments ParseRunnerArguments(string[] args) {
            var parsedArgs = new RunnerArguments();
            var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var llamaParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Default values (some might be overridden by CLI args)
            parsedArgs.LiveBenchScriptsPath = "./livebench"; // Common default
            parsedArgs.ApiBaseUrl = "http://127.0.0.1:8080"; // Default if no local server specified
            parsedArgs.LlamaServerHost = "127.0.0.1";
            parsedArgs.LlamaServerPort = 8080;
            parsedArgs.LiveBenchParallelRequests = 1;
            parsedArgs.LiveBenchMaxTokens = 2048;
            parsedArgs.LiveBenchNumberOfQuestions = -1; // All
            parsedArgs.LiveBenchBenchName = "live_bench/coding";
            parsedArgs.LiveBenchReleaseOption = "2024-11-25";

            try {
                for (int i = 0; i < args.Length; i++) {
                    string currentArg = args[i];
                    string? nextArgValue = (i + 1 < args.Length && !args[i + 1].StartsWith("-")) ? args[++i] : null;
                    Logger.Debug("LB_PROG_ARGS", "Parsing argument: {Arg} with value: {Value}", currentArg, nextArgValue ?? "N/A");
                    switch (currentArg.ToLowerInvariant()) {
                        // Runner specific verbosity
                        case "--verbose-runner": parsedArgs.VerboseRunner = true; break; // No value consumed
                        case "--help": parsedArgs.ShowHelp = true; return parsedArgs; // Early exit for help

                        // LiveBenchRunner constructor args
                        case "--livebench-scripts-path": parsedArgs.LiveBenchScriptsPath = nextArgValue ?? throw new ArgumentNullException(currentArg); break;
                        case "--model-name": parsedArgs.ModelNameForLiveBench = nextArgValue ?? throw new ArgumentNullException(currentArg); break;
                        case "--api-base-url": parsedArgs.ApiBaseUrl = nextArgValue ?? throw new ArgumentNullException(currentArg); break;
                        case "--api-key": parsedArgs.ApiKey = nextArgValue; break; // Can be null/empty
                        case "--lb-bench-name": parsedArgs.LiveBenchBenchName = nextArgValue ?? throw new ArgumentNullException(currentArg); break;
                        case "--lb-release": parsedArgs.LiveBenchReleaseOption = nextArgValue ?? throw new ArgumentNullException(currentArg); break;
                        case "--lb-parallel": parsedArgs.LiveBenchParallelRequests = int.Parse(nextArgValue ?? "1"); break;
                        case "--lb-max-tokens": parsedArgs.LiveBenchMaxTokens = int.Parse(nextArgValue ?? "2048"); break;
                        case "--lb-num-questions": parsedArgs.LiveBenchNumberOfQuestions = int.Parse(nextArgValue ?? "-1"); break;
                        case "--lb-system-prompt": parsedArgs.LiveBenchSystemPrompt = nextArgValue; break;

                        // Temperature can be specified through different argument names
                        case "-t":
                        case "--temp":
                        case "--temperature":
                            parsedArgs.LiveBenchForceTemperature = double.Parse(nextArgValue ?? throw new ArgumentNullException(currentArg), CultureInfo.InvariantCulture);
                            llamaParams[currentArg] = nextArgValue; // Also add to samplerParams
                            break;

                        // ServerManager constructor args (if local server is used)
                        case "--llama-server-exe": parsedArgs.LlamaServerExecutablePath = nextArgValue ?? throw new ArgumentNullException(currentArg); break;
                        case "--llama-model": parsedArgs.LlamaModelPath = nextArgValue ?? throw new ArgumentNullException(currentArg); break;
                        case "--llama-host": parsedArgs.LlamaServerHost = nextArgValue ?? "127.0.0.1"; break;
                        case "--llama-port": parsedArgs.LlamaServerPort = int.Parse(nextArgValue ?? "8080"); break;
                        case "--llama-logs": parsedArgs.EnableLlamaServerLogs = true; break; // Flag
                        case "--llama-log-verbosity": parsedArgs.LlamaServerLogVerbosity = int.Parse(nextArgValue ?? "0"); break;

                        default:
                            // Assume other --key value pairs are either env vars or parameters
                            if (currentArg.StartsWith("--env-")) { // Environment Var: --env-CUDA_VISIBLE_DEVICES 0
                                string key = currentArg.Substring(6);
                                envVars[key] = nextArgValue ?? throw new ArgumentNullException(currentArg);
                            } else if (currentArg.StartsWith("-")) { // Any other param
                                string key = currentArg;
                                object? value = ParseObjectValue(nextArgValue);
                                llamaParams[key] = nextArgValue; // Also add as string to sampler params
                            } else {
                                throw new ArgumentException($"Unknown or malformed argument: {currentArg}");
                            }
                            break;
                    }
                }
            } catch (Exception ex) {
                throw new ArgumentException($"Error parsing arguments: {ex.Message}. Use --help for usage.", ex);
            }

            parsedArgs.LlamaParameters = llamaParams;
            parsedArgs.LlamaEnvironmentVariables = envVars;

            // If local server path is given, override ApiBaseUrl to use local server's host/port
            if (!string.IsNullOrWhiteSpace(parsedArgs.LlamaServerExecutablePath)) {
                parsedArgs.ApiBaseUrl = $"http://{parsedArgs.LlamaServerHost}:{parsedArgs.LlamaServerPort}";
            }


            // Basic validation
            if (string.IsNullOrWhiteSpace(parsedArgs.ModelNameForLiveBench)) {
                throw new ArgumentException("--model-name is required.");
            }

            return parsedArgs;
        }

        private static object? ParseObjectValue(string? stringValue) {
            if (string.IsNullOrWhiteSpace(stringValue)) { return null; } // Null or empty
            if (stringValue == "null") { return null; } // For flags
            if (int.TryParse(stringValue, out int intVal)) { return intVal; }
            if (double.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double doubleVal)) { return doubleVal; }
            if (bool.TryParse(stringValue, out bool boolVal)) { return boolVal; }
            return stringValue; // Default to string
        }

        private static void PrintUsage() {
            Console.WriteLine("TaguchiBench.LiveBenchRunner Usage:");
            Console.WriteLine("  Executes a single LiveBench evaluation run.");
            Console.WriteLine("\nRequired Arguments:");
            Console.WriteLine("  --model-name <name>              Model name identifier for LiveBench.");
            Console.WriteLine("\nLiveBench Configuration:");
            Console.WriteLine("  --livebench-scripts-path <path>  Path to LiveBench scripts directory (default: ./livebench).");
            Console.WriteLine("  --api-base-url <url>             API base URL (e.g., http://localhost:8080, or external API).");
            Console.WriteLine("                                   (default: http://127.0.0.1:8080, overridden if local server params are set).");
            Console.WriteLine("  --api-key <key>                  API key for the target API (optional).");
            Console.WriteLine("  --lb-bench-name <name>           LiveBench benchmark name (default: 'live_bench/coding').");
            Console.WriteLine("  --lb-release <option>            LiveBench release option (default: '2024-11-25').");
            Console.WriteLine("  --lb-parallel <num>              Number of parallel requests for LiveBench (default: 1).");
            Console.WriteLine("  --lb-max-tokens <num>            Max tokens for LiveBench requests (default: 2048).");
            Console.WriteLine("  --lb-num-questions <num>         Number of questions for LiveBench (default: -1 for all).");
            Console.WriteLine("  --lb-system-prompt <prompt>      System prompt for LiveBench (optional).");
            Console.WriteLine("\nLocal Llama-Server Management (Optional - if these are set, a local server is managed):");
            Console.WriteLine("  --llama-server-exe <path>        Path to llama-server executable.");
            Console.WriteLine("  --llama-model <path>             Path to GGUF model file for llama-server.");
            Console.WriteLine("  --llama-host <host>              Host for local llama-server (default: 127.0.0.1).");
            Console.WriteLine("  --llama-port <port>              Port for local llama-server (default: 8080).");
            Console.WriteLine("  --llama-logs                     Enable llama-server console logs (flag).");
            Console.WriteLine("  --llama-log-verbosity <0-3>      Verbosity for llama-server logs (default: 0).");
            Console.WriteLine("\nParameter Passing to Target (llama-server or other):");
            Console.WriteLine("  --<param_name> (<value>)?           Passes '--<param_name> (<value>)?' to local llama-server as a parameter.");
            Console.WriteLine("                                   Use '--flagname null' for boolean flags.");
            Console.WriteLine("  -t, --temp, --temperature <val>  Sets temperature for sampling. Also passed to LiveBench.");
            Console.WriteLine("  --env-<VAR_NAME> <value>         Sets environment variable VAR_NAME=value for local llama-server.");
            Console.WriteLine("\nOther Options:");
            Console.WriteLine("  --verbose-runner                 Enable verbose logging for this runner utility itself (flag).");
            Console.WriteLine("  --help                           Show this help message.");
            Console.WriteLine("\nOutput:");
            Console.WriteLine($"  Prints metrics as JSON to STDOUT, preceded by: {ResultSentinel}");
        }
    }

    // Helper class to hold parsed arguments
    internal class RunnerArguments {
        public bool ShowHelp { get; set; }
        public bool VerboseRunner { get; set; }

        public string? LiveBenchScriptsPath { get; set; }
        public string? ModelNameForLiveBench { get; set; }
        public string? ApiBaseUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? LiveBenchBenchName { get; set; }
        public string? LiveBenchReleaseOption { get; set; }
        public int LiveBenchParallelRequests { get; set; }
        public int LiveBenchMaxTokens { get; set; }
        public int LiveBenchNumberOfQuestions { get; set; }
        public string? LiveBenchSystemPrompt { get; set; }
        public double? LiveBenchForceTemperature { get; set; }

        public string? LlamaServerExecutablePath { get; set; }
        public string? LlamaModelPath { get; set; }
        public string? LlamaServerHost { get; set; }
        public int LlamaServerPort { get; set; }
        public bool EnableLlamaServerLogs { get; set; }
        public int LlamaServerLogVerbosity { get; set; }

        public Dictionary<string, string> LlamaParameters { get; set; } = new();
        public Dictionary<string, string> LlamaEnvironmentVariables { get; set; } = new();
    }
}