// TaguchiBench.Engine/Core/TaguchiBenchmarkEngine.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TaguchiBench.Common; // For Logger, TimingUtilities
using TaguchiBench.Engine.Configuration;
using TaguchiBench.Engine.Interfaces;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions; // For ITargetRunner

namespace TaguchiBench.Engine.Core {

    // Temporary record to pass data to ResultAnalyzer for a single metric's analysis
    // This ensures ResultAnalyzer only sees data for the metric it's currently processing.
    public record SingleMetricExperimentRunData(
        ParameterSettings Configuration,
        IReadOnlyList<double> MetricValues // Values for the single metric across repetitions
    );


    public class TaguchiBenchmarkEngine {
        private readonly EngineConfiguration _config;
        private readonly ITargetRunner _targetRunner;
        private ExperimentState _currentExperimentState;

        public TaguchiBenchmarkEngine(EngineConfiguration config, ITargetRunner targetRunner) {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _targetRunner = targetRunner ?? throw new ArgumentNullException(nameof(targetRunner));
        }

        public async Task<ExperimentState> RunFullExperimentAsync() {
            InitializeNewExperimentState();
            return await ExecuteExperimentInternalAsync();
        }

        public async Task<ExperimentState> RecoverExperimentAsync(ExperimentState recoveredState) {
            ArgumentNullException.ThrowIfNull(recoveredState, nameof(recoveredState));
            _currentExperimentState = recoveredState;

            string currentConfigHash = _config.CalculateConfigHash();
            if (_currentExperimentState.OriginalConfigHash != null &&
                _currentExperimentState.OriginalConfigHash != currentConfigHash) {
                Logger.Warning("ENGINE", "Recovered state's original config hash ({RecoveredHash}) " +
                                       "differs from current config hash ({CurrentHash}). Ensure compatibility.",
                                       _currentExperimentState.OriginalConfigHash, currentConfigHash);
            } else if (_currentExperimentState.OriginalConfigHash == null) {
                 Logger.Warning("ENGINE", "Recovered state lacks an original config hash for compatibility check.");
            }

            Logger.Info("ENGINE", "Recovering experiment. Next OA run index: {NextRun}", _currentExperimentState.NextRunIndexToExecute);
            return await ExecuteExperimentInternalAsync();
        }

        private void InitializeNewExperimentState() {
            Logger.Info("ENGINE", "Initializing new experiment state.");
            var factorsForDesign = _config.ControlFactors
                .Select(cf => new FactorToAssignForDesign(cf.Name, cf.Levels.Count))
                .ToList();

            OrthogonalArrayDesign design = OrthogonalArrayFactory.CreateOrthogonalArrayDesign(factorsForDesign, _config.Interactions);
            LogOrthogonalArrayDesign(design);

            _currentExperimentState = new ExperimentState {
                Configuration = _config,
                ArrayDesign = design,
                OriginalConfigHash = _config.CalculateConfigHash(),
                NextRunIndexToExecute = 0,
                RawMetricsPerRun = new Dictionary<int, List<Dictionary<string, double>>>(),
                EngineVersion = GetEngineVersion(),
                AnalysisResults = new List<FullAnalysisReportData>() // Initialize AnalysisResults
            };
            PersistExperimentState("initial_setup");
        }
        
        private static string GetEngineVersion() {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        }

        private async Task<ExperimentState> ExecuteExperimentInternalAsync() {
            List<ParameterSettings> oaConfigurations = GenerateOAConfigurations();

            if (_currentExperimentState.NextRunIndexToExecute < oaConfigurations.Count) {
                Logger.Info("ENGINE", "Executing experimental runs from OA index {StartIndex} to {EndIndex} (inclusive).",
                    _currentExperimentState.NextRunIndexToExecute, oaConfigurations.Count - 1);
                await ExecuteAllExperimentalRunsAsync(oaConfigurations);
            } else {
                Logger.Info("ENGINE", "All experimental runs previously completed. Proceeding to analysis phase.");
            }

            Logger.Info("ENGINE", "All experimental runs are complete. Performing analyses for configured metrics...");
            PerformAllMetricAnalyses(); // This will populate _currentExperimentState.AnalysisResults

            _currentExperimentState.LastUpdated = DateTime.UtcNow;
            PersistExperimentState("experiment_completed");

            return _currentExperimentState;
        }

        private List<ParameterSettings> GenerateOAConfigurations() {
            var parameterDefinitions = _config.ControlFactors
                .ToDictionary(cf => cf.Name, cf => cf.Levels, StringComparer.OrdinalIgnoreCase);

            return OrthogonalArrayFactory.CreateParameterConfigurations(
                _currentExperimentState.ArrayDesign,
                parameterDefinitions
            ).Select(dict => new ParameterSettings(dict)).ToList();
        }

        private async Task ExecuteAllExperimentalRunsAsync(IReadOnlyList<ParameterSettings> oaConfigurations) {
            TimingUtilities.StartTimer("total_experiments_execution");
            int totalOARuns = oaConfigurations.Count;
            int totalTargetExecutions = totalOARuns * _config.Repetitions;

            for (int oaRunIndex = _currentExperimentState.NextRunIndexToExecute; oaRunIndex < totalOARuns; oaRunIndex++) {
                ParameterSettings currentOAConfig = oaConfigurations[oaRunIndex];
                Logger.Info("ENGINE_RUN", "Starting Orthogonal Array (OA) Run {OARunNum}/{TotalOARuns}.",
                            oaRunIndex + 1, totalOARuns);
                LogParameterSettings(currentOAConfig);

                var metricsForAllRepetitionsOfThisOARun = new List<Dictionary<string, double>>();
                TimingUtilities.StartTimer($"oa_run_{oaRunIndex}");

                for (int repIndex = 0; repIndex < _config.Repetitions; repIndex++) {
                    int overallTargetExecutionNumber = (oaRunIndex * _config.Repetitions) + repIndex + 1;
                    Logger.Info("ENGINE_REP", "OA Run {OARunNum}, Repetition {RepNum}/{TotalReps} (Target Execution {ExecNum}/{TotalExecs})",
                                oaRunIndex + 1, repIndex + 1, _config.Repetitions, overallTargetExecutionNumber, totalTargetExecutions);

                    Dictionary<string, object> currentCliArgs = PrepareBaseCliArguments(currentOAConfig);
                    Dictionary<string, string> currentEnvVars = PrepareBaseEnvironmentVariables(currentOAConfig);

                    ApplyNoiseFactors(currentCliArgs, currentEnvVars, oaRunIndex, repIndex);
                    
                    string evalTimerId = $"target_exec_oa{oaRunIndex}_rep{repIndex}";
                    TimingUtilities.StartTimer(evalTimerId);

                    Dictionary<string, double> metricsFromTarget = await _targetRunner.RunAsync(
                        currentCliArgs,
                        currentEnvVars,
                        _config.ShowTargetOutput
                    );
                    metricsForAllRepetitionsOfThisOARun.Add(metricsFromTarget ?? new Dictionary<string, double>()); // Ensure not null

                    TimeSpan evalTime = TimingUtilities.StopTimer(evalTimerId);
                    LogProgress(overallTargetExecutionNumber, totalTargetExecutions, evalTime);
                }
                _currentExperimentState.RawMetricsPerRun[oaRunIndex] = metricsForAllRepetitionsOfThisOARun;
                _currentExperimentState.NextRunIndexToExecute = oaRunIndex + 1; // Mark this OA run as complete
                _currentExperimentState.LastUpdated = DateTime.UtcNow;
                PersistExperimentState($"after_oa_run_{oaRunIndex + 1}");

                TimeSpan oaRunTotalTime = TimingUtilities.StopTimer($"oa_run_{oaRunIndex}");
                LogOARunCompletion(oaRunIndex + 1, oaRunTotalTime, metricsForAllRepetitionsOfThisOARun);
            }
            TimeSpan totalExperimentsTime = TimingUtilities.StopTimer("total_experiments_execution");
            Logger.Info("ENGINE", "All experimental runs completed in {Duration}.", TimingUtilities.FormatElapsedTime(totalExperimentsTime));
        }

        private Dictionary<string, object> PrepareBaseCliArguments(ParameterSettings oaConfig) {
            var cliArgs = new Dictionary<string, object>(_config.FixedCommandLineArguments ?? new Dictionary<string,object>(), StringComparer.OrdinalIgnoreCase);
            foreach (var (factorName, level) in oaConfig.Settings) {
                Factor controlFactor = _config.ControlFactors.First(cf => cf.Name.Equals(factorName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(controlFactor.CliArgument)) {
                    cliArgs[controlFactor.CliArgument] = level.Value;
                }
            }
            return cliArgs;
        }

        private Dictionary<string, string> PrepareBaseEnvironmentVariables(ParameterSettings oaConfig) {
            var envVars = new Dictionary<string, string>(_config.FixedEnvironmentVariables ?? new Dictionary<string,string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var (factorName, level) in oaConfig.Settings) {
                Factor controlFactor = _config.ControlFactors.First(cf => cf.Name.Equals(factorName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(controlFactor.EnvironmentVariable)) {
                    envVars[controlFactor.EnvironmentVariable] = level.Value;
                }
            }
            return envVars;
        }

        private void ApplyNoiseFactors(
            Dictionary<string, object> currentCliArgs,
            Dictionary<string, string> currentEnvVars,
            int oaRunIndex,
            int repetitionIndex) {
            if (_config.NoiseFactors == null || !_config.NoiseFactors.Any()) {
                return;
            }

            foreach (Factor noiseFactor in _config.NoiseFactors) {
                if (noiseFactor.Levels == null || !noiseFactor.Levels.Any()) {
                    Logger.Warning("ENGINE_NOISE", "Noise factor '{NoiseFactorName}' has no defined levels. Skipping.", noiseFactor.Name);
                    continue;
                }
                int levelIndexToUse = repetitionIndex % noiseFactor.Levels.Count;
                Level noiseLevel = noiseFactor.Levels.Values.ElementAtOrDefault(levelIndexToUse); // Assumes ParameterLevelSet values are ordered by OAlevel key

                if (noiseLevel == null) {
                    Logger.Warning("ENGINE_NOISE", "Could not determine noise level for factor '{NoiseFactorName}' at repetition {RepIndex} (level index {LevelIdx}). Skipping.",
                                   noiseFactor.Name, repetitionIndex, levelIndexToUse);
                    continue;
                }

                Logger.Debug("ENGINE_NOISE", "Applying Noise Factor '{NoiseFactorName}': Level '{NoiseLevelValue}' for Repetition {RepNum}",
                             noiseFactor.Name, noiseLevel.Value, repetitionIndex + 1);

                if (!string.IsNullOrWhiteSpace(noiseFactor.CliArgument)) {
                    currentCliArgs[noiseFactor.CliArgument] = noiseLevel.Value;
                }
                if (!string.IsNullOrWhiteSpace(noiseFactor.EnvironmentVariable)) {
                    currentEnvVars[noiseFactor.EnvironmentVariable] = noiseLevel.Value;
                }
            }
        }

        private void PerformAllMetricAnalyses() {
            var allAnalyses = new List<FullAnalysisReportData>();
            List<ParameterSettings> oaConfigurations = GenerateOAConfigurations(); // To map configurations back

            ResultAnalyzer analyzer = CreateResultAnalyzer(); // Create one analyzer instance

            foreach (MetricToAnalyze metricConfig in _config.MetricsToAnalyze) {
                Logger.Info("ENGINE_ANALYSIS", "Performing Taguchi analysis for metric: '{MetricName}' (Method: {Method})",
                            metricConfig.Name, metricConfig.Method);

                // Prepare data specifically for this metric
                var singleMetricRunDataList = new List<SingleMetricExperimentRunData>();
                for (int oaRunIndex = 0; oaRunIndex < oaConfigurations.Count; oaRunIndex++) {
                    if (!_currentExperimentState.RawMetricsPerRun.TryGetValue(oaRunIndex, out var repetitionsForOARun)) {
                        Logger.Warning("ENGINE_ANALYSIS", "No raw metrics found for OA run index {OARunIndex} when analyzing metric '{MetricName}'. This run will have NaN values.", oaRunIndex, metricConfig.Name);
                        repetitionsForOARun = Enumerable.Repeat(new Dictionary<string, double>(), _config.Repetitions).ToList(); // Fill with empty dicts for NaN generation
                    }

                    List<double> metricValuesForThisOARun = repetitionsForOARun
                        .Select(repMetrics => repMetrics.TryGetValue(metricConfig.Name, out double val) ? val : double.NaN)
                        .ToList();
                    
                    singleMetricRunDataList.Add(new SingleMetricExperimentRunData(
                        oaConfigurations[oaRunIndex],
                        metricValuesForThisOARun
                    ));
                }
                
                // ResultAnalyzer's PerformSingleMetricFullAnalysis will now take this simpler list.
                // It will internally convert SingleMetricExperimentRunData to its ExperimentRunResult format if needed,
                // or directly use the list of values.
                FullAnalysisReportData metricAnalysis = analyzer.PerformSingleMetricFullAnalysis(
                    singleMetricRunDataList, // Pass the list of SingleMetricExperimentRunData
                    metricConfig.GetSignalToNoiseType(),
                    metricConfig.Name
                );
                allAnalyses.Add(metricAnalysis);
            }

            _currentExperimentState.AnalysisResults = allAnalyses; // Store all analyses
            LogAllAnalysesCompletion(allAnalyses);
        }
        
        private ResultAnalyzer CreateResultAnalyzer() {
             // Cast Factor to IFactorDefinition for ResultAnalyzer
            var controlFactorDefinitions = _config.ControlFactors.Cast<IFactorDefinition>().ToList();

            return new ResultAnalyzer(
                controlFactorDefinitions,
                _currentExperimentState.ArrayDesign.ArrayDesignation,
                _config.Interactions,
                _currentExperimentState.ArrayDesign.ColumnAssignments,
                _currentExperimentState.ArrayDesign.Array,
                confidenceLevel: 0.95,
                poolingThresholdPercentage: _config.PoolingThresholdPercentage // Use from config
            );
        }
        
        private void PersistExperimentState(string stageHint) {
            if (_currentExperimentState == null) {
                Logger.Warning("ENGINE_STATE", "Attempted to persist null experiment state. Hint: {Hint}", stageHint);
                return;
            }
            try {
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string executableNameBase = "unknown_target";
                if (!string.IsNullOrWhiteSpace(_config.TargetExecutablePath)) {
                    executableNameBase = Path.GetFileNameWithoutExtension(_config.TargetExecutablePath);
                }
                
                string stateFileName = $"state_{executableNameBase}_{stageHint}_{timestamp}.yaml";
                string stateFilePath = Path.Combine(_config.OutputDirectory, stateFileName);

                Directory.CreateDirectory(_config.OutputDirectory);

                string yamlState = Program.YamlSerializer.Serialize(_currentExperimentState);
                File.WriteAllText(stateFilePath, yamlState);
                Logger.Info("ENGINE_STATE", "Experiment state persisted: {FilePath}", stateFilePath);

            } catch (Exception ex) {
                Logger.Exception("ENGINE_STATE", ex, "Failed to persist experiment state. Hint: {Hint}", stageHint);
            }
        }

        #region Logging Helpers
        private void LogOrthogonalArrayDesign(OrthogonalArrayDesign design) {
            Logger.Info("ENGINE", "Orthogonal Array Design: {ArrayDesignation} ({Rows} Runs x {Cols} Standard Columns)",
                        design.ArrayDesignation, design.Array.GetLength(0), design.Array.GetLength(1));
            if (_config.Verbose) {
                Logger.Debug("ENGINE", "Column Assignments (Factor/Interaction -> 0-based OA Column Index):");
                foreach (var kvp in design.ColumnAssignments.OrderBy(c => c.Value)) {
                    Logger.Debug("ENGINE", "  '{Key}': OA Col {Value}", kvp.Key, kvp.Value + 1);
                }
            }
        }

        private void LogParameterSettings(ParameterSettings settings) {
            if (!_config.Verbose) { return; }
            Logger.Debug("ENGINE_DETAIL", "Current OA Configuration Settings:");
            foreach (var (paramName, level) in settings.Settings.OrderBy(s => s.Key)) {
                Logger.Debug("ENGINE_DETAIL", "  Factor '{ParamName}': Level '{LevelValue}' (OA Symbol {OASymbol})",
                             paramName, level.Value, level.OALevel.Level);
            }
        }
        
        private void LogProgress(int completedTargetExecs, int totalTargetExecs, TimeSpan durationLastExec) {
            TimeSpan timeElapsedOverall = TimingUtilities.GetElapsedTime("total_experiments_execution");
            double percentComplete = (double)completedTargetExecs / totalTargetExecs * 100;
            var (remainingTime, estimatedCompletion) = TimingUtilities.EstimateCompletion(timeElapsedOverall, percentComplete);

            Logger.Info("ENGINE_PROGRESS", "Target Exec {Completed}/{Total} finished in {DurationLast}. Progress: {Percent:F1}%. ETA: {ETR} ({ETC:HH:mm:ss, MMM dd})",
                completedTargetExecs, totalTargetExecs, TimingUtilities.FormatElapsedTime(durationLastExec),
                percentComplete, TimingUtilities.FormatElapsedTime(remainingTime), estimatedCompletion);
        }

        private void LogOARunCompletion(int oaRunNum, TimeSpan duration, List<Dictionary<string, double>> metricsForAllReps) {
            Logger.Info("ENGINE_RUN", "OA Run {OARunNum} completed in {Duration}.", oaRunNum, TimingUtilities.FormatElapsedTime(duration));
            if (_config.Verbose && metricsForAllReps.Any()) {
                var averagedMetrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var allKeys = metricsForAllReps.SelectMany(d => d.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
                
                foreach (string key in allKeys) {
                    averagedMetrics[key] = metricsForAllReps
                        .Select(repMetrics => repMetrics.TryGetValue(key, out double val) ? val : double.NaN)
                        .Where(v => !double.IsNaN(v))
                        .DefaultIfEmpty(double.NaN)
                        .Average();
                }
                LogMetrics($"  Average metrics for OA Run {oaRunNum}:", averagedMetrics);
            }
        }
        
        private void LogMetrics(string title, IReadOnlyDictionary<string, double> metrics) {
            if (!_config.Verbose || !metrics.Any()) { return; }
            Logger.Debug("ENGINE_METRICS", title);
            foreach (var metric in metrics.OrderBy(m => m.Key)) {
                Logger.Debug("ENGINE_METRICS", "    {MetricKey}: {MetricValue:F4}", metric.Key, metric.Value);
            }
        }

        private void LogAllAnalysesCompletion(IReadOnlyList<FullAnalysisReportData> allAnalyses) {
            Logger.Info("ENGINE_ANALYSIS", "All configured metric analyses complete ({Count} total).", allAnalyses.Count);
            if (!_config.Verbose || !allAnalyses.Any()) { return; }

            foreach(var metricData in allAnalyses) {
                Logger.Debug("ENGINE_ANALYSIS", "Summary for Metric: '{MetricName}'", metricData.MetricAnalyzed);
                Logger.Debug("ENGINE_ANALYSIS", "  Optimal Configuration ({OptimalParamCount} params):", metricData.OptimalConfig.Settings.Count);
                foreach (var (param, level) in metricData.OptimalConfig.Settings.OrderBy(s => s.Key)) {
                    Logger.Debug("ENGINE_ANALYSIS", "    {ParamName}: {LevelValue}", param, level.Value);
                }
                if (metricData.PredictedPerformance != null) {
                    Logger.Debug("ENGINE_ANALYSIS", "  Predicted Performance: {Value:F4} (CI: [{Lower:F4} - {Upper:F4}])",
                                 metricData.PredictedPerformance.PredictedValue,
                                 metricData.PredictedPerformance.LowerBound,
                                 metricData.PredictedPerformance.UpperBound);
                }
            }
        }
        #endregion
    }
}