// TaguchiBench.Engine/Reporting/MarkdownReportGenerator.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TaguchiBench.Common; // For Logger
using TaguchiBench.Engine.Configuration;
using TaguchiBench.Engine.Core;

namespace TaguchiBench.Engine.Reporting {
    public class MarkdownReportGenerator {
        private readonly EngineConfiguration _config;
        private readonly IReadOnlyList<FullAnalysisReportData> _analysisResultsList;
        private readonly IReadOnlyDictionary<int, List<Dictionary<string, double>>> _rawMetricsPerRun;
        private readonly string _htmlReportFileName; // Relative path for linking
        private readonly string _appVersion;
        private readonly IReadOnlyList<ParameterSettings> _oaConfigurations;


        public MarkdownReportGenerator(
            EngineConfiguration config,
            IReadOnlyList<FullAnalysisReportData> analysisResultsList,
            IReadOnlyDictionary<int, List<Dictionary<string, double>>> rawMetricsPerRun,
            string htmlReportFileName, // Can be null if HTML report not generated
            string appVersion,
            OrthogonalArrayDesign arrayDesign
            ) {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _analysisResultsList = analysisResultsList ?? new List<FullAnalysisReportData>();
            _rawMetricsPerRun = rawMetricsPerRun ?? new Dictionary<int, List<Dictionary<string, double>>>();
            _htmlReportFileName = htmlReportFileName; // Allowed to be null
            _appVersion = appVersion ?? throw new ArgumentNullException(nameof(appVersion));

            var paramDefs = _config.ControlFactors.ToDictionary(cf => cf.Name, cf => cf.Levels);
            _oaConfigurations = OrthogonalArrayFactory.CreateParameterConfigurations(arrayDesign, paramDefs)
                                .Select(dict => new ParameterSettings(dict))
                                .ToList();
        }

        public string GenerateReportContent() {
            var sb = new StringBuilder();

            AppendHeader(sb);
            AppendConfigurationSummary(sb);

            if (!_analysisResultsList.Any()) {
                sb.AppendLine("\n## Analysis Results\n*   No analysis results available to report.");
            } else {
                foreach (FullAnalysisReportData analysisData in _analysisResultsList) {
                    AppendSingleMetricAnalysis(sb, analysisData, $"Analysis for Metric: '{analysisData.MetricAnalyzed}'");
                }
            }

            AppendExperimentalRunsTable(sb);

            return sb.ToString();
        }

        public async Task SaveReportToFileAsync(string filePath) {
            string content = GenerateReportContent();
            await File.WriteAllTextAsync(filePath, content);
            Logger.Info("REPORTER_MD", "Markdown analysis report saved to: {File}", filePath);
        }

        private void AppendHeader(StringBuilder sb) {
            sb.AppendLine($"# TaguchiBench Engine Analysis Report");
            sb.AppendLine($"\n*   **Target Executable:** `{_config.TargetExecutablePath}`");
            sb.AppendLine($"*   **Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss K}, **Engine Version:** {_appVersion}");
            if (!string.IsNullOrEmpty(_htmlReportFileName)) {
                sb.AppendLine($"*   **Companion HTML Report:** [`{Path.GetFileName(_htmlReportFileName)}`](./{Path.GetFileName(_htmlReportFileName)})");
            }
        }

        private void AppendConfigurationSummary(StringBuilder sb) {
            sb.AppendLine("\n## Experiment Configuration Summary");
            sb.AppendLine($"*   **Repetitions per OA Run:** {_config.Repetitions}");
            sb.AppendLine($"*   **Output Directory:** `{_config.OutputDirectory}`");
            sb.AppendLine($"*   **Verbose Engine Logging:** {_config.Verbose}");
            sb.AppendLine($"*   **Show Target Output:** {_config.ShowTargetOutput}");

            string arrayUsed = _analysisResultsList.FirstOrDefault()?.ArrayDesignationUsed ?? "N/A";
            sb.AppendLine($"*   **Orthogonal Array Used:** {arrayUsed}");

            sb.AppendLine($"\n### Metrics Configured for Analysis ({_config.MetricsToAnalyze.Count}):");
            foreach (var metric in _config.MetricsToAnalyze) {
                sb.AppendLine($"*   **{metric.Name}**: Method = `{metric.Method}`{(metric.Method == MetricOptimizationMethod.Nominal ? $", Target = `{metric.Target?.ToString(CultureInfo.InvariantCulture)}`" : "")}");
            }

            sb.AppendLine($"\n### Control Factors Optimized ({_config.ControlFactors.Count}):");
            foreach (var factor in _config.ControlFactors.OrderBy(f => f.Name)) {
                string levelsStr = string.Join(", ", factor.Levels.Values.OrderBy(l => l.OALevel.Level).Select(l => $"'{l.Value}'"));
                sb.AppendLine($"*   **{factor.Name}**: {(string.IsNullOrWhiteSpace(factor.CliArgument) ? "" : $"CLI=`{factor.CliArgument}`, ")}{(string.IsNullOrWhiteSpace(factor.EnvironmentVariable) ? "" : $"ENV=`{factor.EnvironmentVariable}`, ")}Levels = [{levelsStr}]");
            }
            if (_config.NoiseFactors.Any()) {
                sb.AppendLine($"\n### Noise Factors Considered ({_config.NoiseFactors.Count}):");
                foreach (var factor in _config.NoiseFactors.OrderBy(f => f.Name)) {
                    string levelsStr = string.Join(", ", factor.Levels.Values.OrderBy(l => l.OALevel.Level).Select(l => $"'{l.Value}'"));
                    sb.AppendLine($"*   **{factor.Name}**: {(string.IsNullOrWhiteSpace(factor.CliArgument) ? "" : $"CLI=`{factor.CliArgument}`, ")}{(string.IsNullOrWhiteSpace(factor.EnvironmentVariable) ? "" : $"ENV=`{factor.EnvironmentVariable}`, ")}Levels = [{levelsStr}]");
                }
            }
            if (_config.Interactions.Any()) {
                sb.AppendLine($"\n### Interactions Analyzed ({_config.Interactions.Count}):");
                _config.Interactions.OrderBy(i => i.FirstParameterName).ThenBy(i => i.SecondParameterName)
                    .ToList().ForEach(i => sb.AppendLine($"*   {i.FirstParameterName} × {i.SecondParameterName}"));
            }

            // Add fixed command line arguments
            var commandLine = _config.GetFixedCommandLineForDisplay();
            if (!string.IsNullOrWhiteSpace(commandLine)) {
                sb.AppendLine($"\n### Fixed Command Line:");
                sb.AppendLine("```");
                sb.AppendLine(commandLine);
                sb.AppendLine("```");
            }

            // Add fixed environment variables
            var envVars = _config.GetFixedEnvironmentVariablesForDisplay();
            if (!string.IsNullOrWhiteSpace(envVars)) {
                sb.AppendLine($"\n### Fixed Environment Variables:");
                sb.AppendLine("```");
                sb.AppendLine(envVars);
                sb.AppendLine("```");
            }
        }

        private void AppendAnalysisWarningsMd(StringBuilder sb, List<string> warnings, string context) {
            if (warnings != null && warnings.Any()) {
                sb.AppendLine($"\n#### Analysis Warnings ({context}):");
                foreach (var warning in warnings) {
                    sb.AppendLine($"*   <span style='color:orange;'>⚠ {warning}</span>"); // Markdown might not render style, but good for HTML later
                }
            }
        }

        private void AppendPredictionNotesMd(StringBuilder sb, List<string> notes, string context) {
            if (notes != null && notes.Any()) {
                sb.AppendLine($"\n##### Prediction Notes ({context}):");
                foreach (var note in notes) {
                    sb.AppendLine($"*   *{note}*");
                }
            }
        }

        private void AppendSingleMetricAnalysis(StringBuilder sb, FullAnalysisReportData metricData, string metricTitle) {
            sb.AppendLine($"\n## {metricTitle}");
            sb.AppendLine($"*   **S/N Ratio Type Used:** `{metricData.SnTypeUsed}`");

            AppendAnalysisWarningsMd(sb, metricData.InitialAnova?.AnalysisWarnings, $"{metricData.MetricAnalyzed} - Initial ANOVA");
            AppendAnalysisWarningsMd(sb, metricData.PooledAnova?.AnalysisWarnings, $"{metricData.MetricAnalyzed} - Pooled ANOVA");

            sb.AppendLine($"\n### Optimal Configuration (for '{metricData.MetricAnalyzed}')");
            sb.AppendLine("```yaml");
            metricData.OptimalConfig.Settings.OrderBy(kvp => kvp.Key)
                .ToList().ForEach(kvp => sb.AppendLine($"{kvp.Key}: {kvp.Value.Value} # OA Level: {kvp.Value.OALevel.Level}"));
            sb.AppendLine("```");

            if (metricData.PredictedPerformance != null) {
                sb.AppendLine($"\n### Predicted Performance (for '{metricData.MetricAnalyzed}' at Optimal)");
                sb.AppendLine($"*   **Value:** `{metricData.PredictedPerformance.PredictedValue:F4}` (Original Scale)");
                sb.AppendLine($"*   **95% CI:** `[{metricData.PredictedPerformance.LowerBound:F4} - {metricData.PredictedPerformance.UpperBound:F4}]`");
                AppendPredictionNotesMd(sb, metricData.PredictedPerformance.PredictionNotes, metricData.MetricAnalyzed);
            }

            AppendAnovaResultsMd(sb, metricData.InitialAnova, $"Initial ANOVA Results ('{metricData.MetricAnalyzed}')");
            if (metricData.PooledAnova != null) {
                AppendAnovaResultsMd(sb, metricData.PooledAnova, $"Pooled ANOVA Results ('{metricData.MetricAnalyzed}')");
            }
            // Main effects, interactions, effect estimates can be lengthy for Markdown.
            // Users are typically directed to HTML for detailed plots and tables.
            // We can include a summary or skip for brevity in Markdown.
            // For now, let's include them.
            AppendMainEffectsMd(sb, metricData.MainEffects, metricData.MetricAnalyzed);
            AppendInteractionEffectsMd(sb, metricData.InteractionEffectsSn, metricData.MetricAnalyzed);
            AppendEffectEstimatesMd(sb, metricData.EffectEstimates, metricData.MetricAnalyzed);
        }

        private void AppendAnovaResultsMd(StringBuilder sb, AnovaAnalysisResult anovaResult, string title) {
            if (anovaResult == null || anovaResult.AnovaTable == null || !anovaResult.AnovaTable.Any()) {
                sb.AppendLine($"\n### {title}\n*   No ANOVA data available.");
                return;
            }
            sb.AppendLine($"\n### {title}");
            if (anovaResult.IsPooled && anovaResult.PooledSources.Any()) {
                sb.AppendLine($"*   *Factors Pooled into Error: {string.Join(", ", anovaResult.PooledSources.Select(s => $"`{s}`"))}*");
            }
            sb.AppendLine("\n| Factor/Interaction         | Contrib (%) | SS      | DF | MS      | F-Value | p-Value | Significant (α=0.05) |");
            sb.AppendLine("|----------------------------|-------------|---------|----|---------|---------|---------|----------------------|");
            anovaResult.AnovaTable.OrderByDescending(r => r.Source == AnovaResult.ErrorSource || r.Source.Contains("(Pooled)") ? double.MinValue : r.ContributionPercentage)
                .ToList().ForEach(r => sb.AppendLine(
                    $"| {r.Source,-26} | {r.ContributionPercentage,11:F2} | {r.SumOfSquares,7:F4} | {r.DegreesOfFreedom,2} | {(double.IsNaN(r.MeanSquare) ? "N/A" : r.MeanSquare.ToString("F4")),7} | {(double.IsNaN(r.FValue) ? "N/A" : r.FValue.ToString("F2")),7} | {(double.IsNaN(r.PValue) ? "N/A" : r.PValue.ToString("F4")),7} | {(r.Source.Contains(AnovaResult.ErrorSource) ? "N/A" : (r.IsSignificant ? "**Yes**" : "No")),-20} |"));
            sb.AppendLine($"| **Total**                  |             | {anovaResult.TotalSumOfSquares,7:F4} | {anovaResult.TotalDegreesOfFreedom,2} |         |         |         |                      |");
        }

        private void AppendMainEffectsMd(StringBuilder sb, Dictionary<string, ParameterMainEffect> mainEffects, string metricName) {
            if (mainEffects == null || !mainEffects.Any()) { return; }
            sb.AppendLine($"\n### Main Effects (for '{metricName}')");
            sb.AppendLine("| Parameter                  | Level Value | Avg S/N Ratio | Avg Raw Metric |");
            sb.AppendLine("|----------------------------|-------------|---------------|----------------|");
            foreach (var paramEffectPair in mainEffects.OrderBy(p => p.Key)) {
                bool firstLevel = true;
                foreach (var levelEffectPair in paramEffectPair.Value.EffectsByLevelSn.OrderBy(kvp => kvp.Key.OALevel.Level)) {
                    paramEffectPair.Value.EffectsByLevelRaw.TryGetValue(levelEffectPair.Key, out double rawValue);
                    sb.AppendLine($"| {(firstLevel ? $"`{paramEffectPair.Key}`" : ""),-26} | `{levelEffectPair.Key.Value,-9}` | {levelEffectPair.Value,13:F4} | {(double.IsNaN(rawValue) ? "N/A" : rawValue.ToString("F4")),14} |");
                    firstLevel = false;
                }
            }
        }

        private void AppendInteractionEffectsMd(StringBuilder sb, Dictionary<string, ParameterInteractionEffect> interactionEffects, string metricName) {
            if (interactionEffects == null || !interactionEffects.Any()) { return; }
            sb.AppendLine($"\n### Interaction Effects (S/N Ratios - for '{metricName}')");
            foreach (var interactionPair in interactionEffects.OrderBy(i => i.Key)) {
                sb.AppendLine($"\n#### Interaction: `{interactionPair.Key}`");
                sb.AppendLine("| Level (Factor 1) | Level (Factor 2) | Avg S/N Ratio |");
                sb.AppendLine("|------------------|------------------|---------------|");
                foreach (var levelPairEffect in interactionPair.Value.EffectsByLevelPair.OrderBy(kvp => kvp.Key.Level1.OALevel.Level).ThenBy(kvp => kvp.Key.Level2.OALevel.Level)) {
                    sb.AppendLine($"| `{levelPairEffect.Key.Level1.Value,-16}` | `{levelPairEffect.Key.Level2.Value,-16}` | {levelPairEffect.Value,13:F4} |");
                }
            }
        }

        private void AppendEffectEstimatesMd(StringBuilder sb, List<EffectEstimate> effectEstimates, string metricName) {
            if (effectEstimates == null || !effectEstimates.Any()) { return; }
            sb.AppendLine($"\n### Effect Estimates (S/N Scale - for '{metricName}')");
            sb.AppendLine("| Source                     | Effect Est. | Abs(Effect) |");
            sb.AppendLine("|----------------------------|-------------|-------------|");
            effectEstimates.OrderByDescending(e => e.AbsoluteEffect).ToList().ForEach(e =>
                sb.AppendLine($"| `{e.Source,-26}` | {e.Effect,11:F4} | {e.AbsoluteEffect,11:F4} |"));
        }


        private void AppendExperimentalRunsTable(StringBuilder sb) {
            if (_rawMetricsPerRun == null || !_rawMetricsPerRun.Any() || !_oaConfigurations.Any()) {
                sb.AppendLine("\n## Experimental Run Details\n*   No detailed experimental run data available.");
                return;
            }

            var allMetricNames = _config.MetricsToAnalyze.Select(m => m.Name).ToList();

            sb.AppendLine("\n## Experimental Run Details");
            sb.AppendLine($"*Averages over {_config.Repetitions} repetition(s) per OA run.*");

            // Header
            sb.Append("| OA Run | Configuration ");
            foreach (string metricName in allMetricNames) {
                sb.Append($"| Avg '{metricName}' | S/N '{metricName}' ");
            }
            sb.AppendLine("| Repetition Details |");

            // Separator
            sb.Append("|--------|-----------------");
            foreach (string metricName in allMetricNames) {
                sb.Append($"|-------------------|-------------------");
            }
            sb.AppendLine("|--------------------|");

            // Data Rows
            for (int oaRunIndex = 0; oaRunIndex < _oaConfigurations.Count; oaRunIndex++) {
                ParameterSettings currentConfig = _oaConfigurations[oaRunIndex];
                string configStr = string.Join("; ", currentConfig.Settings.OrderBy(s => s.Key)
                                                    .Select(s => $"`{s.Key}: {s.Value.Value}`"));
                sb.Append($"| {oaRunIndex + 1,-6} | {configStr,-15} ");

                foreach (string metricName in allMetricNames) {
                    double avgMetricValue = double.NaN;
                    double snRatioForMetric = double.NaN;

                    if (_rawMetricsPerRun.TryGetValue(oaRunIndex, out var repetitionsForThisOARun)) {
                        var metricValuesThisRun = repetitionsForThisOARun
                            .Select(repData => repData.TryGetValue(metricName, out double val) ? val : double.NaN)
                            .Where(val => !double.IsNaN(val)).ToList();
                        if (metricValuesThisRun.Any()) { avgMetricValue = metricValuesThisRun.Average(); }
                    }

                    var analysisForThisMetric = _analysisResultsList.FirstOrDefault(ar => ar.MetricAnalyzed == metricName);
                    var runDetailForThisMetric = analysisForThisMetric?.ExperimentRunDetails?.FirstOrDefault(rd => rd.RunNumber == oaRunIndex + 1);
                    if (runDetailForThisMetric != null) { snRatioForMetric = runDetailForThisMetric.SnRatioValue; }

                    sb.Append($"| {(double.IsNaN(avgMetricValue) ? "N/A" : avgMetricValue.ToString("F4")),-17} | {(double.IsNaN(snRatioForMetric) ? "N/A" : snRatioForMetric.ToString("F4")),-17} ");
                }

                // Details link (conceptual for Markdown, more interactive in HTML)
                // For Markdown, we can list them if few, or just say "See HTML report".
                // Let's try to list first few reps if verbose, else a summary.
                if (_rawMetricsPerRun.TryGetValue(oaRunIndex, out var repsData) && repsData.Any()) {
                    if (_config.Verbose && repsData.Count <= 3) { // Show if verbose and few reps
                        var repSummaries = new List<string>();
                        for (int r = 0; r < repsData.Count; ++r) {
                            var repMetrics = allMetricNames.Select(mn => $"{mn}={(repsData[r].TryGetValue(mn, out var v) ? v.ToString("F2") : "N/A")}");
                            repSummaries.Add($"Rep{r + 1}: {string.Join(", ", repMetrics)}");
                        }
                        sb.AppendLine($"| {string.Join("<br>", repSummaries)} |");
                    } else {
                        sb.AppendLine($"| {repsData.Count} reps (details in HTML/YAML) |");
                    }
                } else {
                    sb.AppendLine($"| No rep data |");
                }
            }
        }
    }
}