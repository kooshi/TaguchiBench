// TaguchiBench.Engine/Reporting/HtmlReportGenerator.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web; // For HttpUtility
using TaguchiBench.Common; // For Logger
using TaguchiBench.Engine.Configuration;
using TaguchiBench.Engine.Core;
// MathNet.Numerics.Distributions for Normal plot is still relevant if used by ResultAnalyzer

namespace TaguchiBench.Engine.Reporting {
    public class HtmlReportGenerator {
        private readonly EngineConfiguration _config;
        private readonly IReadOnlyList<FullAnalysisReportData> _analysisResultsList; // Changed from Bundle
        private readonly IReadOnlyDictionary<int, List<Dictionary<string, double>>> _rawMetricsPerRun; // New
        private readonly IReadOnlyList<ParameterSettings> _oaConfigurations; // Derived for convenience

        public HtmlReportGenerator(
            EngineConfiguration config,
            IReadOnlyList<FullAnalysisReportData> analysisResultsList,
            IReadOnlyDictionary<int, List<Dictionary<string, double>>> rawMetricsPerRun,
            OrthogonalArrayDesign arrayDesign // Pass the design to get configurations
            ) {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _analysisResultsList = analysisResultsList ?? new List<FullAnalysisReportData>();
            _rawMetricsPerRun = rawMetricsPerRun ?? new Dictionary<int, List<Dictionary<string, double>>>();

            // Derive OA configurations for the experimental runs table
            var paramDefs = _config.ControlFactors.ToDictionary(cf => cf.Name, cf => cf.Levels);
            _oaConfigurations = OrthogonalArrayFactory.CreateParameterConfigurations(arrayDesign, paramDefs)
                                .Select(dict => new ParameterSettings(dict))
                                .ToList();
        }

        public string GenerateReport() {
            var sb = new StringBuilder();
            BuildHtmlHeader(sb);
            BuildOverallSummary(sb);

            if (!_analysisResultsList.Any()) {
                sb.Append("<div class='section'><p>No analysis results available to report.</p></div>");
            } else {
                foreach (FullAnalysisReportData analysisData in _analysisResultsList) {
                    BuildMetricSection(sb, analysisData, $"Analysis for Metric: '{analysisData.MetricAnalyzed}'");
                }
            }

            BuildExperimentalRunsTable(sb); // This will now use _rawMetricsPerRun primarily
            BuildHtmlFooter(sb);
            return sb.ToString();
        }

        public void SaveReportToFile(string filePath) {
            string htmlContent = GenerateReport();
            File.WriteAllText(filePath, htmlContent);
            Logger.Info("REPORTER_HTML", "HTML report saved to: {FilePath}", filePath);
        }

        private void BuildHtmlHeader(StringBuilder sb) {
            // Using verbatim interpolated strings
            sb.Append(@$"
<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1.0'>
  <title>TaguchiBench Engine Analysis Report</title>
  <style>
    :root {{ 
      --primary-color: #2c6fbb; 
      --primary-light: #e6f2ff;
      --secondary-color: #6c757d; 
      --light-gray: #f8f9fa; 
      --dark-gray: #343a40; 
      --border-color: #dee2e6; 
      --warning-bg: #fff3cd; 
      --warning-text: #856404; 
      --warning-border: #ffeeba; 
      --success-color: #28a745;
      --danger-color: #dc3545;
      --card-shadow: 0 4px 6px rgba(0,0,0,0.1);
      --transition-speed: 0.3s;
    }}
    
    body {{ 
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; 
      margin: 0; 
      padding: 0; 
      background-color: var(--light-gray); 
      color: var(--dark-gray); 
      line-height: 1.6; 
    }}
    
    .container {{ 
      max-width: 1300px; 
      margin: 20px auto; 
      padding: 20px; 
      background-color: #fff; 
      box-shadow: 0 0 20px rgba(0,0,0,0.12); 
      border-radius: 8px; 
    }}
    
    h1, h2, h3, h4 {{ 
      color: var(--primary-color); 
      margin-top: 1.5em; 
      margin-bottom: 0.5em; 
    }}
    
    h1 {{ 
      text-align: center; 
      border-bottom: 2px solid var(--primary-color); 
      padding-bottom: 15px; 
      margin-bottom: 25px;
    }}
    
    h2 {{ 
      border-bottom: 1px solid var(--border-color); 
      padding-bottom: 8px; 
      margin-top: 2em;
    }}
    
    table {{ 
      border-collapse: collapse; 
      width: 100%; 
      margin-bottom: 20px; 
      font-size: 0.9em; 
      box-shadow: var(--card-shadow);
      border-radius: 4px;
      overflow: hidden;
    }}
    
    th, td {{ 
      border: 1px solid var(--border-color); 
      padding: 10px 12px; 
      text-align: left; 
      vertical-align: top; 
    }}
    
    th {{ 
      background-color: #e9ecef; 
      font-weight: 600; 
    }}
    
    tr:nth-child(even) {{ 
      background-color: var(--light-gray); 
    }}
    
    .plot-grid {{ 
      display: grid; 
      grid-template-columns: repeat(auto-fit, minmax(450px, 1fr)); 
      gap: 25px; 
      margin-bottom: 25px; 
    }}
    
    .plot-container {{ 
      background-color: #fff; 
      padding: 18px; 
      border-radius: 6px; 
      box-shadow: var(--card-shadow);
      transition: box-shadow var(--transition-speed) ease;
    }}
    
    .plot-container:hover {{
      box-shadow: 0 6px 12px rgba(0,0,0,0.15);
    }}
    
    .chart-wrapper {{ 
      position: relative; 
      height: 380px; 
      width: 100%; 
      margin-bottom: 15px; 
    }}
    
    .section {{ 
      margin-bottom: 35px; 
      padding: 25px; 
      border: 1px solid var(--border-color); 
      border-radius: 8px; 
      background-color: #fff; 
      box-shadow: var(--card-shadow);
    }}
    
    .metric-section {{ 
      margin-top: 45px; 
      padding-top: 25px; 
      border-top: 3px solid var(--primary-color); 
    }}
    
    pre {{ 
      background-color: #f5f5f5; 
      padding: 12px; 
      border: 1px solid var(--border-color); 
      border-radius: 4px; 
      overflow-x: auto; 
      font-size: 0.85em; 
      white-space: pre-wrap; 
      word-wrap: break-word; 
    }}
    
    .summary-item {{ 
      margin-bottom: 0.7em; 
    }} 
    
    .summary-item strong {{ 
      color: var(--secondary-color); 
    }}
    
    .optimal-param {{ 
      background-color: #e6f7ff !important;
      border-left: 3px solid var(--primary-color) !important;
    }}
    
    .significant {{ 
      color: var(--success-color); 
      font-weight: bold; 
    }} 
    
    .not-significant {{ 
      color: var(--secondary-color); 
    }}
    
    .analysis-warning {{ 
      background-color: var(--warning-bg); 
      color: var(--warning-text); 
      border: 1px solid var(--warning-border); 
      padding: 12px 15px; 
      margin-bottom: 20px; 
      border-radius: 5px; 
      font-size: 0.9em; 
    }}
    
    .analysis-warning strong {{ 
      color: var(--warning-text); 
    }}
    
    .collapsible-btn {{ 
      background-color: #f1f1f1; 
      color: var(--dark-gray); 
      cursor: pointer; 
      padding: 10px 15px; 
      width: 100%; 
      border: none; 
      text-align: left; 
      outline: none; 
      font-size: 0.95em; 
      margin-top: 12px; 
      border-radius: 4px; 
      transition: background-color var(--transition-speed);
      position: relative;
    }}
    
    .collapsible-btn:after {{
      content: '+';
      font-size: 18px;
      font-weight: bold;
      float: right;
      margin-left: 5px;
      transition: transform var(--transition-speed);
    }}
    
    .collapsible-btn.active:after {{
      content: '-';
    }}
    
    .collapsible-btn:hover {{ 
      background-color: #ddd; 
    }} 
    
    .collapsible-btn.active {{ 
      background-color: #e6f2ff; 
      border-bottom-left-radius: 0;
      border-bottom-right-radius: 0;
    }}
    
    .collapsible-content {{ 
      padding: 0 15px; 
      max-height: 0; 
      overflow: hidden;
      transition: max-height var(--transition-speed) ease-out; 
      background-color: white; 
      border: 1px solid #e6f2ff; 
      border-top: none; 
      margin-bottom: 15px; 
      border-bottom-left-radius: 4px;
      border-bottom-right-radius: 4px;
    }}
    
    .heatmap-table td {{ 
      text-align: center; 
    }}
    
    .details-table td, .details-table th {{ 
      font-size: 0.85em; 
      padding: 6px; 
    }}
    
    @media (max-width: 768px) {{ 
      .plot-grid {{ 
        grid-template-columns: 1fr; 
      }} 
      .section {{
        padding: 15px;
      }}
    }}
    
    @media print {{ 
      body {{ 
        background-color: white; 
      }} 
      .container {{ 
        box-shadow: none; 
        margin: 0; 
        padding: 0; 
      }} 
      .chart-wrapper, .section, .plot-container {{ 
        break-inside: avoid; 
        box-shadow: none;
      }} 
      .collapsible-btn, .collapsible-btn + .collapsible-content {{ 
        display: block !important; 
        max-height: none !important;
        page-break-inside: avoid;
      }}
      .collapsible-content {{
        border: none;
      }}
    }}
  </style>
  <script src='https://cdn.jsdelivr.net/npm/chart.js@3.9.1/dist/chart.min.js'></script>
  <script src='https://cdn.jsdelivr.net/npm/chartjs-adapter-date-fns@2.0.0/dist/chartjs-adapter-date-fns.bundle.min.js'></script>
</head>
<body>
<div class='container'>
  <h1>TaguchiBench Engine Analysis Report</h1>
");
        }
        private void BuildOverallSummary(StringBuilder sb) {
            string arrayUsed = _analysisResultsList.FirstOrDefault()?.ArrayDesignationUsed ?? (_config.ControlFactors.Any() ? "N/A (Design not run or available in results)" : "N/A (No control factors)");
            int runsInDesign = _analysisResultsList.FirstOrDefault()?.NumberOfRunsInExperiment ?? 0;
            string dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            sb.Append(@$"
  <div class='section' id='overall-summary'>
    <h2>Overall Experiment Setup</h2>
    <div style='display: grid; grid-template-columns: repeat(auto-fit, minmax(350px, 1fr)); gap: 20px;'>
      <div>
        <p class='summary-item'><strong>Target Executable:</strong> <small><code>{HttpUtility.HtmlEncode(_config.TargetExecutablePath)}</code></small></p>
        <p class='summary-item'><strong>Report Generated:</strong> {dateTime}</p>
        <p class='summary-item'><strong>Repetitions per OA Run:</strong> {_config.Repetitions}</p>
        <p class='summary-item'><strong>Output Directory:</strong> <small><code>{HttpUtility.HtmlEncode(_config.OutputDirectory)}</code></small></p>
      </div>
      <div>
        <p class='summary-item'><strong>Orthogonal Array Used:</strong> {arrayUsed}</p>
        <p class='summary-item'><strong>Number of Runs in Design:</strong> {runsInDesign}</p>
        <p class='summary-item'><strong>Verbose Engine Logging:</strong> {_config.Verbose}</p>
        <p class='summary-item'><strong>Show Target Output:</strong> {_config.ShowTargetOutput}</p>
      </div>
    </div>
    <h4>Metrics Configured for Analysis:</h4>
    <ul>");
            foreach (var metric in _config.MetricsToAnalyze) {
                sb.Append(@$"<li><strong>{HttpUtility.HtmlEncode(metric.Name)}:</strong> Method = {metric.Method}{(metric.Method == MetricOptimizationMethod.Nominal ? $", Target = {metric.Target?.ToString(CultureInfo.InvariantCulture)}" : "")}</li>");
            }
            sb.Append(@"
    </ul>
    <h4>Control Factors Optimized:</h4>
    <ul>");
            foreach (var factor in _config.ControlFactors.OrderBy(f => f.Name)) {
                string levelsStr = string.Join(", ", factor.Levels.Values.OrderBy(l => l.OALevel.Level).Select(l => $"'{HttpUtility.HtmlEncode(l.Value)}'"));
                sb.Append(@$"<li><strong>{HttpUtility.HtmlEncode(factor.Name)}:</strong> {(string.IsNullOrWhiteSpace(factor.CliArgument) ? "" : $"CLI='{factor.CliArgument}', ")}{(string.IsNullOrWhiteSpace(factor.EnvironmentVariable) ? "" : $"ENV='{factor.EnvironmentVariable}', ")}Levels = {levelsStr}</li>");
            }
            sb.Append(@"
    </ul>");
            if (_config.NoiseFactors.Any()) {
                sb.Append(@"
    <h4>Noise Factors Considered:</h4>
    <ul>");
                foreach (var factor in _config.NoiseFactors.OrderBy(f => f.Name)) {
                    string levelsStr = string.Join(", ", factor.Levels.Values.OrderBy(l => l.OALevel.Level).Select(l => $"'{HttpUtility.HtmlEncode(l.Value)}'"));
                    sb.Append(@$"<li><strong>{HttpUtility.HtmlEncode(factor.Name)}:</strong> {(string.IsNullOrWhiteSpace(factor.CliArgument) ? "" : $"CLI='{factor.CliArgument}', ")}{(string.IsNullOrWhiteSpace(factor.EnvironmentVariable) ? "" : $"ENV='{factor.EnvironmentVariable}', ")}Levels = {levelsStr}</li>");
                }
                sb.Append(@"
    </ul>");
            }
            if (_config.Interactions.Any()) {
                sb.Append(@"
    <h4>Interactions Analyzed:</h4>
    <ul>");
                foreach (var interaction in _config.Interactions.OrderBy(i => i.FirstParameterName).ThenBy(i => i.SecondParameterName)) {
                    sb.Append(@$"<li>{HttpUtility.HtmlEncode(interaction.FirstParameterName)} × {HttpUtility.HtmlEncode(interaction.SecondParameterName)}</li>");
                }
                sb.Append(@"
    </ul>");
            }

            // Add fixed command line arguments and environment variables
            var commandLine = _config.GetFixedCommandLineForDisplay();
            var envVars = _config.GetFixedEnvironmentVariablesForDisplay();

            if (!string.IsNullOrWhiteSpace(commandLine)) {
                sb.Append(@"
    <h4>Fixed Command Line:</h4>
    <pre style='background-color: #f5f5f5; padding: 12px; border: 1px solid var(--border-color); border-radius: 4px; overflow-x: auto; font-size: 0.85em;'>");
                sb.Append(HttpUtility.HtmlEncode(commandLine));
                sb.Append(@"</pre>");
            }

            if (!string.IsNullOrWhiteSpace(envVars)) {
                sb.Append(@"
    <h4>Fixed Environment Variables:</h4>
    <pre style='background-color: #f5f5f5; padding: 12px; border: 1px solid var(--border-color); border-radius: 4px; overflow-x: auto; font-size: 0.85em;'>");
                sb.Append(HttpUtility.HtmlEncode(envVars));
                sb.Append(@"</pre>");
            }

            sb.Append(@"
  </div>");
        }
        private void BuildMetricSection(StringBuilder sb, FullAnalysisReportData analysisData, string sectionTitle) {
            string sectionId = sectionTitle.ToLowerInvariant().Replace(" ", "-").Replace("'", "").Replace(":", "").Replace("(", "").Replace(")", "").Replace(".", "");
            sb.Append(@$"
  <div class='section metric-section' id='{sectionId}'>
    <h2>{HttpUtility.HtmlEncode(sectionTitle)}</h2>
    <p class='summary-item'><strong>S/N Ratio Type Used:</strong> {analysisData.SnTypeUsed}</p>");

            BuildAnalysisWarnings(sb, analysisData.InitialAnova?.AnalysisWarnings, "Initial ANOVA");
            BuildAnalysisWarnings(sb, analysisData.PooledAnova?.AnalysisWarnings, "Pooled ANOVA");

            sb.Append(@"
    <h3>Optimal Configuration:</h3>
    <div style='display: flex; flex-wrap: wrap; gap: 20px; align-items: flex-start;'>
      <div style='flex: 1; min-width: 300px;'>
        <table>
          <thead><tr><th>Parameter</th><th>Optimal Value</th><th>OA Level</th></tr></thead>
          <tbody>");
            foreach (var item in analysisData.OptimalConfig.Settings.OrderBy(kv => kv.Key)) {
                sb.Append(@$"<tr class='optimal-param'><td>{HttpUtility.HtmlEncode(item.Key)}</td><td>{HttpUtility.HtmlEncode(item.Value.Value)}</td><td>{item.Value.OALevel.Level}</td></tr>");
            }
            sb.Append(@"
          </tbody>
        </table>
      </div>");

            if (analysisData.PredictedPerformance != null) {
                sb.Append(@$"
      <div style='flex: 1; min-width: 300px;'>
        <h4>Predicted Performance (at Optimal)</h4>
        <table>
          <tbody>
            <tr><td><strong>Predicted Value:</strong></td><td>{analysisData.PredictedPerformance.PredictedValue:F4}</td></tr>
            <tr><td><strong>95% CI Lower:</strong></td><td>{analysisData.PredictedPerformance.LowerBound:F4}</td></tr>
            <tr><td><strong>95% CI Upper:</strong></td><td>{analysisData.PredictedPerformance.UpperBound:F4}</td></tr>
          </tbody>
        </table>");
                BuildPredictionNotes(sb, analysisData.PredictedPerformance.PredictionNotes, analysisData.MetricAnalyzed);
                sb.Append(@"
      </div>");
            }
            sb.Append(@"
    </div>"); // End flexbox

            sb.Append(@"
    <h3>ANOVA Results:</h3>");
            BuildSingleAnovaTable(sb, analysisData.InitialAnova, "Initial ANOVA");
            BuildAnovaContributionChart(sb, analysisData.InitialAnova, "Initial ANOVA Contributions", $"{sectionId}_initial_contrib");

            if (analysisData.PooledAnova != null) {
                BuildSingleAnovaTable(sb, analysisData.PooledAnova, "Pooled ANOVA");
                sb.Append(@$"<p><strong>Sources Pooled into Error:</strong> {HttpUtility.HtmlEncode(string.Join(", ", analysisData.PooledAnova.PooledSources.DefaultIfEmpty("None")))}</p>");
                BuildAnovaContributionChart(sb, analysisData.PooledAnova, "Pooled ANOVA Contributions", $"{sectionId}_pooled_contrib");
            }

            sb.Append(@"
    <h3>Analysis Charts:</h3>
    <div class='plot-grid'>");
            BuildMainEffectsPlots(sb, analysisData);
            BuildInteractionPlots(sb, analysisData);
            BuildNormalPlotsSection(sb, analysisData);
            sb.Append(@"
    </div>"); // End plot-grid

            BuildRawDataCollapsibleSections(sb, analysisData);
            sb.Append(@"
  </div>"); // End metric-section
        }

        private void BuildAnalysisWarnings(StringBuilder sb, List<string> warnings, string context) {
            if (warnings != null && warnings.Any()) {
                sb.Append(@$"
    <div class='analysis-warning'>
      <strong>Analysis Warnings ({HttpUtility.HtmlEncode(context)}):</strong>
      <ul>");
                foreach (var warning in warnings) {
                    sb.Append(@$"<li>{HttpUtility.HtmlEncode(warning)}</li>");
                }
                sb.Append(@"
      </ul>
    </div>");
            }
        }

        private void BuildPredictionNotes(StringBuilder sb, List<string> notes, string context) {
            if (notes != null && notes.Any()) {
                sb.Append(@$"
        <div class='analysis-warning' style='background-color: #e9ecef; border-color: #ced4da; color: #495057;'>
          <strong>Prediction Notes ({HttpUtility.HtmlEncode(context)}):</strong>
          <ul>");
                foreach (var note in notes) {
                    sb.Append(@$"<li><em>{HttpUtility.HtmlEncode(note)}</em></li>");
                }
                sb.Append(@"
          </ul>
        </div>");
            }
        }

        private void BuildSingleAnovaTable(StringBuilder sb, AnovaAnalysisResult anovaResult, string title) {
            if (anovaResult == null || anovaResult.AnovaTable == null || !anovaResult.AnovaTable.Any()) {
                sb.Append(@$"<h4>{HttpUtility.HtmlEncode(title)}</h4><p>No ANOVA data available or calculated.</p>");
                return;
            }
            sb.Append(@$"
    <h4>{HttpUtility.HtmlEncode(title)}</h4>
    <div style='overflow-x: auto;'>
      <table>
        <thead>
          <tr>
            <th>Source</th><th>SS</th><th>DF</th><th>MS</th><th>F-Value</th><th>P-Value</th><th>Contrib (%)</th><th>Significant (α=0.05)</th>
          </tr>
        </thead>
        <tbody>");
            foreach (var row in anovaResult.AnovaTable.OrderByDescending(r => r.Source == AnovaResult.ErrorSource || r.Source.Contains("(Pooled)") ? double.MinValue : r.ContributionPercentage)) {
                string significanceClass = row.Source.Contains(AnovaResult.ErrorSource) ? "" : (row.IsSignificant ? "significant" : "not-significant");
                sb.Append(@$"
          <tr>
            <td>{HttpUtility.HtmlEncode(row.Source)}</td>
            <td>{row.SumOfSquares:F4}</td>
            <td>{row.DegreesOfFreedom}</td>
            <td>{(double.IsNaN(row.MeanSquare) ? "N/A" : row.MeanSquare.ToString("F4"))}</td>
            <td>{(double.IsNaN(row.FValue) ? "N/A" : row.FValue.ToString("F2"))}</td>
            <td>{(double.IsNaN(row.PValue) ? "N/A" : row.PValue.ToString("F4"))}</td>
            <td>{row.ContributionPercentage:F2}</td>
            <td class='{significanceClass}'>{(row.Source.Contains(AnovaResult.ErrorSource) ? "N/A" : (row.IsSignificant ? "Yes" : "No"))}</td>
          </tr>");
            }
            sb.Append(@$"
        </tbody>
        <tfoot>
          <tr><td colspan='2'><strong>Total SS:</strong> {anovaResult.TotalSumOfSquares:F4}</td><td colspan='6'><strong>Total DF:</strong> {anovaResult.TotalDegreesOfFreedom}</td></tr>
        </tfoot>
      </table>
    </div>");
        }

        private void BuildAnovaContributionChart(StringBuilder sb, AnovaAnalysisResult anovaResult, string title, string canvasIdSuffix) {
            if (anovaResult == null || anovaResult.AnovaTable == null || !anovaResult.AnovaTable.Any()) {
                return;
            }

            var chartData = anovaResult.AnovaTable
                .Where(r => r.ContributionPercentage > 0.01 && !r.Source.Contains(AnovaResult.ErrorSource))
                .OrderByDescending(r => r.ContributionPercentage)
                .ToList();

            if (!chartData.Any()) {
                return;
            }

            var labels = chartData.Select(r => $"'{HttpUtility.JavaScriptStringEncode(r.Source)}'").ToList();
            var dataValues = chartData.Select(r => r.ContributionPercentage.ToString("F2", CultureInfo.InvariantCulture)).ToList();
            string canvasId = $"anovaContribChart_{canvasIdSuffix}";

            sb.Append(@$"
      <div class='plot-container'>
        <h4>{HttpUtility.HtmlEncode(title)}</h4>
        <div class='chart-wrapper' style='height: {Math.Max(150, chartData.Count * 30 + 50)}px;'>
          <canvas id='{canvasId}'></canvas>
        </div>
        <script>
        (function() {{
          const ctx = document.getElementById('{canvasId}').getContext('2d');
          new Chart(ctx, {{
            type: 'bar',
            data: {{ 
              labels: [{string.Join(",", labels)}], 
              datasets: [{{ 
                label: 'Contribution (%)', 
                data: [{string.Join(",", dataValues)}], 
                backgroundColor: 'rgba(44, 111, 187, 0.7)', 
                borderColor: 'rgba(44, 111, 187, 1)', 
                borderWidth: 1 
              }}] 
            }},
            options: {{ 
              indexAxis: 'y', 
              responsive: true, 
              maintainAspectRatio: false, 
              plugins: {{ 
                legend: {{ display: false }}, 
                tooltip: {{ 
                  backgroundColor: 'rgba(0,0,0,0.7)', 
                  titleFont: {{size:14}}, 
                  bodyFont: {{size:13}}, 
                  padding: 10, 
                  cornerRadius: 4 
                }} 
              }}, 
              scales: {{ 
                x: {{ 
                  title: {{ 
                    display: true, 
                    text: 'Contribution (%)' 
                  }} 
                }}, 
                y: {{ 
                  ticks: {{ autoSkip: false }} 
                }} 
              }} 
            }}
          }});
        }})();
        </script>
      </div>");
        }

        private void BuildMainEffectsPlots(StringBuilder sb, FullAnalysisReportData analysisData) {
            if (analysisData.MainEffects == null || !analysisData.MainEffects.Any()) {
                sb.Append("<div class='plot-container'><h4>Main Effects Plots</h4><p>No main effects data available.</p></div>");
                return;
            }

            string metricId = analysisData.MetricAnalyzed.ToLowerInvariant().Replace(" ", "").Replace(".", "");

            foreach (var paramName in analysisData.MainEffects.Keys.OrderBy(k => k)) {
                var effectData = analysisData.MainEffects[paramName];
                var orderedLevelsSn = effectData.EffectsByLevelSn.OrderBy(kvp => kvp.Key.OALevel.Level).ToList();
                var orderedLevelsRaw = effectData.EffectsByLevelRaw.OrderBy(kvp => kvp.Key.OALevel.Level).ToList();
                var labels = orderedLevelsSn.Select(kvp => $"'{HttpUtility.JavaScriptStringEncode(kvp.Key.Value)}'").ToList();
                var dataValuesSn = orderedLevelsSn.Select(kvp => kvp.Value.ToString("F4", CultureInfo.InvariantCulture)).ToList();
                var dataValuesRaw = orderedLevelsRaw.Select(kvp => kvp.Value.ToString("F4", CultureInfo.InvariantCulture)).ToList();
                string canvasId = $"mainEffectChart_{metricId}_{HttpUtility.UrlEncode(paramName).Replace('%', '_')}";

                sb.Append(@$"
      <div class='plot-container'>
        <h4>Main Effect: {HttpUtility.HtmlEncode(paramName)}</h4>
        <div class='chart-wrapper'><canvas id='{canvasId}'></canvas></div>
        <script>
        (function() {{
          const ctx = document.getElementById('{canvasId}').getContext('2d');
          new Chart(ctx, {{
            type: 'line',
            data: {{
              labels: [{string.Join(",", labels)}],
              datasets: [
                {{ 
                  label: 'S/N Ratio', 
                  yAxisID: 'ySn', 
                  data: [{string.Join(",", dataValuesSn)}], 
                  borderColor: 'rgba(44, 111, 187, 0.8)', 
                  backgroundColor: 'rgba(44, 111, 187, 0.1)', 
                  borderWidth: 2, 
                  pointRadius: 5, 
                  pointHoverRadius: 7, 
                  tension: 0.1, 
                  fill: false 
                }},
                {{ 
                  label: 'Avg Raw Metric', 
                  yAxisID: 'yRaw', 
                  data: [{string.Join(",", dataValuesRaw)}], 
                  borderColor: 'rgba(220, 53, 69, 0.8)', 
                  backgroundColor: 'rgba(220, 53, 69, 0.1)', 
                  borderWidth: 2, 
                  pointRadius: 5, 
                  pointHoverRadius: 7, 
                  tension: 0.1, 
                  fill: false, 
                  borderDash: [5, 5] 
                }}
              ]
            }},
            options: {{ 
              responsive: true, 
              maintainAspectRatio: false, 
              interaction: {{ mode: 'index', intersect: false }}, 
              plugins: {{ 
                legend: {{ position: 'top' }}, 
                tooltip: {{ 
                  backgroundColor: 'rgba(0,0,0,0.7)', 
                  titleFont: {{size:14}}, 
                  bodyFont: {{size:13}}, 
                  padding: 10, 
                  cornerRadius: 4, 
                  boxPadding: 3 
                }} 
              }},
              scales: {{ 
                ySn: {{ 
                  type: 'linear', 
                  display: true, 
                  position: 'left', 
                  title: {{ 
                    display: true, 
                    text: 'S/N Ratio', 
                    font: {{weight:'bold'}} 
                  }}, 
                  ticks: {{precision:2}} 
                }}, 
                yRaw: {{ 
                  type: 'linear', 
                  display: true, 
                  position: 'right', 
                  title: {{ 
                    display: true, 
                    text: 'Avg Raw Metric', 
                    font: {{weight:'bold'}} 
                  }}, 
                  ticks: {{precision:2}}, 
                  grid: {{ drawOnChartArea: false }} 
                }}, 
                x: {{ 
                  title: {{ 
                    display: true, 
                    text: '{HttpUtility.JavaScriptStringEncode(paramName)}', 
                    font: {{weight:'bold'}} 
                  }} 
                }} 
              }}
            }}
          }});
        }})();
        </script>
      </div>");
            }
        }

        private void BuildInteractionPlots(StringBuilder sb, FullAnalysisReportData analysisData) {
            if (analysisData.InteractionEffectsSn == null || !analysisData.InteractionEffectsSn.Any()) {
                sb.Append("<div class='plot-container'><h4>Interaction Plots</h4><p>No interaction effects data available.</p></div>");
                return;
            }

            string metricId = analysisData.MetricAnalyzed.ToLowerInvariant().Replace(" ", "").Replace(".", "");
            var plotColors = new[] {
                "rgba(44, 111, 187, 0.8)",
                "rgba(220, 53, 69, 0.8)",
                "rgba(40, 167, 69, 0.8)",
                "rgba(255, 193, 7, 0.8)",
                "rgba(111, 66, 193, 0.8)",
                "rgba(23, 162, 184, 0.8)"
            };

            foreach (var intKey in analysisData.InteractionEffectsSn.Keys.OrderBy(k => k)) {
                var interactionEffect = analysisData.InteractionEffectsSn[intKey];
                string[] factorNames = intKey.Split('*');

                if (factorNames.Length != 2) {
                    continue;
                }

                string factor1Name = factorNames[0];
                string factor2Name = factorNames[1];
                var factor1Def = _config.ControlFactors.FirstOrDefault(p => p.Name == factor1Name);
                var factor2Def = _config.ControlFactors.FirstOrDefault(p => p.Name == factor2Name);

                if (factor1Def == null || factor2Def == null) {
                    continue;
                }

                var factor1LevelsOrdered = factor1Def.Levels.Values.OrderBy(l => l.OALevel.Level).ToList();
                var factor2LevelsOrdered = factor2Def.Levels.Values.OrderBy(l => l.OALevel.Level).ToList();
                string canvasId = $"interactionChart_{metricId}_{HttpUtility.UrlEncode(intKey).Replace('%', '_')}";
                string collapsibleId = $"interactionHeatmap_{metricId}_{HttpUtility.UrlEncode(intKey).Replace('%', '_')}";

                sb.Append(@$"
      <div class='plot-container'>
        <h4>Interaction: {HttpUtility.HtmlEncode(factor1Name)} × {HttpUtility.HtmlEncode(factor2Name)} (S/N Ratio)</h4>
        <div class='chart-wrapper'><canvas id='{canvasId}'></canvas></div>
        <script>
        (function() {{
          const ctx = document.getElementById('{canvasId}').getContext('2d');
          new Chart(ctx, {{ 
            type: 'line', 
            data: {{
              labels: [{string.Join(",", factor1LevelsOrdered.Select(l => $"'{HttpUtility.JavaScriptStringEncode(l.Value)}'"))}], 
              datasets: [");

                int colorIdx = 0;
                foreach (var levelF2 in factor2LevelsOrdered) {
                    var dataValuesForLine = factor1LevelsOrdered.Select(levelF1 =>
                        interactionEffect.EffectsByLevelPair.TryGetValue((levelF1, levelF2), out double snVal) && !double.IsNaN(snVal)
                        ? snVal.ToString("F4", CultureInfo.InvariantCulture) : "null").ToList();

                    sb.Append(@$"{{ 
                label: '{HttpUtility.JavaScriptStringEncode(factor2Name)} = {HttpUtility.JavaScriptStringEncode(levelF2.Value)}', 
                data: [{string.Join(",", dataValuesForLine)}], 
                borderColor: '{plotColors[colorIdx % plotColors.Length]}', 
                borderWidth: 2, 
                pointRadius: 4, 
                pointHoverRadius: 6, 
                tension: 0.1, 
                fill: false 
              }}{(colorIdx < factor2LevelsOrdered.Count - 1 ? "," : "")}");

                    colorIdx++;
                }

                sb.Append(@$"
              ]
            }}, 
            options: {{ 
              responsive: true, 
              maintainAspectRatio: false, 
              plugins: {{ 
                legend: {{ 
                  position: 'top', 
                  align: 'center', 
                  labels: {{ 
                    boxWidth: 12, 
                    usePointStyle: true, 
                    padding: 10 
                  }} 
                }}, 
                tooltip: {{
                  backgroundColor: 'rgba(0,0,0,0.7)', 
                  titleFont: {{size:14}}, 
                  bodyFont: {{size:13}}, 
                  padding: 10, 
                  cornerRadius: 4
                }} 
              }}, 
              scales: {{ 
                y: {{ 
                  title: {{ 
                    display: true, 
                    text: 'S/N Ratio', 
                    font: {{weight:'bold'}} 
                  }}, 
                  ticks: {{precision:2}} 
                }}, 
                x: {{ 
                  title: {{ 
                    display: true, 
                    text: '{HttpUtility.JavaScriptStringEncode(factor1Name)}', 
                    font: {{weight:'bold'}} 
                  }} 
                }} 
              }} 
            }}
          }});
        }})();
        </script>");

                BuildInteractionHeatmapTable(sb, interactionEffect, factor1LevelsOrdered, factor2LevelsOrdered, factor1Name, factor2Name, collapsibleId);

                sb.Append(@"
      </div>");
            }
        }

        private void BuildInteractionHeatmapTable(StringBuilder sb, ParameterInteractionEffect effectData, List<Level> factor1Levels, List<Level> factor2Levels, string factor1Name, string factor2Name, string collapsibleId) {
            sb.Append(@$"
        <button type='button' class='collapsible-btn' data-target='{collapsibleId}'>
          Show/Hide Interaction Heatmap: {HttpUtility.HtmlEncode(factor1Name)} × {HttpUtility.HtmlEncode(factor2Name)}
        </button>
        <div class='collapsible-content' id='{collapsibleId}'>
          <p><small>Heatmap of S/N Ratios. Colors indicate relative S/N strength (darker green = higher/better S/N).</small></p>
          <table class='heatmap-table'>
            <thead>
              <tr>
                <th>{HttpUtility.HtmlEncode(factor1Name)} \ {HttpUtility.HtmlEncode(factor2Name)}</th>");

            foreach (var levelF2 in factor2Levels) {
                sb.Append(@$"<th>{HttpUtility.HtmlEncode(levelF2.Value)}</th>");
            }

            sb.Append(@"
              </tr>
            </thead>
            <tbody>");

            var allSnValues = effectData.EffectsByLevelPair.Values.Where(v => !double.IsNaN(v)).ToList();
            double minSn = allSnValues.Any() ? allSnValues.Min() : 0;
            double maxSn = allSnValues.Any() ? allSnValues.Max() : 0;
            double rangeSn = (maxSn - minSn) > 1e-6 ? (maxSn - minSn) : 1.0;

            foreach (var levelF1 in factor1Levels) {
                sb.Append(@$"
              <tr>
                <td><strong>{HttpUtility.HtmlEncode(levelF1.Value)}</strong></td>");

                foreach (var levelF2 in factor2Levels) {
                    effectData.EffectsByLevelPair.TryGetValue((levelF1, levelF2), out double snVal);
                    string cellStyle = "";

                    if (!double.IsNaN(snVal)) {
                        double normalized = rangeSn == 0 ? 0.5 : (snVal - minSn) / rangeSn; // Avoid div by zero if all values same
                        int lightness = (int)(90 - (normalized * 40));
                        cellStyle = $"style='background-color: hsl(120, 60%, {lightness}%); color: {(lightness < 70 ? "white" : "black")};'";
                    }

                    sb.Append(@$"
                <td {cellStyle}>{(double.IsNaN(snVal) ? "N/A" : snVal.ToString("F2"))}</td>");
                }

                sb.Append(@"
              </tr>");
            }

            sb.Append(@"
            </tbody>
          </table>
        </div>");
        }

        private void BuildNormalPlotsSection(StringBuilder sb, FullAnalysisReportData analysisData) {
            if (analysisData.EffectEstimates == null || !analysisData.EffectEstimates.Any()) {
                sb.Append("<div class='plot-container'><h4>Normal Probability Plot of Effects</h4><p>No effect estimates available (typically for 2-level factors/interactions only).</p></div>");
                return;
            }

            string metricId = analysisData.MetricAnalyzed.ToLowerInvariant().Replace(" ", "").Replace(".", "");
            var sortedEffects = analysisData.EffectEstimates.OrderBy(e => e.Effect).ToList();
            var dataPoints = new List<string>();
            int N = sortedEffects.Count;

            if (N > 0) {
                for (int i = 0; i < N; i++) {
                    double p_i = (i + 0.5) / N; // Using (i+0.5)/N for plotting position
                    double z_i = MathNet.Numerics.Distributions.Normal.InvCDF(0, 1, p_i);
                    dataPoints.Add($"{{ x: {z_i.ToString("F4", CultureInfo.InvariantCulture)}, y: {sortedEffects[i].Effect.ToString("F4", CultureInfo.InvariantCulture)}, label: '{HttpUtility.JavaScriptStringEncode(sortedEffects[i].Source)}' }}");
                }
            }

            string canvasId = $"normalPlotEffects_{metricId}";

            sb.Append(@$"
      <div class='plot-container'>
        <h4>Normal Probability Plot of Effects (S/N Scale)</h4>
        <div class='chart-wrapper'><canvas id='{canvasId}'></canvas></div>
        <script>
        (function() {{
          const ctx = document.getElementById('{canvasId}').getContext('2d');
          new Chart(ctx, {{ 
            type: 'scatter', 
            data: {{ 
              datasets: [{{
                label: 'Effect Estimates', 
                data: [{string.Join(",", dataPoints)}],
                backgroundColor: 'rgba(220, 53, 69, 0.7)', 
                pointRadius: 6, 
                pointHoverRadius: 8, 
                borderColor: 'rgba(220, 53, 69, 0.9)', 
                borderWidth: 1
              }}]
            }}, 
            options: {{ 
              responsive: true, 
              maintainAspectRatio: false, 
              plugins: {{ 
                legend: {{ display: false }}, 
                tooltip: {{ 
                  callbacks: {{ 
                    label: function(context) {{ 
                      return context.raw.label + ': (' + context.parsed.x.toFixed(4) + ', ' + context.parsed.y.toFixed(4) + ')'; 
                    }} 
                  }}, 
                  backgroundColor: 'rgba(0,0,0,0.7)', 
                  titleFont: {{size:14}}, 
                  bodyFont: {{size:13}}, 
                  padding: 10, 
                  cornerRadius: 4 
                }} 
              }}, 
              scales: {{ 
                x: {{ 
                  title: {{ 
                    display: true, 
                    text: 'Normal Quantile (Z-score)', 
                    font: {{weight:'bold'}} 
                  }} 
                }}, 
                y: {{ 
                  title: {{ 
                    display: true, 
                    text: 'Effect Estimate (S/N)', 
                    font: {{weight:'bold'}} 
                  }} 
                }} 
              }} 
            }}
          }});
        }})();
        </script>
      </div>");
        }

        private void BuildRawDataCollapsibleSections(StringBuilder sb, FullAnalysisReportData analysisData) {
            // Table for Main Effects (S/N and Raw)
            if (analysisData.MainEffects != null && analysisData.MainEffects.Any()) {
                string mainEffectsId = $"mainEffects_{analysisData.MetricAnalyzed.ToLowerInvariant().Replace(" ", "_")}";

                sb.Append(@$"
    <button type='button' class='collapsible-btn' data-target='{mainEffectsId}'>
      Show/Hide Main Effects Data Table ({HttpUtility.HtmlEncode(analysisData.MetricAnalyzed)})
    </button>
    <div class='collapsible-content' id='{mainEffectsId}'>
      <table>
        <thead>
          <tr>
            <th>Parameter</th>
            <th>Level</th>
            <th>Avg S/N</th>
            <th>Avg Raw Metric</th>
          </tr>
        </thead>
        <tbody>");

                foreach (var paramEntry in analysisData.MainEffects.OrderBy(p => p.Key)) {
                    bool first = true;

                    foreach (var levelSnEntry in paramEntry.Value.EffectsByLevelSn.OrderBy(l => l.Key.OALevel.Level)) {
                        paramEntry.Value.EffectsByLevelRaw.TryGetValue(levelSnEntry.Key, out double rawVal);

                        sb.Append(@$"
          <tr>
            <td>{(first ? HttpUtility.HtmlEncode(paramEntry.Key) : "")}</td>
            <td>{HttpUtility.HtmlEncode(levelSnEntry.Key.Value)}</td>
            <td>{levelSnEntry.Value:F4}</td>
            <td>{(double.IsNaN(rawVal) ? "N/A" : rawVal.ToString("F4"))}</td>
          </tr>");

                        first = false;
                    }
                }

                sb.Append(@"
        </tbody>
      </table>
    </div>");
            }

            // Table for Interaction Effects (S/N only)
            if (analysisData.InteractionEffectsSn != null && analysisData.InteractionEffectsSn.Any()) {
                string interactionEffectsId = $"interactionEffects_{analysisData.MetricAnalyzed.ToLowerInvariant().Replace(" ", "_")}";

                sb.Append(@$"
    <button type='button' class='collapsible-btn' data-target='{interactionEffectsId}'>
      Show/Hide Interaction Effects Data Table (S/N - {HttpUtility.HtmlEncode(analysisData.MetricAnalyzed)})
    </button>
    <div class='collapsible-content' id='{interactionEffectsId}'>
      <table>
        <thead>
          <tr>
            <th>Interaction</th>
            <th>Level (F1)</th>
            <th>Level (F2)</th>
            <th>Avg S/N</th>
          </tr>
        </thead>
        <tbody>");

                foreach (var intEntry in analysisData.InteractionEffectsSn.OrderBy(i => i.Key)) {
                    bool first = true;

                    foreach (var levelPairEntry in intEntry.Value.EffectsByLevelPair
                        .OrderBy(l => l.Key.Item1.OALevel.Level)
                        .ThenBy(l => l.Key.Item2.OALevel.Level)) {

                        sb.Append(@$"
          <tr>
            <td>{(first ? HttpUtility.HtmlEncode(intEntry.Key) : "")}</td>
            <td>{HttpUtility.HtmlEncode(levelPairEntry.Key.Item1.Value)}</td>
            <td>{HttpUtility.HtmlEncode(levelPairEntry.Key.Item2.Value)}</td>
            <td>{levelPairEntry.Value:F4}</td>
          </tr>");

                        first = false;
                    }
                }

                sb.Append(@"
        </tbody>
      </table>
    </div>");
            }
        }

        private void BuildExperimentalRunsTable(StringBuilder sb) {
            if (_rawMetricsPerRun == null || !_rawMetricsPerRun.Any() || !_oaConfigurations.Any()) {
                sb.Append("<div class='section'><h2>Experimental Run Details</h2><p>No detailed raw experimental run data available or OA configurations missing.</p></div>");
                return;
            }

            var allMetricNames = _config.MetricsToAnalyze.Select(m => m.Name).ToList();

            sb.Append(@$"
  <div class='section' id='experimental-runs'>
    <h2>Experimental Run Details</h2>
    <p>This table shows data averaged over {_config.Repetitions} repetition(s) for each OA run configuration. S/N Ratios are from the respective metric's analysis.</p>
    <div style='overflow-x: auto;'>
      <table>
        <thead>
          <tr>
            <th>OA Run #</th>
            <th>Configuration</th>");

            foreach (string metricName in allMetricNames) {
                sb.Append(@$"<th>Avg '{HttpUtility.HtmlEncode(metricName)}'</th><th>S/N '{HttpUtility.HtmlEncode(metricName)}'</th>");
            }

            sb.Append(@"
            <th>Repetition Details</th>
          </tr>
        </thead>
        <tbody>");

            for (int oaRunIndex = 0; oaRunIndex < _oaConfigurations.Count; oaRunIndex++) {
                ParameterSettings currentConfig = _oaConfigurations[oaRunIndex];
                string repContentId = $"repContent_{oaRunIndex}";

                sb.Append(@$"
          <tr>
            <td>{oaRunIndex + 1}</td>
            <td><pre style='margin:0; padding:5px; font-size:0.8em;'>{string.Join("<br>", currentConfig.Settings.OrderBy(s => s.Key).Select(s => $"{HttpUtility.HtmlEncode(s.Key)}: {HttpUtility.HtmlEncode(s.Value.Value)}"))}</pre></td>");

                foreach (string metricName in allMetricNames) {
                    double avgMetricValue = double.NaN;
                    double snRatioForMetric = double.NaN;

                    if (_rawMetricsPerRun.TryGetValue(oaRunIndex, out var repetitionsForThisOARun)) {
                        var metricValuesThisRun = repetitionsForThisOARun
                            .Select(repData => repData.TryGetValue(metricName, out double val) ? val : double.NaN)
                            .Where(val => !double.IsNaN(val))
                            .ToList();

                        if (metricValuesThisRun.Any()) {
                            avgMetricValue = metricValuesThisRun.Average();
                        }
                    }

                    var analysisForThisMetric = _analysisResultsList.FirstOrDefault(ar => ar.MetricAnalyzed == metricName);
                    var runDetailForThisMetric = analysisForThisMetric?.ExperimentRunDetails?.FirstOrDefault(rd => rd.RunNumber == oaRunIndex + 1);

                    if (runDetailForThisMetric != null) {
                        snRatioForMetric = runDetailForThisMetric.SnRatioValue;
                    }

                    sb.Append(@$"
            <td>{(double.IsNaN(avgMetricValue) ? "N/A" : avgMetricValue.ToString("F4"))}</td>
            <td>{(double.IsNaN(snRatioForMetric) ? "N/A" : snRatioForMetric.ToString("F4"))}</td>");
                }

                // Collapsible Repetition Details button
                sb.Append(@$"
            <td>
              <button type='button' class='collapsible-btn' data-target='{repContentId}'>Show Reps</button>
            </td>
          </tr>
          <tr>
            <td colspan='{2 + allMetricNames.Count * 2 + 1}' style='padding:0;'>
              <div class='collapsible-content' id='{repContentId}'>
                <table class='details-table'>
                  <thead>
                    <tr>
                      <th>Rep #</th>");

                foreach (string metricName in allMetricNames) {
                    sb.Append(@$"<th>{HttpUtility.HtmlEncode(metricName)}</th>");
                }

                sb.Append(@"
                    </tr>
                  </thead>
                  <tbody>");

                if (_rawMetricsPerRun.TryGetValue(oaRunIndex, out var repsData)) {
                    for (int repIdx = 0; repIdx < repsData.Count; repIdx++) {
                        sb.Append(@$"
                    <tr>
                      <td>{repIdx + 1}</td>");

                        foreach (string metricName in allMetricNames) {
                            repsData[repIdx].TryGetValue(metricName, out double val);
                            sb.Append(@$"
                      <td>{(double.IsNaN(val) ? "N/A" : val.ToString("F4"))}</td>");
                        }

                        sb.Append(@"
                    </tr>");
                    }
                } else {
                    sb.Append(@$"
                    <tr>
                      <td colspan='{1 + allMetricNames.Count}'>No repetition data recorded for this OA run.</td>
                    </tr>");
                }

                sb.Append(@"
                  </tbody>
                </table>
              </div>
            </td>
          </tr>");
            }

            sb.Append(@"
        </tbody>
      </table>
    </div>
  </div>");
        }

        private void BuildHtmlFooter(StringBuilder sb) {
            sb.Append(@$"
  <div style='text-align: center; margin-top: 40px; color: #6c757d; font-size: 0.9em;'>
    <p>Generated by TaguchiBench Engine on {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>
  </div>
<script>
  document.addEventListener('DOMContentLoaded', function() {{
    // Get all collapsible buttons
    const collapsibleButtons = document.querySelectorAll('.collapsible-btn');
    
    // Add click event listener to each button
    collapsibleButtons.forEach(button => {{
      button.addEventListener('click', function() {{
        // Toggle active class on the button
        this.classList.toggle('active');
        
        // Get the target content element
        const targetId = this.getAttribute('data-target');
        const content = document.getElementById(targetId);
        
        if (!content) {{
          console.error(`Content element with id ${{targetId}} not found`);
          return;
        }}
        
        // Toggle the content visibility
        if (content.style.maxHeight) {{
          content.style.maxHeight = null;
        }} else {{
          // Calculate proper height - for tables, include the table height
          const contentHeight = getContentFullHeight(content);
          content.style.maxHeight = contentHeight + 'px';
        }}
      }});
    }});
    
    // Helper function to calculate the proper height for collapsible content
    function getContentFullHeight(element) {{
      // Clone the element to measure its natural height
      const clone = element.cloneNode(true);
      
      // Set properties for measurement
      clone.style.maxHeight = 'none';
      clone.style.opacity = '0';
      clone.style.position = 'absolute';
      clone.style.pointerEvents = 'none';
      
      // Add to DOM to measure
      document.body.appendChild(clone);
      
      // Measure
      const height = clone.scrollHeight;
      
      // Remove from DOM
      document.body.removeChild(clone);
      
      return height;
    }}
  }});
</script>
</div>
</body>
</html>");
        }
    }
}