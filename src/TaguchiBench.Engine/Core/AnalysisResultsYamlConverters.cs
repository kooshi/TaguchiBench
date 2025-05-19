// TaguchiBench.Engine.Core/AnalysisResultsYamlConverters.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaguchiBench.Engine.Configuration; // For access to EngineConfiguration if needed for context
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace TaguchiBench.Engine.Core {

    // --- ParameterMainEffect Converter ---
    // In TaguchiBench.Engine.Core.AnalysisResultsYamlConverters.cs

    // Helper DTO for serializing/deserializing KeyValuePair<Level, double>
    public class LevelDoublePairDto {
        public Level Key { get; set; } // The Level object itself
        public double Value { get; set; }
    }
    // Helper DTO for serializing/deserializing ParameterMainEffect
    public class ParameterMainEffectDto {
        public List<LevelDoublePairDto> EffectsByLevelSn { get; set; }
        public List<LevelDoublePairDto> EffectsByLevelRaw { get; set; }
    }

    public class ParameterMainEffectYamlConverter : IYamlTypeConverter {
        public bool Accepts(Type type) {
            return type == typeof(ParameterMainEffect);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            var dto = (ParameterMainEffectDto)rootDeserializer(typeof(ParameterMainEffectDto));

            var effectsByLevelSn = dto.EffectsByLevelSn?
                .ToDictionary(pair => pair.Key, pair => pair.Value)
                ?? new Dictionary<Level, double>();

            var effectsByLevelRaw = dto.EffectsByLevelRaw?
                .ToDictionary(pair => pair.Key, pair => pair.Value)
                ?? new Dictionary<Level, double>();

            return new ParameterMainEffect(effectsByLevelSn, effectsByLevelRaw);
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            var mainEffect = (ParameterMainEffect)value;

            var dto = new ParameterMainEffectDto {
                EffectsByLevelSn = mainEffect.EffectsByLevelSn
                    .OrderBy(kvp => kvp.Key.OALevel.Level) // Ensure consistent order for readability
                    .Select(kvp => new LevelDoublePairDto { Key = kvp.Key, Value = kvp.Value })
                    .ToList(),
                EffectsByLevelRaw = mainEffect.EffectsByLevelRaw
                    .OrderBy(kvp => kvp.Key.OALevel.Level)
                    .Select(kvp => new LevelDoublePairDto { Key = kvp.Key, Value = kvp.Value })
                    .ToList()
            };

            rootSerializer(dto, typeof(ParameterMainEffectDto));
        }
    }

    // --- ParameterInteractionEffect Converter ---
    // In TaguchiBench.Engine.Core.AnalysisResultsYamlConverters.cs

    // Helper DTO for serializing/deserializing KeyValuePair<(Level, Level), double>
    public class LevelTupleDoublePairDto {
        public Level Level1 { get; set; }
        public Level Level2 { get; set; }
        public double SnValue { get; set; } // Changed name from "Value" to avoid confusion if Level had a "Value" property
    }

    public class ParameterInteractionEffectYamlConverter : IYamlTypeConverter {
        public bool Accepts(Type type) {
            return type == typeof(ParameterInteractionEffect);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            var dtoList = (List<LevelTupleDoublePairDto>)rootDeserializer(typeof(List<LevelTupleDoublePairDto>));

            var effectsByLevelPair = dtoList?
                .ToDictionary(dto => (dto.Level1, dto.Level2), dto => dto.SnValue)
                ?? new Dictionary<(Level Level1, Level Level2), double>();

            return new ParameterInteractionEffect(effectsByLevelPair);
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            var interactionEffect = (ParameterInteractionEffect)value;

            var dtoList = interactionEffect.EffectsByLevelPair
                .OrderBy(kvp => kvp.Key.Level1.OALevel.Level)
                .ThenBy(kvp => kvp.Key.Level2.OALevel.Level)
                .Select(kvp => new LevelTupleDoublePairDto {
                    Level1 = kvp.Key.Level1,
                    Level2 = kvp.Key.Level2,
                    SnValue = kvp.Value
                })
                .ToList();

            rootSerializer(dtoList, typeof(List<LevelTupleDoublePairDto>));
        }
    }

    // --- OptimalConfiguration Converter ---
    // In TaguchiBench.Engine.Core.AnalysisResultsYamlConverters.cs (or where it's defined)

    public class OptimalConfigurationYamlConverter : IYamlTypeConverter {
        public bool Accepts(Type type) {
            return type == typeof(OptimalConfiguration);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            // Expects a mapping of string to Level object representation
            var settings = (Dictionary<string, Level>)rootDeserializer(typeof(Dictionary<string, Level>));
            return new OptimalConfiguration(settings ?? new Dictionary<string, Level>());
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            var optimalConfig = (OptimalConfiguration)value;
            // Let the rootSerializer handle the dictionary. It will use LevelYamlConverter for Level objects.
            // Ensure settings are ordered for consistent output.
            var orderedSettings = optimalConfig.Settings
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            rootSerializer(orderedSettings, typeof(Dictionary<string, Level>));
        }
    }
    // --- ParameterSettings Converter (similar to OptimalConfiguration) ---
    // In TaguchiBench.Engine.Core.AnalysisResultsYamlConverters.cs (or where it's defined)

    public class ParameterSettingsYamlConverter : IYamlTypeConverter {
        public bool Accepts(Type type) {
            return type == typeof(ParameterSettings);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            var settings = (Dictionary<string, Level>)rootDeserializer(typeof(Dictionary<string, Level>));
            return new ParameterSettings(settings ?? new Dictionary<string, Level>());
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            var paramSettings = (ParameterSettings)value;
            var orderedSettings = paramSettings.Settings
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            rootSerializer(orderedSettings, typeof(Dictionary<string, Level>));
        }
    }




    // --- FullAnalysisReportData Converter (Conceptual - might be too complex, DTOs preferred) ---
    // For FullAnalysisReportData, given its complexity and many fields that YamlDotNet can handle
    // by default (if its members have converters or are simple), a full custom converter
    // might be overkill. The primary issue is ensuring its members like ParameterMainEffect,
    // OptimalConfiguration, etc., are handled by THEIR specific converters.
    // If YamlDotNet is configured with converters for all custom types *within* FullAnalysisReportData,
    // then FullAnalysisReportData itself might not need a dedicated converter, UNLESS
    // you want to change its top-level YAML structure significantly or handle missing fields gracefully.

    // For now, we will rely on the main SerializerBuilder/DeserializerBuilder in Program.cs
    // being configured with all the necessary child converters.
    // If deserialization of FullAnalysisReportData still fails, it means one of its members
    // is not being handled correctly by either a default mechanism or a registered custom converter.
}