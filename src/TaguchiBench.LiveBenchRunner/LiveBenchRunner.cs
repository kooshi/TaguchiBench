// TaguchiBench.LiveBenchRunner/LiveBenchRunner.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaguchiBench.Common; // For Logger, TimingUtilities

namespace TaguchiBench.LiveBenchRunner {
    /// <summary>
    /// Orchestrates the execution of LiveBench benchmarks against a specified OpenAI-compatible API endpoint.
    /// This class is the core logic for the TaguchiBench.LiveBenchRunner executable.
    /// It does NOT manage the server; it assumes the server is running at the provided API base URL.
    /// </summary>
    public class LiveBenchRunner {
        private readonly string _liveBenchScriptsPath; // Path to the directory containing run_livebench.py etc.
        private readonly string _modelNameForLiveBench; // The model name LiveBench should use in its requests
        private readonly string _apiBaseUrl;          // Full URL of the target API, e.g., http://127.0.0.1:8080 or https://api.openai.com/v1
        private readonly string _apiKey;              // API key, if required by the target API (can be "dummy-key" for local servers)

        private readonly string _liveBenchBenchName; // e.g., "live_bench/coding"
        private readonly string _liveBenchReleaseOption;
        private readonly int _liveBenchParallelRequests;
        private readonly int _liveBenchMaxTokens;
        private readonly int _liveBenchNumberOfQuestions;
        private readonly string _liveBenchSystemPrompt;
        private readonly bool _verboseLogging;
        private readonly double? _liveBenchForceTemperature = null; // Default to 0.0, can be overridden by factor values

        public LiveBenchRunner(
            string liveBenchScriptsPath,
            string modelNameForLiveBench,
            string apiBaseUrl,
            string apiKey,
            string liveBenchBenchName,
            string liveBenchReleaseOption,
            int liveBenchParallelRequests,
            int liveBenchMaxTokens,
            int liveBenchNumberOfQuestions,
            string liveBenchSystemPrompt,
            bool verboseLogging,
            double? liveBenchForceTemperature = null
        ) {

            if (string.IsNullOrWhiteSpace(liveBenchScriptsPath) || !Directory.Exists(liveBenchScriptsPath)) {
                throw new DirectoryNotFoundException($"LiveBench scripts path not found or invalid: {liveBenchScriptsPath}");
            }
            if (string.IsNullOrWhiteSpace(modelNameForLiveBench)) {
                throw new ArgumentException("Model name for LiveBench cannot be empty.", nameof(modelNameForLiveBench));
            }
            if (string.IsNullOrWhiteSpace(apiBaseUrl)) {
                throw new ArgumentException("API base URL cannot be empty.", nameof(apiBaseUrl));
            }
            // apiKey can be null or empty if not needed.

            _liveBenchScriptsPath = liveBenchScriptsPath;
            _modelNameForLiveBench = modelNameForLiveBench;
            _apiBaseUrl = apiBaseUrl;
            _apiKey = apiKey ?? "dummy-key"; // Default to dummy if null, as LiveBench scripts often expect it.

            _liveBenchBenchName = liveBenchBenchName;
            _liveBenchReleaseOption = liveBenchReleaseOption;
            _liveBenchParallelRequests = liveBenchParallelRequests;
            _liveBenchMaxTokens = liveBenchMaxTokens;
            _liveBenchNumberOfQuestions = liveBenchNumberOfQuestions;
            _liveBenchSystemPrompt = liveBenchSystemPrompt;
            _liveBenchForceTemperature = liveBenchForceTemperature;
            _verboseLogging = verboseLogging;
        }

        public Task InitializeAsync() {
            Logger.Info("LB_RUNNER", "Initializing LiveBenchRunner for model: {ModelName}, targeting API: {ApiBase}", _modelNameForLiveBench, _apiBaseUrl);
            CleanupPreviousBenchmarkResults(); // Clean up before any runs
            return Task.CompletedTask;
        }

        private void CleanupPreviousBenchmarkResults() {
            try {
                string[] benchNameParts = _liveBenchBenchName.Split('/');
                if (benchNameParts.Length < 2) {
                    Logger.Warning("LB_RUNNER", "Invalid LiveBench benchmark name format '{BenchName}' for cleanup. Skipping some cleanup steps.", _liveBenchBenchName);
                    return;
                }

                string benchBase = benchNameParts[0];
                string benchCategory = benchNameParts[1];
                string dataPathForCategory = Path.Combine(_liveBenchScriptsPath, "data", benchBase, benchCategory);

                Logger.Info("LB_RUNNER", "Cleaning up previous LiveBench results for model '{ModelName}' in benchmark '{BenchName}'...", _modelNameForLiveBench, _liveBenchBenchName);

                if (Directory.Exists(dataPathForCategory)) {
                    var taskDirectories = Directory.GetDirectories(dataPathForCategory);
                    foreach (string taskDir in taskDirectories) {
                        DeleteFileIfExists(Path.Combine(taskDir, "model_answer", $"{_modelNameForLiveBench}.jsonl"));
                        DeleteFileIfExists(Path.Combine(taskDir, "model_judgment", $"{_modelNameForLiveBench}_judgment.jsonl"));
                        DeleteFileIfExists(Path.Combine(taskDir, "model_judgment", "ground_truth_judgment.jsonl"));
                    }
                } else {
                    Logger.Debug("LB_RUNNER", "Benchmark data path for '{BenchName}' not found at '{DataPath}'. Specific task file cleanup skipped.", _liveBenchBenchName, dataPathForCategory);
                }

                DeleteFileIfExists(Path.Combine(_liveBenchScriptsPath, "all_groups.csv"));
                DeleteFileIfExists(Path.Combine(_liveBenchScriptsPath, "all_tasks.csv"));
                DeleteFileIfExists(Path.Combine(_liveBenchScriptsPath, "df_raw.csv"));

                Logger.Info("LB_RUNNER", "Cleanup of previous results complete.");
            } catch (Exception ex) {
                Logger.Exception("LB_RUNNER", ex, "An error occurred during cleanup of previous benchmark results.");
            }
        }

        private void DeleteFileIfExists(string filePath) {
            if (File.Exists(filePath)) {
                try {
                    Logger.Debug("LB_RUNNER_CLEANUP", "Deleting file: {FilePath}", filePath);
                    File.Delete(filePath);
                } catch (IOException ex) {
                    Logger.Warning("LB_RUNNER_CLEANUP", "IOException while deleting file {FilePath}: {Message}", filePath, ex.Message);
                }
            }
        }

        /// <summary>
        /// Executes a full LiveBench evaluation cycle against the configured API endpoint.
        /// </summary>
        /// <param name="factorValuesForRun">
        /// A dictionary of all factor names and their string values for the current run.
        /// The LiveBenchRunner will select relevant ones (e.g., "temp", "top_p" if supported by run_livebench.py)
        /// to pass as arguments to LiveBench scripts. Other factors are assumed to be handled by the server
        /// if this runner is part of a Taguchi experiment where the server configuration is varied externally.
        /// </param>
        /// <returns>A dictionary of metric names to their values.</returns>
        public async Task<Dictionary<string, double>> EvaluateAsync() {
            // Server is assumed to be running externally and managed by the caller if necessary.
            Logger.Info("LB_RUNNER", "Starting LiveBench evaluation cycle for model '{ModelName}' against API '{ApiBaseUrl}'.", _modelNameForLiveBench, _apiBaseUrl);

            TimingUtilities.StartTimer("LiveBench_FullCycle");

            // 1. Run LiveBench Generation (run_livebench.py)
            // Pass only the sampler parameters that run_livebench.py itself can interpret (e.g., temperature).
            // Other parameters are assumed to be set at the server level if this is part of a larger experiment.
            var (generationSuccess, generationTime) = await RunLiveBenchGenerationAsync();
            if (!generationSuccess) {
                TimingUtilities.StopTimer("LiveBench_FullCycle"); // Stop timer even on failure
                return new Dictionary<string, double> { { "AverageScore", 0 }, { "Time", generationTime.TotalSeconds } };
            }

            // 2. Run LiveBench Judgment (gen_ground_truth_judgment.py)
            bool judgmentSuccess = await RunLiveBenchJudgmentAsync();
            if (!judgmentSuccess) {
                Logger.Warning("LB_RUNNER", "LiveBench judgment script reported an error. Results might be incomplete.");
            }

            // 3. Show LiveBench Results (show_livebench_result.py) and parse CSVs
            bool showResultsSuccess = await RunLiveBenchShowResultsAsync();
            if (!showResultsSuccess) {
                Logger.Warning("LB_RUNNER", "LiveBench show_results script reported an error. Result parsing might fail or be incomplete.");
            }

            Dictionary<string, double> metrics = await ParseLiveBenchCsvResultsAsync();

            metrics.TryAdd("AverageScore", 0); // Ensure primary metric is present
            metrics["Time"] = generationTime.TotalSeconds; // Use the generation script's execution time

            TimeSpan fullCycleTime = TimingUtilities.StopTimer("LiveBench_FullCycle");
            Logger.Info("LB_RUNNER", "LiveBench full cycle completed in {Duration}. Score: {Score:F4}, Time: {Time:F2}s",
                TimingUtilities.FormatElapsedTime(fullCycleTime), metrics["AverageScore"], metrics["Time"]);

            return metrics;
        }


        private async Task<(bool success, TimeSpan duration)> RunLiveBenchGenerationAsync() {
            var arguments = new StringBuilder();
            arguments.Append($"run_livebench.py ");
            arguments.Append($"--model \"{_modelNameForLiveBench}\" ");
            arguments.Append($"--max-tokens {_liveBenchMaxTokens} ");
            arguments.Append($"--bench-name \"{_liveBenchBenchName}\" ");
            arguments.Append($"--api-base \"{_apiBaseUrl}\" ");
            arguments.Append($"--api-key \"{_apiKey}\" "); // Pass the configured API key
            arguments.Append($"--skip-grading ");
            arguments.Append($"--livebench-release-option \"{_liveBenchReleaseOption}\" ");
            arguments.Append($"--parallel-requests {_liveBenchParallelRequests} ");
            if (_liveBenchForceTemperature.HasValue) {
                arguments.Append($"--force-temperature {_liveBenchForceTemperature} ");
            }


            if (_liveBenchNumberOfQuestions > 0) {
                arguments.Append($"--question-begin 1 --question-end {_liveBenchNumberOfQuestions + 1} --ignore-missing-answers ");
            }
            if (!string.IsNullOrEmpty(_liveBenchSystemPrompt)) {
                string escapedPrompt = _liveBenchSystemPrompt.Replace("\"", "\\\"");
                arguments.Append($"--system-prompt \"{escapedPrompt}\" ");
            }

            Logger.Info("LB_RUNNER_GEN", "Executing LiveBench generation: python {Arguments}", arguments.ToString());
            TimingUtilities.StartTimer("LiveBench_GenerationScript");
            var (output, exitCode) = await ExecutePythonScriptAsync(arguments.ToString(), _liveBenchScriptsPath);
            TimeSpan duration = TimingUtilities.StopTimer("LiveBench_GenerationScript");

            if (exitCode != 0) {
                Logger.Error("LB_RUNNER_GEN", "LiveBench generation script (run_livebench.py) failed with exit code {ExitCode}. Duration: {Duration}.\nOutput:\n{Output}",
                    exitCode, TimingUtilities.FormatElapsedTime(duration), output);
                return (false, duration);
            }
            Logger.Info("LB_RUNNER_GEN", "LiveBench generation script completed successfully in {Duration}.", TimingUtilities.FormatElapsedTime(duration));
            return (true, duration);
        }

        private async Task<bool> RunLiveBenchJudgmentAsync() {
            var arguments = new StringBuilder();
            arguments.Append($"gen_ground_truth_judgment.py ");
            arguments.Append($"--bench-name \"{_liveBenchBenchName}\" ");
            arguments.Append($"--model \"{_modelNameForLiveBench}\" ");
            arguments.Append($"--parallel 1 "); // Often safer for judgment script
            arguments.Append($"--livebench-release-option \"{_liveBenchReleaseOption}\" ");
            arguments.Append($"--remove-existing-file ");
            if (_liveBenchNumberOfQuestions > 0) {
                arguments.Append($"--question-begin 1 --question-end {_liveBenchNumberOfQuestions + 1} --ignore-missing-answers ");
            }

            Logger.Info("LB_RUNNER_JUDGE", "Executing LiveBench judgment: python {Arguments}", arguments.ToString());
            TimingUtilities.StartTimer("LiveBench_JudgmentScript");
            var (output, exitCode) = await ExecutePythonScriptAsync(arguments.ToString(), _liveBenchScriptsPath, showOutput: _verboseLogging);
            TimeSpan duration = TimingUtilities.StopTimer("LiveBench_JudgmentScript");

            if (exitCode != 0) {
                Logger.Warning("LB_RUNNER_JUDGE", "LiveBench judgment script (gen_ground_truth_judgment.py) failed with exit code {ExitCode}. Duration: {Duration}.\nOutput:\n{Output}",
                    exitCode, TimingUtilities.FormatElapsedTime(duration), output);
                return false;
            }
            Logger.Info("LB_RUNNER_JUDGE", "LiveBench judgment script completed successfully in {Duration}.", TimingUtilities.FormatElapsedTime(duration));
            return true;
        }

        private async Task<bool> RunLiveBenchShowResultsAsync() {
            var arguments = new StringBuilder();
            arguments.Append($"show_livebench_result.py ");
            arguments.Append($"--bench-name \"{_liveBenchBenchName}\" ");
            arguments.Append($"--model-list \"{_modelNameForLiveBench}\" ");
            arguments.Append($"--livebench-release-option \"{_liveBenchReleaseOption}\" ");

            if (_liveBenchNumberOfQuestions > 0) {
                arguments.Append($"--ignore-missing-judgments ");
            }

            Logger.Info("LB_RUNNER_RESULTS", "Executing LiveBench show results: python {Arguments}", arguments.ToString());
            TimingUtilities.StartTimer("LiveBench_ShowResultsScript");
            var (output, exitCode) = await ExecutePythonScriptAsync(arguments.ToString(), _liveBenchScriptsPath, showOutput: _verboseLogging);
            TimeSpan duration = TimingUtilities.StopTimer("LiveBench_ShowResultsScript");

            if (exitCode != 0) {
                Logger.Warning("LB_RUNNER_RESULTS", "LiveBench show_results script failed with exit code {ExitCode}. Duration: {Duration}.\nOutput:\n{Output}",
                    exitCode, TimingUtilities.FormatElapsedTime(duration), output);
                return false;
            }
            Logger.Info("LB_RUNNER_RESULTS", "LiveBench show_results script completed successfully in {Duration}.", TimingUtilities.FormatElapsedTime(duration));
            return true;
        }

        private async Task<(string output, int exitCode)> ExecutePythonScriptAsync(
            string arguments, string workingDirectory, Dictionary<string, string> environmentVariables = null, bool showOutput = true) {
            string pythonExecutable = FindPythonExecutable();
            if (string.IsNullOrEmpty(pythonExecutable)) {
                Logger.Error("LB_RUNNER_PY", "Python executable not found. Ensure Python 3 is installed and in PATH, or virtual environment is active.");
                throw new InvalidOperationException("Python executable not found.");
            }

            using Process process = new();
            process.StartInfo = new ProcessStartInfo {
                FileName = pythonExecutable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            // Set explicit environment variables passed in
            if (environmentVariables != null) {
                foreach (KeyValuePair<string, string> kvp in environmentVariables) {
                    process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }
            // Ensure OPENAI_API_KEY and OPENAI_API_BASE are set from instance fields,
            // as LiveBench scripts expect these for any API interaction.
            process.StartInfo.EnvironmentVariables["OPENAI_API_KEY"] = _apiKey;
            process.StartInfo.EnvironmentVariables["OPENAI_API_BASE"] = _apiBaseUrl;

            StringBuilder outputBuilder = new();
            StringBuilder errorBuilder = new();

            process.OutputDataReceived += (sender, e) => {
                if (e.Data != null) {
                    if (showOutput && _verboseLogging) {
                        Logger.Debug("LIVEBENCH_PY_STDOUT", e.Data);
                    }
                    outputBuilder.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (sender, e) => {
                if (e.Data != null) {
                    if (showOutput && _verboseLogging) {
                        Logger.Debug("LIVEBENCH_PY_STDERR", e.Data);
                    } else if (showOutput) {
                        Logger.Info("LIVEBENCH_PY_STDERR", e.Data);  // Show non-verbose stderr as Info
                    }
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            string combinedOutput = outputBuilder.ToString();
            if (errorBuilder.Length > 0) {
                combinedOutput += $"\n--- STDERR ---\n{errorBuilder}";
            }
            return (combinedOutput, process.ExitCode);
        }

        private string FindPythonExecutable() {
            string venvPythonDir = Environment.GetEnvironmentVariable("VIRTUAL_ENV");
            if (!string.IsNullOrEmpty(venvPythonDir)) {
                string exeName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "python.exe" : "python";
                string venvPythonExe = Path.Combine(venvPythonDir, "bin", exeName); // Unix-like venv structure
                if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
                    venvPythonExe = Path.Combine(venvPythonDir, "Scripts", exeName); // Windows venv structure
                }
                if (File.Exists(venvPythonExe)) {
                    Logger.Debug("LB_RUNNER_PY", "Found Python in VIRTUAL_ENV: {PythonPath}", venvPythonExe);
                    return venvPythonExe;
                }
            }

            string[] pythonCommands = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? new[] { "python.exe", "python3.exe" }
                : new[] { "python3", "python" };

            foreach (string cmd in pythonCommands) {
                string pathVar = Environment.GetEnvironmentVariable("PATH");
                if (pathVar != null) {
                    foreach (string pathDir in pathVar.Split(Path.PathSeparator)) {
                        try {
                            string fullPath = Path.Combine(pathDir, cmd);
                            if (File.Exists(fullPath)) {
                                Logger.Debug("LB_RUNNER_PY", "Found Python in PATH: {PythonPath}", fullPath);
                                return fullPath;
                            }
                        } catch (ArgumentException) { /* Invalid char in path segment */ }
                    }
                }
            }
            Logger.Debug("LB_RUNNER_PY", "Could not find Python in PATH, trying command '{PythonCmd}' directly.", pythonCommands.First());
            return pythonCommands.First();
        }

        private async Task<Dictionary<string, double>> ParseLiveBenchCsvResultsAsync() {
            var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            await ParseDfRawCsvAsync(Path.Combine(_liveBenchScriptsPath, "df_raw.csv"), metrics);

            return metrics;
        }

        private async Task ParseDfRawCsvAsync(string csvPath, Dictionary<string, double> metrics) {
            if (!File.Exists(csvPath)) {
                Logger.Debug("BENCHMARK_CSV", "df_raw.csv not found: {CsvPath}", csvPath);
                return;
            }
            Logger.Debug("BENCHMARK_CSV", "Parsing df_raw.csv from: {CsvPath}", csvPath);

            try {
                var lines = await File.ReadAllLinesAsync(csvPath);
                if (lines.Length < 2) {
                    Logger.Warning("BENCHMARK_CSV", "df_raw.csv {CsvPath} is empty or has no data rows.", csvPath);
                    return;
                }

                var headers = lines[0].Split(',').Select(h => h.Trim()).ToList();
                int modelColIdx = headers.IndexOf("model");
                int scoreColIdx = headers.IndexOf("score");
                int taskColIdx = headers.IndexOf("task");
                int categoryColIdx = headers.IndexOf("category");

                if (new[] { modelColIdx, scoreColIdx, taskColIdx, categoryColIdx }.Any(idx => idx == -1)) {
                    Logger.Error("BENCHMARK_CSV", "df_raw.csv {CsvPath} is missing one or more required columns (model, score, task, category). Headers: {Headers}", csvPath, string.Join(",", headers));
                    return;
                }

                var categoryScores = new Dictionary<string, Dictionary<string, List<double>>>();
                for (int i = 1; i < lines.Length; i++) {
                    var columns = lines[i].Split(',').Select(c => c.Trim()).ToList();
                    if (columns.Count <= Math.Max(modelColIdx, Math.Max(scoreColIdx, Math.Max(taskColIdx, categoryColIdx)))) continue;

                    string currentModel = columns[modelColIdx];
                    if (!currentModel.Equals(_modelNameForLiveBench, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string currentCategory = columns[categoryColIdx];
                    string currentTask = columns[taskColIdx];

                    if (!double.TryParse(columns[scoreColIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out double score))
                        continue;

                    if (!categoryScores.TryGetValue(currentCategory, out Dictionary<string, List<double>>? taskScores)) {
                        taskScores = [];
                        categoryScores[currentCategory] = taskScores;
                    }
                    if (!taskScores.TryGetValue(currentTask, out List<double>? scores)) {
                        scores = [];
                        taskScores[currentTask] = scores;
                    }

                    scores.Add(score);
                }

                foreach (var categoryEntry in categoryScores) {
                    if (categoryEntry.Value.Count == 0)
                        continue;
                    var categoryTaskScores = categoryEntry.Value.Values.SelectMany(v => v);
                    metrics[$"Raw_AverageScore_{categoryEntry.Key}"] = categoryTaskScores.Average();
                    metrics[$"Raw_QuestionCount_{categoryEntry.Key}"] = categoryTaskScores.Count();
                }

                foreach (var taskEntry in categoryScores.SelectMany(kvp => kvp.Value)) {
                    if (taskEntry.Value.Count == 0)
                        continue;
                    metrics[$"Raw_Task_AverageScore_{taskEntry.Key}"] = taskEntry.Value.Average();
                    metrics[$"Raw_Task_QuestionCount_{taskEntry.Key}"] = taskEntry.Value.Count;
                }

                double totalAverage = categoryScores.Values
                    .SelectMany(v => v.Values)
                    .SelectMany(v => v)
                    .Average();

                metrics["AverageScore"] = totalAverage;

            } catch (Exception ex) {
                Logger.Exception("BENCHMARK_CSV", ex, "Error reading df_raw.csv from file: {CsvPath}", csvPath);
            }
        }
    }
}