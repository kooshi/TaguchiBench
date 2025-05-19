// TaguchiBench.LiveBenchRunner/ServerManager.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq; // For LINQ methods like Contains
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TaguchiBench.Common; // For Logger, TimingUtilities

namespace TaguchiBench.LiveBenchRunner {
    /// <summary>
    /// Manages an external server process, specifically tailored for `llama-server`
    /// in the context of the LiveBench runner.
    /// </summary>
    public class ServerManager : IDisposable {
        private readonly string _serverExecutablePath;
        private readonly string _modelPath;
        private readonly string _host;
        private readonly int _port;
        private readonly IReadOnlyDictionary<string, string> _environmentVariables;
        private readonly int _maxTokensForContextCalculation; // Max tokens per request for -c calculation
        private readonly int _parallelRequests; // For --parallel and -c calculation
        private readonly bool _enableServerLogs;
        private readonly int _serverLogVerbosity; // 0-3, used if _enableServerLogs is true

        private Process _serverProcess;
        private bool _isServerRunning;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public string ServerUrl => $"http://{_host}:{_port}";
        public bool IsRunning => _isServerRunning && _serverProcess != null && !_serverProcess.HasExited;

        public ServerManager(
            string serverExecutablePath,
            string modelPath,
            string host,
            int port,
            IReadOnlyDictionary<string, string> environmentVariables,
            int maxTokensForContextCalculation,
            int parallelRequests,
            bool enableServerLogs,
            int serverLogVerbosity = 0 // Default verbosity if logs are enabled
            ) {
            if (string.IsNullOrWhiteSpace(serverExecutablePath)) {
                throw new ArgumentException("Server executable path cannot be null or whitespace.", nameof(serverExecutablePath));
            }
            if (!File.Exists(serverExecutablePath)) {
                Logger.Error("SERVER_MGR", "Server executable not found at {Path}", serverExecutablePath);
                throw new FileNotFoundException("Server executable not found.", serverExecutablePath);
            }
            if (string.IsNullOrWhiteSpace(modelPath)) {
                throw new ArgumentException("Model path cannot be null or whitespace.", nameof(modelPath));
            }
            if (!File.Exists(modelPath)) {
                Logger.Error("SERVER_MGR", "Model file not found at {Path}", modelPath);
                throw new FileNotFoundException("Model file not found.", modelPath);
            }
            if (string.IsNullOrWhiteSpace(host)) {
                throw new ArgumentException("Host cannot be null or whitespace.", nameof(host));
            }
            if (port <= 0 || port > 65535) {
                throw new ArgumentOutOfRangeException(nameof(port), "Port number is out of valid range.");
            }
            if (maxTokensForContextCalculation <= 0) {
                throw new ArgumentOutOfRangeException(nameof(maxTokensForContextCalculation), "Max tokens for context calculation must be positive.");
            }
            if (parallelRequests <= 0) {
                throw new ArgumentOutOfRangeException(nameof(parallelRequests), "Parallel requests must be positive.");
            }

            _serverExecutablePath = serverExecutablePath;
            _modelPath = modelPath;
            _host = host;
            _port = port;
            _environmentVariables = environmentVariables ?? new Dictionary<string, string>();
            _maxTokensForContextCalculation = maxTokensForContextCalculation;
            _parallelRequests = parallelRequests;
            _enableServerLogs = enableServerLogs;
            _serverLogVerbosity = serverLogVerbosity;

            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; // Generous timeout for server interactions

            Logger.Debug("SERVER_MGR", "ServerManager initialized: Executable='{Exe}', Model='{Model}', Host='{Host}:{Port}'",
                Path.GetFileName(_serverExecutablePath), Path.GetFileName(_modelPath), _host, _port);
        }

        /// <summary>
        /// Starts the server process with the specified sampler parameters.
        /// </summary>
        /// <param name="samplerParameters">
        /// A dictionary of sampler parameters (e.g., "temp", "top_k") and their values,
        /// specific to the current experimental run.
        /// </param>
        /// <param name="startupTimeoutMilliseconds">Timeout in milliseconds for the server to become responsive.</param>
        /// <returns>True if the server started successfully and is responsive, false otherwise.</returns>
        public async Task<bool> StartServerAsync(
            IReadOnlyDictionary<string, string> additionalParameters,
            int startupTimeoutMilliseconds = 60000) {
            if (IsRunning) {
                Logger.Info("SERVER_MGR", "Attempting to stop existing server instance before starting anew.");
                await StopServerAsync();
            }

            string arguments = BuildServerArguments(additionalParameters);
            Logger.Info("SERVER_MGR", "Attempting to start server: {Executable}", Path.GetFileName(_serverExecutablePath));
            Logger.Debug("SERVER_MGR", "Server arguments: {Arguments}", arguments);

            _serverProcess = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = _serverExecutablePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true // Prefer non-interactive
                },
                EnableRaisingEvents = true // For Exited event, though WaitForExitAsync is primary
            };

            if (_environmentVariables != null) {
                foreach (KeyValuePair<string, string> envVar in _environmentVariables) {
                    _serverProcess.StartInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
                }
            }

            TimingUtilities.StartTimer("server_startup");

            _serverProcess.OutputDataReceived += (sender, e) => {
                if (e.Data != null) { Logger.Debug("LLAMA_SERVER_STDOUT", e.Data); }
            };
            _serverProcess.ErrorDataReceived += (sender, e) => {
                if (e.Data != null) { Logger.Warning("LLAMA_SERVER_STDERR", e.Data); }
            };

            try {
                _serverProcess.Start();
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();
                _isServerRunning = true; // Assume running once Start() succeeds, verify with health check

                bool isServerAvailable = await WaitForServerAvailabilityAsync(startupTimeoutMilliseconds);
                TimeSpan elapsedStartupTime = TimingUtilities.StopTimer("server_startup");

                if (!isServerAvailable) {
                    Logger.Error("SERVER_MGR", "Server failed to become responsive within {Timeout}ms.", startupTimeoutMilliseconds);
                    await StopServerAsync(); // Ensure cleanup
                    return false;
                }

                Logger.Info("SERVER_MGR", "Server started successfully at {Url} in {Duration}.",
                    ServerUrl, TimingUtilities.FormatElapsedTime(elapsedStartupTime));
                return true;
            } catch (Exception ex) {
                Logger.Exception("SERVER_MGR", ex, "An exception occurred while starting the server process.");
                await EnsureProcessStoppedAsync();
                return false;
            }
        }

        private string BuildServerArguments(IReadOnlyDictionary<string, string> additionalParameters) {
            StringBuilder argsBuilder = new();
            argsBuilder.Append($"-m \"{_modelPath}\" ");
            argsBuilder.Append($"--host {_host} ");
            argsBuilder.Append($"--port {_port} ");
            argsBuilder.Append($"--no-webui ");

            if (_enableServerLogs) {
                argsBuilder.Append($"--log-verbosity {_serverLogVerbosity} ");
            } else {
                argsBuilder.Append($"--log-disable ");
            }

            // Context size calculation: A heuristic giving room for prompts and parallel generation.
            // LiveBench itself might have a max_tokens per request.
            // This needs to be coordinated with how LiveBench uses the server.
            // Assuming _maxTokensForContextCalculation is the per-request generation limit.
            int estimatedPromptTokens = 2048; // A generous estimate for typical prompts
            int contextSize = (_maxTokensForContextCalculation + estimatedPromptTokens) * Math.Max(1, _parallelRequests);
            argsBuilder.Append($"--ctx-size {contextSize} ");
            // --predict is often tied to the model's max sequence length or a user limit.
            // For a server, it's often set high, and individual requests specify their n_predict.
            // Let's assume _maxTokensForContextCalculation is also a sensible upper bound for --predict.
            argsBuilder.Append($"--predict {_maxTokensForContextCalculation} ");


            if (_parallelRequests > 0) { // llama-server --parallel expects a value > 0
                argsBuilder.Append($"--parallel {_parallelRequests} ");
            }

            // Append fixed server parameters (e.g., n-gpu-layers, cache-type-k)
            // These are non-sampler parameters that define the server's operational mode.
            var serverSpecificArgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "m", "model", "host", "port", "seed", "log-disable", "log-verbosity",
                "ctx-size", "n_ctx", "predict", "n_predict", "parallel", "no-webui"
            };

            // Append sampler parameters for this specific run
            if (additionalParameters != null) {
                foreach (KeyValuePair<string, string> param in additionalParameters) {
                    // Sampler params usually don't have null values, but good to be safe.
                    if (param.Value != null) {
                        // Ensure sampler keys are passed as is, assuming they are correct CLI arg names
                        argsBuilder.Append($"{param.Key} {param.Value} ");
                    } else { // A sampler parameter that is just a flag
                        argsBuilder.Append($"{param.Key} ");
                    }
                }
            }
            return argsBuilder.ToString().Trim();
        }


        public async Task StopServerAsync() {
            if (!IsRunning) {
                Logger.Debug("SERVER_MGR", "StopServerAsync called, but server is not considered running.");
                return;
            }
            Logger.Info("SERVER_MGR", "Attempting to stop server at {Url}.", ServerUrl);
            TimingUtilities.StartTimer("server_shutdown");
            await EnsureProcessStoppedAsync();
            TimeSpan shutdownTime = TimingUtilities.StopTimer("server_shutdown");
            Logger.Info("SERVER_MGR", "Server shutdown process completed in {Duration}.", TimingUtilities.FormatElapsedTime(shutdownTime));
        }

        private async Task EnsureProcessStoppedAsync() {
            if (_serverProcess != null && !_isServerRunning) { // Process exists but already marked not running
                 if (!_serverProcess.HasExited) {
                    Logger.Debug("SERVER_MGR", "Server process object exists but marked not running; ensuring it has exited.");
                    try { _serverProcess.Kill(true); } catch { /* Ignore */ }
                 }
            } else if (_serverProcess != null) { // Process exists and possibly running
                try {
                    if (!_serverProcess.HasExited) {
                        Logger.Debug("SERVER_MGR", "Sending kill signal to server process ID: {ProcessId}", _serverProcess.Id);
                        _serverProcess.Kill(true); // true to kill entire process tree
                        await _serverProcess.WaitForExitAsync();
                        if (!_serverProcess.HasExited) {
                             Logger.Warning("SERVER_MGR", "Server process ID: {ProcessId} did not exit gracefully after kill signal. It might be stuck.", _serverProcess.Id);
                        }
                    }
                } catch (InvalidOperationException) {
                    Logger.Debug("SERVER_MGR", "Server process has already exited.");
                } catch (Exception ex) {
                    Logger.Exception("SERVER_MGR", ex, "Exception during server process termination.");
                } finally {
                    _serverProcess.Dispose();
                    _serverProcess = null;
                }
            }
            _isServerRunning = false;
        }

        private async Task<bool> WaitForServerAvailabilityAsync(int timeoutMilliseconds) {
            Stopwatch sw = Stopwatch.StartNew();
            const int delayMilliseconds = 500; // Check interval

            Logger.Debug("SERVER_MGR", "Awaiting server availability at {Url} (timeout: {Timeout}ms)...", ServerUrl, timeoutMilliseconds);

            while (sw.ElapsedMilliseconds < timeoutMilliseconds) {
                if (_serverProcess == null || _serverProcess.HasExited) {
                    Logger.Error("SERVER_MGR", "Server process terminated prematurely during startup sequence.");
                    return false;
                }
                try {
                    HttpResponseMessage response = await _httpClient.GetAsync($"{ServerUrl}/health");
                    if (response.IsSuccessStatusCode) {
                        var healthStatus = await response.Content.ReadAsStringAsync();
                        // llama-server /health returns JSON like {"status":"ok"} or {"status":"loading"}
                        if (healthStatus.Contains("\"status\":\"ok\"")) {
                            Logger.Info("SERVER_MGR", "Server health check passed (status: ok) after {Elapsed}ms.", sw.ElapsedMilliseconds);
                             return true;
                        }
                        Logger.Debug("SERVER_MGR", "Server health check status: {Status}, continuing to wait.", healthStatus);
                    }
                } catch (HttpRequestException ex) {
                    // Common during startup if server isn't listening yet
                    Logger.Debug("SERVER_MGR", "Health check connection attempt failed (normal during startup): {Message}", ex.Message.Split('\n')[0]);
                } catch (TaskCanceledException) {
                     Logger.Warning("SERVER_MGR", "Health check HTTP request timed out.");
                     // This might indicate a problem, but we'll let the main timeout handle it.
                }

                await Task.Delay(delayMilliseconds);
            }

            sw.Stop();
            Logger.Warning("SERVER_MGR", "Server did not become available at {Url} within the {Timeout}ms timeout.", ServerUrl, timeoutMilliseconds);
            return false;
        }

        // GetServerConfigAsync might not be strictly needed by the LiveBenchRunner itself,
        // but could be useful for debugging or if the runner needs to adapt to server capabilities.
        public async Task<Dictionary<string, object>> GetServerConfigAsync() {
            if (!IsRunning) {
                Logger.Warning("SERVER_MGR", "Cannot get server config, server is not running.");
                return new Dictionary<string, object>();
            }
            try {
                Logger.Debug("SERVER_MGR", "Retrieving server configuration from {Url}/props", ServerUrl); // llama-server uses /props
                HttpResponseMessage response = await _httpClient.GetAsync($"{ServerUrl}/props");

                if (response.IsSuccessStatusCode) {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    var config = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                    Logger.Debug("SERVER_MGR", "Server configuration/properties retrieved successfully.");
                    return config ?? new Dictionary<string, object>();
                } else {
                    Logger.Warning("SERVER_MGR", "Failed to get server configuration/properties, status: {StatusCode} - {Reason}", response.StatusCode, response.ReasonPhrase);
                }
            } catch (Exception ex) {
                Logger.Exception("SERVER_MGR", ex, "Error getting server configuration/properties.");
            }
            return new Dictionary<string, object>();
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (!_disposed) {
                if (disposing) {
                    Logger.Debug("SERVER_MGR", "Disposing ServerManager instance.");
                    // Ensure server is stopped synchronously if possible, or fire-and-forget async
                    Task.Run(StopServerAsync).Wait(TimeSpan.FromSeconds(15)); // Give some time to stop
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}