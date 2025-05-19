# TaguchiBench Suite

TaguchiBench is a suite of tools designed for robust parameter optimization using the Taguchi method. It currently consists of two main components:

1.  **TaguchiBench Engine**: A generic, target-agnostic C# framework for designing and executing Taguchi experiments, analyzing results, and predicting optimal parameter configurations for *any* command-line executable or script.
2.  **TaguchiBench LiveBench Runner**: A specific C# command-line utility that acts as a target executable for the Engine. It facilitates running coding benchmarks using the [LiveBench framework](https://github.com/livebench/livebench) against OpenAI-compatible APIs, including locally managed `llama-server` instances.

This suite allows for systematic and efficient optimization of parameters, considering multiple performance metrics and interactions between parameters.

## Core Philosophy

The TaguchiBench Engine is built on the principle of **agnosticism and genericity**. It does not make assumptions about the target program being optimized, other than requiring it to accept parameters via command-line arguments/environment variables and to output results in a specific JSON format. This allows it to be a versatile tool for a wide range of optimization tasks.

The LiveBench Runner serves as a prime example of such a target executable, tailored for LLM performance evaluation in coding tasks.

## Prerequisites (Overall)

-   [.NET SDK](https://dotnet.microsoft.com/download) (.NET 8.0).
-   **Python 3.6+**: Required if you intend to use `TaguchiBench.LiveBenchRunner` or run the `install-livebench.sh` script.
-   **Git**: For cloning LiveBench via the install script.

## Initial Setup for LiveBench Optimization (Recommended First Steps)

If your primary goal is to optimize parameters for `llama-server` using the LiveBench coding benchmarks (the original use case of this project), follow these steps:

1.  **Clone this Repository**:
    ```bash
    git clone https://github.com/kooshi/TaguchiBench.git
    cd TaguchiBench
    ```
2.  **Set up LiveBench**:
    Run the provided script to download and prepare the LiveBench framework. This will clone the LiveBench repository into a `livebench` subdirectory.
    ```bash
    chmod +x install-livebench.sh
    ./install-livebench.sh
    ```

3.  **Build the TaguchiBench Suite**:
    This command will build all projects (`Common`, `Engine`, `LiveBenchRunner`).
    ```bash
    dotnet build src/TaguchiBench.sln -c Release 
    ```

4.  **Configure a Simple Experiment**:
    *   A `simple-livebench-config.yaml` file is provided in the repository root. This file is pre-configured to use `TaguchiBench.LiveBenchRunner` as the target for optimizing common `llama-server` sampler parameters.
    *   **You MUST edit `simple-livebench-config.yaml`** to set the correct paths for:
        *   The first argument under `fixedCommandLineArguments`: If `targetExecutablePath` is `"dotnet"`, this argument must be the path to your compiled `TaguchiBench.LiveBenchRunner.dll` (e.g., `src/TaguchiBench.LiveBenchRunner/bin/Release/netX.X/TaguchiBench.LiveBenchRunner.dll`). Adjust `netX.X` (e.g., `net6.0`, `net8.0`) to your .NET version. If `targetExecutablePath` points directly to an `.exe`, this DLL argument is not needed.
        *   `--livebench-scripts-path` (under `fixedCommandLineArguments`): Point this to the `livebench` directory created by `install-livebench.sh` (e.g., `./livebench/livebench`).
        *   `--llama-server-exe` (under `fixedCommandLineArguments`): Path to your compiled `llama-server` executable.
        *   `--llama-model` (under `fixedCommandLineArguments`): Path to your GGUF model file.
    *   Review other settings in `simple-livebench-config.yaml` like `--lb-num-questions` if you want a faster initial test.

5.  **Run Your First Experiment**:
    Once `simple-livebench-config.yaml` is updated, run the Engine from the repository root:
    ```bash
    chmod +x run-livebench-experiment.sh
    ./run-livebench-experiment.sh --config simple-livebench-config.yaml
    ```
    This script will use `dotnet run` (or execute a compiled version) to start the Engine with your specified configuration. Results will appear in the `outputDirectory` defined in the config file (e.g., `./livebench_optimization_results/`).

## Components

### 1. TaguchiBench Engine (`src/TaguchiBench.Engine/`)

The heart of the suite. It handles:
-   Experimental design using Taguchi Orthogonal Arrays.
-   Execution of a user-defined target program with varying parameters.
-   Collection of multiple numerical metrics from the target.
-   Advanced statistical analysis: S/N ratios, ANOVA (with pooling), main effects, interactions.
-   Prediction of optimal parameter settings and performance with confidence intervals for each analyzed metric.
-   Comprehensive HTML and Markdown reporting.
-   Experiment state persistence and recovery for long-running tasks.

[**➡️ Go to TaguchiBench Engine README for detailed usage and configuration.**](./src/TaguchiBench.Engine/README.md)

### 2. TaguchiBench LiveBench Runner (`src/TaguchiBench.LiveBenchRunner/`)

A specialized utility that:
-   Runs LiveBench coding benchmarks.
-   Can target any OpenAI-compatible API endpoint.
-   Optionally manages a local `llama-server` instance for evaluations.
-   Outputs results in the JSON format expected by the TaguchiBench Engine.
-   Can also be used as a standalone tool for single LiveBench runs.

[**➡️ Go to TaguchiBench LiveBench Runner README for detailed usage and CLI options.**](./src/TaguchiBench.LiveBenchRunner/README.md)

## General Workflow (Custom Target)

1.  **Set up your Environment**:
    *   Install .NET SDK.
2.  **Prepare your Target Executable**:
    *   Ensure it adheres to the Engine's [input/output contract](./src/TaguchiBench.Engine/README.md#for-developers-target-executable-contract).
3.  **Configure the TaguchiBench Engine**:
    *   Create a `config.yaml` file for the Engine (see `src/TaguchiBench.Engine/sample-config.yaml`).
    *   Specify your `targetExecutablePath`.
    *   Define the `metricsToAnalyze`, `controlFactors`, `fixedCommandLineArguments`, etc., relevant to your target.
4.  **Run the Engine**:
    ```bash
    # Example using dotnet run for the Engine
    dotnet run --project TaguchiBench.Engine -- --config your_experiment_config.yaml
    ```
5.  **Review Results**:
    *   Check the `outputDirectory` for detailed HTML and Markdown reports, and the experiment state YAML file.


## Future Enhancements (Conceptual)
-   More sophisticated state management for recovery (e.g., handling Ctrl-C interruption).
-   Advanced ANOVA pooling strategies (e.g., based on F-distribution rather than just percentage threshold if error DF is low).
-   GUI for configuration and result visualization.

## License

This project is licensed under the MIT License - see the `LICENSE` file for details.

## Acknowledgments

-   [llama.cpp](https://github.com/ggml-org/llama.cpp) for the LLM inference engine.
-   [LiveBench](https://github.com/livebench/livebench) for the coding benchmark framework.
-   [YamlDotNet](https://github.com/aaubry/YamlDotNet) for YAML serialization in C#.
-   [Serilog](https://serilog.net/) for flexible logging.
-   [Chart.js](https://www.chartjs.org/) for chart rendering in HTML reports.
-   [MathNet.Numerics](https://numerics.mathdotnet.com/) for statistical distributions.