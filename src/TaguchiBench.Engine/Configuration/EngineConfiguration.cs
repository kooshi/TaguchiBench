// TaguchiBench.Engine/Configuration/EngineConfiguration.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TaguchiBench.Common; // For Logger
using TaguchiBench.Engine.Core; // For Level, OALevel, ParameterLevelSet, ParameterInteraction
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TaguchiBench.Engine.Configuration {
    /// <summary>
    /// Defines the method for interpreting a metric's values.
    /// </summary>
    public enum MetricOptimizationMethod {
        LargerIsBetter,
        SmallerIsBetter,
        Nominal
    }

    /// <summary>
    /// Configuration for a specific metric to be analyzed by the Taguchi engine.
    /// </summary>
    public class MetricToAnalyze {
        public string Name { get; }
        public MetricOptimizationMethod Method { get; }
        public double? Target { get; } // Required only if Method is Nominal

        public MetricToAnalyze(string name, MetricOptimizationMethod method, double? target = null) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Metric name cannot be null or whitespace.", nameof(name));
            }
            if (method == MetricOptimizationMethod.Nominal && !target.HasValue) {
                throw new ArgumentException("Target value must be provided for Nominal optimization method.", nameof(target));
            }
            Name = name;
            Method = method;
            Target = target;
        }

        public SignalToNoiseType GetSignalToNoiseType() {
            return Method switch {
                MetricOptimizationMethod.LargerIsBetter => SignalToNoiseType.LargerIsBetter,
                MetricOptimizationMethod.SmallerIsBetter => SignalToNoiseType.SmallerIsBetter,
                MetricOptimizationMethod.Nominal => SignalToNoiseType.NominalIsBest(Target!.Value), // Null check done in constructor
                _ => throw new InvalidOperationException($"Unsupported metric optimization method: {Method}")
            };
        }
    }

    /// <summary>
    /// Defines a factor (either control or noise) for the Taguchi experiment.
    /// </summary>
    public class Factor : IFactorDefinition {
        public string Name { get; }
        public string CliArgument { get; }
        public string EnvironmentVariable { get; }
        public ParameterLevelSet Levels { get; } // Always populated, even if derived from a range

        // Private constructor to enforce creation via DTO conversion or specific factory methods.
        private Factor(string name, string cliArgument, string environmentVariable, ParameterLevelSet levels) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Factor name cannot be null or whitespace.", nameof(name));
            }
            if (string.IsNullOrWhiteSpace(cliArgument) && string.IsNullOrWhiteSpace(environmentVariable)) {
                throw new ArgumentException($"Factor '{name}' must specify at least a CLI argument or an environment variable.", nameof(name));
            }
            if (levels == null || !levels.Any()) {
                throw new ArgumentException($"Factor '{name}' must have at least one level defined.", nameof(levels));
            }

            Name = name;
            CliArgument = cliArgument;
            EnvironmentVariable = environmentVariable;
            Levels = levels;
        }

        internal static Factor FromDto(FactorDto dto, bool isControlFactor) {
            if (dto == null) {
                throw new ArgumentNullException(nameof(dto));
            }
            if (string.IsNullOrWhiteSpace(dto.Name)) {
                throw new ConfigurationException("Factor DTO is missing a name.");
            }

            ParameterLevelSet levelSet;

            if (dto.Levels != null && dto.Levels.Any()) {
                if ((dto.FloatRange != null && dto.FloatRange.Any()) || (dto.IntRange != null && dto.IntRange.Any())) {
                    throw new ConfigurationException($"Factor '{dto.Name}': Cannot specify 'levels' along with 'floatRange' or 'intRange'.");
                }
                var levels = dto.Levels
                    .Select((levelValue, index) => new Level(OALevel.Parse((index + 1).ToString()), levelValue))
                    .ToList();
                levelSet = new ParameterLevelSet(levels);
            } else if (dto.FloatRange != null && dto.FloatRange.Any()) {
                if (dto.IntRange != null && dto.IntRange.Any()) {
                    throw new ConfigurationException($"Factor '{dto.Name}': Cannot specify 'floatRange' along with 'intRange'.");
                }
                levelSet = CreateLevelsFromFloatRange(dto.Name, dto.FloatRange, isControlFactor);
            } else if (dto.IntRange != null && dto.IntRange.Any()) {
                levelSet = CreateLevelsFromIntRange(dto.Name, dto.IntRange, isControlFactor);
            } else {
                throw new ConfigurationException($"Factor '{dto.Name}' must define 'levels', 'floatRange', or 'intRange'.");
            }

            return new Factor(dto.Name, dto.CliArgument, dto.EnvironmentVariable, levelSet);
        }

        private static ParameterLevelSet CreateLevelsFromFloatRange(string factorName, List<double> range, bool isControlFactor) {
            if (range.Count != 2) {
                throw new ConfigurationException($"Factor '{factorName}': floatRange must contain exactly two values [min, max].");
            }
            double min = range[0];
            double max = range[1];
            if (min >= max) {
                throw new ConfigurationException($"Factor '{factorName}': In floatRange, min ({min}) must be less than max ({max}).");
            }

            // For control factors, typically 2 or 3 levels are chosen for OA compatibility.
            // For noise factors, more levels might be sampled if repetitions allow.
            // This example defaults to 3 levels for ranges if it's a control factor, 2 for noise (can be adjusted).
            int numLevels = isControlFactor ? 3 : 2; // Or make numLevels configurable in DTO for ranges
            var levels = new ParameterLevelSet();
            for (int i = 0; i < numLevels; i++) {
                double value = min + (max - min) * i / (numLevels - 1);
                levels.Add(OALevel.Parse((i + 1).ToString()), value.ToString("G6")); // General format, 6 significant digits
            }
            return levels;
        }

        private static ParameterLevelSet CreateLevelsFromIntRange(string factorName, List<int> range, bool isControlFactor) {
            if (range.Count != 2) {
                throw new ConfigurationException($"Factor '{factorName}': intRange must contain exactly two values [min, max].");
            }
            int min = range[0];
            int max = range[1];
            if (min >= max) {
                throw new ConfigurationException($"Factor '{factorName}': In intRange, min ({min}) must be less than max ({max}).");
            }

            int numLevels = isControlFactor ? 3 : 2;
            if ((max - min + 1) < numLevels && isControlFactor) { // Ensure enough distinct integers for control factor levels
                numLevels = max - min + 1;
            }

            var levels = new ParameterLevelSet();
            if (numLevels == 1) { // Only one possible integer value
                levels.Add(OALevel.One, min.ToString());
            } else {
                for (int i = 0; i < numLevels; i++) {
                    // Distribute levels across the integer range
                    int value = min + (int)Math.Round((double)(max - min) * i / (numLevels - 1));
                    levels.Add(OALevel.Parse((i + 1).ToString()), value.ToString());
                }
            }
            return levels;
        }
    }


    /// <summary>
    /// Configuration for the TaguchiBench Engine.
    /// This class encapsulates all settings required to define and run an experiment.
    /// </summary>
    public class EngineConfiguration {
        public int Repetitions { get; }
        public string OutputDirectory { get; }
        public string TargetExecutablePath { get; }
        public bool Verbose { get; }
        public bool ShowTargetOutput { get; }
        public int PoolingThresholdPercentage { get; } = 5; // Default to 5% pooling threshold

        public IReadOnlyList<MetricToAnalyze> MetricsToAnalyze { get; }
        public IReadOnlyDictionary<string, object> FixedCommandLineArguments { get; }
        public IReadOnlyDictionary<string, string> FixedEnvironmentVariables { get; }
        public IReadOnlyList<Factor> ControlFactors { get; }
        public IReadOnlyList<Factor> NoiseFactors { get; }
        public IReadOnlyList<ParameterInteraction> Interactions { get; } // Using existing ParameterInteraction record

        // Private constructor, instances are created via LoadFromFile or factory methods.
        internal EngineConfiguration(EngineConfigDto dto) {
            // Apply defaults from DTO if not set, or use hardcoded defaults here
            Repetitions = dto.Repetitions > 0 ? dto.Repetitions : 1;
            OutputDirectory = !string.IsNullOrWhiteSpace(dto.OutputDirectory) ? dto.OutputDirectory : "./taguchi_results";
            TargetExecutablePath = dto.TargetExecutablePath; // This is mandatory, validated later
            Verbose = dto.Verbose;
            ShowTargetOutput = dto.ShowTargetOutput;

            MetricsToAnalyze = dto.MetricsToAnalyze?
                .Select(mDto => new MetricToAnalyze(
                    mDto.Name,
                    Enum.TryParse<MetricOptimizationMethod>(mDto.Method, true, out var method) ? method
                        : throw new ConfigurationException($"Invalid metric optimization method: {mDto.Method} for metric {mDto.Name}"),
                    mDto.Target
                )).ToList() ?? new List<MetricToAnalyze>();

            FixedCommandLineArguments = dto.FixedCommandLineArguments ?? new Dictionary<string, object>();
            FixedEnvironmentVariables = dto.FixedEnvironmentVariables ?? new Dictionary<string, string>();

            ControlFactors = dto.ControlFactors?.Select(fDto => Factor.FromDto(fDto, true)).ToList() ?? new List<Factor>();
            NoiseFactors = dto.NoiseFactors?.Select(fDto => Factor.FromDto(fDto, false)).ToList() ?? new List<Factor>();

            Interactions = dto.Interactions?
                .Select(iDto => new ParameterInteraction(iDto.FirstFactorName, iDto.SecondFactorName))
                .ToList() ?? new List<ParameterInteraction>();

            ValidateConfiguration();
        }

        private void ValidateConfiguration() {
            if (string.IsNullOrWhiteSpace(TargetExecutablePath)) {
                throw new ConfigurationException("'targetExecutablePath' must be specified.");
            }
            if (!MetricsToAnalyze.Any()) {
                throw new ConfigurationException("'metricsToAnalyze' must contain at least one metric definition.");
            }
            if (!ControlFactors.Any()) {
                throw new ConfigurationException("'controlFactors' must contain at least one factor definition for optimization.");
            }
            // Further validation for interactions (e.g., factor names exist in ControlFactors)
            var controlFactorNames = ControlFactors.Select(cf => cf.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var interaction in Interactions) {
                if (!controlFactorNames.Contains(interaction.FirstParameterName)) {
                    throw new ConfigurationException($"Interaction '{interaction.FirstParameterName}*{interaction.SecondParameterName}': Factor '{interaction.FirstParameterName}' not found in controlFactors.");
                }
                if (!controlFactorNames.Contains(interaction.SecondParameterName)) {
                    throw new ConfigurationException($"Interaction '{interaction.FirstParameterName}*{interaction.SecondParameterName}': Factor '{interaction.SecondParameterName}' not found in controlFactors.");
                }
                if (string.Equals(interaction.FirstParameterName, interaction.SecondParameterName, StringComparison.OrdinalIgnoreCase)) {
                    throw new ConfigurationException($"Interaction '{interaction.FirstParameterName}*{interaction.SecondParameterName}': Cannot interact a factor with itself.");
                }
            }
        }


        public static EngineConfiguration LoadFromFile(string filePath) {
            if (!File.Exists(filePath)) {
                throw new FileNotFoundException("Engine configuration file not found.", filePath);
            }
            if (!filePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) && !filePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException("Engine configuration file must have .yaml or .yml extension.", nameof(filePath));
            }

            try {
                string yamlString = File.ReadAllText(filePath);
                EngineConfigDto dto = Program.YamlDeserializer.Deserialize<EngineConfigDto>(yamlString);

                if (dto == null) {
                    throw new ConfigurationException("Failed to deserialize engine configuration. The file might be empty or malformed.");
                }
                return new EngineConfiguration(dto);
            } catch (YamlDotNet.Core.YamlException yamlEx) {
                throw new ConfigurationException($"Error parsing YAML in configuration file '{filePath}': {yamlEx.Message}", yamlEx);
            } catch (Exception ex) {
                throw new ConfigurationException($"Error loading engine configuration from '{filePath}': {ex.Message}", ex);
            }
        }

        public void SaveToFile(string filePath) {
            try {
                string fileToUse = filePath;
                if (!filePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) && !filePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)) {
                    fileToUse = Path.ChangeExtension(filePath, ".yaml");
                }

                EngineConfigDto dto = ConvertToDto(this);
                string yamlString = Program.YamlSerializer.Serialize(dto);

                string directory = Path.GetDirectoryName(fileToUse);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(fileToUse, yamlString);
                Logger.Info("CONFIG", "Engine configuration saved to: {File}", fileToUse);
            } catch (Exception ex) {
                throw new InvalidOperationException($"Error saving engine configuration to '{filePath}': {ex.Message}", ex);
            }
        }

        public string CalculateConfigHash() {
            // Serialize to a canonical YAML string first to ensure consistent hashing
            // Use a DTO that has ordered collections or sort them before serializing for hashing
            var dtoForHashing = ConvertToDto(this, forHashing: true);
            string canonicalYaml = Program.YamlSerializer.Serialize(dtoForHashing);

            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonicalYaml));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        internal static EngineConfigDto ConvertToDto(EngineConfiguration config, bool forHashing = false) {
            // When converting for hashing, ensure collections are ordered if their order affects the hash
            Func<IEnumerable<FactorDto>, List<FactorDto>> orderFactors = factors =>
                forHashing ? factors.OrderBy(f => f.Name).ToList() : factors.ToList();
            Func<IEnumerable<InteractionDto>, List<InteractionDto>> orderInteractions = interactions =>
                forHashing ? interactions.OrderBy(i => i.FirstFactorName).ThenBy(i => i.SecondFactorName).ToList() : interactions.ToList();
            Func<IEnumerable<MetricToAnalyzeDto>, List<MetricToAnalyzeDto>> orderMetrics = metrics =>
                forHashing ? metrics.OrderBy(m => m.Name).ToList() : metrics.ToList();

            return new EngineConfigDto {
                Repetitions = config.Repetitions,
                OutputDirectory = config.OutputDirectory,
                TargetExecutablePath = config.TargetExecutablePath,
                Verbose = config.Verbose,
                ShowTargetOutput = config.ShowTargetOutput,
                MetricsToAnalyze = orderMetrics(config.MetricsToAnalyze.Select(m => new MetricToAnalyzeDto {
                    Name = m.Name,
                    Method = m.Method.ToString(),
                    Target = m.Target
                })),
                FixedCommandLineArguments = config.FixedCommandLineArguments?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), // Order doesn't matter for dicts in YAML generally
                FixedEnvironmentVariables = config.FixedEnvironmentVariables?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                ControlFactors = orderFactors(config.ControlFactors.Select(f => new FactorDto {
                    Name = f.Name,
                    CliArgument = f.CliArgument,
                    EnvironmentVariable = f.EnvironmentVariable,
                    Levels = f.Levels.Values.OrderBy(l => l.OALevel.Level).Select(l => l.Value).ToList() // Assuming Levels are always discrete strings for DTO
                })),
                NoiseFactors = orderFactors(config.NoiseFactors.Select(f => new FactorDto {
                    Name = f.Name,
                    CliArgument = f.CliArgument,
                    EnvironmentVariable = f.EnvironmentVariable,
                    Levels = f.Levels.Values.OrderBy(l => l.OALevel.Level).Select(l => l.Value).ToList()
                })),
                Interactions = orderInteractions(config.Interactions.Select(i => new InteractionDto {
                    FirstFactorName = i.FirstParameterName, // Assuming ParameterInteraction maps directly
                    SecondFactorName = i.SecondParameterName
                }))
            };
        }

        public string GetFixedCommandLineForDisplay() {
            return Path.GetFileName(TargetExecutablePath) + Environment.NewLine +
             string.Join(Environment.NewLine, FixedCommandLineArguments.Select(kvp => $"{kvp.Key}{(kvp.Value is null ? "" : $" {kvp.Value}")}"));
        }

        public string GetFixedEnvironmentVariablesForDisplay() {
            return string.Join(Environment.NewLine, FixedEnvironmentVariables.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }
    }

    /// <summary>
    /// Custom exception for configuration-related errors.
    /// </summary>
    public class ConfigurationException : Exception {
        public ConfigurationException(string message) : base(message) { }
        public ConfigurationException(string message, Exception innerException) : base(message, innerException) { }
    }
}