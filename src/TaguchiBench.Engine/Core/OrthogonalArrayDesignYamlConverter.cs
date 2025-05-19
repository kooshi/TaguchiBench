// TaguchiBench.Engine/Core/OrthogonalArrayDesignYamlConverter.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace TaguchiBench.Engine.Core {
    public class OrthogonalArrayDesignYamlConverter : IYamlTypeConverter {
        private const string ArrayDesignationKey = "arrayDesignation";
        private const string ArrayKey = "array";
        private const string ColumnAssignmentsKey = "columnAssignments";

        public bool Accepts(Type type) {
            return type == typeof(OrthogonalArrayDesign);
        }

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) {
            parser.Consume<MappingStart>();

            string arrayDesignation = null;
            OALevel[,] oaArray = null;
            IReadOnlyDictionary<string, int> columnAssignments = null;

            List<List<OALevel>> tempListArray = null; // To build the 2D structure

            while (!parser.TryConsume<MappingEnd>(out _)) {
                var keyScalar = parser.Consume<Scalar>();
                switch (keyScalar.Value) {
                    case ArrayDesignationKey:
                        arrayDesignation = parser.Consume<Scalar>().Value;
                        break;
                    case ArrayKey:
                        tempListArray = new List<List<OALevel>>();
                        parser.Consume<SequenceStart>(); // Expect outer sequence (list of rows)
                        int expectedCols = -1;
                        while (!parser.TryConsume<SequenceEnd>(out _)) {
                            var innerList = new List<OALevel>();
                            parser.Consume<SequenceStart>(); // Expect inner sequence (a row)
                            while (!parser.TryConsume<SequenceEnd>(out _)) {
                                var scalar = parser.Consume<Scalar>();
                                if (int.TryParse(scalar.Value, out int levelInt)) {
                                    innerList.Add(OALevel.Parse(levelInt.ToString()));
                                } else {
                                    throw new YamlException(scalar.Start, scalar.End, $"Expected integer for OALevel in array, got '{scalar.Value}'");
                                }
                            }
                            // Validate consistent number of columns
                            if (expectedCols == -1 && innerList.Any()) {
                                expectedCols = innerList.Count;
                            } else if (innerList.Any() && innerList.Count != expectedCols) {
                                throw new YamlException(keyScalar.Start, keyScalar.End, "Jagged array detected for OA levels. All rows must have the same number of columns.");
                            }
                            tempListArray.Add(innerList);
                        }
                        // Convert List<List<OALevel>> to OALevel[,]
                        if (tempListArray.Any()) {
                            int rows = tempListArray.Count;
                            int cols = tempListArray.First().Count; // All rows have same number of cols
                            oaArray = new OALevel[rows, cols];
                            for (int i = 0; i < rows; i++) {
                                for (int j = 0; j < cols; j++) {
                                    oaArray[i, j] = tempListArray[i][j];
                                }
                            }
                        }
                        break;
                    case ColumnAssignmentsKey:
                        columnAssignments = (IReadOnlyDictionary<string, int>)rootDeserializer(typeof(Dictionary<string, int>));
                        break;
                    default:
                        parser.SkipThisAndNestedEvents();
                        break;
                }
            }

            if (string.IsNullOrEmpty(arrayDesignation) || oaArray == null || columnAssignments == null) {
                throw new YamlException("OrthogonalArrayDesign is missing 'arrayDesignation', 'array', or 'columnAssignments', or 'array' was malformed.");
            }

            return new OrthogonalArrayDesign(arrayDesignation, oaArray, columnAssignments);
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer rootSerializer) {
            var design = (OrthogonalArrayDesign)value;

            emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));

            emitter.Emit(new Scalar(ArrayDesignationKey));
            emitter.Emit(new Scalar(design.ArrayDesignation));

            emitter.Emit(new Scalar(ArrayKey));
            emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block)); // Outer list for rows
            if (design.Array != null) {
                for (int i = 0; i < design.Array.GetLength(0); i++) {
                    emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Flow)); // Inner list for columns (flow style for compactness)
                    for (int j = 0; j < design.Array.GetLength(1); j++) {
                        // Emit the integer value of the OALevel
                        emitter.Emit(new Scalar(design.Array[i, j].Level.ToString(CultureInfo.InvariantCulture)));
                    }
                    emitter.Emit(new SequenceEnd()); // End inner list (row)
                }
            }
            emitter.Emit(new SequenceEnd()); // End outer list for array

            emitter.Emit(new Scalar(ColumnAssignmentsKey));
            rootSerializer(design.ColumnAssignments, typeof(Dictionary<string, int>)); // Let default handle dictionary

            emitter.Emit(new MappingEnd());
        }
    }
}