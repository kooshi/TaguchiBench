// TaguchiBench.Engine/Core/ExperimentStateYamlConverter.cs

using System;
using System.Collections.Generic;
using System.Linq;
using TaguchiBench.Engine.Configuration; // For EngineConfiguration
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace TaguchiBench.Engine.Core {
    public class ExperimentStateYamlConverter : IYamlTypeConverter {
        // Define keys used in YAML for consistency
        private const string ConfigurationKey = "configuration";
        private const string ArrayDesignKey = "arrayDesign";
        private const string OriginalConfigHashKey = "originalConfigHash";
        private const string NextRunIndexToExecuteKey = "nextRunIndexToExecute";
        private const string RawMetricsPerRunKey = "rawMetricsPerRun";
        private const string AnalysisResultsKey = "analysisResults"; // Changed from AnalysisBundle
        private const string LastUpdatedKey = "lastUpdated";
        private const string EngineVersionKey = "engineVersion";
        private const string HtmlReportPathKey = "htmlReportPath";
        private const string MarkdownReportPathKey = "markdownReportPath";


        public bool Accepts(Type type) {
            return type == typeof(ExperimentState);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            var state = new ExperimentState(); // Create instance to populate

            parser.Consume<MappingStart>();
            while (!parser.TryConsume<MappingEnd>(out _)) {
                var keyScalar = parser.Consume<Scalar>();
                string key = keyScalar.Value;

                switch (key) {
                    case ConfigurationKey:
                        // Deserialize the 'configuration' block into EngineConfigDto first,
                        // then construct EngineConfiguration from it.
                        var configDto = (EngineConfigDto)rootDeserializer(typeof(EngineConfigDto));
                        state.Configuration = new EngineConfiguration(configDto); // Assumes EngineConfiguration constructor takes DTO
                        break;
                    case ArrayDesignKey:
                        state.ArrayDesign = (OrthogonalArrayDesign)rootDeserializer(typeof(OrthogonalArrayDesign));
                        break;
                    case OriginalConfigHashKey:
                        state.OriginalConfigHash = parser.Consume<Scalar>().Value;
                        break;
                    case NextRunIndexToExecuteKey:
                        state.NextRunIndexToExecute = int.Parse(parser.Consume<Scalar>().Value);
                        break;
                    case RawMetricsPerRunKey:
                        state.RawMetricsPerRun = (Dictionary<int, List<Dictionary<string, double>>>)rootDeserializer(typeof(Dictionary<int, List<Dictionary<string, double>>>));
                        break;
                    case AnalysisResultsKey:
                        state.AnalysisResults = (List<FullAnalysisReportData>)rootDeserializer(typeof(List<FullAnalysisReportData>));
                        break;
                    case LastUpdatedKey:
                        state.LastUpdated = DateTime.Parse(parser.Consume<Scalar>().Value, null, System.Globalization.DateTimeStyles.RoundtripKind);
                        break;
                    case EngineVersionKey:
                        state.EngineVersion = parser.Consume<Scalar>().Value;
                        break;
                    case HtmlReportPathKey:
                        state.HtmlReportPath = parser.Consume<Scalar>().Value;
                        break;
                    case MarkdownReportPathKey:
                        state.MarkdownReportPath = parser.Consume<Scalar>().Value;
                        break;
                    default:
                        // Unknown key, skip its value to allow for future additions
                        parser.SkipThisAndNestedEvents();
                        break;
                }
            }
            return state;
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            var state = (ExperimentState)value;

            emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));

            // Serialize EngineConfiguration by converting to DTO first
            emitter.Emit(new Scalar(ConfigurationKey));
            EngineConfigDto configDto = EngineConfiguration.ConvertToDto(state.Configuration); // Needs a static public or internal method in EngineConfiguration
            rootSerializer(configDto, typeof(EngineConfigDto));

            emitter.Emit(new Scalar(ArrayDesignKey));
            rootSerializer(state.ArrayDesign, typeof(OrthogonalArrayDesign));

            if (!string.IsNullOrEmpty(state.OriginalConfigHash)) {
                emitter.Emit(new Scalar(OriginalConfigHashKey));
                emitter.Emit(new Scalar(state.OriginalConfigHash));
            }

            emitter.Emit(new Scalar(NextRunIndexToExecuteKey));
            emitter.Emit(new Scalar(state.NextRunIndexToExecute.ToString()));

            emitter.Emit(new Scalar(RawMetricsPerRunKey));
            rootSerializer(state.RawMetricsPerRun, typeof(Dictionary<int, List<Dictionary<string, double>>>));

            if (state.AnalysisResults != null && state.AnalysisResults.Any()) {
                emitter.Emit(new Scalar(AnalysisResultsKey));
                rootSerializer(state.AnalysisResults, typeof(List<FullAnalysisReportData>));
            }

            emitter.Emit(new Scalar(LastUpdatedKey));
            emitter.Emit(new Scalar(state.LastUpdated.ToString("o"))); // ISO 8601 format

            if (!string.IsNullOrEmpty(state.EngineVersion)) {
                emitter.Emit(new Scalar(EngineVersionKey));
                emitter.Emit(new Scalar(state.EngineVersion));
            }
            if (!string.IsNullOrEmpty(state.HtmlReportPath)) {
                emitter.Emit(new Scalar(HtmlReportPathKey));
                emitter.Emit(new Scalar(state.HtmlReportPath));
            }
            if (!string.IsNullOrEmpty(state.MarkdownReportPath)) {
                emitter.Emit(new Scalar(MarkdownReportPathKey));
                emitter.Emit(new Scalar(state.MarkdownReportPath));
            }

            emitter.Emit(new MappingEnd());
        }
    }
}