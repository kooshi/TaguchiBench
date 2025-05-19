// TaguchiBench.Engine/Core/TargetRunner.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TaguchiBench.Common; // For Logger
using TaguchiBench.Engine.Interfaces;

namespace TaguchiBench.Engine.Core {
    /// <summary>
    /// Implements <see cref="ITargetRunner"/> to execute an external program
    /// and parse its JSON output for metrics.
    /// </summary>
    public class TargetRunner : ITargetRunner {
        private readonly string _targetExecutablePath;
        private const string ResultSentinel = "v^v^v^RESULT^v^v^v";

        /// <summary>
        /// Initializes a new instance of the <see cref="TargetRunner"/> class.
        /// </summary>
        /// <param name="targetExecutablePath">The full path to the target executable.</param>
        public TargetRunner(string targetExecutablePath) {
            if (string.IsNullOrWhiteSpace(targetExecutablePath)) {
                throw new ArgumentException("Target executable path cannot be null or whitespace.", nameof(targetExecutablePath));
            }
            // It's tempting to check File.Exists here, but the path might be resolvable via PATH or be a script.
            // The actual execution will reveal if the path is truly invalid.
            _targetExecutablePath = targetExecutablePath;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, double>> RunAsync(
            Dictionary<string, object> commandLineArguments,
            Dictionary<string, string> environmentVariables,
            bool showTargetOutput) {
            string argumentsString = BuildArgumentsString(commandLineArguments);

            Logger.Info("TARGET_RUNNER", "Executing target: {Executable}", _targetExecutablePath);
            Logger.Debug("TARGET_RUNNER", "With arguments: {Arguments}", argumentsString);
            if (environmentVariables != null && environmentVariables.Count > 0) {
                Logger.Debug("TARGET_RUNNER", "With environment variables: {Variables}", string.Join("; ", environmentVariables.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            }

            using Process process = new();
            process.StartInfo.FileName = _targetExecutablePath;
            process.StartInfo.Arguments = argumentsString;
            process.StartInfo.UseShellExecute = false; // Crucial for redirecting IO
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true; // No separate window
            process.StartInfo.WorkingDirectory = Environment.CurrentDirectory; // Use current directory

            if (environmentVariables != null) {
                foreach (KeyValuePair<string, string> envVar in environmentVariables) {
                    process.StartInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
                }
            }

            StringBuilder outputBuilder = new();
            StringBuilder errorBuilder = new();
            TaskCompletionSource<Dictionary<string, double>> tcs = new();

            process.OutputDataReceived += (sender, e) => {
                if (e.Data == null) { return; }
                if (showTargetOutput) { Logger.RawText("TARGET_STDOUT", e.Data); }
                outputBuilder.AppendLine(e.Data);

                // Check for sentinel string
                if (e.Data.Trim() == ResultSentinel) {
                    // The next lines should be the JSON. We need to capture them.
                    // This simple handler assumes JSON is immediately next. A more robust parser might be needed.
                }
            };

            process.ErrorDataReceived += (sender, e) => {
                if (e.Data == null) { return; }
                if (showTargetOutput) { Logger.RawText("TARGET_STDERR", e.Data); } // Log errors as warnings from target
                errorBuilder.AppendLine(e.Data);
            };

            try {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(); // Asynchronously wait for the process to exit

                if (process.ExitCode != 0) {
                    Logger.Error("TARGET_RUNNER", "Target executable exited with error code {ExitCode}.", process.ExitCode);
                    Logger.Debug("TARGET_RUNNER", "Full Target STDOUT:\n{Stdout}", outputBuilder.ToString());
                    Logger.Debug("TARGET_RUNNER", "Full Target STDERR:\n{Stderr}", errorBuilder.ToString());
                    tcs.TrySetResult(new Dictionary<string, double>()); // Return empty on error, or throw
                    return await tcs.Task;
                }

                string fullOutput = outputBuilder.ToString();
                int sentinelIndex = fullOutput.IndexOf(ResultSentinel, StringComparison.Ordinal);

                if (sentinelIndex == -1) {
                    Logger.Error("TARGET_RUNNER", "Result sentinel '{Sentinel}' not found in target output.", ResultSentinel);
                    Logger.Debug("TARGET_RUNNER", "Full Target STDOUT:\n{Stdout}", fullOutput);
                    tcs.TrySetResult(new Dictionary<string, double>()); // Return empty, or throw
                    return await tcs.Task;
                }

                // Extract JSON part: from after sentinel and newline to the end of the output.
                int jsonStartIndex = sentinelIndex + ResultSentinel.Length;
                // Skip immediate newline after sentinel if present
                if (jsonStartIndex < fullOutput.Length && (fullOutput[jsonStartIndex] == '\r' || fullOutput[jsonStartIndex] == '\n')) {
                    jsonStartIndex++;
                    if (jsonStartIndex < fullOutput.Length && fullOutput[jsonStartIndex] == '\n' && fullOutput[jsonStartIndex-1] == '\r') { // Handle \r\n
                        jsonStartIndex++;
                    }
                }
                
                string jsonOutput = fullOutput.Substring(jsonStartIndex).Trim();

                try {
                    var resultContainer = JsonSerializer.Deserialize<ResultContainer>(jsonOutput);
                    if (resultContainer?.Result == null) {
                         Logger.Error("TARGET_RUNNER", "Failed to deserialize or 'result' field missing in JSON output: {Json}", jsonOutput);
                         tcs.TrySetResult(new Dictionary<string, double>());
                    } else {
                        tcs.TrySetResult(resultContainer.Result);
                    }
                } catch (JsonException jsonEx) {
                    Logger.Exception("TARGET_RUNNER", jsonEx, "Error deserializing JSON from target: {Json}", jsonOutput);
                    tcs.TrySetResult(new Dictionary<string, double>());
                }

            } catch (Exception ex) {
                Logger.Exception("TARGET_RUNNER", ex, "An error occurred while running or processing the target executable.");
                tcs.TrySetResult(new Dictionary<string, double>()); // Ensure TCS completes
            }

            return await tcs.Task;
        }

        private string BuildArgumentsString(Dictionary<string, object> commandLineArguments) {
            StringBuilder argsBuilder = new();
            if (commandLineArguments == null) {
                return string.Empty;
            }

            foreach (KeyValuePair<string, object> arg in commandLineArguments) {
                if (argsBuilder.Length > 0) {
                    argsBuilder.Append(' ');
                }

                // The key IS the argument string. No prefixing is done here.
                argsBuilder.Append(arg.Key);

                if (arg.Value != null) { // If value is null, it's a flag (argument name only)
                    argsBuilder.Append(' ');
                    string valueStr = arg.Value.ToString();
                    // Quote if it contains spaces, to ensure it's treated as a single argument value
                    if (valueStr.Contains(' ') || valueStr.Contains('\t')) {
                        argsBuilder.Append($"\"{valueStr}\"");
                    } else {
                        argsBuilder.Append(valueStr);
                    }
                }
            }
            return argsBuilder.ToString();
        }

        // Helper class for deserializing the {"result": {...}} structure
        private class ResultContainer {
            [JsonPropertyName("result")]
            public Dictionary<string, double> Result { get; set; }
        }
    }
}