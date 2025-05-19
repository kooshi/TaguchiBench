# TaguchiBench LiveBench Runner

The TaguchiBench LiveBench Runner is a command-line utility designed to execute coding benchmarks using the [LiveBench framework](https://github.com/livebench/livebench) against an OpenAI-compatible API endpoint. It can optionally manage a local `llama-server` (from the [llama.cpp](https://github.com/ggml-org/llama.cpp) project) for these evaluations.

This runner acts as a "target executable" for the `TaguchiBench.Engine`, allowing the Engine to optimize parameters (like sampler settings for `llama-server` or even LiveBench-specific parameters) by repeatedly invoking this runner with different configurations.

## Features

-   **LiveBench Integration**: Runs specified LiveBench coding benchmarks.
-   **Flexible API Targeting**: Can connect to any OpenAI-compatible API (e.g., a local `llama-server`, commercial LLM APIs).
-   **Optional Local `llama-server` Management**: Can automatically start and stop a local `llama-server` instance for each evaluation, configured with specific model, GGUF parameters, and sampler settings.
-   **Parameter Passthrough**: Accepts various parameters via CLI to control LiveBench execution and `llama-server` settings.
-   **Standardized Output**: Produces results in a JSON format (preceded by a sentinel) on STDOUT, making it compatible with `TaguchiBench.Engine`.

## Prerequisites

-   [.NET SDK](https://dotnet.microsoft.com/download) (Version specified in `.csproj`).
-   **Python 3.6+** with `venv` recommended for LiveBench.
-   **LiveBench**: A local clone of the LiveBench repository. You may need to run its `install_livebench.sh` script (or equivalent for your setup) to install dependencies.
-   **(Optional) `llama-server`**: If managing a local server, a compiled `llama-server` executable from the `llama.cpp` project.
-   **(Optional) GGUF Model File**: A model file compatible with `llama-server` if running locally.

## Quick Start

1.  **Build the Runner**:
    ```bash
    dotnet build TaguchiBench.LiveBenchRunner -c Release
    ```
    The executable will be in `TaguchiBench.LiveBenchRunner/bin/Release/netX.X/`.

2.  **Ensure LiveBench is Set Up**:
    Make sure your LiveBench installation is functional and its Python dependencies are met (e.g., by activating its virtual environment if you use one).

3.  **Example: Running against an External API (e.g., a `llama-server` you started manually)**
    ```bash
    ./TaguchiBench.LiveBenchRunner \
        --model-name "my-test-model-external" \
        --api-base-url "http://localhost:8080" \
        --api-key "dummy-key" \
        --livebench-scripts-path "/path/to/your/LiveBench/livebench" \
        --lb-bench-name "live_bench/coding" \
        --lb-release "2024-11-25" \
        --lb-num-questions 5 \
        --verbose-runner
    ```

4.  **Example: Managing a Local `llama-server`**
    ```bash
    ./TaguchiBench.LiveBenchRunner \
        --model-name "my-test-model-local" \
        --livebench-scripts-path "/path/to/your/LiveBench/livebench" \
        --lb-bench-name "live_bench/coding" \
        --lb-release "2024-11-25" \
        --lb-num-questions 5 \
        --llama-server-exe "/path/to/llama.cpp/server" \
        --llama-model "/path/to/your/model.gguf" \
        --llama-port 8088 \
        --llama-logs \
        --n-gpu-layers -1 \
        --flash-attn \
        --temp 0.7 \
        --top-k 40 \
        --verbose-runner
    ```

## Command-Line Interface

```text
TaguchiBench.LiveBenchRunner Usage:
  Executes a single LiveBench evaluation run.

Required Arguments:
  --model-name <name>              Model name identifier for LiveBench.

LiveBench Configuration:
  --livebench-scripts-path <path>  Path to LiveBench scripts directory (default: ./livebench).
  --api-base-url <url>             API base URL (e.g., http://localhost:8080, or external API).
                                   (default: http://127.0.0.1:8080, overridden if local server params are set).
  --api-key <key>                  API key for the target API (optional).
  --lb-bench-name <name>           LiveBench benchmark name (default: 'live_bench/coding').
  --lb-release <option>            LiveBench release option (default: '2024-11-25').
  --lb-parallel <num>              Number of parallel requests for LiveBench (default: 1).
  --lb-max-tokens <num>            Max tokens for LiveBench requests (default: 2048).
  --lb-num-questions <num>         Number of questions for LiveBench (default: -1 for all).
  --lb-system-prompt <prompt>      System prompt for LiveBench (optional).
  --lb-force-temperature <temp>    Force temperature for LiveBench's run_livebench.py (optional).

Local Llama-Server Management (Optional - if these are set, a local server is managed):
  --llama-server-exe <path>        Path to llama-server executable.
  --llama-model <path>             Path to GGUF model file for llama-server.
  --llama-host <host>              Host for local llama-server (default: 127.0.0.1).
  --llama-port <port>              Port for local llama-server (default: 8080).
  --llama-seed <seed>              Seed for local llama-server (default: -1 for random).
  --llama-logs                     Enable llama-server console logs (flag).
  --llama-log-verbosity <0-3>      Verbosity for llama-server logs (default: 0).

Parameter Passing to Target (llama-server or other):
  --<param_name> (<value>)?           Passes '--<param_name> (<value>)?' to local llama-server as a parameter.
                                   The TaguchiBench.Engine passes arguments directly.
  -t, --temp, --temperature <val>  Sets temperature for sampling. Also passed to LiveBench if --lb-force-temperature is not set.
  --env-<VAR_NAME> <value>         Sets environment variable VAR_NAME=value for local llama-server process.

Other Options:
  --verbose-runner                 Enable verbose logging for this runner utility itself (flag).
  --help                           Show this help message.

Output:
  Prints metrics as JSON to STDOUT, preceded by: v^v^v^RESULT^v^v^v
  Example: {"result":{"AverageScore":75.3,"Time":120.5}}
```

## Integration with TaguchiBench Engine

When using this runner as a `targetExecutablePath` for `TaguchiBench.Engine`:
1.  Set `targetExecutablePath` in the Engine's `config.yaml` to the path of the compiled `TaguchiBench.LiveBenchRunner` executable.
2.  In the Engine's `config.yaml`:
    *   `fixedCommandLineArguments` should contain arguments that `TaguchiBench.LiveBenchRunner` expects (e.g., `--livebench-scripts-path`, `--llama-server-exe`, `--model-name`).
    *   `controlFactors` (and `noiseFactors`) should define `cliArg` or `envVar` properties that correspond to arguments accepted by `TaguchiBench.LiveBenchRunner` for varying parameters (e.g., a control factor with `cliArg: "--temp"` will pass `--temp <value>` to this runner).
    *   The `metricsToAnalyze` in the Engine's config must match the keys output by this runner (e.g., "AverageScore", "Time").

## License

This project is licensed under the MIT License - see the `LICENSE` file for details.