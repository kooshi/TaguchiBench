// TaguchiBench.Engine/Core/ResultAnalyzer.cs

using System;
using System.Collections.Generic;
using System.Linq;
using TaguchiBench.Common; // For Logger, TimingUtilities
// IFactorDefinition is in TaguchiBench.Engine.Core
using MathNet.Numerics.Distributions;
using MathNet.Numerics.Statistics;
using YamlDotNet.Serialization;

namespace TaguchiBench.Engine.Core {
    #region Helper Records for Type Safety and Readability (Largely Unchanged)

    public record ParameterSettings(Dictionary<string, Level> Settings) {
        public string GetConfigurationKey() {
            return string.Join(";", Settings.OrderBy(kvp => kvp.Key)
                                            .Select(kvp => $"{kvp.Key}:{kvp.Value.Value}"));
        }
    }

    // This record is now primarily for internal use within ResultAnalyzer after processing SingleMetricExperimentRunData
    internal record ExperimentRunResultForAnalysis(
        ParameterSettings Configuration,
        IReadOnlyList<double> MetricValues, // Values for the single metric being analyzed
        double SnRatioValue // S/N ratio for these MetricValues
    );

    public record ParameterMainEffect(
        Dictionary<Level, double> EffectsByLevelSn,
        Dictionary<Level, double> EffectsByLevelRaw // Raw values of the *single metric* being analyzed
    );

    public record ParameterInteractionEffect(Dictionary<(Level Level1, Level Level2), double> EffectsByLevelPair);

    public record OptimalConfiguration(Dictionary<string, Level> Settings);

    #endregion

    public enum SignalToNoiseEnum {
        LargerIsBetter,
        SmallerIsBetter,
        NominalIsBest
    }

    public static class SignalToNoiseEnumExtensions {
        public static SignalToNoiseType ToSignalToNoiseType(this SignalToNoiseEnum enumValue, double? target = null) {
            return SignalToNoiseType.FromEnum(enumValue, target);
        }
    }

    public abstract class SignalToNoiseType {
        protected SignalToNoiseType() { }

        public static readonly SignalToNoiseType LargerIsBetter = new LargerIsBetterType();
        public static readonly SignalToNoiseType SmallerIsBetter = new SmallerIsBetterType();
        
        public static SignalToNoiseType NominalIsBest(double target) => new NominalIsBestType(target);

        public sealed class LargerIsBetterType : SignalToNoiseType { }

        public sealed class SmallerIsBetterType : SignalToNoiseType { }

        public sealed class NominalIsBestType : SignalToNoiseType {
            public double Target { get; }
            public NominalIsBestType(double target) { Target = target; }
        }
        public virtual (SignalToNoiseEnum, double? target) ToEnum() {
            return this switch {
                LargerIsBetterType => (SignalToNoiseEnum.LargerIsBetter, null),
                SmallerIsBetterType => (SignalToNoiseEnum.SmallerIsBetter, null),
                NominalIsBestType nominal => (SignalToNoiseEnum.NominalIsBest, nominal.Target),
                _ => throw new InvalidOperationException("Unknown SignalToNoiseType")
            };
        }
        public static SignalToNoiseType FromEnum(SignalToNoiseEnum enumValue, double? target = null) {
            return enumValue switch {
                SignalToNoiseEnum.LargerIsBetter => LargerIsBetter,
                SignalToNoiseEnum.SmallerIsBetter => SmallerIsBetter,
                SignalToNoiseEnum.NominalIsBest => target.HasValue
                    ? NominalIsBest(target.Value)
                    : throw new ArgumentNullException(nameof(target), "Target value is required for NominalIsBest"),
                _ => throw new ArgumentOutOfRangeException(nameof(enumValue))
            };
        }
        public override string ToString() {
            return this switch {
                LargerIsBetterType => "Larger is Better",
                SmallerIsBetterType => "Smaller is Better",
                NominalIsBestType n => $"Nominal is Best (Target: {n.Target})",
                _ => "Unknown SignalToNoiseType"
            };
        }
    }

    public class AnovaResult {
        public const string ErrorSource = "Error";
        public string Source { get; set; }
        public double SumOfSquares { get; set; }
        public int DegreesOfFreedom { get; set; }
        public double MeanSquare { get; set; }
        public double FValue { get; set; }
        public double PValue { get; set; }
        public double ContributionPercentage { get; set; }
        public bool IsSignificant { get; set; }
    }

    public class PredictionResult {
        public double PredictedValue { get; set; }
        public double LowerBound { get; set; }
        public double UpperBound { get; set; }
        public bool IsSnScale { get; set; } = true; // Indicates if the CI was calculated on S/N scale then inverted
        public List<string> PredictionNotes { get; set; } = new List<string>();
    }

    public class AnovaAnalysisResult {
        public List<AnovaResult> AnovaTable { get; set; } = new List<AnovaResult>();
        public List<string> PooledSources { get; set; } = new List<string>();
        public bool IsPooled { get; set; } = false;
        public double TotalSumOfSquares { get; set; }
        public int TotalDegreesOfFreedom { get; set; }
        public List<string> AnalysisWarnings { get; set; } = new List<string>();
        public AnovaAnalysisResult() { } // For Yaml deserialization
        public AnovaAnalysisResult(List<AnovaResult> table, double totalSS, int totalDF) {
            AnovaTable = table;
            TotalSumOfSquares = totalSS;
            TotalDegreesOfFreedom = totalDF;
        }
    }

     public class EffectEstimate {
        public string Source { get; set; }
        public double Effect { get; set; }
        [YamlIgnore]
        public double AbsoluteEffect => Math.Abs(Effect);
    }


    /// <summary>
    /// Represents the detailed results for a single experimental run (OA row)
    /// for the specific metric being analyzed.
    /// </summary>
    public class ExperimentRunDetail {
        public int RunNumber { get; set; } // OA Run Index + 1
        public ParameterSettings Configuration { get; set; }
        public List<double> MetricValues { get; set; } // Raw values for the metric being analyzed, across repetitions
        public double SnRatioValue { get; set; } // S/N ratio for this metric's values
        // public IReadOnlyDictionary<string, double> AllMetricsFromRunRepetitionsAverage { get; set; } // Optional: if we want to show other metrics for context
    }

    /// <summary>
    /// Contains all analysis data for a single metric.
    /// </summary>
    public class FullAnalysisReportData {
        public string MetricAnalyzed { get; set; } // Name of the metric this report data pertains to
        public OptimalConfiguration OptimalConfig { get; set; }
        public AnovaAnalysisResult InitialAnova { get; set; }
        public AnovaAnalysisResult PooledAnova { get; set; }
        public PredictionResult PredictedPerformance { get; set; }
        public List<EffectEstimate> EffectEstimates { get; set; }
        public Dictionary<string, ParameterMainEffect> MainEffects { get; set; }
        public Dictionary<string, ParameterInteractionEffect> InteractionEffectsSn { get; set; }
        public string ArrayDesignationUsed { get; set; }
        public SignalToNoiseType SnTypeUsed { get; set; }
        public int NumberOfRunsInExperiment { get; set; } // Number of rows in the OA
        public List<ExperimentRunDetail> ExperimentRunDetails { get; set; }
    }


    public class ResultAnalyzer {
        private readonly IReadOnlyList<IFactorDefinition> _controlFactors;
        private readonly IReadOnlyList<ParameterInteraction> _interactionsToAnalyze;
        private readonly double _confidenceLevelAlpha;

        private readonly IReadOnlyDictionary<string, int> _columnAssignments; // 0-based column index in OA matrix
        private readonly OALevel[,] _orthogonalArray;
        private readonly string _arrayDesignationUsed;
        private readonly OrthogonalArrayInfo _arrayInfo; // Cached info for the used OA

        private const double DefaultAnovaAlpha = 0.05;
        private readonly double _poolingThresholdPercentage;
        public double PoolingThresholdPercentage => _poolingThresholdPercentage;

        public ResultAnalyzer(
            IReadOnlyList<IFactorDefinition> controlFactors,
            string arrayDesignationUsed,
            IReadOnlyList<ParameterInteraction> interactionsToAnalyze,
            IReadOnlyDictionary<string, int> columnAssignments,
            OALevel[,] orthogonalArray,
            double confidenceLevel = 0.95,
            double poolingThresholdPercentage = 5.0
        ) {
            _controlFactors = controlFactors ?? throw new ArgumentNullException(nameof(controlFactors));
            _arrayDesignationUsed = arrayDesignationUsed ?? throw new ArgumentNullException(nameof(arrayDesignationUsed));
            _arrayInfo = OrthogonalArrayFactory.GetArrayInfo(_arrayDesignationUsed); // Cache for efficiency
            _interactionsToAnalyze = interactionsToAnalyze ?? new List<ParameterInteraction>();
            _columnAssignments = columnAssignments; // Can be null if OA-based ANOVA is not possible
            _orthogonalArray = orthogonalArray;     // Can be null
            _confidenceLevelAlpha = 1.0 - confidenceLevel;
            _poolingThresholdPercentage = poolingThresholdPercentage;

            Logger.Debug("ANALYZER", "ResultAnalyzer initialized for OA: {ArrayDesignation}, Pooling Threshold: {PoolingThreshold}%",
                _arrayDesignationUsed, _poolingThresholdPercentage);

            if ((_columnAssignments != null && _orthogonalArray == null) || (_columnAssignments == null && _orthogonalArray != null)) {
                Logger.Warning("ANALYZER", "For OA-based ANOVA, both columnAssignments and orthogonalArray must be provided or both null. ANOVA capabilities may be limited.");
            }
        }

        /// <summary>
        /// Performs a full Taguchi analysis (S/N, ANOVA, optimal config, prediction) for a single metric.
        /// </summary>
        /// <param name="singleMetricRunDataList">
        /// Data for each OA run, where each entry contains the configuration and a list of raw values
        /// for the *single metric* being analyzed, across all repetitions for that OA run.
        /// </param>
        /// <param name="snType">The Signal-to-Noise calculation type appropriate for this metric.</param>
        /// <param name="metricName">The name of the metric being analyzed (for reporting).</param>
        /// <returns>A <see cref="FullAnalysisReportData"/> object containing all analysis results for this metric.</returns>
        public FullAnalysisReportData PerformSingleMetricFullAnalysis(
            IReadOnlyList<SingleMetricExperimentRunData> singleMetricRunDataList,
            SignalToNoiseType snType,
            string metricName) {

            if (singleMetricRunDataList == null || !singleMetricRunDataList.Any()) {
                Logger.Error("ANALYZER", "No run data provided to analyze for metric {MetricName}.", metricName);
                // Return a minimal report data object indicating no data
                return new FullAnalysisReportData {
                    MetricAnalyzed = metricName,
                    SnTypeUsed = snType,
                    ArrayDesignationUsed = _arrayDesignationUsed,
                    NumberOfRunsInExperiment = _orthogonalArray?.GetLength(0) ?? 0,
                    InitialAnova = new AnovaAnalysisResult(new List<AnovaResult>(), 0, 0) { AnalysisWarnings = { "No input data." } }
                };
            }

            Logger.Info("ANALYZER", "Starting full analysis for metric: '{MetricName}' (S/N Type: {SNType}). Analyzing {Count} OA configurations.",
                metricName, snType, singleMetricRunDataList.Count);
            TimingUtilities.StartTimer($"analysis_{metricName.Replace(" ", "_")}");

            // 1. Calculate S/N Ratios for each OA run configuration
            var resultsWithSN = singleMetricRunDataList.Select(runData => {
                double snRatio = CalculateSignalToNoiseRatio(runData.MetricValues, snType);
                return new ExperimentRunResultForAnalysis(runData.Configuration, runData.MetricValues, snRatio);
            }).ToList();

            // 2. Calculate Main Effects (S/N and Raw)
            var mainEffects = CalculateMainEffects(resultsWithSN, metricName); // Pass resultsWithSN

            // 3. Calculate Interaction Effects (S/N scale only for now)
            var interactionEffectsSN = CalculateInteractionEffects(resultsWithSN, metricName);

            // 4. Determine Optimal Configuration based on S/N ratios
            var optimalConfig = DetermineOptimalConfiguration(mainEffects, interactionEffectsSN, metricName);

            // 5. Perform ANOVA (Initial and Pooled)
            AnovaAnalysisResult initialAnova = PerformAnovaInternal(resultsWithSN, $"Initial ANOVA ({metricName})", snType);
            AnovaAnalysisResult pooledAnova = PerformPooledAnovaInternal(resultsWithSN, initialAnova, snType, $"Pooled ANOVA ({metricName})");

            // 6. Predict Performance at Optimal Configuration
            var anovaForPrediction = pooledAnova ?? initialAnova;
            var prediction = PredictPerformanceWithConfidence(
                optimalConfig,
                resultsWithSN, // Pass S/N results for prediction context
                anovaForPrediction.AnovaTable,
                snType,
                metricName
            );

            // 7. Calculate Effect Estimates (for 2-level factors/interactions, on S/N scale)
            var effectEstimates = CalculateEffectEstimates(resultsWithSN, metricName);

            // 8. Prepare detailed run information for reporting
            var experimentRunDetails = new List<ExperimentRunDetail>();
            for (int i = 0; i < resultsWithSN.Count; i++) {
                var runResultSn = resultsWithSN[i]; // This is ExperimentRunResultForAnalysis
                experimentRunDetails.Add(new ExperimentRunDetail {
                    RunNumber = i + 1, // Assuming resultsWithSN is ordered by OA run index
                    Configuration = runResultSn.Configuration,
                    MetricValues = runResultSn.MetricValues.ToList(), // Raw values for this metric
                    SnRatioValue = runResultSn.SnRatioValue
                });
            }

            TimeSpan analysisTime = TimingUtilities.StopTimer($"analysis_{metricName.Replace(" ", "_")}");
            Logger.Info("ANALYZER", "Full analysis for metric '{MetricName}' completed in {Duration}.", metricName, TimingUtilities.FormatElapsedTime(analysisTime));

            return new FullAnalysisReportData {
                MetricAnalyzed = metricName,
                OptimalConfig = optimalConfig,
                InitialAnova = initialAnova,
                PooledAnova = pooledAnova,
                PredictedPerformance = prediction,
                EffectEstimates = effectEstimates,
                MainEffects = mainEffects,
                InteractionEffectsSn = interactionEffectsSN,
                ArrayDesignationUsed = _arrayDesignationUsed,
                SnTypeUsed = snType,
                NumberOfRunsInExperiment = _orthogonalArray?.GetLength(0) ?? singleMetricRunDataList.Count,
                ExperimentRunDetails = experimentRunDetails
            };
        }

        // CalculateSignalToNoiseRatio remains unchanged.
        public double CalculateSignalToNoiseRatio(IReadOnlyList<double> values, SignalToNoiseType snType) {
            if (values == null || !values.Any()) {
                Logger.Warning("ANALYZER_SN", "Cannot calculate S/N ratio for empty list of values. Returning NaN.");
                return double.NaN;
            }
            if (values.Any(double.IsNaN)) {
                Logger.Warning("ANALYZER_SN", "Input values for S/N calculation contain NaN. S/N will be NaN.");
                return double.NaN;
            }

            int n = values.Count;
            switch (snType) {
                case SignalToNoiseType.LargerIsBetterType:
                    var ltbValues = values.Select(y => Math.Abs(y) < 1e-9 ? (y >= 0 ? 1e-9 : -1e-9) : y).ToList(); // Handle near-zero, preserve sign for context
                    if (ltbValues.Any(y => y == 0)) { // Should be rare with above
                        Logger.Warning("ANALYZER_SN", "Zero value encountered in LargerIsBetter S/N calculation. Result may be problematic.");
                    }
                    double sumOfInvertedSquares = ltbValues.Sum(y => y == 0 ? double.PositiveInfinity : (1.0 / (y * y))); // Handle exact zero explicitly if it bypasses previous
                    if (double.IsInfinity(sumOfInvertedSquares) || double.IsNaN(sumOfInvertedSquares) || sumOfInvertedSquares <= 0) {
                        return -200; // A very small S/N value
                    }
                    return -10 * Math.Log10(sumOfInvertedSquares / n);
                case SignalToNoiseType.SmallerIsBetterType:
                    double sumOfSquares = values.Sum(y => y * y);
                    if (sumOfSquares < 1e-9 && n > 0) { // All values are zero or near-zero
                        return 200; // A very large S/N value
                    }
                    if (sumOfSquares == 0 && n == 0) return double.NaN; // Avoid Log10(0/0)
                    if (sumOfSquares == 0 && n > 0) return 200; // Effectively perfect
                    return -10 * Math.Log10(sumOfSquares / n);
                case SignalToNoiseType.NominalIsBestType nominal:
                    double sumSqDevFromTarget = values.Sum(y => Math.Pow(y - nominal.Target, 2));
                    if (sumSqDevFromTarget < 1e-9 && n > 0) {
                        return 200; // Perfect match to target
                    }
                    if (sumSqDevFromTarget == 0 && n == 0) return double.NaN;
                    if (sumSqDevFromTarget == 0 && n > 0) return 200;
                    return -10 * Math.Log10(sumSqDevFromTarget / n);
                default:
                    throw new ArgumentOutOfRangeException(nameof(snType), "Invalid S/N ratio type specified.");
            }
        }

        // CalculateMainEffects now takes List<ExperimentRunResultForAnalysis>
        // It calculates S/N effects from resultSn.SnRatioValue
        // It calculates Raw effects from resultSn.MetricValues.Average()
        private Dictionary<string, ParameterMainEffect> CalculateMainEffects(
            IReadOnlyList<ExperimentRunResultForAnalysis> resultsWithSnAndRaw, // Contains raw values for the single metric
            string metricNameForLog) {
            Logger.Debug("ANALYZER", "Calculating main effects (S/N and Raw) for {FactorCount} factors for metric: {MetricName}.",
                         _controlFactors.Count, metricNameForLog);
            var mainEffects = new Dictionary<string, ParameterMainEffect>();

            foreach (IFactorDefinition factor in _controlFactors) {
                var effectsByLevelSn = new Dictionary<Level, List<double>>();
                var effectsByLevelRaw = new Dictionary<Level, List<double>>(); // For the single metric's raw values

                foreach (Level level in factor.Levels.Values) { // factor.Levels is ParameterLevelSet
                    effectsByLevelSn[level] = new List<double>();
                    effectsByLevelRaw[level] = new List<double>();
                }

                foreach (ExperimentRunResultForAnalysis resultSnRaw in resultsWithSnAndRaw) {
                    if (resultSnRaw.Configuration.Settings.TryGetValue(factor.Name, out Level actualLevelForRun)) {
                        // S/N effect
                        if (!double.IsNaN(resultSnRaw.SnRatioValue)) {
                            if (effectsByLevelSn.ContainsKey(actualLevelForRun)) {
                                effectsByLevelSn[actualLevelForRun].Add(resultSnRaw.SnRatioValue);
                            } else {
                                Logger.Warning("ANALYZER_EFFECTS", "S/N: Level '{LevelValue}' for factor '{FactorName}' from run config not in predefined levels for metric {Metric}. Ignoring.",
                                   actualLevelForRun.Value, factor.Name, metricNameForLog);
                            }
                        }

                        // Raw metric effect (average of repetitions for this OA run)
                        if (resultSnRaw.MetricValues.Any(v => !double.IsNaN(v))) {
                            double avgRawMetricForRun = resultSnRaw.MetricValues.Where(v => !double.IsNaN(v)).Average();
                            if (effectsByLevelRaw.ContainsKey(actualLevelForRun)) {
                                effectsByLevelRaw[actualLevelForRun].Add(avgRawMetricForRun);
                            } else {
                                Logger.Warning("ANALYZER_EFFECTS", "Raw: Level '{LevelValue}' for factor '{FactorName}' from run config not in predefined levels for metric {Metric}. Ignoring.",
                                    actualLevelForRun.Value, factor.Name, metricNameForLog);
                            }
                        }
                    }
                }

                var averagedEffectsSn = effectsByLevelSn.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Any() ? kvp.Value.Average() : double.NaN
                );
                var averagedEffectsRaw = effectsByLevelRaw.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Any() ? kvp.Value.Average() : double.NaN
                );

                mainEffects[factor.Name] = new ParameterMainEffect(averagedEffectsSn, averagedEffectsRaw);

                if (averagedEffectsSn.Any()) {
                    var bestLevelEntrySn = averagedEffectsSn
                        .Where(kvp => !double.IsNaN(kvp.Value))
                        .OrderByDescending(kvp => kvp.Value)
                        .FirstOrDefault();

                    if (bestLevelEntrySn.Key != null && !double.IsNaN(bestLevelEntrySn.Value)) {
                        Logger.Debug("ANALYSIS", "Metric '{MetricName}', Param '{Parameter}' best S/N level: {Level} (S/N: {SN:F4}, Raw: {Raw:F4})",
                            metricNameForLog, factor.Name, bestLevelEntrySn.Key, bestLevelEntrySn.Value,
                            averagedEffectsRaw.TryGetValue(bestLevelEntrySn.Key, out var rawVal) ? rawVal : double.NaN);
                    }
                }
            }
            return mainEffects;
        }

        // CalculateInteractionEffects now takes List<ExperimentRunResultForAnalysis>
        // It uses resultSn.SnRatioValue
        private Dictionary<string, ParameterInteractionEffect> CalculateInteractionEffects(
            IReadOnlyList<ExperimentRunResultForAnalysis> resultsWithSn,
            string metricNameForLog) {
            var interactionEffects = new Dictionary<string, ParameterInteractionEffect>();
            if (_interactionsToAnalyze == null || !_interactionsToAnalyze.Any()) {
                return interactionEffects;
            }
            Logger.Debug("ANALYZER", "Calculating interaction effects for {InteractionCount} interactions for metric: {MetricName}.",
                         _interactionsToAnalyze.Count, metricNameForLog);

            foreach (ParameterInteraction interaction in _interactionsToAnalyze) {
                string interactionKey = GetInteractionKey(interaction.FirstParameterName, interaction.SecondParameterName);
                var effectsByLevelPair = new Dictionary<(Level Level1, Level Level2), List<double>>();

                IFactorDefinition factor1Def = _controlFactors.FirstOrDefault(f => f.Name.Equals(interaction.FirstParameterName, StringComparison.OrdinalIgnoreCase));
                IFactorDefinition factor2Def = _controlFactors.FirstOrDefault(f => f.Name.Equals(interaction.SecondParameterName, StringComparison.OrdinalIgnoreCase));

                if (factor1Def == null || factor2Def == null) {
                    Logger.Warning("ANALYZER_INTERACTIONS", "One or both factors for interaction {InteractionKey} not found in control factor definitions for metric {MetricName}. Skipping.",
                                   interactionKey, metricNameForLog);
                    continue;
                }

                foreach (Level level1 in factor1Def.Levels.Values) {
                    foreach (Level level2 in factor2Def.Levels.Values) {
                        effectsByLevelPair[(level1, level2)] = new List<double>();
                    }
                }

                foreach (ExperimentRunResultForAnalysis resultSn in resultsWithSn) {
                    if (double.IsNaN(resultSn.SnRatioValue)) { continue; }

                    if (resultSn.Configuration.Settings.TryGetValue(interaction.FirstParameterName, out Level actualLevel1ForRun) &&
                        resultSn.Configuration.Settings.TryGetValue(interaction.SecondParameterName, out Level actualLevel2ForRun)) {
                        var pairKey = (actualLevel1ForRun, actualLevel2ForRun);
                        if (effectsByLevelPair.ContainsKey(pairKey)) {
                            effectsByLevelPair[pairKey].Add(resultSn.SnRatioValue);
                        } else {
                             Logger.Warning("ANALYZER_INTERACTIONS", "Level pair ({Level1Val}, {Level2Val}) for interaction {InteractionKey} from run not predefined for metric {MetricName}. Ignoring.",
                                actualLevel1ForRun.Value, actualLevel2ForRun.Value, interactionKey, metricNameForLog);
                        }
                    }
                }
                var averagedEffects = effectsByLevelPair.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Any() ? kvp.Value.Average() : double.NaN
                );
                interactionEffects[interactionKey] = new ParameterInteractionEffect(averagedEffects);

                if (averagedEffects.Any(kvp => !double.IsNaN(kvp.Value))) {
                    var bestInteractionEntry = averagedEffects
                        .Where(kvp => !double.IsNaN(kvp.Value))
                        .OrderByDescending(kvp => kvp.Value)
                        .FirstOrDefault();

                    if (bestInteractionEntry.Key.Level1 != null && !double.IsNaN(bestInteractionEntry.Value)) {
                        Logger.Debug("ANALYSIS", "Metric '{MetricName}', Interaction '{Interaction}' best level combination: ({Level1}, {Level2}) with S/N ratio: {SN:F4}",
                            metricNameForLog, interactionKey, bestInteractionEntry.Key.Item1, bestInteractionEntry.Key.Item2, bestInteractionEntry.Value);
                    }
                }
            }
            return interactionEffects;
        }

        // DetermineOptimalConfiguration remains largely unchanged logically, uses mainEffects and interactionEffectsSN.
        private OptimalConfiguration DetermineOptimalConfiguration(
            IReadOnlyDictionary<string, ParameterMainEffect> mainEffects,
            IReadOnlyDictionary<string, ParameterInteractionEffect> interactionEffectsSN, // S/N scale
            string metricNameForLog) {
            // Logic unchanged from previous version
            Logger.Info("ANALYZER", "Determining optimal configuration for metric: {MetricName}.", metricNameForLog);
            TimingUtilities.StartTimer($"optimal_config_{metricNameForLog}");

            var optimalSettings = new Dictionary<string, Level>();

            foreach (var parameter in _controlFactors) { // Iterating IFactorDefinition
                if (mainEffects.TryGetValue(parameter.Name, out var effectsContainer) &&
                    effectsContainer.EffectsByLevelSn.Any(kvp => !double.IsNaN(kvp.Value))) {
                    optimalSettings[parameter.Name] = effectsContainer.EffectsByLevelSn
                        .Where(kvp => !double.IsNaN(kvp.Value))
                        .OrderByDescending(kvp => kvp.Value)
                        .First().Key;
                } else {
                    // Fallback: use the first defined level if no S/N data (should be rare)
                    optimalSettings[parameter.Name] = parameter.Levels.Values.FirstOrDefault()
                        ?? throw new InvalidOperationException($"Factor {parameter.Name} has no defined levels.");
                    Logger.Warning("ANALYZER_OPTIMAL", "No valid main S/N effects for factor '{FactorName}' for metric {MetricName}, using its first defined level: {DefaultLevel}",
                        parameter.Name, metricNameForLog, optimalSettings[parameter.Name].Value);
                }
            }

            if (_interactionsToAnalyze == null || !_interactionsToAnalyze.Any() || interactionEffectsSN == null || !interactionEffectsSN.Any()) {
                TimeSpan configTimespan = TimingUtilities.StopTimer($"optimal_config_{metricNameForLog}");
                Logger.Debug("ANALYZER_OPTIMAL", "Optimal configuration for {MetricName} determined in {Duration} (no interaction adjustments made).",
                    metricNameForLog, TimingUtilities.FormatElapsedTime(configTimespan));
                return new OptimalConfiguration(optimalSettings);
            }

            Logger.Debug("ANALYZER_OPTIMAL", "Starting iterative adjustment for interactions for metric {MetricName} (max {MaxIterations} iterations).", metricNameForLog, _controlFactors.Count * 2);
            for (int iter = 0; iter < _controlFactors.Count * 2; iter++) { // Max iterations to prevent infinite loops
                bool changedInIteration = false;
                foreach (var interaction in _interactionsToAnalyze) {
                    string p1Name = interaction.FirstParameterName;
                    string p2Name = interaction.SecondParameterName;
                    string interactionKey = GetInteractionKey(p1Name, p2Name);

                    if (!interactionEffectsSN.TryGetValue(interactionKey, out var effectsByPairContainer) ||
                        !effectsByPairContainer.EffectsByLevelPair.Any(kvp => !double.IsNaN(kvp.Value))) {
                        continue; // No S/N data for this interaction
                    }
                    var effectsByPair = effectsByPairContainer.EffectsByLevelPair;

                    Level currentLvl1 = optimalSettings[p1Name];
                    Level currentLvl2 = optimalSettings[p2Name];

                    double currentPairSN = effectsByPair.TryGetValue((currentLvl1, currentLvl2), out var snVal) && !double.IsNaN(snVal)
                        ? snVal : double.MinValue; // Use MinValue if current pair S/N is NaN or not found

                    var bestPairForInteraction = effectsByPair
                        .Where(kvp => !double.IsNaN(kvp.Value)) // Consider only valid S/N values
                        .OrderByDescending(kvp => kvp.Value)
                        .FirstOrDefault(); // Default if all are NaN

                    if (bestPairForInteraction.Key.Level1 == null) { continue; } // No valid best pair found

                    if (bestPairForInteraction.Value > currentPairSN + 1e-6) { // If there's a meaningful improvement
                        Level newLvl1 = bestPairForInteraction.Key.Level1;
                        Level newLvl2 = bestPairForInteraction.Key.Level2;

                        double snImprovementFromInteraction = bestPairForInteraction.Value - currentPairSN;

                        // Get S/N effects from the mainEffects container
                        double mainEffect1OldSN = mainEffects[p1Name].EffectsByLevelSn.TryGetValue(currentLvl1, out var me1o) && !double.IsNaN(me1o) ? me1o : double.MinValue;
                        double mainEffect1NewSN = mainEffects[p1Name].EffectsByLevelSn.TryGetValue(newLvl1, out var me1n) && !double.IsNaN(me1n) ? me1n : double.MinValue;
                        double mainEffect2OldSN = mainEffects[p2Name].EffectsByLevelSn.TryGetValue(currentLvl2, out var me2o) && !double.IsNaN(me2o) ? me2o : double.MinValue;
                        double mainEffect2NewSN = mainEffects[p2Name].EffectsByLevelSn.TryGetValue(newLvl2, out var me2n) && !double.IsNaN(me2n) ? me2n : double.MinValue;

                        if (mainEffect1OldSN == double.MinValue || mainEffect1NewSN == double.MinValue || 
                            mainEffect2OldSN == double.MinValue || mainEffect2NewSN == double.MinValue) {
                            Logger.Debug("ANALYZER_OPTIMAL", "Skipping interaction adjustment for {InteractionKey} due to missing main effect S/N for one of the levels involved.", interactionKey);
                            continue;
                        }
                        
                        double mainEffectSNChange = (mainEffect1NewSN - mainEffect1OldSN) + (mainEffect2NewSN - mainEffect2OldSN);

                        // If the gain from interaction outweighs the loss from main effects (or adds to gain)
                        if (snImprovementFromInteraction + mainEffectSNChange > 1e-6) { // Use a small epsilon for comparison
                            if (!Equals(optimalSettings[p1Name], newLvl1) || !Equals(optimalSettings[p2Name], newLvl2)) {
                                Logger.Debug("ANALYZER_OPTIMAL", "Metric '{MetricName}', Adjusting for interaction '{InteractionKey}': ({OldL1},{OldL2}) -> ({NewL1},{NewL2}). Gain_Int={GainInt:F2}, Change_Main={ChangeMain:F2}, Total={Total:F2}",
                                    metricNameForLog, interactionKey, currentLvl1.Value, currentLvl2.Value, newLvl1.Value, newLvl2.Value,
                                    snImprovementFromInteraction, mainEffectSNChange, snImprovementFromInteraction + mainEffectSNChange);

                                optimalSettings[p1Name] = newLvl1;
                                optimalSettings[p2Name] = newLvl2;
                                changedInIteration = true;
                            }
                        }
                    }
                }
                if (!changedInIteration) {
                    break; // Converged
                }
            }

            TimeSpan configTime = TimingUtilities.StopTimer($"optimal_config_{metricNameForLog}");
            Logger.Debug("ANALYZER_OPTIMAL", "Optimal configuration for {MetricName} determined in {Duration} after interaction adjustments.",
                metricNameForLog, TimingUtilities.FormatElapsedTime(configTime));
            return new OptimalConfiguration(optimalSettings);
        }


        // PerformAnovaInternal now takes List<ExperimentRunResultForAnalysis>
        // It uses resultSn.SnRatioValue
        private AnovaAnalysisResult PerformAnovaInternal(
            IReadOnlyList<ExperimentRunResultForAnalysis> resultsWithSn,
            string analysisName,
            SignalToNoiseType snTypeContext) { // snTypeContext is for logging/context, not direct calculation here
            Logger.Info("ANALYZER_ANOVA", "Starting {AnalysisName}", analysisName);
            var analysisResult = new AnovaAnalysisResult(new List<AnovaResult>(), 0, 0);

            if (_orthogonalArray == null || _columnAssignments == null) {
                Logger.Warning("ANALYZER_ANOVA", "Orthogonal array or column assignments not provided. Cannot perform OA-based ANOVA for {AnalysisName}.", analysisName);
                analysisResult.AnalysisWarnings.Add("Orthogonal array or column assignments not provided. ANOVA based on OA structure aborted.");
                // Create a dummy error term if there's any S/N data at all
                var validSnResults = resultsWithSn.Where(r => !double.IsNaN(r.SnRatioValue)).ToList();
                if (!validSnResults.Any()) {
                    analysisResult.AnalysisWarnings.Add("No valid S/N data available for ANOVA.");
                    return analysisResult;
                }
                double sumOfSquares = validSnResults.Sum(r => Math.Pow(r.SnRatioValue - validSnResults.Average(s => s.SnRatioValue), 2));
                int df = Math.Max(0, validSnResults.Count - 1);
                var dummyError = new AnovaResult { Source = AnovaResult.ErrorSource, DegreesOfFreedom = df, SumOfSquares = sumOfSquares };
                if (dummyError.DegreesOfFreedom > 0) { dummyError.MeanSquare = dummyError.SumOfSquares / dummyError.DegreesOfFreedom; } 
                else { dummyError.MeanSquare = double.NaN; }
                analysisResult.AnovaTable.Add(dummyError);
                analysisResult.TotalSumOfSquares = dummyError.SumOfSquares;
                analysisResult.TotalDegreesOfFreedom = dummyError.DegreesOfFreedom;
                return analysisResult;
            }

            TimingUtilities.StartTimer($"anova_{analysisName.Replace(" ", "_").Replace("(", "").Replace(")", "")}");
            // Map S/N values to the OA runs. This needs to be robust to missing runs if recovery incomplete.
            double[] snValuesPerOARun = MapSnResultsToOAStructure(resultsWithSn);
            
            var anovaTable = CalculateAnovaTable(snValuesPerOARun, analysisResult.AnalysisWarnings);
            TimeSpan anovaTime = TimingUtilities.StopTimer($"anova_{analysisName.Replace(" ", "_").Replace("(", "").Replace(")", "")}");
            Logger.Info("ANALYZER_ANOVA", "{AnalysisName} completed in {Duration}.", analysisName, TimingUtilities.FormatElapsedTime(anovaTime));
            LogSignificantFactors(anovaTable, analysisName); // Unchanged

            var validSnValuesForTotal = snValuesPerOARun.Where(sn => !double.IsNaN(sn)).ToArray();
            double totalSS = validSnValuesForTotal.Any() ? validSnValuesForTotal.Sum(sn => Math.Pow(sn - validSnValuesForTotal.Average(), 2)) : 0;
            int totalDF = Math.Max(0, validSnValuesForTotal.Length - 1);

            analysisResult.AnovaTable = anovaTable;
            analysisResult.TotalSumOfSquares = totalSS;
            analysisResult.TotalDegreesOfFreedom = totalDF;
            return analysisResult;
        }


        // PerformPooledAnovaInternal now takes List<ExperimentRunResultForAnalysis>
        private AnovaAnalysisResult PerformPooledAnovaInternal(
            IReadOnlyList<ExperimentRunResultForAnalysis> resultsWithSn, // Used only if initial ANOVA fails and needs re-calc
            AnovaAnalysisResult initialAnova,
            SignalToNoiseType snTypeContext, // For logging/context
            string analysisName) {
            // Logic unchanged from previous version, as it operates on the initialAnova table.
            // ... (Assume this method's internal logic is the same as before) ...
            Logger.Info("ANALYZER_POOLING", "Attempting ANOVA with pooling for {AnalysisName}.", analysisName);
            var pooledAnovaResult = new AnovaAnalysisResult(new List<AnovaResult>(), initialAnova.TotalSumOfSquares, initialAnova.TotalDegreesOfFreedom) {
                IsPooled = true
            };

            var initialErrorTerm = initialAnova.AnovaTable.FirstOrDefault(r => r.Source == AnovaResult.ErrorSource);
            if (initialErrorTerm == null) {
                Logger.Error("ANALYZER_POOLING", "Initial ANOVA for {AnalysisName} is missing an error term. Cannot perform pooling.", analysisName);
                pooledAnovaResult.AnalysisWarnings.Add("Initial ANOVA missing error term; pooling aborted.");
                return null;
            }

            var sourcesToConsiderForPooling = initialAnova.AnovaTable
                .Where(r => r.Source != AnovaResult.ErrorSource)
                .ToList();

            if (!sourcesToConsiderForPooling.Any()) {
                Logger.Info("ANALYZER_POOLING", "No factor sources available to consider for pooling in {AnalysisName}.", analysisName);
                return null;
            }

            bool significantFactorsExist = initialAnova.AnovaTable.Any(r => r.IsSignificant && r.Source != AnovaResult.ErrorSource);
            bool isSaturated = initialErrorTerm.DegreesOfFreedom == 0;

            var factorsToActuallyPool = new List<AnovaResult>();

            if (!significantFactorsExist && sourcesToConsiderForPooling.Any()) { // Ensure there's something to pool
                Logger.Info("ANALYZER_POOLING", "No significant factors in initial ANOVA for {AnalysisName}. Attempting to pool least significant/contributing factor.", analysisName);
                AnovaResult factorToPool = null;
                if (isSaturated || Math.Abs(initialErrorTerm.MeanSquare) < 1e-9) {
                    factorToPool = sourcesToConsiderForPooling.OrderBy(r => r.ContributionPercentage).ThenBy(r => r.FValue).FirstOrDefault(); // Break ties with F-value
                } else {
                    factorToPool = sourcesToConsiderForPooling.OrderBy(r => r.FValue).FirstOrDefault();
                }
                if (factorToPool != null) {
                    factorsToActuallyPool.Add(factorToPool);
                    pooledAnovaResult.AnalysisWarnings.Add($"No significant factors initially; pooled '{factorToPool.Source}' (smallest F-value/contribution).");
                }
            } else {
                factorsToActuallyPool.AddRange(sourcesToConsiderForPooling
                    .Where(r => !r.IsSignificant && r.ContributionPercentage < _poolingThresholdPercentage));
                if (factorsToActuallyPool.Any()) {
                    pooledAnovaResult.AnalysisWarnings.Add($"Factors pooled based on significance and threshold ({_poolingThresholdPercentage}%): {string.Join(", ", factorsToActuallyPool.Select(f => $"'{f.Source}'"))}.");
                }
            }

            if (!factorsToActuallyPool.Any()) {
                Logger.Info("ANALYZER_POOLING", "No factors met pooling criteria for {AnalysisName}.", analysisName);
                return null;
            }

            pooledAnovaResult.PooledSources.AddRange(factorsToActuallyPool.Select(f => f.Source));
            double pooledErrorSS = initialErrorTerm.SumOfSquares + factorsToActuallyPool.Sum(f => f.SumOfSquares);
            int pooledErrorDF = initialErrorTerm.DegreesOfFreedom + factorsToActuallyPool.Sum(f => f.DegreesOfFreedom);

            factorsToActuallyPool.ForEach(entryToPool => 
                Logger.Debug("ANALYZER_POOLING", "For {AnalysisName}, pooling source '{Source}' (SS={SS:F4}, DF={DF}, Contrib={Contrib:F2}%) into error term.",
                   analysisName, entryToPool.Source, entryToPool.SumOfSquares, entryToPool.DegreesOfFreedom, entryToPool.ContributionPercentage)
            );
            
            var pooledAnovaTable = new List<AnovaResult>();
            foreach (var entry in initialAnova.AnovaTable.Where(r => r.Source != AnovaResult.ErrorSource && !pooledAnovaResult.PooledSources.Contains(r.Source))) {
                pooledAnovaTable.Add(new AnovaResult { // Create new objects
                    Source = entry.Source,
                    SumOfSquares = entry.SumOfSquares,
                    DegreesOfFreedom = entry.DegreesOfFreedom,
                    MeanSquare = entry.MeanSquare // MS will be recalculated if errorMS changes, but F/P will
                });
            }

            double pooledErrorMS = (pooledErrorDF > 0) ? pooledErrorSS / pooledErrorDF : double.NaN;
            if (pooledErrorDF <= 0 && pooledAnovaTable.Any()) {
                Logger.Warning("ANALYZER_POOLING", "Pooling for {AnalysisName} resulted in error DF <= 0 ({PooledErrorDF}). Pooled ANOVA may not be reliable.", analysisName, pooledErrorDF);
                pooledAnovaResult.AnalysisWarnings.Add($"Pooling resulted in error DF <= 0 ({pooledErrorDF}). Pooled ANOVA F/P values unreliable.");
            }

            pooledAnovaTable.Add(new AnovaResult {
                Source = AnovaResult.ErrorSource + " (Pooled)",
                SumOfSquares = pooledErrorSS,
                DegreesOfFreedom = pooledErrorDF,
                MeanSquare = pooledErrorMS,
                FValue = double.NaN, PValue = double.NaN, IsSignificant = false
            });

            pooledAnovaResult.AnovaTable = pooledAnovaTable;
            CalculateStatisticalSignificance(pooledAnovaResult.AnovaTable, initialAnova.TotalSumOfSquares, pooledAnovaResult.AnalysisWarnings); // Recalculate F, P, Contrib for remaining

            LogSignificantFactors(pooledAnovaResult.AnovaTable, analysisName);
            return pooledAnovaResult;
        }
        
        private double[] MapSnResultsToOAStructure(IReadOnlyList<ExperimentRunResultForAnalysis> resultsWithSn) {
            int numRunsOA = _orthogonalArray.GetLength(0);
            var snValues = new double[numRunsOA];
            for (int i = 0; i < numRunsOA; ++i) { snValues[i] = double.NaN; } // Initialize with NaN

            // Assuming resultsWithSn is ordered by OA run index (0 to N-1)
            // And that its count matches numRunsOA. If not, some OA runs won't have data.
            if (resultsWithSn.Count != numRunsOA) {
                Logger.Warning("ANALYZER_ANOVA", "Number of S/N results ({CountSN}) does not match OA runs ({CountOA}). Some S/N values in ANOVA will be NaN. This might occur during recovery if not all runs completed.",
                               resultsWithSn.Count, numRunsOA);
            }

            for (int i = 0; i < resultsWithSn.Count; ++i) {
                if (i < numRunsOA) { // Ensure we don't go out of bounds for snValues array
                    snValues[i] = resultsWithSn[i].SnRatioValue;
                }
            }
            return snValues;
        }


        // CalculateEffectEstimates now takes List<ExperimentRunResultForAnalysis>
        private List<EffectEstimate> CalculateEffectEstimates(
            IReadOnlyList<ExperimentRunResultForAnalysis> resultsWithSn,
            string metricNameForLog) {
            // Logic unchanged from previous version, uses resultSn.SnRatioValue
            var estimates = new List<EffectEstimate>();
            if (_orthogonalArray == null || _columnAssignments == null) {
                Logger.Warning("ANALYZER_EFFECTS", "Cannot calculate effect estimates for {MetricName} without OA and column assignments.", metricNameForLog);
                return estimates;
            }
            var validSnResults = resultsWithSn.Where(r => !double.IsNaN(r.SnRatioValue)).ToList();
            if (!validSnResults.Any()) {
                Logger.Warning("ANALYZER_EFFECTS", "No valid S/N results to calculate effect estimates for {MetricName}.", metricNameForLog);
                return estimates;
            }
            double[] snValuesPerOARun = MapSnResultsToOAStructure(validSnResults); // Use the helper

            foreach (var factor in _controlFactors) { // IFactorDefinition
                if (factor.Levels.Count == 2 && _columnAssignments.TryGetValue(factor.Name, out int colIdx)) {
                    var level1Sn = new List<double>();
                    var level2Sn = new List<double>();
                    for (int i = 0; i < _orthogonalArray.GetLength(0); i++) {
                        if (double.IsNaN(snValuesPerOARun[i])) { continue; }

                        if (_orthogonalArray[i, colIdx] == OALevel.One) {
                            level1Sn.Add(snValuesPerOARun[i]);
                        } else if (_orthogonalArray[i, colIdx] == OALevel.Two) {
                            level2Sn.Add(snValuesPerOARun[i]);
                        }
                    }
                    if (level1Sn.Any() && level2Sn.Any()) {
                        estimates.Add(new EffectEstimate { Source = factor.Name, Effect = level2Sn.Average() - level1Sn.Average() });
                    }
                }
            }

            foreach (var interaction in _interactionsToAnalyze) {
                var p1 = _controlFactors.First(f => f.Name.Equals(interaction.FirstParameterName, StringComparison.OrdinalIgnoreCase));
                var p2 = _controlFactors.First(f => f.Name.Equals(interaction.SecondParameterName, StringComparison.OrdinalIgnoreCase));
                string intKey = GetInteractionKey(interaction.FirstParameterName, interaction.SecondParameterName);

                if (p1.Levels.Count == 2 && p2.Levels.Count == 2 && _columnAssignments.TryGetValue(intKey, out int intColIdx)) {
                    var level1Sn = new List<double>();
                    var level2Sn = new List<double>();
                    for (int i = 0; i < _orthogonalArray.GetLength(0); i++) {
                        if (double.IsNaN(snValuesPerOARun[i])) { continue; }

                        if (_orthogonalArray[i, intColIdx] == OALevel.One) {
                            level1Sn.Add(snValuesPerOARun[i]);
                        } else if (_orthogonalArray[i, intColIdx] == OALevel.Two) {
                            level2Sn.Add(snValuesPerOARun[i]);
                        }
                    }
                    if (level1Sn.Any() && level2Sn.Any()) {
                        estimates.Add(new EffectEstimate { Source = intKey, Effect = level2Sn.Average() - level1Sn.Average() });
                    }
                }
            }
            return estimates.OrderByDescending(e => e.AbsoluteEffect).ToList();
        }


        private void LogSignificantFactors(IReadOnlyList<AnovaResult> anovaTable, string analysisName) {
            var significantFactors = anovaTable
                .Where(a => a.Source != AnovaResult.ErrorSource && a.Source != (AnovaResult.ErrorSource + " (Pooled)") && a.IsSignificant)
                .OrderByDescending(a => a.ContributionPercentage)
                .ToList();

            if (significantFactors.Any()) {
                Logger.Info($"ANALYSIS ({analysisName})", "Significant factors/interactions ({Count}) at α={Alpha:F2} level:", significantFactors.Count, DefaultAnovaAlpha);
                foreach (var factor in significantFactors) {
                    Logger.Info($"ANALYSIS ({analysisName})", "  {Source}: Contribution={Contribution:F2}%, F={FValue:F2}, p={PValue:F4}",
                        factor.Source, factor.ContributionPercentage, factor.FValue, factor.PValue);
                }
            } else {
                Logger.Info($"ANALYSIS ({analysisName})", "No significant factors or interactions found at α={Alpha:F2} level.", DefaultAnovaAlpha);
            }
        }

        private List<AnovaResult> CalculateAnovaTable(double[] snValuesPerOARun, List<string> analysisWarnings) {
            var anovaTable = new List<AnovaResult>();
            var validSnValues = snValuesPerOARun.Where(sn => !double.IsNaN(sn)).ToArray();
            if (!validSnValues.Any()) {
                Logger.Warning("ANALYSIS", "No valid S/N values to perform ANOVA. All S/N values were NaN.");
                analysisWarnings.Add("No valid S/N values for ANOVA; all S/N values were NaN.");
                return CreateDefaultAnovaTable(snValuesPerOARun.Length - 1, analysisWarnings);
            }

            double grandMeanSN = validSnValues.Average();
            double totalSumOfSquares = validSnValues.Sum(sn => Math.Pow(sn - grandMeanSN, 2));
            int totalDf = validSnValues.Length - 1;

            if (Math.Abs(totalSumOfSquares) < 1e-9 && validSnValues.Length > 1) {
                Logger.Warning("ANALYSIS", "Total Sum of Squares of S/N ratios is near zero ({TotalSS:E6}). ANOVA results will not be meaningful.", totalSumOfSquares);
                analysisWarnings.Add($"Total Sum of Squares (TSS) of S/N ratios is near zero ({totalSumOfSquares:E2}). ANOVA results may not be meaningful. This often occurs when all responses are very similar.");
                return CreateDefaultAnovaTable(totalDf, analysisWarnings);
            }
            if (totalDf <= 0) {
                Logger.Warning("ANALYSIS", "Total degrees of freedom is {TotalDF}. Not enough data for ANOVA.", totalDf);
                analysisWarnings.Add($"Total degrees of freedom is {totalDf}, insufficient for ANOVA.");
                return CreateDefaultAnovaTable(totalDf, analysisWarnings);
            }

            double sumOfSquaresAccounted = 0;
            int dfAccounted = 0;

            CalculateMainEffectAnovaEntries(anovaTable, snValuesPerOARun, grandMeanSN, ref sumOfSquaresAccounted, ref dfAccounted);
            CalculateInteractionAnovaEntries(anovaTable, snValuesPerOARun, grandMeanSN, ref sumOfSquaresAccounted, ref dfAccounted);
            AddErrorTermToAnovaTable(anovaTable, totalSumOfSquares, sumOfSquaresAccounted, totalDf, dfAccounted, analysisWarnings);
            CalculateStatisticalSignificance(anovaTable, totalSumOfSquares, analysisWarnings);

            return anovaTable;
        }

        private List<AnovaResult> CreateDefaultAnovaTable(int totalDf, List<string> analysisWarnings) {
            var defaultTable = new List<AnovaResult>();
            analysisWarnings.Add("ANOVA table created with default (zero/NaN) values due to insufficient data or zero variance.");
            foreach (var parameter in _controlFactors) {
                defaultTable.Add(new AnovaResult {
                    Source = parameter.Name,
                    ContributionPercentage = 0,
                    DegreesOfFreedom = parameter.Levels.Count - 1,
                    SumOfSquares = 0, MeanSquare = 0, FValue = double.NaN, PValue = double.NaN, IsSignificant = false
                });
            }

            foreach (var interaction in _interactionsToAnalyze) {
                string interactionKey = GetInteractionKey(interaction.FirstParameterName, interaction.SecondParameterName);
                var p1 = _controlFactors.First(p => p.Name == interaction.FirstParameterName);
                var p2 = _controlFactors.First(p => p.Name == interaction.SecondParameterName);
                defaultTable.Add(new AnovaResult {
                    Source = interactionKey, ContributionPercentage = 0,
                    DegreesOfFreedom = (p1.Levels.Count - 1) * (p2.Levels.Count - 1),
                    SumOfSquares = 0, MeanSquare = 0, FValue = double.NaN, PValue = double.NaN, IsSignificant = false
                });
            }

            int currentDf = defaultTable.Sum(a => a.DegreesOfFreedom);
            defaultTable.Add(new AnovaResult {
                Source = AnovaResult.ErrorSource, ContributionPercentage = 0,
                DegreesOfFreedom = Math.Max(0, totalDf - currentDf),
                SumOfSquares = 0, MeanSquare = 0, FValue = double.NaN, PValue = double.NaN, IsSignificant = false
            });
            return defaultTable;
        }

        private void CalculateMainEffectAnovaEntries(
            List<AnovaResult> anovaTable,
            double[] snValuesPerOARun,
            double grandMeanSN,
            ref double sumOfSquaresAccounted,
            ref int dfAccounted
        ) {
            int numRunsOA = _orthogonalArray.GetLength(0);
            var validSnValuesForCF = snValuesPerOARun.Where(sn => !double.IsNaN(sn)).ToArray();
            if (!validSnValuesForCF.Any()) { return; }

            double correctionFactor = Math.Pow(validSnValuesForCF.Sum(), 2) / validSnValuesForCF.Length;

            foreach (var parameter in _controlFactors) {
                if (!_columnAssignments.TryGetValue(parameter.Name, out int colIdx0Based)) {
                    continue;
                }

                var levelData = new Dictionary<OALevel, List<double>>();
                for (int run = 0; run < numRunsOA; run++) {
                    if (double.IsNaN(snValuesPerOARun[run])) { continue; }

                    OALevel oaLevelFromCell = _orthogonalArray[run, colIdx0Based];
                    if (!levelData.ContainsKey(oaLevelFromCell)) {
                        levelData[oaLevelFromCell] = new List<double>();
                    }
                    levelData[oaLevelFromCell].Add(snValuesPerOARun[run]);
                }

                double ssFactor = levelData.Values.Where(list => list.Any())
                                          .Sum(list => Math.Pow(list.Sum(), 2) / list.Count)
                               - correctionFactor;

                int dfFactor = parameter.Levels.Count - 1;
                if (dfFactor <= 0) {
                    continue;
                }
                ssFactor = Math.Max(0, ssFactor);

                anovaTable.Add(new AnovaResult {
                    Source = parameter.Name,
                    SumOfSquares = ssFactor,
                    DegreesOfFreedom = dfFactor,
                    MeanSquare = (dfFactor > 0) ? ssFactor / dfFactor : 0
                });

                sumOfSquaresAccounted += ssFactor;
                dfAccounted += dfFactor;
            }
        }

        private void CalculateInteractionAnovaEntries(
            List<AnovaResult> anovaTable,
            double[] snValuesPerOARun,
            double grandMeanSN,
            ref double sumOfSquaresAccounted,
            ref int dfAccounted
        ) {
            int numRunsOA = _orthogonalArray.GetLength(0);
            var validSnValuesForCF = snValuesPerOARun.Where(sn => !double.IsNaN(sn)).ToArray();
            if (!validSnValuesForCF.Any()) { return; }

            double correctionFactor = Math.Pow(validSnValuesForCF.Sum(), 2) / validSnValuesForCF.Length;

            foreach (var interaction in _interactionsToAnalyze) {
                string interactionKey = GetInteractionKey(interaction.FirstParameterName, interaction.SecondParameterName);

                var p1Factor = _controlFactors.FirstOrDefault(p => p.Name == interaction.FirstParameterName);
                var p2Factor = _controlFactors.FirstOrDefault(p => p.Name == interaction.SecondParameterName);

                if (p1Factor == null || p2Factor == null) {
                    Logger.Error("ANALYSIS", "Definition for main factors '{P1}' or '{P2}' of interaction '{Key}' not found. Skipping interaction in ANOVA.",
                        interaction.FirstParameterName, interaction.SecondParameterName, interactionKey);
                    continue;
                }

                if (!_columnAssignments.TryGetValue(p1Factor.Name, out int p1AssignedCol0Based) ||
                    !_columnAssignments.TryGetValue(p2Factor.Name, out int p2AssignedCol0Based)) {
                    Logger.Warning("ANALYSIS", "Main factors '{P1}' or '{P2}' for interaction '{Key}' not found in column assignments. Skipping interaction in ANOVA.",
                        p1Factor.Name, p2Factor.Name, interactionKey);
                    continue;
                }

                int expectedInteractionDF = (p1Factor.Levels.Count - 1) * (p2Factor.Levels.Count - 1);
                if (expectedInteractionDF == 0) {
                    continue;
                }

                LinearGraph currentRunLinearGraph = _arrayInfo.LinearGraph;
                List<int> interactionOaColumns0BasedToUse = GetInteractionColumns(
                    currentRunLinearGraph,
                    interactionKey,
                    p1AssignedCol0Based,
                    p2AssignedCol0Based,
                    p1Factor,
                    p2Factor
                );

                if (!interactionOaColumns0BasedToUse.Any()) {
                    continue;
                }

                double ssInteractionTotal = 0;
                foreach (int interactionCol0Based in interactionOaColumns0BasedToUse.Distinct()) {
                    if (interactionCol0Based < 0 || interactionCol0Based >= _orthogonalArray.GetLength(1)) {
                        Logger.Error("ANALYSIS", "Interaction {Key} component column index {ColIdx} is out of bounds for the OA. Skipping this component.", interactionKey, interactionCol0Based);
                        continue;
                    }

                    var levelDataInteractionCol = new Dictionary<OALevel, List<double>>();
                    for (int run = 0; run < numRunsOA; run++) {
                        if (double.IsNaN(snValuesPerOARun[run])) { continue; }

                        OALevel oaLevel = _orthogonalArray[run, interactionCol0Based];
                        if (!levelDataInteractionCol.ContainsKey(oaLevel)) {
                            levelDataInteractionCol[oaLevel] = new List<double>();
                        }
                        levelDataInteractionCol[oaLevel].Add(snValuesPerOARun[run]);
                    }
                    double ssComponent = levelDataInteractionCol.Values.Where(list => list.Any())
                                                 .Sum(list => Math.Pow(list.Sum(), 2) / list.Count)
                                          - correctionFactor;
                    ssInteractionTotal += ssComponent;
                }
                ssInteractionTotal = Math.Max(0, ssInteractionTotal);

                int dfProvidedByFoundColumns = 0;
                if (p1Factor.Levels.Count == 2 && p2Factor.Levels.Count == 2) {
                    dfProvidedByFoundColumns = interactionOaColumns0BasedToUse.Count * (2 - 1);
                } else if (p1Factor.Levels.Count == 3 && p2Factor.Levels.Count == 3) {
                    dfProvidedByFoundColumns = interactionOaColumns0BasedToUse.Count * (3 - 1);
                } else {
                    dfProvidedByFoundColumns = expectedInteractionDF;
                }

                if (dfProvidedByFoundColumns != expectedInteractionDF && interactionOaColumns0BasedToUse.Any()) {
                    Logger.Warning("ANALYSIS", "Interaction {Key}: Expected DF={ExpectedDF} but found columns providing {ProvidedDF} DF. ANOVA results might be based on partial interaction effect.",
                       interactionKey, expectedInteractionDF, dfProvidedByFoundColumns);
                }

                if (expectedInteractionDF > 0) {
                    anovaTable.Add(new AnovaResult {
                        Source = interactionKey,
                        SumOfSquares = ssInteractionTotal,
                        DegreesOfFreedom = expectedInteractionDF,
                        MeanSquare = ssInteractionTotal / expectedInteractionDF
                    });
                    sumOfSquaresAccounted += ssInteractionTotal;
                    dfAccounted += expectedInteractionDF;
                }
            }
        }

        private List<int> GetInteractionColumns(
            LinearGraph linearGraph,
            string interactionKey,
            int p1AssignedCol0Based,
            int p2AssignedCol0Based,
            IFactorDefinition factor1Def,
            IFactorDefinition factor2Def
        ) {
            var interactionOaColumns0BasedToUse = new List<int>();

            if (linearGraph != null) {
                var lgInteraction = linearGraph.Interactions.FirstOrDefault(lg_i =>
                    (lg_i.Column1 == p1AssignedCol0Based + 1 && lg_i.Column2 == p2AssignedCol0Based + 1) ||
                    (lg_i.Column1 == p2AssignedCol0Based + 1 && lg_i.Column2 == p1AssignedCol0Based + 1));

                if (lgInteraction != null && lgInteraction.InteractionColumns.Any()) {
                    interactionOaColumns0BasedToUse.AddRange(lgInteraction.InteractionColumns.Select(c => c - 1));
                    Logger.Debug("ANALYSIS", "Interaction {Key} found {Count} column(s) via Linear Graph: [{Cols}]",
                        interactionKey, interactionOaColumns0BasedToUse.Count, string.Join(",", interactionOaColumns0BasedToUse.Select(c => c + 1)));
                    return interactionOaColumns0BasedToUse.Distinct().ToList();
                }
            }

            if (_columnAssignments.TryGetValue(interactionKey, out int primaryComponentCol0Based)) {
                interactionOaColumns0BasedToUse.Add(primaryComponentCol0Based);
                Logger.Debug("ANALYSIS", "Interaction {Key} primary component found via direct assignment to OA col {Col}",
                    interactionKey, primaryComponentCol0Based + 1);

                if (factor1Def.Levels.Count == 3 && factor2Def.Levels.Count == 3) {
                    string secondaryComponentKey = interactionKey + "_comp2";
                    if (_columnAssignments.TryGetValue(secondaryComponentKey, out int secondaryComponentCol0Based)) {
                        if (!interactionOaColumns0BasedToUse.Contains(secondaryComponentCol0Based)) {
                            interactionOaColumns0BasedToUse.Add(secondaryComponentCol0Based);
                            Logger.Debug("ANALYSIS", "Interaction {Key} secondary component found via direct assignment to OA col {Col}",
                                interactionKey, secondaryComponentCol0Based + 1);
                        }
                    } else {
                        Logger.Warning("ANALYSIS", "Interaction {Key} is 3-level x 3-level and primary component assigned to col {C1}, but secondary component ('{Key2}') not found in assignments. ANOVA for this interaction might be incomplete.",
                            interactionKey, primaryComponentCol0Based + 1, secondaryComponentKey);
                    }
                }
            }

            if (!interactionOaColumns0BasedToUse.Any()) {
                Logger.Warning("ANALYSIS", "No OA column(s) could be determined for interaction {InteractionKey} (factors in cols {C1}, {C2}). It will be skipped in ANOVA.",
                   interactionKey, p1AssignedCol0Based + 1, p2AssignedCol0Based + 1);
            }
            return interactionOaColumns0BasedToUse.Distinct().ToList();
        }

        private void AddErrorTermToAnovaTable(
            List<AnovaResult> anovaTable,
            double totalSumOfSquares,
            double sumOfSquaresAccounted,
            int totalDf,
            int dfAccounted,
            List<string> analysisWarnings
        ) {
            double errorSumOfSquares = totalSumOfSquares - sumOfSquaresAccounted;
            int errorDf = totalDf - dfAccounted;

            if (errorSumOfSquares < 0 && Math.Abs(errorSumOfSquares) < 1e-9) {
                Logger.Debug("ANALYSIS", "Negative error SS ({Value:E6}) is nearly zero due to rounding, setting to zero.", errorSumOfSquares);
                errorSumOfSquares = 0;
            }

            Logger.Debug("ANALYSIS", "Error term: SS={ErrorSS:F6}, DF={ErrorDF}, MSE={MSE:F6} (Total SS={TotalSS:F6}, Accounted SS={AccountedSS:F6})",
                errorSumOfSquares, errorDf, errorDf > 0 ? errorSumOfSquares / errorDf : double.NaN, totalSumOfSquares, sumOfSquaresAccounted);


            if (errorSumOfSquares < 0) {
                HandleNegativeErrorSumOfSquares(anovaTable, errorSumOfSquares, errorDf, analysisWarnings);
                return;
            }
            if (errorDf <= 0) {
                Logger.Warning("ANALYSIS", "Error degrees of freedom is {ErrorDF}. F-values and P-values may not be calculable or reliable. This often happens in saturated designs.", errorDf);
                analysisWarnings.Add($"Saturated Design or Model: Error Degrees of Freedom is {errorDf}. F-values and P-values may not be calculable or reliable. Significance tests will be impacted.");
            }

            double errorMeanSquare = (errorDf > 0) ? errorSumOfSquares / errorDf : double.NaN;
            double errorContribution = (totalSumOfSquares > 1e-9) ? (errorSumOfSquares / totalSumOfSquares) * 100 : (errorSumOfSquares == 0 ? 0 : 100);

            if (errorContribution > 50 && errorDf > 0) {
                Logger.Warning("ANALYSIS", "Error term accounts for {ErrorPercent:F1}% of total variation. This may indicate important factors or interactions are missing from the analysis, or there is high variability.",
                    errorContribution);
                analysisWarnings.Add($"High Error Contribution: Error term accounts for {errorContribution:F1}% of total variation. This may indicate missing factors/interactions or high variability.");
            } else if (errorContribution < 5 && errorDf > 0) {
                Logger.Debug("ANALYSIS", "Error term accounts for only {ErrorPercent:F1}% of variation, indicating strong factor effects or potential overfitting.",
                    errorContribution);
            }

            anovaTable.Add(new AnovaResult {
                Source = AnovaResult.ErrorSource,
                SumOfSquares = errorSumOfSquares,
                DegreesOfFreedom = errorDf,
                MeanSquare = errorMeanSquare,
                FValue = double.NaN,
                PValue = double.NaN,
                IsSignificant = false,
                ContributionPercentage = errorContribution
            });
        }

        private void HandleNegativeErrorSumOfSquares(
           List<AnovaResult> anovaTable,
           double errorSumOfSquares,
           int errorDf,
           List<string> analysisWarnings
        ) {
            Logger.Warning("ANALYSIS", "Negative Error Sum of Squares ({ErrorSS:F6}). This indicates a problem with SS calculations or model fit.", errorSumOfSquares);
            analysisWarnings.Add($"Negative Error Sum of Squares ({errorSumOfSquares:F2}). This indicates a problem with SS calculations or model fit. ANOVA results are unreliable.");
            double totalSS = anovaTable.Sum(a => a.SumOfSquares) + errorSumOfSquares;
            double factorSSSum = anovaTable.Sum(a => a.SumOfSquares);
            double errorPercentage = (Math.Abs(factorSSSum) > 1e-9) ? (Math.Abs(errorSumOfSquares) / Math.Abs(factorSSSum)) * 100 : 0;

            Logger.Debug("ANALYSIS", "SS diagnostic: Factor SS Sum={FactorSum:F6}, Total SS with Error={TotalSS:F6}, Error/Factor ratio={ErrorRatio:F2}%",
                factorSSSum, totalSS, errorPercentage);

            if (errorPercentage < 1.0) {
                Logger.Debug("ANALYSIS", "Error is less than 1% of factor effects, likely due to rounding errors. Consider treating as zero.");
            } else if (errorPercentage < 5.0) {
                Logger.Debug("ANALYSIS", "Error is between 1-5% of factor effects. May be due to numerical precision issues in the calculations.");
            } else {
                Logger.Debug("ANALYSIS", "Error is significant (>5% of factor effects). Possible causes: model misspecification, measurement error, or invalid assumptions.");
                var largestFactors = anovaTable.OrderByDescending(a => a.SumOfSquares).Take(3).ToList();
                if (largestFactors.Any()) {
                    Logger.Debug("ANALYSIS", "Largest factor contributions: {Factors}",
                        string.Join(", ", largestFactors.Select(f => $"{f.Source}={f.SumOfSquares:F6}")));
                }
            }

            foreach (var entry in anovaTable) {
                entry.ContributionPercentage = double.NaN;
                entry.FValue = double.NaN;
                entry.PValue = double.NaN;
                entry.IsSignificant = false;
            }

            var errorEntryExisting = anovaTable.FirstOrDefault(a => a.Source == AnovaResult.ErrorSource);
            if (errorEntryExisting == null) {
                anovaTable.Add(new AnovaResult {
                    Source = AnovaResult.ErrorSource,
                    SumOfSquares = errorSumOfSquares,
                    DegreesOfFreedom = errorDf,
                    MeanSquare = double.NaN,
                    FValue = double.NaN,
                    PValue = double.NaN,
                    IsSignificant = false,
                    ContributionPercentage = double.NaN
                });
            } else {
                errorEntryExisting.SumOfSquares = errorSumOfSquares;
                errorEntryExisting.DegreesOfFreedom = errorDf;
                errorEntryExisting.MeanSquare = double.NaN;
                errorEntryExisting.ContributionPercentage = double.NaN;
            }
        }


        private void CalculateStatisticalSignificance(List<AnovaResult> anovaTable, double totalSumOfSquares, List<string> analysisWarnings) {
            var errorTerm = anovaTable.FirstOrDefault(a => a.Source == AnovaResult.ErrorSource || a.Source == (AnovaResult.ErrorSource + " (Pooled)"));

            if (errorTerm == null || errorTerm.DegreesOfFreedom <= 0 || double.IsNaN(errorTerm.MeanSquare) || errorTerm.MeanSquare < 1e-12) {
                string errorMsg = $"Error term is invalid (DF={errorTerm?.DegreesOfFreedom ?? 0}, MSE={errorTerm?.MeanSquare ?? double.NaN}). F-values and P-values cannot be reliably calculated. This might be due to a saturated design or zero error variance.";
                Logger.Warning("ANALYSIS", errorMsg);
                analysisWarnings.Add(errorMsg);
                HandleZeroErrorDegreesOfFreedom(anovaTable, totalSumOfSquares, analysisWarnings); // Pass warnings
                return;
            }

            double errorMeanSquare = errorTerm.MeanSquare;
            int errorDf = errorTerm.DegreesOfFreedom;

            foreach (var entry in anovaTable.Where(a => a.Source != errorTerm.Source)) {
                CalculateEntryStatistics(entry, errorMeanSquare, errorDf, totalSumOfSquares);
            }
            if (errorTerm.SumOfSquares >= 0 && totalSumOfSquares > 1e-9) {
                errorTerm.ContributionPercentage = (errorTerm.SumOfSquares / totalSumOfSquares) * 100;
            } else if (errorTerm.SumOfSquares < 0) {
                errorTerm.ContributionPercentage = double.NaN;
            } else {
                errorTerm.ContributionPercentage = (totalSumOfSquares > 1e-9) ? 0 : (errorTerm.SumOfSquares == 0 ? 0 : 100);
            }
        }

        private void HandleZeroErrorDegreesOfFreedom(List<AnovaResult> anovaTable, double totalSumOfSquares, List<string> analysisWarnings) {
            Logger.Warning("ANALYSIS", "Error DF is ≤ 0. ANOVA F-values and P-values cannot be calculated. Statistical significance will be unreliable.");
            analysisWarnings.Add("Error DF is ≤ 0. ANOVA F-values and P-values cannot be calculated. Statistical significance is unreliable.");
            int factorCount = anovaTable.Count(a => a.Source != AnovaResult.ErrorSource && a.Source != (AnovaResult.ErrorSource + " (Pooled)"));
            var allFactors = anovaTable.Where(a => a.Source != AnovaResult.ErrorSource && a.Source != (AnovaResult.ErrorSource + " (Pooled)")).ToList();
            int totalAssignedDf = allFactors.Sum(a => a.DegreesOfFreedom);
            double factorSSSum = allFactors.Sum(a => a.SumOfSquares);
            double largestSS = allFactors.Any() ? allFactors.Max(a => a.SumOfSquares) : 0;
            string largestFactor = allFactors.OrderByDescending(a => a.SumOfSquares).FirstOrDefault()?.Source ?? "None";

            Logger.Debug("ANALYSIS", "Zero error DF diagnostic: {FactorCount} factors using total {AssignedDf} DF", factorCount, totalAssignedDf);
            Logger.Debug("ANALYSIS", "Factor contributions: Total SS={TotalSS:F6}, Factor SS={FactorSS:F6}, Largest SS={LargestSS:F6} ({LargestFactor})",
                totalSumOfSquares, factorSSSum, largestSS, largestFactor);

            if (totalSumOfSquares <= factorSSSum + 1e-9 && totalSumOfSquares > 1e-9) {
                Logger.Debug("ANALYSIS", "Saturated design detected: all available degrees of freedom are assigned to factors");
                Logger.Debug("ANALYSIS", "Consider pooling insignificant factors into error term or using a larger experimental design");
                analysisWarnings.Add("Saturated Design: All available degrees of freedom assigned to factors. Consider pooling or a larger design.");
            }

            var smallFactors = allFactors.Where(a => a.SumOfSquares < (totalSumOfSquares * 0.05)).ToList();
            if (smallFactors.Any()) {
                Logger.Debug("ANALYSIS", "Potential pooling candidates (SS < 5% of total): {SmallFactors}",
                    string.Join(", ", smallFactors.Select(f => $"{f.Source}")));
                Logger.Debug("ANALYSIS", "Pooling these would give error term {PooledDf} DF and SS ≈ {PooledSS:F6}",
                    smallFactors.Sum(f => f.DegreesOfFreedom), smallFactors.Sum(f => f.SumOfSquares));
            }
            foreach (var entry in anovaTable) {
                entry.ContributionPercentage = (totalSumOfSquares > 1e-9) ? (entry.SumOfSquares / totalSumOfSquares) * 100 : (entry.SumOfSquares == 0 ? 0 : 100);
                if (entry.Source != AnovaResult.ErrorSource && entry.Source != (AnovaResult.ErrorSource + " (Pooled)")) {
                    entry.FValue = double.NaN;
                    entry.PValue = double.NaN;
                    entry.IsSignificant = false;
                }
            }
        }

        private void CalculateEntryStatistics(AnovaResult entry, double errorMeanSquare, int errorDf, double totalSumOfSquares) {
            if (entry.DegreesOfFreedom <= 0) {
                entry.FValue = double.NaN;
                entry.PValue = double.NaN;
                entry.IsSignificant = false;
                Logger.Debug("ANALYSIS", "Factor {Source} has DF={FactorDF}, cannot calculate F/P values.", entry.Source, entry.DegreesOfFreedom);
            } else if (errorMeanSquare > 1e-12) {
                entry.FValue = entry.MeanSquare / errorMeanSquare;
                try {
                    entry.PValue = 1.0 - FisherSnedecor.CDF(entry.DegreesOfFreedom, errorDf, entry.FValue);
                    if (double.IsNaN(entry.PValue)) {
                        Logger.Warning("ANALYSIS", "P-value calculation for {Source} resulted in NaN. F={F:F4}, DF1={DF1}, DF2={DF2}. This can happen with very large F-values.",
                               entry.Source ?? "Unknown", entry.FValue, entry.DegreesOfFreedom, errorDf);
                        if (entry.FValue > 1e10) {
                            entry.PValue = 0.0;
                        } else if (entry.FValue < 1e-10 && entry.FValue >= 0) {
                            entry.PValue = 1.0;
                        }
                    }
                } catch (Exception ex) {
                    Logger.Exception("ANALYSIS", ex, "Error calculating P-value for {Source}. DF1={DF1}, DF2={DF2}, F={F:F4}.",
                        entry.Source ?? "Unknown", entry.DegreesOfFreedom, errorDf, entry.FValue);
                    entry.PValue = double.NaN;
                }
            } else {
                if (entry.MeanSquare > 1e-12) {
                    entry.FValue = double.PositiveInfinity;
                    entry.PValue = 0.0;
                } else {
                    entry.FValue = double.NaN;
                    entry.PValue = double.NaN;
                }
                Logger.Debug("ANALYSIS", "Factor {Source} F-value calculation with near-zero error MS (FactorMS={FactorMS:E6}, ErrorMS={ErrorMS:E6}). F={F}, P={P}",
                    entry.Source ?? "Unknown", entry.MeanSquare, errorMeanSquare, entry.FValue, entry.PValue);
            }

            entry.IsSignificant = !double.IsNaN(entry.PValue) && entry.PValue < DefaultAnovaAlpha;
            entry.ContributionPercentage = (totalSumOfSquares > 1e-9) ? (entry.SumOfSquares / totalSumOfSquares) * 100 : (entry.SumOfSquares == 0 ? 0 : 100);
        }

        private PredictionResult PredictPerformanceWithConfidence(
            OptimalConfiguration optimalConfiguration,
            IReadOnlyList<ExperimentRunResultForAnalysis> resultsWithSnAndRaw, // Contains S/N and raw values for the single metric
            IReadOnlyList<AnovaResult> anovaTable,
            SignalToNoiseType snType,
            string metricName
        ) {
            var predictionResult = new PredictionResult();
            var allRawMetricValuesFromExperiment = resultsWithSnAndRaw
                .SelectMany(r => r.MetricValues) // Correctly uses MetricValues from ExperimentRunResultForAnalysis
                .Where(v => !double.IsNaN(v))
                .ToList();

            if (!allRawMetricValuesFromExperiment.Any()) {
                Logger.Error("ANALYZER_PREDICT", "No valid raw metric values ({MetricName}) to base prediction on.", metricName);
                predictionResult.PredictionNotes.Add($"No valid raw metric values for {metricName} to base prediction on.");
                predictionResult.PredictedValue = double.NaN;
                predictionResult.LowerBound = double.NaN;
                predictionResult.UpperBound = double.NaN;
                predictionResult.IsSnScale = false; // Not on S/N scale as no data
                return predictionResult;
            }
            if (optimalConfiguration == null || !optimalConfiguration.Settings.Any()) {
                Logger.Error("ANALYZER_PREDICT", "Optimal configuration is empty for {MetricName}.", metricName);
                // This should ideally be an exception as it's a logical error in the calling flow.
                throw new ArgumentException($"Optimal configuration is empty for {metricName}.", nameof(optimalConfiguration));
            }

            Logger.Info("ANALYZER_PREDICT", "Predicting performance for {MetricName} with CI based on optimal config.", metricName);
            TimingUtilities.StartTimer($"prediction_{metricName}");

            if (anovaTable == null || !anovaTable.Any() || anovaTable.All(a => double.IsNaN(a.FValue))) {
                string anovaWarning = $"ANOVA table for {metricName} is empty, null, or contains no valid F-values. Prediction confidence will be less reliable. Falling back to average of runs matching optimal (if any), else overall average.";
                Logger.Warning("ANALYZER_PREDICT", anovaWarning);
                predictionResult.PredictionNotes.Add(anovaWarning);

                // Fallback: Try to find runs that exactly match the optimal configuration
                var matchingRunsRawValues = resultsWithSnAndRaw
                    .Where(r => r.Configuration.Settings.Count == optimalConfiguration.Settings.Count &&
                                r.Configuration.Settings.All(kvp =>
                                    optimalConfiguration.Settings.TryGetValue(kvp.Key, out var optLevel) &&
                                    Equals(kvp.Value, optLevel)))
                    .SelectMany(r => r.MetricValues) // Get raw values for the current metric from these matching runs
                    .Where(v => !double.IsNaN(v))
                    .ToList();

                double predictedValue;
                if (matchingRunsRawValues.Any()) {
                    predictedValue = matchingRunsRawValues.Average();
                    Logger.Debug("ANALYZER_PREDICT", "Prediction fallback for {MetricName}: Using average of {Count} raw values from {MatchingRunsCount} run(s) matching optimal configuration.",
                                 metricName, matchingRunsRawValues.Count,
                                 resultsWithSnAndRaw.Count(r => r.Configuration.Settings.Count == optimalConfiguration.Settings.Count &&
                                                               r.Configuration.Settings.All(kvp => optimalConfiguration.Settings.TryGetValue(kvp.Key, out var ol) && Equals(kvp.Value, ol))));
                } else {
                    predictedValue = allRawMetricValuesFromExperiment.Average();
                    Logger.Debug("ANALYZER_PREDICT", "Prediction fallback for {MetricName}: No exact match for optimal config. Using overall average of all raw values for this metric.", metricName);
                }

                TimeSpan predTimeFallback = TimingUtilities.StopTimer($"prediction_{metricName}");
                Logger.Debug("ANALYZER_PREDICT", "Prediction for {MetricName} (fallback due to ANOVA issues) completed in {Duration}.", metricName, TimingUtilities.FormatElapsedTime(predTimeFallback));
                predictionResult.PredictedValue = predictedValue;
                predictionResult.LowerBound = predictedValue; // No CI in this fallback
                predictionResult.UpperBound = predictedValue;
                predictionResult.IsSnScale = false; // Result is on original scale, CI not S/N based
                return predictionResult;
            }

            var validSnResults = resultsWithSnAndRaw.Where(r => !double.IsNaN(r.SnRatioValue)).ToList();
            if (!validSnResults.Any()) {
                Logger.Warning("ANALYZER_PREDICT", "No valid S/N results for {MetricName} to predict performance. Prediction will be NaN.", metricName);
                predictionResult.PredictionNotes.Add($"No valid S/N results for {metricName} for Taguchi prediction method.");
                predictionResult.PredictedValue = double.NaN;
                predictionResult.LowerBound = double.NaN;
                predictionResult.UpperBound = double.NaN;
                predictionResult.IsSnScale = true; // S/N method was attempted
                TimingUtilities.StopTimer($"prediction_{metricName}");
                return predictionResult;
            }

            double overallMeanSN = validSnResults.Average(r => r.SnRatioValue);
            double predictedSN = overallMeanSN;
            int dfForNeffCalculation = 1; // Start with 1 for the grand mean

            foreach (var factor in _controlFactors) {
                var anovaEntry = anovaTable.FirstOrDefault(a => a.Source.Equals(factor.Name, StringComparison.OrdinalIgnoreCase));
                if (optimalConfiguration.Settings.TryGetValue(factor.Name, out Level optLevelForFactor) &&
                    anovaEntry != null && anovaEntry.IsSignificant) {
                    double avgSnForOptimalLevel = validSnResults
                        .Where(rws => rws.Configuration.Settings.TryGetValue(factor.Name, out var runLevel) &&
                                      Equals(runLevel, optLevelForFactor))
                        .Select(rws => rws.SnRatioValue)
                        .DefaultIfEmpty(overallMeanSN)
                        .Average();
                    predictedSN += (avgSnForOptimalLevel - overallMeanSN);
                    dfForNeffCalculation += anovaEntry.DegreesOfFreedom;
                }
            }

            foreach (var interaction in _interactionsToAnalyze) {
                string interactionKey = GetInteractionKey(interaction.FirstParameterName, interaction.SecondParameterName);
                var anovaEntry = anovaTable.FirstOrDefault(a => a.Source.Equals(interactionKey, StringComparison.OrdinalIgnoreCase));
                if (anovaEntry != null && anovaEntry.IsSignificant) {
                    // The optimal configuration already reflects choices made considering interactions.
                    // The main effect deviations calculated above use these optimal levels.
                    // For predicting the S/N value, the sum of main effect deviations (from overall mean)
                    // at the chosen optimal levels is the standard Taguchi method.
                    // The interaction's significance contributes to dfForNeffCalculation, affecting CI.
                    dfForNeffCalculation += anovaEntry.DegreesOfFreedom;
                }
            }
            
            var errorAnova = anovaTable.FirstOrDefault(a => a.Source == AnovaResult.ErrorSource || a.Source == (AnovaResult.ErrorSource + " (Pooled)"));
            if (errorAnova == null || errorAnova.DegreesOfFreedom <= 0 || double.IsNaN(errorAnova.MeanSquare) || errorAnova.MeanSquare < 1e-12) {
                string ciWarning = $"Valid error term not found or MSE is effectively zero in ANOVA for {metricName}. Confidence interval cannot be calculated reliably.";
                Logger.Warning("ANALYZER_PREDICT", ciWarning);
                predictionResult.PredictionNotes.Add(ciWarning);
                double predictedOriginalFallback = InverseSN(predictedSN, allRawMetricValuesFromExperiment, snType);
                predictionResult.PredictedValue = predictedOriginalFallback;
                predictionResult.LowerBound = predictedOriginalFallback;
                predictionResult.UpperBound = predictedOriginalFallback;
                predictionResult.IsSnScale = false; // Original scale, but CI is unreliable
                if (Math.Abs(predictedOriginalFallback - predictionResult.LowerBound) < 1e-6 && Math.Abs(predictedOriginalFallback - predictionResult.UpperBound) < 1e-6) {
                    predictionResult.PredictionNotes.Add("Confidence interval is zero-width or very narrow due to low error variance. True uncertainty may be larger.");
                }
                TimingUtilities.StopTimer($"prediction_{metricName}");
                return predictionResult;
            }

            double meanSquareError = errorAnova.MeanSquare;
            int dfError = errorAnova.DegreesOfFreedom;

            if (_orthogonalArray == null) {
                Logger.Error("ANALYZER_PREDICT", "Orthogonal array is null, cannot calculate nEff for prediction CI for {MetricName}.", metricName);
                predictionResult.PredictionNotes.Add($"Orthogonal array data is missing; cannot calculate nEff for CI for {metricName}.");
                double predOriginalError = InverseSN(predictedSN, allRawMetricValuesFromExperiment, snType);
                predictionResult.PredictedValue = predOriginalError;
                predictionResult.LowerBound = predOriginalError;
                predictionResult.UpperBound = predOriginalError;
                predictionResult.IsSnScale = false;
                TimingUtilities.StopTimer($"prediction_{metricName}");
                return predictionResult;
            }
            // Effective number of replications for the prediction
            double nEff = (double)_orthogonalArray.GetLength(0) / Math.Max(1, dfForNeffCalculation);

            double tValue;
            try {
                if (dfError <= 0) throw new ArgumentOutOfRangeException(nameof(dfError), "Degrees of freedom for error term must be positive to calculate t-value.");
                tValue = StudentT.InvCDF(0, 1, dfError, 1.0 - (_confidenceLevelAlpha / 2.0));
            } catch (Exception ex) {
                Logger.Exception("ANALYZER_PREDICT", ex, "Failed to calculate t-value for CI for {MetricName}. DF Error: {DFError}, Alpha: {Alpha}.", metricName, dfError, _confidenceLevelAlpha);
                predictionResult.PredictionNotes.Add($"Failed to calculate t-value for CI (DF_error={dfError}). CI unreliable.");
                double predOriginalTError = InverseSN(predictedSN, allRawMetricValuesFromExperiment, snType);
                predictionResult.PredictedValue = predOriginalTError;
                predictionResult.LowerBound = predOriginalTError;
                predictionResult.UpperBound = predOriginalTError;
                predictionResult.IsSnScale = false;
                TimingUtilities.StopTimer($"prediction_{metricName}");
                return predictionResult;
            }

            double marginOfErrorSN = tValue * Math.Sqrt(meanSquareError / nEff);

            double predictedOriginalScale = InverseSN(predictedSN, allRawMetricValuesFromExperiment, snType);
            double lowerBoundOriginalScale = InverseSN(predictedSN - marginOfErrorSN, allRawMetricValuesFromExperiment, snType);
            double upperBoundOriginalScale = InverseSN(predictedSN + marginOfErrorSN, allRawMetricValuesFromExperiment, snType);

            // Ensure bounds are logical (lower < upper for LTB, upper < lower for STB after inversion)
            if (snType is SignalToNoiseType.SmallerIsBetterType) {
                // For STB, a higher S/N (predictedSN + marginOfErrorSN) means a smaller (better) metric value.
                // So, upperBoundOriginalScale (from higher S/N) should be less than lowerBoundOriginalScale (from lower S/N).
                if (upperBoundOriginalScale > lowerBoundOriginalScale) {
                    (lowerBoundOriginalScale, upperBoundOriginalScale) = (upperBoundOriginalScale, lowerBoundOriginalScale);
                }
            } else { // LTB or Nominal (where higher S/N is better for the S/N value itself)
                if (lowerBoundOriginalScale > upperBoundOriginalScale) {
                    (lowerBoundOriginalScale, upperBoundOriginalScale) = (upperBoundOriginalScale, lowerBoundOriginalScale);
                }
            }

            TimeSpan predictionTime = TimingUtilities.StopTimer($"prediction_{metricName}");
            Logger.Info("ANALYZER_PREDICT", "Predicted performance for {MetricName}: {Value:F4} [{Lower:F4}, {Upper:F4}] (Original Scale). S/N: {SN_Pred:F4} [{SN_Lower:F4}, {SN_Upper:F4}]",
                metricName, predictedOriginalScale, lowerBoundOriginalScale, upperBoundOriginalScale,
                predictedSN, predictedSN - marginOfErrorSN, predictedSN + marginOfErrorSN);
            
            predictionResult.PredictedValue = predictedOriginalScale;
            predictionResult.LowerBound = lowerBoundOriginalScale;
            predictionResult.UpperBound = upperBoundOriginalScale;
            predictionResult.IsSnScale = false; // Final result is on original scale

            if (Math.Abs(predictedOriginalScale - lowerBoundOriginalScale) < 1e-6 && Math.Abs(predictedOriginalScale - upperBoundOriginalScale) < 1e-6 && meanSquareError > 1e-9) {
                predictionResult.PredictionNotes.Add("Confidence interval is very narrow. This might be due to a very small nEff denominator or other ANOVA characteristics. Review dfForNeffCalculation and MSE.");
            } else if (meanSquareError < 1e-9) {
                predictionResult.PredictionNotes.Add("Confidence interval is zero-width or very narrow due to near-zero Mean Square Error (MSE). True uncertainty may be larger.");
            }
            return predictionResult;
        }

        public double InverseSN(double snRatio, IReadOnlyList<double> contextMetricValues, SignalToNoiseType snType) {
            // Logic unchanged from previous version, but contextMetricValues is now specific to the current metric.
            if (double.IsNaN(snRatio)) { return double.NaN; }

            if (!contextMetricValues.Any() && snType is not SignalToNoiseType.NominalIsBestType) {
                Logger.Warning("ANALYZER_INVSN", "InverseSN called with no context metric values for LTB/STB. Result may be speculative or less accurate in scale.");
            }

            double rawPredictedValue;
            switch (snType) {
                case SignalToNoiseType.LargerIsBetterType:
                    // SN = 20 * log10(y_pred) => y_pred = 10^(SN/20)
                    if (double.IsNegativeInfinity(snRatio) || snRatio < -199) { // Heuristic for very poor S/N
                        rawPredictedValue = 0;
                    } else if (double.IsPositiveInfinity(snRatio) || snRatio > 199) { // Heuristic for very good S/N
                        // This depends on the scale of original metric. For now, let it be large.
                        // If contextScores available, could try to scale relative to max(contextScores)
                        rawPredictedValue = double.MaxValue / 2; // Avoid actual infinity
                    } else {
                        rawPredictedValue = Math.Pow(10, snRatio / 20.0);
                    }
                    break;
                case SignalToNoiseType.SmallerIsBetterType:
                    // SN = -20 * log10(y_pred) => y_pred = 10^(-SN/20)
                    if (double.IsPositiveInfinity(snRatio) || snRatio > 199) { // Heuristic for very good S/N (small value)
                        rawPredictedValue = 0;
                    } else if (double.IsNegativeInfinity(snRatio) || snRatio < -199) { // Heuristic for very poor S/N (large value)
                        rawPredictedValue = double.MaxValue / 2;
                    } else {
                        rawPredictedValue = Math.Pow(10, -snRatio / 20.0);
                    }
                    break;
                case SignalToNoiseType.NominalIsBestType nominal:
                    // For Nominal is Best, the S/N ratio itself doesn't directly invert to a single y value
                    // without knowing the variance. The prediction is usually the target itself.
                    // If this InverseSN is used for prediction, it implies we are predicting the target.
                    rawPredictedValue = nominal.Target;
                    break;
                default:
                    Logger.Error("ANALYSIS", "Unsupported S/N type in InverseSN: {SnType}", snType);
                    throw new ArgumentOutOfRangeException(nameof(snType), "Unsupported S/N type for inversion.");
            }

            return rawPredictedValue;
        }

        private string GetInteractionKey(string param1, string param2) {
            return string.Compare(param1, param2, StringComparison.OrdinalIgnoreCase) < 0
                ? $"{param1}*{param2}"
                : $"{param2}*{param1}";
        }
    }
}