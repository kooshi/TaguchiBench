// TaguchiBench.Engine/Configuration/SignalToNoiseTypeYamlConverter.cs

using System;
using System.Globalization;
using TaguchiBench.Engine.Core; // For SignalToNoiseType and its derived classes
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace TaguchiBench.Engine.Configuration {
    public class SignalToNoiseTypeYamlConverter : IYamlTypeConverter {
        private const string TypeKey = "method"; // Matching EngineConfigDto.MetricToAnalyzeDto
        private const string TargetKey = "target";

        public bool Accepts(Type type) {
            return typeof(SignalToNoiseType).IsAssignableFrom(type);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            // Expect a mapping for SignalToNoiseType
            parser.Consume<MappingStart>();

            string method = null;
            double? target = null;

            while (!parser.TryConsume<MappingEnd>(out _)) {
                var keyScalar = parser.Consume<Scalar>();
                switch (keyScalar.Value) {
                    case TypeKey:
                        method = parser.Consume<Scalar>().Value;
                        break;
                    case TargetKey:
                        target = double.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
                        break;
                    default:
                        // Unknown key, could skip or throw
                        parser.SkipThisAndNestedEvents();
                        break;
                }
            }

            if (string.IsNullOrEmpty(method)) {
                throw new YamlException("SignalToNoiseType 'method' key not found or empty.");
            }

            return method.ToLowerInvariant() switch {
                "largerisbetter" => SignalToNoiseType.LargerIsBetter,
                "smallerisbetter" => SignalToNoiseType.SmallerIsBetter,
                "nominal" => target.HasValue
                                ? SignalToNoiseType.NominalIsBest(target.Value)
                                : throw new YamlException($"SignalToNoiseType 'Nominal' requires a '{TargetKey}'."),
                _ => throw new YamlException($"Unknown SignalToNoiseType method: '{method}'")
            };
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            var snType = (SignalToNoiseType)value;

            emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));
            emitter.Emit(new Scalar(TypeKey));

            switch (snType) {
                case SignalToNoiseType.LargerIsBetterType:
                    emitter.Emit(new Scalar("LargerIsBetter"));
                    break;
                case SignalToNoiseType.SmallerIsBetterType:
                    emitter.Emit(new Scalar("SmallerIsBetter"));
                    break;
                case SignalToNoiseType.NominalIsBestType n:
                    emitter.Emit(new Scalar("Nominal"));
                    emitter.Emit(new Scalar(TargetKey));
                    emitter.Emit(new Scalar(n.Target.ToString(CultureInfo.InvariantCulture)));
                    break;
                default:
                    throw new YamlException($"Unknown SignalToNoiseType encountered during serialization: {snType.GetType().Name}");
            }
            emitter.Emit(new MappingEnd());
        }
    }
}