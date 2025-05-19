// TaguchiBench.Engine/Configuration/EngineConfigDtos.cs

using System.Collections.Generic;
using YamlDotNet.Serialization; // For YamlMember attribute if needed for fine-grained control

namespace TaguchiBench.Engine.Configuration {
    /// <summary>
    /// DTO for defining a metric to be analyzed by the Taguchi engine.
    /// </summary>
    public class MetricToAnalyzeDto {
        [YamlMember(Alias = "name")]
        public string Name { get; set; }

        [YamlMember(Alias = "method")]
        public string Method { get; set; } // Expected: "LargerIsBetter", "SmallerIsBetter", "Nominal"

        [YamlMember(Alias = "target", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
        public double? Target { get; set; } // Required only if Method is "Nominal"
    }

    /// <summary>
    /// DTO for defining a control or noise factor.
    /// </summary>
    public class FactorDto {
        [YamlMember(Alias = "name")]
        public string Name { get; set; }

        [YamlMember(Alias = "cliArg", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
        public string CliArgument { get; set; }

        [YamlMember(Alias = "envVar", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
        public string EnvironmentVariable { get; set; }

        // Only one of Levels, FloatRange, or IntRange should be populated.
        [YamlMember(Alias = "levels", DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)]
        public List<string> Levels { get; set; }

        [YamlMember(Alias = "floatRange", DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)]
        public List<double> FloatRange { get; set; } // Expected: [min, max]

        [YamlMember(Alias = "intRange", DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)]
        public List<int> IntRange { get; set; }       // Expected: [min, max]
    }

    /// <summary>
    /// DTO for defining a parameter interaction to be analyzed.
    /// </summary>
    public class InteractionDto {
        [YamlMember(Alias = "firstFactorName")]
        public string FirstFactorName { get; set; }

        [YamlMember(Alias = "secondFactorName")]
        public string SecondFactorName { get; set; }
    }

    /// <summary>
    /// Root DTO for the TaguchiBench Engine configuration file.
    /// </summary>
    public class EngineConfigDto {
        [YamlMember(Alias = "repetitions")]
        public int Repetitions { get; set; } = 1;

        [YamlMember(Alias = "outputDirectory")]
        public string OutputDirectory { get; set; } = "./taguchi_results"; // Matching old default

        [YamlMember(Alias = "targetExecutablePath")]
        public string TargetExecutablePath { get; set; }

        [YamlMember(Alias = "verbose")]
        public bool Verbose { get; set; } = false;

        [YamlMember(Alias = "showTargetOutput")]
        public bool ShowTargetOutput { get; set; } = true;

        [YamlMember(Alias = "metricsToAnalyze", DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)]
        public List<MetricToAnalyzeDto> MetricsToAnalyze { get; set; }

        [YamlMember(Alias = "fixedCommandLineArguments", DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)]
        public Dictionary<string, object> FixedCommandLineArguments { get; set; } // Value can be string or null for flags

        [YamlMember(Alias = "fixedEnvironmentVariables", DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)]
        public Dictionary<string, string> FixedEnvironmentVariables { get; set; }

        [YamlMember(Alias = "controlFactors", DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)]
        public List<FactorDto> ControlFactors { get; set; }

        [YamlMember(Alias = "noiseFactors", DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)]
        public List<FactorDto> NoiseFactors { get; set; }

        [YamlMember(Alias = "interactions", DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)]
        public List<InteractionDto> Interactions { get; set; }
    }
}