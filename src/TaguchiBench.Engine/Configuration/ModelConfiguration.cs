using System;
using System.Collections.Generic;
using System.IO;

namespace TaguchiBench.Configuration {
    /// <summary>
    /// Configuration for a model to benchmark.
    /// </summary>
    public class ModelConfiguration {
        /// <summary>
        /// Path to the llama.cpp executable.
        /// </summary>
        public string LlamaExecutablePath { get; }

        /// <summary>
        /// Path to the model file.
        /// </summary>
        public string ModelPath { get; }

        /// <summary>
        /// Name of the model.
        /// </summary>
        public string ModelName { get; }

        /// <summary>
        /// Fixed parameters to use for all benchmark runs.
        /// </summary>
        public Dictionary<string, object> FixedParameters { get; }

        /// <summary>
        /// Environment variables to set before starting the server process.
        /// </summary>
        public Dictionary<string, string> EnvironmentVariables { get; }

        /// <summary>
        /// Random seed for reproducibility.
        /// </summary>
        public int Seed { get; }

        /// <summary>
        /// Initializes a new model configuration.
        /// </summary>
        /// <param name="llamaExecutablePath">Path to the llama.cpp executable</param>
        /// <param name="modelPath">Path to the model file</param>
        /// <param name="modelName">Name of the model</param>
        /// <param name="fixedParameters">Fixed parameters to use for all runs</param>
        /// <param name="environmentVariables">Environment variables to set before starting the server</param>
        /// <param name="seed">Random seed for reproducibility</param>
        public ModelConfiguration(
            string llamaExecutablePath,
            string modelPath,
            string modelName = null,
            Dictionary<string, object> fixedParameters = null,
            Dictionary<string, string> environmentVariables = null,
            int seed = -1) {

            if (string.IsNullOrWhiteSpace(llamaExecutablePath)) {
                throw new ArgumentException("Llama executable path cannot be empty", nameof(llamaExecutablePath));
            }

            if (string.IsNullOrWhiteSpace(modelPath)) {
                throw new ArgumentException("Model path cannot be empty", nameof(modelPath));
            }

            if (!File.Exists(llamaExecutablePath)) {
                throw new FileNotFoundException("Llama executable not found", llamaExecutablePath);
            }

            if (!File.Exists(modelPath)) {
                throw new FileNotFoundException("Model file not found", modelPath);
            }

            LlamaExecutablePath = llamaExecutablePath;
            ModelPath = modelPath;
            ModelName = modelName ?? Path.GetFileNameWithoutExtension(modelPath);
            FixedParameters = fixedParameters ?? new Dictionary<string, object>();
            EnvironmentVariables = environmentVariables ?? new Dictionary<string, string>();
            Seed = seed;
        }

        /// <summary>
        /// Creates a common set of fixed parameters for llama.cpp.
        /// </summary>
        /// <returns>Dictionary of fixed parameters</returns>
        public static Dictionary<string, object> CreateCommonFixedParameters() {
            return new Dictionary<string, object> {
                ["threads"] = Environment.ProcessorCount,
                ["batch-size"] = 512,
                ["keep"] = 0,
                ["mlock"] = true
            };
        }

        /// <summary>
        /// Creates a common set of environment variables for llama.cpp.
        /// </summary>
        /// <returns>Dictionary of environment variables</returns>
        public static Dictionary<string, string> CreateCommonEnvironmentVariables() {
            return new Dictionary<string, string>();
        }
    }
}
