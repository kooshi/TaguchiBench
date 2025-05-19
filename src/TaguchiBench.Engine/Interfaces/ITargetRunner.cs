// TaguchiBench.Engine/Interfaces/ITargetRunner.cs

using System.Collections.Generic;
using System.Threading.Tasks;

namespace TaguchiBench.Engine.Interfaces {
    /// <summary>
    /// Defines the contract for running a target executable and retrieving its metrics.
    /// This abstraction allows the Taguchi engine to remain agnostic of how the target is executed
    /// and how its results are communicated.
    /// </summary>
    public interface ITargetRunner {
        /// <summary>
        /// Executes the configured target executable with the specified command-line arguments and environment variables.
        /// </summary>
        /// <param name="commandLineArguments">
        /// A dictionary where keys are argument names (without prefixes like '-' or '--')
        /// and values are the argument values. If a value is null, the argument is treated as a flag.
        /// Example: { "config", "path/to/file.cfg" }, { "verbose", null }
        /// would translate to something like "--config path/to/file.cfg --verbose".
        /// </param>
        /// <param name="environmentVariables">
        /// A dictionary of environment variables to set for the target process.
        /// </param>
        /// <param name="showTargetOutput">
        /// If true, the target's standard output and standard error streams should be actively displayed or logged.
        /// </param>
        /// <returns>
        /// A task that resolves to a dictionary of metric names to their corresponding double values,
        /// as reported by the target executable.
        /// </returns>
        Task<Dictionary<string, double>> RunAsync(
            Dictionary<string, object> commandLineArguments,
            Dictionary<string, string> environmentVariables,
            bool showTargetOutput);
    }
}