// TaguchiBench.Engine/Core/ExperimentState.cs

using System;
using System.Collections.Generic;
using TaguchiBench.Engine.Configuration; // For EngineConfiguration
// OrthogonalArrayDesign and FullAnalysisReportDataBundle are also in TaguchiBench.Engine.Core

namespace TaguchiBench.Engine.Core {
    /// <summary>
    /// Represents the complete state of a Taguchi experiment,
    /// facilitating recovery and comprehensive reporting.
    /// </summary>
    public class ExperimentState {
        /// <summary>
        /// The configuration used to initiate this experiment.
        /// </summary>
        public EngineConfiguration Configuration { get; set; }

        /// <summary>
        /// The orthogonal array design generated for this experiment,
        /// including the array itself and column assignments.
        /// </summary>
        public OrthogonalArrayDesign ArrayDesign { get; set; }

        /// <summary>
        /// A hash of the original configuration file content or critical settings,
        /// used to detect if a recovery attempt is being made with an incompatible configuration.
        /// </summary>
        public string OriginalConfigHash { get; set; }

        /// <summary>
        /// The index of the next orthogonal array run (0-based) to be executed.
        /// This is used for resuming experiments.
        /// </summary>
        public int NextRunIndexToExecute { get; set; }

        /// <summary>
        /// Stores the raw metrics collected from each repetition of each orthogonal array run.
        /// The outer dictionary key is the OA Run Index (0-based).
        /// The list contains one entry per repetition for that OA run.
        /// Each entry in the list is a dictionary mapping metric names (string) to their values (double).
        /// </summary>
        /// <example>
        /// RawMetricsPerRun[0] might be a list with one entry (for 1 repetition):
        ///   [ { "Score": 75.5, "Time": 120.3 } ]
        /// If Repetitions = 2, it might be:
        ///   [ { "Score": 75.5, "Time": 120.3 }, { "Score": 76.1, "Time": 119.8 } ]
        /// </example>
        public Dictionary<int, List<Dictionary<string, double>>> RawMetricsPerRun { get; set; } = new();

        /// <summary>
        /// Contains the results of the full Taguchi analysis (ANOVA, optimal configs, predictions)
        /// for each metric analyzed. This is populated after all experimental runs are complete.
        /// </summary>
        public List<FullAnalysisReportData> AnalysisResults { get; set; }

        /// <summary>
        /// Timestamp of when this state was last updated.
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Version of the TaguchiBench engine that created/updated this state file.
        /// </summary>
        public string EngineVersion { get; set; }

        /// <summary>
        /// Path to html report of the experiment.
        /// </summary>
        public string HtmlReportPath { get; set; }

        /// <summary>
        /// Path to markdown report of the experiment.
        /// </summary>
        public string MarkdownReportPath { get; set; }

        public ExperimentState() {
            LastUpdated = DateTime.UtcNow;
            // EngineVersion would typically be set upon creation/saving.
        }
    }
}