// TaguchiBench.Engine/Configuration/YamlConverters.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaguchiBench.Engine.Core; // For OALevel, Level, ParameterLevelSet
// Factor is also in TaguchiBench.Engine.Configuration
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace TaguchiBench.Engine.Configuration {

    public class OALevelYamlConverter : IYamlTypeConverter {
        public bool Accepts(Type type) {
            return type == typeof(OALevel);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            // For simple scalar types like OALevel (represented as int),
            // we typically don't need the rootDeserializer.
            var scalar = parser.Consume<Scalar>();
            if (int.TryParse(scalar.Value, out int levelInt)) {
                try {
                    return OALevel.Parse(levelInt.ToString()); // Use existing Parse, which validates
                } catch (ArgumentOutOfRangeException ex) {
                    throw new YamlException(scalar.Start, scalar.End, $"Invalid integer value '{levelInt}' for OALevel. {ex.Message}", ex);
                } catch (FormatException ex) { // Should not happen if int.TryParse succeeded
                    throw new YamlException(scalar.Start, scalar.End, $"Formatting error for OALevel with value '{levelInt}'. {ex.Message}", ex);
                }
            }
            throw new YamlException(scalar.Start, scalar.End, $"Expected integer for OALevel, got '{scalar.Value}'");
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            // For simple scalar types, we typically don't need the rootSerializer.
            var oaLevel = (OALevel)value;
            emitter.Emit(new Scalar(null, null, oaLevel.Level.ToString(CultureInfo.InvariantCulture), ScalarStyle.Plain, true, false));
        }
    }

    public class LevelYamlConverter : IYamlTypeConverter {
        private const string OaLevelKey = "oaLevel"; // Consistent key names
        private const string ValueKey = "value";

        public bool Accepts(Type type) {
            return type == typeof(Level);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            parser.Consume<MappingStart>();
            OALevel oaLevel = null;
            string val = null;

            while (!parser.TryConsume<MappingEnd>(out _)) {
                var keyScalar = parser.Consume<Scalar>();
                switch (keyScalar.Value) {
                    case OaLevelKey:
                        oaLevel = (OALevel)rootDeserializer(typeof(OALevel));
                        break;
                    case ValueKey:
                        val = parser.Consume<Scalar>().Value;
                        break;
                    default:
                        // Log or handle unknown keys if necessary, then skip
                        parser.SkipThisAndNestedEvents();
                        break;
                }
            }

            if (oaLevel == null) {
                throw new YamlException("Level object in YAML is missing the 'oaLevel' field.");
            }
            // 'val' can be null if the original Level.Value was null, which is allowed.
            // However, Level constructor requires non-null value. If null is a valid serialized state,
            // Level constructor or this logic needs to adapt. Assuming Value is always non-null from YAML.
            if (val == null) {
                throw new YamlException("Level object in YAML is missing the 'value' field.");
            }

            return new Level(oaLevel, val);
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            var level = (Level)value;
            emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));

            emitter.Emit(new Scalar(OaLevelKey));
            rootSerializer(level.OALevel, typeof(OALevel)); // Delegate OALevel serialization

            emitter.Emit(new Scalar(ValueKey));
            emitter.Emit(new Scalar(level.Value)); // Value is a simple string

            emitter.Emit(new MappingEnd());
        }
    }

    public class ParameterLevelSetYamlConverter : IYamlTypeConverter {
        public bool Accepts(Type type) {
            return type == typeof(ParameterLevelSet);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            var levelsList = new List<string>();
            parser.Consume<SequenceStart>();
            while (!parser.TryConsume<SequenceEnd>(out _)) {
                // Each item in the sequence is expected to be a scalar string.
                var scalar = parser.Consume<Scalar>();
                levelsList.Add(scalar.Value);
            }

            var parameterLevelSet = new ParameterLevelSet();
            for (int i = 0; i < levelsList.Count; i++) {
                try {
                    // OALevels are 1-based from the list index.
                    parameterLevelSet.Add(OALevel.Parse((i + 1).ToString()), levelsList[i]);
                } catch (ArgumentException ex) { // Catch issues from OALevel.Parse or ParameterLevelSet.Add
                    throw new YamlException(parser.Current?.Start ?? Mark.Empty, parser.Current?.End ?? Mark.Empty, $"Error constructing ParameterLevelSet: {ex.Message}", ex);
                }
            }
            return parameterLevelSet;
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            var parameterLevelSet = (ParameterLevelSet)value;
            emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));
            // Ensure levels are written in order of their OALevel (1, 2, 3...)
            foreach (var levelEntry in parameterLevelSet.OrderBy(kvp => kvp.Key.Level)) {
                // We are serializing the string value of the Level object.
                // The LevelYamlConverter would handle serializing the Level object itself if we passed levelEntry.Value directly
                // to rootSerializer, but we want just the string.
                emitter.Emit(new Scalar(levelEntry.Value.Value));
            }
            emitter.Emit(new SequenceEnd());
        }
    }

    public class FactorYamlConverter : IYamlTypeConverter {
        public bool Accepts(Type type) {
            return type == typeof(Factor);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            // Deserialize into a FactorDto using the rootDeserializer for the DTO type.
            // This is cleaner than manually parsing the mapping here.
            var factorDto = (FactorDto)rootDeserializer(typeof(FactorDto));

            if (factorDto == null) {
                throw new YamlException(parser.Current?.Start ?? Mark.Empty, parser.Current?.End ?? Mark.Empty, "Failed to deserialize factor data into DTO.");
            }

            // Convert FactorDto to Factor domain object.
            // The 'isControlFactor' context is tricky here. When deserializing a Factor directly
            // (e.g., if it's part of a list of Factors in some other structure not EngineConfiguration),
            // we might not know its role.
            // For EngineConfiguration within ExperimentState, these are ControlFactors.
            // If this converter is used more generically, this assumption might need to be revisited
            // or the Factor class itself might need to be less dependent on this boolean.
            // For now, assume true for deserialization via this direct converter.
            try {
                return Factor.FromDto(factorDto, true);
            } catch (ConfigurationException ex) { // Catch specific exceptions from FromDto
                throw new YamlException(parser.Current?.Start ?? Mark.Empty, parser.Current?.End ?? Mark.Empty, $"Error converting DTO to Factor: {ex.Message}", ex);
            }
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            var factor = (Factor)value;

            // Create a FactorDto representation to leverage the rootSerializer for DTOs,
            // ensuring consistency with how FactorDto would normally be serialized.
            var factorDto = new FactorDto {
                Name = factor.Name,
                CliArgument = string.IsNullOrWhiteSpace(factor.CliArgument) ? null : factor.CliArgument,
                EnvironmentVariable = string.IsNullOrWhiteSpace(factor.EnvironmentVariable) ? null : factor.EnvironmentVariable,
                // Extract level values, ordered by OALevel
                Levels = factor.Levels?.OrderBy(kvp => kvp.Key.Level).Select(kvp => kvp.Value.Value).ToList()
                // FloatRange and IntRange are not part of the Factor domain object directly,
                // they are converted to Levels upon Factor creation. So, they won't be serialized back out from Factor.
            };

            // Use the rootSerializer to serialize the DTO. This handles all standard DTO serialization logic.
            rootSerializer(factorDto, typeof(FactorDto));
        }
    }
}