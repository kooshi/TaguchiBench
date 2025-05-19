# TaguchiBench Engine

TaguchiBench Engine is a powerful and generic C# framework for optimizing parameters of any target executable using the Taguchi method. It systematically designs experiments, executes a target program with varied parameters, collects numerical metrics, and performs comprehensive statistical analysis (S/N Ratios, ANOVA, Optimal Configuration Prediction) to identify robust parameter settings.

The Engine is designed to be **target-agnostic**. It can work with any script or program that accepts command-line arguments and/or environment variables and outputs its results as a simple JSON object to standard output.

## Features

-   **Generic Target Execution**: Works with any executable or script.
-   **Taguchi Method Optimization**: Efficiently finds optimal parameter values with a minimal number of experiments using Orthogonal Arrays.
-   **Multi-Metric Analysis**: Analyzes multiple user-defined metrics simultaneously, each with its own optimization criteria (Larger is Better, Smaller is Better, Nominal is Best).
-   **Control and Noise Factors**: Supports both control factors (for optimization) and noise factors (for robustness analysis).
-   **Parameter Interaction Analysis**: Detects and analyzes interactions between control factors.
-   **Flexible Parameter Definition**: Factors can be defined with discrete levels or numerical ranges (integer/float).
-   **Statistical Deep Dive**:
    -   Signal-to-Noise (S/N) Ratios calculated for each metric.
    -   Analysis of Variance (ANOVA) to identify significant factors and interactions.
    -   Pooling of insignificant effects in ANOVA for refined analysis.
    -   Determination of optimal factor levels for each analyzed metric.
    -   Prediction of performance at the optimal configuration with confidence intervals.
-   **Comprehensive Reporting**: Generates detailed HTML and Markdown reports, including per-run experimental data and analysis charts.
-   **Experiment State Persistence & Recovery**: Saves full experiment state after each run, allowing for recovery and continuation of long experiments.
-   **YAML Configuration**: Human-readable YAML for all engine settings, factor definitions, and metric analysis criteria.
-   **Cross-Platform**: Built with .NET, enabling execution on Windows, Linux, and macOS.

## Prerequisites

-   [.NET SDK](https://dotnet.microsoft.com/download) (Version specified in `.csproj`, typically .NET 6.0 or newer).
-   The **Target Executable**: Your script or program that will be run by the engine. It must:
    1.  Accept parameters via command-line arguments and/or environment variables.
    2.  After completing its task, print a specific sentinel string `v^v^v^RESULT^v^v^v` to STDOUT.
    3.  Immediately after the sentinel, print a single line of JSON to STDOUT representing a dictionary of metrics. Example:
        ```json
        {"result":{"MetricA": 0.75, "TimeInSeconds": 123.4, "AnotherCustomMetric": 1024}}
        ```
        The keys in this JSON (e.g., "MetricA", "TimeInSeconds") must match the `name` fields in the `metricsToAnalyze` section of the Engine's configuration file.

## Quick Start

1.  **Clone the Repository** (if applicable, or ensure you have the `TaguchiBench.Engine` project).
2.  **Build the Engine**:
    ```bash
    dotnet build TaguchiBench.Engine -c Release
    ```
3.  **Prepare your Target Executable**: Ensure it meets the output requirements described above.
4.  **Create a Configuration File**:
    Copy the `sample-config.yaml` (located within the `TaguchiBench.Engine` project or documentation) to your working directory and rename it (e.g., `my_experiment_config.yaml`).
    Edit this file to:
    *   Specify the `targetExecutablePath`.
    *   Define `metricsToAnalyze` matching your target's JSON output.
    *   List `fixedCommandLineArguments` and `fixedEnvironmentVariables` your target always needs.
    *   Define `controlFactors` (parameters to optimize) with their names, how they are passed (`cliArg` or `envVar`), and their levels/ranges.
    *   Optionally, define `noiseFactors` and `interactions`.
5.  **Run the Engine**:
    ```bash
    # Using dotnet run (if in the project directory)
    dotnet run --project TaguchiBench.Engine -- --config my_experiment_config.yaml

    # Or using the compiled executable
    # ./TaguchiBench.Engine/bin/Release/netX.X/TaguchiBench.Engine --config my_experiment_config.yaml
    ```
    (Replace `netX.X` with your .NET version, e.g., `net6.0`)

## Configuration (`config.yaml`)

All settings are managed through a YAML configuration file. Key sections include:

-   `repetitions`: Number of times each OA run is repeated.
-   `outputDirectory`: Where logs, state files, and reports are saved.
-   `targetExecutablePath`: Path to your script/program.
-   `verbose`: Enables detailed engine logging.
-   `showTargetOutput`: If true, engine logs STDOUT/STDERR from the target.
-   `metricsToAnalyze`: List of metrics to analyze. Each needs:
    -   `name`: Exact key from target's JSON output.
    -   `method`: `LargerIsBetter`, `SmallerIsBetter`, or `Nominal`.
    -   `target` (double): Required if method is `Nominal`.
-   `fixedCommandLineArguments`: Dictionary of arguments always passed to the target. Keys are the full argument string (e.g., `"--seed"`), value is the argument's value (or `null` for flags).
-   `fixedEnvironmentVariables`: Dictionary of environment variables always set for the target.
-   `controlFactors`: List of parameters to optimize. Each needs:
    -   `name`: A unique name for the factor.
    -   `cliArg` (optional): The command-line argument string for this factor (e.g., `"--learning-rate"`).
    -   `envVar` (optional): The environment variable name for this factor.
    -   `levels` (list of strings), or `floatRange: [min, max]`, or `intRange: [min, max]`.
-   `noiseFactors` (optional): Similar structure to `controlFactors`. Used for robustness analysis.
-   `interactions` (optional): List of pairs of `controlFactor` names to analyze for interaction effects.

Refer to `sample-config.yaml` for a detailed example.

## Command-Line Interface

```text
TaguchiBench.Engine - LLM Parameter Optimization Framework (Version X.Y.Z)
Usage: TaguchiBench.Engine [mode_option] [other_options]

Operation Modes (Mutually Exclusive):
  -c, --config <path>          Run a new experiment using the specified YAML configuration file.
  -r, --recover <state_path>   Recover and continue an experiment from a .yaml state file.
  --report-html <state_path>   Generate only an HTML report from an experiment state file.
  --report-md <state_path>     Generate only a Markdown report from an experiment state file.

Other Options:
  -o, --output-dir <dir>       Override the output directory specified in the config/state file.
  -v, --verbose                Enable verbose logging globally, overriding config/state settings.
  -h, --help                   Show this help message and exit.

If no arguments are provided, the program will look for 'config.yaml' in the current directory.
```

## Understanding Results

The `outputDirectory` will contain:

1.  **HTML Report (`*_TaguchiAnalysisReport.html`)**: The main graphical report with detailed analysis for *each configured metric*. Includes optimal configurations, predicted performance, ANOVA tables, main effect plots, interaction plots, and normal probability plots.
2.  **Markdown Report (`*_analysis_report.md`)**: A text-based summary of key findings for all analyzed metrics.
3.  **Experiment State / Summary YAML (`state_*_experiment_summary.yaml` or `state_*_after_oa_run_X.yaml`)**:
    *   Contains the full configuration, OA design, all raw collected metrics for every run and repetition, and all analysis results.
    *   The latest version of this file (typically `*_experiment_summary.yaml` or the one from the last completed run) can be used for recovery (`--recover`) or report regeneration.
4.  **Logs (`logs_engine/taguchibench-*.log`)**: Detailed operational logs from the Engine.

Key insights from the reports (per analyzed metric):
-   **Optimal Parameters**: The best settings for each control factor to optimize that specific metric.
-   **Significant Factors**: ANOVA tables highlight which parameters (and their interactions) have a statistically significant impact.
-   **Effect Plots**: Visualize how changing each parameter level affects performance (S/N ratio and raw metric average).
-   **Predicted Performance**: The expected outcome if the optimal parameter settings are used, with a confidence interval.

## How It Works (Simplified)

1.  **Configuration**: Reads the YAML config.
2.  **Design**: Selects/generates a Taguchi Orthogonal Array based on control factors.
3.  **Execution**: For each unique parameter combination (an "OA run"):
    *   Combines fixed arguments, current control factor levels, and (if applicable) noise factor levels.
    *   Executes the `targetExecutablePath` with these parameters.
    *   Parses the JSON output from the target to get metric values.
    *   Repeats for configured `repetitions` (cycling through noise factor levels if defined).
    *   Saves state after each OA run.
4.  **Analysis**: For *each* metric in `metricsToAnalyze`:
    *   Calculates Signal-to-Noise (S/N) ratios.
    *   Performs ANOVA on S/N ratios to find significant effects.
    *   Determines the optimal factor levels.
    *   Predicts performance at this optimum.
5.  **Reporting**: Generates HTML and Markdown summaries.

## For Developers: Target Executable Contract

-   **Input**: Receive parameters via CLI arguments and/or environment variables. The Engine sends these exactly as defined in its configuration (e.g., if `cliArg` is `"--myparam"`, the target receives `"--myparam value"`).
-   **Output**:
    1.  Perform its task.
    2.  Print the exact string `v^v^v^RESULT^v^v^v` to STDOUT on its own line.
    3.  Print a single line of JSON to STDOUT immediately after the sentinel. This JSON must be an object with a single top-level key `"result"`, whose value is another object (dictionary) of metric names (strings) to metric values (doubles).
        ```json
        {"result":{"MetricName1":123.45,"MetricName2":0.987,"Time":67.8}}
        ```
    *   Any other output (logs, debug info) from the target should go to STDOUT (before the sentinel) or STDERR. The Engine can be configured to display this (`showTargetOutput: true`).

## License

This project is licensed under the MIT License - see the `LICENSE` file for details.