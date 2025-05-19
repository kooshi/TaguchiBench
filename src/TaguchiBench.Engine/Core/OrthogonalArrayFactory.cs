// TaguchiBench.Engine/Core/OrthogonalArrayFactory.cs

using System;
using System.Collections.Generic;
using System.Linq;
using TaguchiBench.Common; // For Logger
using TaguchiBench.Engine.Configuration; // For Factor (which is now in EngineConfiguration.cs)

namespace TaguchiBench.Engine.Core {
    // ParameterInteraction is now defined in Level.cs within TaguchiBench.Engine.Core

    /// <summary>
    /// Represents a pair of columns that form an interaction in an orthogonal array.
    /// Column indices are 1-based as is conventional in OA literature.
    /// </summary>
    public record ColumnInteraction {
        public int Column1 { get; }
        public int Column2 { get; }
        public IReadOnlyList<int> InteractionColumns { get; }

        public ColumnInteraction(int column1, int column2, List<int> interactionColumns) {
            if (column1 <= 0) { throw new ArgumentOutOfRangeException(nameof(column1), "Column indices must be 1-based and positive."); }
            if (column2 <= 0) { throw new ArgumentOutOfRangeException(nameof(column2), "Column indices must be 1-based and positive."); }
            if (column1 == column2) { throw new ArgumentException("Interacting columns cannot be the same.", nameof(column2)); }
            ArgumentNullException.ThrowIfNull(interactionColumns);
            if (!interactionColumns.Any()) { throw new ArgumentException("Interaction columns list cannot be empty.", nameof(interactionColumns)); }
            if (interactionColumns.Any(c => c <= 0)) { throw new ArgumentException("All interaction column indices must be 1-based and positive.", nameof(interactionColumns)); }

            Column1 = column1;
            Column2 = column2;
            InteractionColumns = interactionColumns.AsReadOnly();
        }

        public ColumnInteraction(int column1, int column2, int interactionColumn)
            : this(column1, column2, new List<int> { interactionColumn }) {
        }

        public override string ToString() {
            return $"{Column1}x{Column2} -> [{string.Join(",", InteractionColumns)}]";
        }
    }

    /// <summary>
    /// Represents a linear graph for an orthogonal array, showing main factor columns and their interactions.
    /// Column indices are 1-based.
    /// </summary>
    public record LinearGraph {
        public IReadOnlyList<int> MainFactorColumns { get; }
        public IReadOnlyList<ColumnInteraction> Interactions { get; }

        public LinearGraph(List<int> mainFactorColumns, List<ColumnInteraction> interactions) {
            ArgumentNullException.ThrowIfNull(mainFactorColumns);
            ArgumentNullException.ThrowIfNull(interactions);
            if (mainFactorColumns.Any(c => c <= 0)) { throw new ArgumentException("All main factor column indices must be 1-based and positive.", nameof(mainFactorColumns)); }

            MainFactorColumns = mainFactorColumns.AsReadOnly();
            Interactions = interactions.AsReadOnly();
        }

        public override string ToString() {
            return $"MainCols:[{string.Join(",", MainFactorColumns)}], Interactions:{Interactions.Count}";
        }
    }

    /// <summary>
    /// Describes an orthogonal array with its properties.
    /// </summary>
    public record OrthogonalArrayInfo {
        public string Designation { get; }
        public int Runs { get; }
        public int MaximumFactors { get; }
        public IReadOnlyList<int> LevelCounts { get; }
        public int Strength { get; }
        public LinearGraph LinearGraph { get; internal set; }

        public OrthogonalArrayInfo(string designation, int runs, int maximumFactors, List<int> levelCounts, int strength = 2) {
            ArgumentException.ThrowIfNullOrWhiteSpace(designation);
            if (runs <= 0) { throw new ArgumentOutOfRangeException(nameof(runs)); }
            if (maximumFactors < 0) { throw new ArgumentOutOfRangeException(nameof(maximumFactors)); }
            ArgumentNullException.ThrowIfNull(levelCounts);
            if (maximumFactors > 0 && levelCounts.Count != maximumFactors) {
                throw new ArgumentException($"LevelCounts length ({levelCounts.Count}) must match MaximumFactors ({maximumFactors}).", nameof(levelCounts));
            }
            if (strength <= 0) { throw new ArgumentOutOfRangeException(nameof(strength)); }

            Designation = designation;
            Runs = runs;
            MaximumFactors = maximumFactors;
            LevelCounts = levelCounts.AsReadOnly();
            Strength = strength;
        }

        public override string ToString() {
            var levelGroups = LevelCounts.GroupBy(l => l)
                                         .OrderBy(g => g.Key)
                                         .Select(g => $"{g.Key}^{g.Count()}");
            string levelDescription = string.Join(" × ", levelGroups);
            return $"{Designation} ({levelDescription}), Runs={Runs}, MaxFactors={MaximumFactors}, Strength={Strength}";
        }
    }

    /// <summary>
    /// Represents a factor to be assigned to an orthogonal array column for design purposes.
    /// This is distinct from the `Factor` class in `EngineConfiguration` which holds more detailed info.
    /// </summary>
    public record FactorToAssignForDesign(string Name, int Levels);

    /// <summary>
    /// Contains the generated orthogonal array and the assignment of factors/interactions to its columns.
    /// </summary>
    public record OrthogonalArrayDesign(
        string ArrayDesignation,
        OALevel[,] Array,
        IReadOnlyDictionary<string, int> ColumnAssignments // Key: Factor/Interaction Name, Value: 0-based column index in Array
    );


    /// <summary>
    /// Generates Taguchi orthogonal arrays for experimental design.
    /// Its methods are pure, relying only on established OA data and input parameters.
    /// </summary>
    public static class OrthogonalArrayFactory {
        // L32 generation logic remains unchanged.
        private static OALevel[,] GenerateL32Array() {
            const int runs = 32;
            const int numBaseCols = 5;
            var baseColPatterns = new OALevel[numBaseCols, runs];

            for (int i = 0; i < numBaseCols; i++) {
                int period = 1 << (i + 1);
                int halfPeriod = period / 2;
                for (int run = 0; run < runs; run++) {
                    baseColPatterns[i, run] = (run % period < halfPeriod) ? OALevel.One : OALevel.Two;
                }
            }

            var l32 = new OALevel[runs, 31];
            var baseColsForL32 = Enumerable.Range(0, numBaseCols)
                                           .Select(i => Enumerable.Range(0, runs).Select(r => baseColPatterns[i, r]).ToArray())
                                           .ToList();
            int l32ColMapIndex = 0;

            for (int i = 0; i < numBaseCols; ++i) {
                int targetCol0Based = OrthogonalArrayFactoryHelper.L32_ColMap[l32ColMapIndex++] - 1;
                for (int r = 0; r < runs; ++r) { l32[r, targetCol0Based] = baseColsForL32[i][r]; }
            }
            for (int i = 0; i < numBaseCols; ++i) {
                for (int j = i + 1; j < numBaseCols; ++j) {
                    int targetCol0Based = OrthogonalArrayFactoryHelper.L32_ColMap[l32ColMapIndex++] - 1;
                    for (int r = 0; r < runs; ++r) { l32[r, targetCol0Based] = (baseColsForL32[i][r] == baseColsForL32[j][r]) ? OALevel.One : OALevel.Two; }
                }
            }
            for (int i = 0; i < numBaseCols; ++i) {
                for (int j = i + 1; j < numBaseCols; ++j) {
                    for (int k = j + 1; k < numBaseCols; ++k) {
                        int targetCol0Based = OrthogonalArrayFactoryHelper.L32_ColMap[l32ColMapIndex++] - 1;
                        for (int r = 0; r < runs; ++r) {
                            OALevel val_ab = (baseColsForL32[i][r] == baseColsForL32[j][r]) ? OALevel.One : OALevel.Two;
                            l32[r, targetCol0Based] = (val_ab == baseColsForL32[k][r]) ? OALevel.One : OALevel.Two;
                        }
                    }
                }
            }
            for (int i = 0; i < numBaseCols; ++i) {
                for (int j = i + 1; j < numBaseCols; ++j) {
                    for (int k = j + 1; k < numBaseCols; ++k) {
                        for (int l = k + 1; l < numBaseCols; ++l) {
                            int targetCol0Based = OrthogonalArrayFactoryHelper.L32_ColMap[l32ColMapIndex++] - 1;
                            for (int r = 0; r < runs; ++r) {
                                OALevel val_ab = (baseColsForL32[i][r] == baseColsForL32[j][r]) ? OALevel.One : OALevel.Two;
                                OALevel val_abc = (val_ab == baseColsForL32[k][r]) ? OALevel.One : OALevel.Two;
                                l32[r, targetCol0Based] = (val_abc == baseColsForL32[l][r]) ? OALevel.One : OALevel.Two;
                            }
                        }
                    }
                }
            }
            int lastTargetCol0Based = OrthogonalArrayFactoryHelper.L32_ColMap[l32ColMapIndex++] - 1;
            for (int r = 0; r < runs; ++r) {
                OALevel val_ab = (baseColsForL32[0][r] == baseColsForL32[1][r]) ? OALevel.One : OALevel.Two;
                OALevel val_abc = (val_ab == baseColsForL32[2][r]) ? OALevel.One : OALevel.Two;
                OALevel val_abcd = (val_abc == baseColsForL32[3][r]) ? OALevel.One : OALevel.Two;
                l32[r, lastTargetCol0Based] = (val_abcd == baseColsForL32[4][r]) ? OALevel.One : OALevel.Two;
            }
            return l32;
        }

        // StandardArrays, ArrayInfoData, LinearGraphsData, and OrthogonalArrayFactoryHelper remain unchanged as they are static data.
        // For brevity, I will not repeat them here but assume they are present as in the original file.
        // (If you need them explicitly listed, I can provide them.)
        public static readonly Dictionary<string, OALevel[,]> StandardArrays = new() {
            ["L4"] = new OALevel[,] { { 1, 1, 1 }, { 1, 2, 2 }, { 2, 1, 2 }, { 2, 2, 1 } },
            ["L8"] = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 1, 2, 2, 2, 2 }, { 1, 2, 2, 1, 1, 2, 2 }, { 1, 2, 2, 2, 2, 1, 1 }, { 2, 1, 2, 1, 2, 1, 2 }, { 2, 1, 2, 2, 1, 2, 1 }, { 2, 2, 1, 1, 2, 2, 1 }, { 2, 2, 1, 2, 1, 1, 2 } },
            ["L8(2^4,4^1)"] = new OALevel[,] { { 1, 1, 1, 1, 1 }, { 1, 2, 2, 2, 2 }, { 2, 1, 1, 2, 2 }, { 2, 2, 2, 1, 1 }, { 3, 1, 2, 1, 2 }, { 3, 2, 1, 2, 1 }, { 4, 1, 2, 2, 1 }, { 4, 2, 1, 1, 2 } },
            ["L9"] = new OALevel[,] { { 1, 1, 1, 1 }, { 1, 2, 2, 2 }, { 1, 3, 3, 3 }, { 2, 1, 2, 3 }, { 2, 2, 3, 1 }, { 2, 3, 1, 2 }, { 3, 1, 3, 2 }, { 3, 2, 1, 3 }, { 3, 3, 2, 1 } },
            ["L12"] = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2 }, { 1, 1, 2, 2, 2, 1, 1, 1, 2, 2, 2 }, { 1, 2, 1, 2, 2, 1, 2, 2, 1, 1, 2 }, { 1, 2, 2, 1, 2, 2, 1, 2, 1, 2, 1 }, { 1, 2, 2, 2, 1, 2, 2, 1, 2, 1, 1 }, { 2, 1, 2, 2, 1, 1, 2, 2, 1, 2, 1 }, { 2, 1, 2, 1, 2, 2, 2, 1, 2, 1, 2 }, { 2, 1, 1, 2, 2, 2, 1, 2, 2, 1, 1 }, { 2, 2, 2, 1, 1, 1, 1, 2, 2, 1, 2 }, { 2, 2, 1, 2, 1, 2, 1, 1, 1, 2, 2 }, { 2, 2, 1, 1, 2, 1, 2, 1, 1, 2, 2 } },
            ["L16"] = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2 }, { 1, 1, 1, 2, 2, 2, 2, 1, 1, 1, 1, 2, 2, 2, 2 }, { 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1 }, { 1, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2 }, { 1, 2, 2, 1, 1, 2, 2, 2, 2, 1, 1, 2, 2, 1, 1 }, { 1, 2, 2, 2, 2, 1, 1, 1, 1, 2, 2, 2, 2, 1, 1 }, { 1, 2, 2, 2, 2, 1, 1, 2, 2, 1, 1, 1, 1, 2, 2 }, { 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2 }, { 2, 1, 2, 1, 2, 1, 2, 2, 1, 2, 1, 2, 1, 2, 1 }, { 2, 1, 2, 2, 1, 2, 1, 1, 2, 1, 2, 2, 1, 2, 1 }, { 2, 1, 2, 2, 1, 2, 1, 2, 1, 2, 1, 1, 2, 1, 2 }, { 2, 2, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2, 1 }, { 2, 2, 1, 1, 2, 2, 1, 2, 1, 1, 2, 2, 1, 1, 2 }, { 2, 2, 1, 2, 1, 1, 2, 1, 2, 2, 1, 2, 1, 1, 2 }, { 2, 2, 1, 2, 1, 1, 2, 2, 1, 1, 2, 1, 2, 2, 1 } },
            ["L16(4^5)"] = new OALevel[,] { { 1, 1, 1, 1, 1 }, { 1, 2, 2, 2, 2 }, { 1, 3, 3, 3, 3 }, { 1, 4, 4, 4, 4 }, { 2, 1, 2, 3, 4 }, { 2, 2, 1, 4, 3 }, { 2, 3, 4, 1, 2 }, { 2, 4, 3, 2, 1 }, { 3, 1, 3, 4, 2 }, { 3, 2, 4, 3, 1 }, { 3, 3, 1, 2, 4 }, { 3, 4, 2, 1, 3 }, { 4, 1, 4, 2, 3 }, { 4, 2, 3, 1, 4 }, { 4, 3, 2, 4, 1 }, { 4, 4, 1, 3, 2 } },
            ["L16(2^12,4^1)"] = OrthogonalArrayFactoryHelper.L16_2_12_4_1_Data,
            ["L16(2^9,4^2)"] = OrthogonalArrayFactoryHelper.L16_2_9_4_2_Data,
            ["L16(2^6,4^3)"] = OrthogonalArrayFactoryHelper.L16_2_6_4_3_Data,
            ["L16(2^3,4^4)"] = OrthogonalArrayFactoryHelper.L16_2_3_4_4_Data,
            ["L18"] = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 2, 2, 2, 2, 2, 2 }, { 1, 1, 3, 3, 3, 3, 3, 3 }, { 1, 2, 1, 1, 2, 2, 3, 3 }, { 1, 2, 2, 2, 3, 3, 1, 1 }, { 1, 2, 3, 3, 1, 1, 2, 2 }, { 1, 3, 1, 2, 1, 3, 2, 3 }, { 1, 3, 2, 3, 2, 1, 3, 1 }, { 1, 3, 3, 1, 3, 2, 1, 2 }, { 2, 1, 1, 3, 3, 2, 2, 1 }, { 2, 1, 2, 1, 1, 3, 3, 2 }, { 2, 1, 3, 2, 2, 1, 1, 3 }, { 2, 2, 1, 2, 3, 1, 3, 2 }, { 2, 2, 2, 3, 1, 2, 1, 3 }, { 2, 2, 3, 1, 2, 3, 2, 1 }, { 2, 3, 1, 3, 2, 3, 1, 2 }, { 2, 3, 2, 1, 3, 1, 2, 3 }, { 2, 3, 3, 2, 1, 2, 3, 1 } },
            ["L18(6^1,3^6)"] = OrthogonalArrayFactoryHelper.L18_6_1_3_6_Data,
            ["L25"] = OrthogonalArrayFactoryHelper.L25_5_6_Data,
            ["L27"] = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2 }, { 1, 1, 1, 1, 1, 3, 3, 3, 3, 3, 3, 3, 3 }, { 1, 1, 1, 2, 2, 1, 1, 1, 2, 2, 2, 3, 3 }, { 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 1, 1 }, { 1, 1, 1, 2, 2, 3, 3, 3, 1, 1, 1, 2, 2 }, { 1, 1, 1, 3, 3, 1, 1, 1, 3, 3, 3, 2, 2 }, { 1, 1, 1, 3, 3, 2, 2, 2, 1, 1, 1, 3, 3 }, { 1, 1, 1, 3, 3, 3, 3, 3, 2, 2, 2, 1, 1 }, { 1, 2, 2, 1, 2, 1, 2, 3, 1, 2, 3, 1, 2 }, { 1, 2, 2, 1, 2, 2, 3, 1, 2, 3, 1, 3, 1 }, { 1, 2, 2, 1, 2, 3, 1, 2, 3, 1, 2, 2, 3 }, { 1, 2, 2, 2, 3, 1, 2, 3, 2, 3, 1, 2, 3 }, { 1, 2, 2, 2, 3, 2, 3, 1, 3, 1, 2, 1, 2 }, { 1, 2, 2, 2, 3, 3, 1, 2, 1, 2, 3, 3, 1 }, { 1, 2, 2, 3, 1, 1, 2, 3, 3, 1, 2, 3, 1 }, { 1, 2, 2, 3, 1, 2, 3, 1, 1, 2, 3, 2, 3 }, { 1, 2, 2, 3, 1, 3, 1, 2, 2, 3, 1, 1, 2 }, { 1, 3, 3, 1, 3, 1, 3, 2, 1, 3, 2, 1, 3 }, { 1, 3, 3, 1, 3, 2, 1, 3, 2, 1, 3, 3, 2 }, { 1, 3, 3, 1, 3, 3, 2, 1, 3, 2, 1, 2, 1 }, { 1, 3, 3, 2, 1, 1, 3, 2, 2, 1, 3, 3, 2 }, { 1, 3, 3, 2, 1, 2, 1, 3, 3, 2, 1, 1, 3 }, { 1, 3, 3, 2, 1, 3, 2, 1, 1, 3, 2, 2, 1 }, { 1, 3, 3, 3, 2, 1, 3, 2, 3, 2, 1, 2, 1 }, { 1, 3, 3, 3, 2, 2, 1, 3, 1, 3, 2, 3, 2 }, { 1, 3, 3, 3, 2, 3, 2, 1, 2, 1, 3, 1, 3 } },
            ["L32"] = GenerateL32Array(), // Assumes GenerateL32Array is defined as before
            ["L32(2^1,4^9)"] = OrthogonalArrayFactoryHelper.L32_2_1_4_9_Data,
            ["L36(2^11,3^12)"] = OrthogonalArrayFactoryHelper.L36_2_11_3_12_Data,
            ["L36(2^3,3^13)"] = OrthogonalArrayFactoryHelper.L36_2_3_3_13_Data,
        };

        private static readonly Dictionary<string, OrthogonalArrayInfo> ArrayInfoData = new() {
            ["L4"] = new("L4", 4, 3, new List<int> { 2, 2, 2 }),
            ["L8"] = new("L8", 8, 7, Enumerable.Repeat(2, 7).ToList()),
            ["L8(2^4,4^1)"] = new("L8(2^4,4^1)", 8, 5, new List<int> { 4, 2, 2, 2, 2 }), // Note: LevelCounts order matters for mixed arrays. Typically largest levels first.
            ["L9"] = new("L9", 9, 4, Enumerable.Repeat(3, 4).ToList()),
            ["L12"] = new("L12", 12, 11, Enumerable.Repeat(2, 11).ToList()),
            ["L16"] = new("L16", 16, 15, Enumerable.Repeat(2, 15).ToList()),
            ["L16(4^5)"] = new("L16(4^5)", 16, 5, Enumerable.Repeat(4, 5).ToList()),
            ["L16(2^12,4^1)"] = new("L16(2^12,4^1)", 16, 13, new List<int> { 4 }.Concat(Enumerable.Repeat(2, 12)).ToList()),
            ["L16(2^9,4^2)"] = new("L16(2^9,4^2)", 16, 11, Enumerable.Repeat(4, 2).Concat(Enumerable.Repeat(2, 9)).ToList()),
            ["L16(2^6,4^3)"] = new("L16(2^6,4^3)", 16, 9, Enumerable.Repeat(4, 3).Concat(Enumerable.Repeat(2, 6)).ToList()),
            ["L16(2^3,4^4)"] = new("L16(2^3,4^4)", 16, 7, Enumerable.Repeat(4, 4).Concat(Enumerable.Repeat(2, 3)).ToList()),
            ["L18"] = new("L18", 18, 8, new List<int> { 2 }.Concat(Enumerable.Repeat(3, 7)).ToList()),
            ["L18(6^1,3^6)"] = new("L18(6^1,3^6)", 18, 7, new List<int> { 6 }.Concat(Enumerable.Repeat(3, 6)).ToList()),
            ["L25"] = new("L25", 25, 6, Enumerable.Repeat(5, 6).ToList()),
            ["L27"] = new("L27", 27, 13, Enumerable.Repeat(3, 13).ToList()),
            ["L32"] = new("L32", 32, 31, Enumerable.Repeat(2, 31).ToList()),
            ["L32(2^1,4^9)"] = new("L32(2^1,4^9)", 32, 10, new List<int> { 2 }.Concat(Enumerable.Repeat(4, 9)).ToList()), // Col 1 is 2-level, rest 4-level
            ["L36(2^11,3^12)"] = new("L36(2^11,3^12)", 36, 23, Enumerable.Repeat(2, 11).Concat(Enumerable.Repeat(3, 12)).ToList()),
            ["L36(2^3,3^13)"] = new("L36(2^3,3^13)", 36, 16, Enumerable.Repeat(2, 3).Concat(Enumerable.Repeat(3, 13)).ToList()),
        };

        private static readonly Dictionary<string, LinearGraph> LinearGraphsData = new() {
            ["L4"] = new(new List<int> { 1, 2 }, new List<ColumnInteraction> { new(1, 2, 3) }),
            ["L8"] = new(new List<int> { 1, 2, 4 }, new List<ColumnInteraction> { new(1, 2, 3), new(1, 4, 5), new(2, 4, 6), new(3, 4, 7) }), // 3x4 is (1x2)x4
            ["L9"] = new(new List<int> { 1, 2 }, new List<ColumnInteraction> { new(1, 2, new List<int> { 3, 4 }) }),
            ["L16"] = new(new List<int> { 1, 2, 4, 8 }, new List<ColumnInteraction> { new(1, 2, 3), new(1, 4, 5), new(2, 4, 6), new(1, 8, 9), new(2, 8, 10), new(4, 8, 12) }),
            ["L27"] = new(new List<int> { 1, 2, 5, 9 }, new List<ColumnInteraction> { new(1, 2, new List<int> { 3, 4 }), new(1, 5, new List<int> { 6, 7 }), new(1, 9, new List<int> { 10, 11 }), new(2, 5, new List<int> { 8, 11 }), new(2, 9, new List<int> { 12, 13 }), new(5, 9, new List<int> { 13/*13,14*/}) }), // Minitab shows 5x9 -> 13,14 but 14 might be typo for 12 or another. Standard tables vary. Using only 13 for now if it's (col1+col2)%N etc. For L27, interactions are complex.
            ["L32"] = new(new List<int> { 1, 2, 4, 8, 16 }, new List<ColumnInteraction> { new(1, 2, 3), new(1, 4, 5), new(1, 8, 9), new(1, 16, 17), new(2, 4, 6), new(2, 8, 10), new(2, 16, 18), new(4, 8, 12), new(4, 16, 20), new(8, 16, 24) })
        };

        //Alternative implementations?
        // private static readonly Dictionary<string, LinearGraph> LinearGraphsData = new() {
        //     ["L4"] = new(new List<int> { 1, 2 }, new List<ColumnInteraction> { new(1, 2, 3) }),
        //     ["L8"] = new(new List<int> { 1, 2, 4 }, new List<ColumnInteraction> { new(1, 2, 3), new(1, 4, 5), new(2, 4, 6), new(1,2,3,7) /*1x2x4 -> (1x2)x4 -> 3x4 -> 7*/ }),
        //     ["L9"] = new(new List<int> { 1, 2 }, new List<ColumnInteraction> { new(1, 2, new List<int> { 3, 4 }) }), // 3-level interaction
        //     ["L16"] = new(new List<int> { 1, 2, 4, 8 }, new List<ColumnInteraction> { /* Standard 2-factor interactions */ new(1,2,3), new(1,4,5), new(2,4,6), new(1,8,9), new(2,8,10), new(4,8,12), /* Selected 3-factor interactions for common use */ new(1,2,4,7), new(1,2,8,11), new(1,4,8,13), new(2,4,8,14), /* 4-factor */ new(1,2,4,8,15) }),
        //     ["L27"] = new(new List<int> { 1, 2, 5, 9 }, new List<ColumnInteraction> { new(1, 2, new List<int> {3,4}), new(1,5,new List<int>{6,7}), new(1,9,new List<int>{10,11}), new(2,5,new List<int>{8,12 /*was 8,11*/}), new(2,9,new List<int>{12,13}), new(5,9,new List<int>{13 /*was 13,14*/}) }), // Corrected L27 interaction columns based on standard tables (e.g. Montgomery or Phadke), interaction 2x5 is often 8,12 (not 8,11), 5x9 is often 13, (col 14 is 1x2x5). This needs careful verification against a specific L27 table if interactions are critical.
        //     ["L32"] = new(new List<int> { 1, 2, 4, 8, 16 }, OrthogonalArrayFactoryHelper.L32_LinearGraphInteractions) // Using helper for L32 interactions
        // };


        public static IReadOnlyList<OrthogonalArrayInfo> GetAvailableArrays() {
            return ArrayInfoData.Values.OrderBy(a => a.Runs).ThenBy(a => a.MaximumFactors).ToList().AsReadOnly();
        }

        public static OrthogonalArrayInfo GetArrayInfo(string arrayDesignation) {
            ArgumentException.ThrowIfNullOrWhiteSpace(arrayDesignation);
            if (!ArrayInfoData.TryGetValue(arrayDesignation, out OrthogonalArrayInfo info)) {
                throw new ArgumentException($"Array information for '{arrayDesignation}' not found.", nameof(arrayDesignation));
            }
            if (LinearGraphsData.TryGetValue(arrayDesignation, out LinearGraph graph)) {
                info.LinearGraph = graph; // Attach graph if available
            }
            return info;
        }

        public static LinearGraph GetLinearGraph(string arrayDesignation) {
            ArgumentException.ThrowIfNullOrWhiteSpace(arrayDesignation);
            LinearGraphsData.TryGetValue(arrayDesignation, out LinearGraph graph);
            return graph;
        }

        public static string RecommendArray(
            IReadOnlyList<FactorToAssignForDesign> factors,
            IReadOnlyList<ParameterInteraction> interactionsToConsider = null,
            int defaultInteractionDofIfNoSpecifics = 0) { // Changed from count to DOF
            if (factors == null || !factors.Any()) {
                throw new ArgumentException("Factors list cannot be null or empty.", nameof(factors));
            }

            int mainFactorDof = factors.Sum(f => f.Levels - 1);
            int interactionDofNeeded = CalculateInteractionDofNeeded(factors, interactionsToConsider, defaultInteractionDofIfNoSpecifics);
            int totalDofNeeded = mainFactorDof + interactionDofNeeded;

            var requiredLevelsCount = factors.GroupBy(f => f.Levels)
                                             .ToDictionary(g => g.Key, g => g.Count());

            // Prefer arrays that can house the total DOF and match factor levels.
            // Then, prefer fewer runs.
            // Then, prefer arrays with defined linear graphs if interactions are specified.
            var suitableArrayDesignation = ArrayInfoData.Values
                .Where(arrayInfo => (arrayInfo.Runs - 1) >= totalDofNeeded && CanAccommodateMainFactorLevels(arrayInfo, requiredLevelsCount))
                .OrderBy(a => a.Runs)
                .ThenBy(a => (interactionsToConsider != null && interactionsToConsider.Any() && GetLinearGraph(a.Designation) == null) ? 1 : 0) // Penalize if LG missing for interactions
                .ThenBy(a => a.MaximumFactors) // As a tie-breaker, more columns might offer flexibility
                .FirstOrDefault()?.Designation;

            return suitableArrayDesignation ?? FallbackArrayRecommendationByDof(totalDofNeeded, factors);
        }

        private static int CalculateInteractionDofNeeded(
            IReadOnlyList<FactorToAssignForDesign> factors,
            IReadOnlyList<ParameterInteraction> interactionsToConsider,
            int defaultInteractionDof) {
            if (interactionsToConsider == null || !interactionsToConsider.Any()) {
                return defaultInteractionDof;
            }

            var factorLevelMap = factors.ToDictionary(f => f.Name, f => f.Levels, StringComparer.OrdinalIgnoreCase);
            int dof = 0;
            foreach (var interaction in interactionsToConsider) {
                if (factorLevelMap.TryGetValue(interaction.FirstParameterName, out int levels1) &&
                    factorLevelMap.TryGetValue(interaction.SecondParameterName, out int levels2)) {
                    dof += (levels1 - 1) * (levels2 - 1);
                } else {
                    // If factors for interaction are not found, this is a config error caught elsewhere.
                    // For DOF calculation, assume a common case like 2-level if unknown, or log warning.
                    Logger.Warning("OA_FACTORY", "Interaction '{InteractionF1}*{InteractionF2}' involves unknown factors. Estimating 1 DOF for it.", interaction.FirstParameterName, interaction.SecondParameterName);
                    dof += 1; // Default to 1 DOF for unknown interaction.
                }
            }
            return dof;
        }

        private static bool CanAccommodateMainFactorLevels(OrthogonalArrayInfo arrayInfo, IReadOnlyDictionary<int, int> requiredFactorCountsByLevel) {
            var availableColumnsByLevelInArray = arrayInfo.LevelCounts.GroupBy(l => l)
                                                            .ToDictionary(g => g.Key, g => g.Count());
            foreach (var requiredEntry in requiredFactorCountsByLevel) {
                int requiredLevel = requiredEntry.Key;
                int requiredCount = requiredEntry.Value;
                if (!availableColumnsByLevelInArray.TryGetValue(requiredLevel, out int availableCount) || availableCount < requiredCount) {
                    return false; // Not enough columns of this level type
                }
            }
            return true;
        }

        private static string FallbackArrayRecommendationByDof(int totalDofNeeded, IReadOnlyList<FactorToAssignForDesign> factors) {
            // Simplified fallback based on DOF and predominant level type
            bool isPurelyTwoLevel = factors.All(f => f.Levels == 2);
            bool isPurelyThreeLevel = factors.All(f => f.Levels == 3);

            if (isPurelyTwoLevel) {
                if (totalDofNeeded <= 3) return "L4";
                if (totalDofNeeded <= 7) return "L8";
                if (totalDofNeeded <= 11) return "L12"; // L12 is good for main effects, less for interactions
                if (totalDofNeeded <= 15) return "L16";
                if (totalDofNeeded <= 31) return "L32";
            } else if (isPurelyThreeLevel) {
                if (totalDofNeeded <= 8) return "L9";  // L9 has 4 cols, (3-1)*4 = 8 DOF
                if (totalDofNeeded <= 26) return "L27"; // L27 has 13 cols, (3-1)*13 = 26 DOF
            } else { // Mixed levels
                if (factors.Any(f => f.Levels > 3) || factors.Count(f => f.Levels == 3) > 7) {
                     if (totalDofNeeded <= ArrayInfoData["L36(2^3,3^13)"].Runs -1 && CanAccommodateMainFactorLevels(ArrayInfoData["L36(2^3,3^13)"], factors.GroupBy(f=>f.Levels).ToDictionary(g=>g.Key, g=>g.Count()))) return "L36(2^3,3^13)";
                     if (totalDofNeeded <= ArrayInfoData["L36(2^11,3^12)"].Runs -1 && CanAccommodateMainFactorLevels(ArrayInfoData["L36(2^11,3^12)"], factors.GroupBy(f=>f.Levels).ToDictionary(g=>g.Key, g=>g.Count()))) return "L36(2^11,3^12)";
                }
                if (totalDofNeeded <= ArrayInfoData["L18"].Runs - 1 && CanAccommodateMainFactorLevels(ArrayInfoData["L18"], factors.GroupBy(f=>f.Levels).ToDictionary(g=>g.Key, g=>g.Count()))) return "L18"; // L18: 1x 2-level, 7x 3-level
            }
            return null; // No suitable fallback found
        }


        public static OrthogonalArrayDesign CreateOrthogonalArrayDesign(
            IReadOnlyList<FactorToAssignForDesign> factors,
            IReadOnlyList<ParameterInteraction> interactionsToConsider = null) {
            if (factors == null || !factors.Any()) {
                throw new ArgumentException("Factors list cannot be null or empty.", nameof(factors));
            }

            // Estimate DOF for interactions. If interactionsToConsider is null, assume 0.
            int interactionDof = interactionsToConsider != null ? CalculateInteractionDofNeeded(factors, interactionsToConsider, 0) : 0;

            string arrayDesignation = RecommendArray(factors, interactionsToConsider, interactionDof);
            if (string.IsNullOrEmpty(arrayDesignation)) {
                throw new DesignException("No suitable orthogonal array could be recommended for the given factors and interactions. " +
                                          "Consider reducing complexity or using a custom design.");
            }

            Logger.Info("OA_FACTORY", "Recommended OA: {ArrayDesignation}", arrayDesignation);

            OrthogonalArrayInfo arrayInfo = GetArrayInfo(arrayDesignation);
            OALevel[,] standardArrayMatrix = StandardArrays[arrayDesignation];
            Dictionary<string, int> assignments = AssignColumns(arrayInfo, standardArrayMatrix, factors, interactionsToConsider);

            return new OrthogonalArrayDesign(arrayDesignation, standardArrayMatrix, assignments);
        }

        private static Dictionary<string, int> AssignColumns(
            OrthogonalArrayInfo arrayInfo,
            OALevel[,] standardArrayMatrix,
            IReadOnlyList<FactorToAssignForDesign> factors,
            IReadOnlyList<ParameterInteraction> interactionsToConsider) {
            var columnAssignments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var usedPhysicalColumns = new HashSet<int>(); // 0-based indices of OA matrix columns

            // Get a list of columns that are likely to be used by interactions defined in the linear graph
            var interactionColumnsFromLinearGraph = GetReservedInteractionColumnsFromLinearGraph(arrayInfo.LinearGraph, interactionsToConsider, factors, columnAssignments /* pass empty initially */);

            var factorsRemaining = new List<FactorToAssignForDesign>(factors);

            // 1. Assign factors to preferred main factor columns from Linear Graph, if available and suitable
            if (arrayInfo.LinearGraph != null && arrayInfo.LinearGraph.MainFactorColumns.Any()) {
                foreach (int mainCol1Based in arrayInfo.LinearGraph.MainFactorColumns.OrderBy(c => c)) {
                    int mainCol0Based = mainCol1Based - 1;
                    if (usedPhysicalColumns.Contains(mainCol0Based) || interactionColumnsFromLinearGraph.Contains(mainCol0Based)) {
                        continue; // Skip if already used or reserved for a specific LG interaction
                    }
                    AssignFactorToColumnIfMatches(factorsRemaining, arrayInfo, mainCol0Based, columnAssignments, usedPhysicalColumns);
                }
            }

            // 2. Assign remaining factors to other available columns, preferring unreserved ones first
            AssignRemainingFactorsIteratively(factorsRemaining, arrayInfo, interactionColumnsFromLinearGraph, columnAssignments, usedPhysicalColumns, preferUnreserved: true);
            // 3. If still factors left, try assigning to reserved columns (if suitable and not used by higher priority assignment)
            AssignRemainingFactorsIteratively(factorsRemaining, arrayInfo, interactionColumnsFromLinearGraph, columnAssignments, usedPhysicalColumns, preferUnreserved: false);


            if (factorsRemaining.Any()) {
                string unassignedFactorNames = string.Join(", ", factorsRemaining.Select(f => $"{f.Name}({f.Levels}L)"));
                throw new DesignException(
                    $"Could not assign all main factors to array '{arrayInfo.Designation}'. Unassigned: {unassignedFactorNames}. " +
                    $"Array has {arrayInfo.MaximumFactors} columns with levels: {string.Join(",", arrayInfo.LevelCounts)}. " +
                    $"Used {usedPhysicalColumns.Count} columns. Check array capacity and factor level compatibility.");
            }

            // 4. Assign interaction names to their columns (either from LG or dynamically calculated)
            AssignInteractionNamesToColumns(arrayInfo, standardArrayMatrix, interactionsToConsider, columnAssignments, usedPhysicalColumns, factors);

            return columnAssignments;
        }
        
        private static void AssignInteractionNamesToColumns(
            OrthogonalArrayInfo arrayInfo,
            OALevel[,] oaMatrix,
            IReadOnlyList<ParameterInteraction> interactionsToConsider,
            Dictionary<string, int> columnAssignments, // Contains main factor assignments
            HashSet<int> usedPhysicalColumnsByMainFactors, // Columns used by main factors
            IReadOnlyList<FactorToAssignForDesign> allFactorsDesign) {

            if (interactionsToConsider == null || !interactionsToConsider.Any()) {
                return;
            }

            var factorLevelMap = allFactorsDesign.ToDictionary(f => f.Name, f => f.Levels, StringComparer.OrdinalIgnoreCase);

            foreach (var userInteraction in interactionsToConsider) {
                string interactionKey = GetCanonicalInteractionKey(userInteraction.FirstParameterName, userInteraction.SecondParameterName);

                if (columnAssignments.ContainsKey(interactionKey)) {
                    Logger.Debug("OA_FACTORY", "Interaction '{Key}' already assigned. Skipping.", interactionKey);
                    continue;
                }

                if (!columnAssignments.TryGetValue(userInteraction.FirstParameterName, out int p1AssignedCol0Based) ||
                    !columnAssignments.TryGetValue(userInteraction.SecondParameterName, out int p2AssignedCol0Based)) {
                    throw new DesignException(
                       $"Cannot assign interaction '{interactionKey}': Constituent main factors ('{userInteraction.FirstParameterName}' or '{userInteraction.SecondParameterName}') were not assigned to columns in the OA design.");
                }

                bool assignedThisInteraction = false;

                // Attempt 1: Use Linear Graph
                if (arrayInfo.LinearGraph != null) {
                    var lgInteraction = arrayInfo.LinearGraph.Interactions.FirstOrDefault(lg_i =>
                        (lg_i.Column1 == p1AssignedCol0Based + 1 && lg_i.Column2 == p2AssignedCol0Based + 1) ||
                        (lg_i.Column1 == p2AssignedCol0Based + 1 && lg_i.Column2 == p1AssignedCol0Based + 1));

                    if (lgInteraction != null && lgInteraction.InteractionColumns.Any()) {
                        int firstLGInteractionCol0Based = lgInteraction.InteractionColumns.First() - 1;
                        
                        // Check if this LG interaction column is already used by another *main factor*
                        bool colUsedByOtherMainFactor = columnAssignments.Any(ca =>
                            ca.Value == firstLGInteractionCol0Based && 
                            !ca.Key.Contains('*') && // It's a main factor
                            !string.Equals(ca.Key, userInteraction.FirstParameterName, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(ca.Key, userInteraction.SecondParameterName, StringComparison.OrdinalIgnoreCase) );

                        if (!colUsedByOtherMainFactor) {
                            columnAssignments[interactionKey] = firstLGInteractionCol0Based;
                            assignedThisInteraction = true;
                            Logger.Debug("OA_FACTORY", "Assigned interaction '{Key}' to column {Column} via Linear Graph.", interactionKey, firstLGInteractionCol0Based + 1);

                            // For 3-level x 3-level interactions, assign the second component if specified in LG
                            if (lgInteraction.InteractionColumns.Count > 1 && 
                                factorLevelMap[userInteraction.FirstParameterName] == 3 && 
                                factorLevelMap[userInteraction.SecondParameterName] == 3) {
                                int secondLGInteractionCol0Based = lgInteraction.InteractionColumns[1] - 1;
                                bool comp2ColUsedByOtherMainFactor = columnAssignments.Any(ca =>
                                    ca.Value == secondLGInteractionCol0Based && !ca.Key.Contains('*') &&
                                    ca.Key != userInteraction.FirstParameterName && ca.Key != userInteraction.SecondParameterName);
                                
                                if (!comp2ColUsedByOtherMainFactor) {
                                    columnAssignments[interactionKey + "_comp2"] = secondLGInteractionCol0Based;
                                     Logger.Debug("OA_FACTORY", "Assigned interaction '{Key}' (component 2) to column {Column} via Linear Graph.", interactionKey, secondLGInteractionCol0Based + 1);
                                } else {
                                     Logger.Warning("OA_FACTORY", "Interaction '{Key}': LG specifies component 2 in column {Column}, but it's used by another main factor. Component 2 unassigned from LG.", interactionKey, secondLGInteractionCol0Based + 1);
                                }
                            }
                        } else {
                             Logger.Warning("OA_FACTORY", "Interaction '{Key}': Linear Graph suggests column {Column}, but it's already used by another main factor. Attempting dynamic search.", interactionKey, firstLGInteractionCol0Based + 1);
                        }
                    }
                }

                // Attempt 2: Dynamic Calculation and Search (if not assigned by LG or LG assignment failed)
                if (!assignedThisInteraction) {
                    if (!factorLevelMap.TryGetValue(userInteraction.FirstParameterName, out int p1Levels) ||
                        !factorLevelMap.TryGetValue(userInteraction.SecondParameterName, out int p2Levels)) {
                        // Should be caught earlier, but defensive check.
                        throw new DesignException($"Internal error: Level definition missing for factors in interaction '{interactionKey}'.");
                    }

                    if (p1Levels == 2 && p2Levels == 2) {
                        OALevel[] calculatedInteractionPattern = Calculate2LevelInteractionColumn(oaMatrix, p1AssignedCol0Based, p2AssignedCol0Based);
                        // Exclude columns already assigned to *any* factor or interaction (key in columnAssignments)
                        var currentlyAssignedCols = new HashSet<int>(columnAssignments.Values);
                        int matchedCol = FindMatchingUnusedColumn(oaMatrix, calculatedInteractionPattern, currentlyAssignedCols, 2);
                        if (matchedCol != -1) {
                            columnAssignments[interactionKey] = matchedCol;
                            assignedThisInteraction = true;
                            Logger.Debug("OA_FACTORY", "Assigned interaction '{Key}' to dynamically matched column {Column} (2-level).", interactionKey, matchedCol + 1);
                        }
                    } else if (p1Levels == 3 && p2Levels == 3) {
                        var (calc_int1, calc_int2) = Calculate3LevelInteractionColumns(oaMatrix, p1AssignedCol0Based, p2AssignedCol0Based);
                        var currentlyAssignedCols = new HashSet<int>(columnAssignments.Values);

                        int matchedCol1 = FindMatchingUnusedColumn(oaMatrix, calc_int1, currentlyAssignedCols, 3);
                        if (matchedCol1 != -1) {
                            columnAssignments[interactionKey] = matchedCol1; // Assign first component
                            currentlyAssignedCols.Add(matchedCol1); // Mark as used for next search

                            int matchedCol2 = FindMatchingUnusedColumn(oaMatrix, calc_int2, currentlyAssignedCols, 3);
                            if (matchedCol2 != -1) {
                                columnAssignments[interactionKey + "_comp2"] = matchedCol2;
                                assignedThisInteraction = true;
                                Logger.Debug("OA_FACTORY", "Assigned interaction '{Key}' to dynamically matched columns {C1} (comp1) and {C2} (comp2) (3-level).", interactionKey, matchedCol1 + 1, matchedCol2 + 1);
                            } else {
                                Logger.Warning("OA_FACTORY", "Interaction '{Key}': First component dynamically assigned to col {C1}, but second component could not be placed. Interaction may be partially analyzed or confounded.", interactionKey, matchedCol1 + 1);
                                columnAssignments.Remove(interactionKey); // Roll back partial assignment
                                assignedThisInteraction = false;
                            }
                        }
                    } else {
                        Logger.Warning("OA_FACTORY", "Dynamic interaction column assignment for mixed-level interaction {Key} ({L1}-level x {L2}-level) is not currently supported by dynamic search. Relies on Linear Graph or manual assignment if array supports it.",
                            interactionKey, p1Levels, p2Levels);
                    }
                }

                if (!assignedThisInteraction) {
                    throw new DesignException(
                        $"Interaction '{interactionKey}' ({factorLevelMap[userInteraction.FirstParameterName]}Lx{factorLevelMap[userInteraction.SecondParameterName]}L) could not be assigned to any suitable column in array '{arrayInfo.Designation}'. " +
                        $"Factors involved: '{userInteraction.FirstParameterName}' (col {p1AssignedCol0Based + 1}), '{userInteraction.SecondParameterName}' (col {p2AssignedCol0Based + 1}). " +
                        "Consider a larger array, fewer interactions, or an array with a more comprehensive Linear Graph.");
                }
            }
        }

        private static string GetCanonicalInteractionKey(string param1, string param2) {
            // Ensure consistent ordering for interaction keys.
            return string.Compare(param1, param2, StringComparison.OrdinalIgnoreCase) < 0
                ? $"{param1}*{param2}"
                : $"{param2}*{param1}";
        }

        private static int FindMatchingUnusedColumn(
            OALevel[,] oaMatrix,
            OALevel[] patternToMatch,
            HashSet<int> alreadyUsedColumns0Based, // Columns already assigned to main factors or other interactions
            int expectedColumnLevelType) { // e.g., 2 for a 2-level column, 3 for a 3-level

            int numRuns = oaMatrix.GetLength(0);
            int numColsInMatrix = oaMatrix.GetLength(1);

            if (numRuns != patternToMatch.Length) {
                Logger.Warning("OA_FACTORY", "Pattern length mismatch in FindMatchingUnusedColumn. Pattern: {PatternLen}, Matrix Runs: {MatrixRuns}", patternToMatch.Length, numRuns);
                return -1;
            }

            for (int colIdx = 0; colIdx < numColsInMatrix; colIdx++) {
                if (alreadyUsedColumns0Based.Contains(colIdx)) {
                    continue; // Column already definitively assigned
                }

                // Check if this column in oaMatrix is suitable for the expected level type
                // This requires consulting the ArrayInfo's LevelCounts for the *physical* OA columns.
                // This step was missing and is crucial for mixed-level arrays.
                // However, OrthogonalArrayInfo.LevelCounts is for the *standard* array, not necessarily the one passed in `oaMatrix` if it's a sub-array or modified.
                // For now, assume oaMatrix IS the standard array.
                // A more robust way: check the actual levels present in oaMatrix[:, colIdx].
                bool levelsInColAreConsistent = true;
                for(int r = 0; r < numRuns; ++r) {
                    if (oaMatrix[r, colIdx].Level > expectedColumnLevelType) {
                        levelsInColAreConsistent = false;
                        break;
                    }
                }
                if (!levelsInColAreConsistent) {
                    continue; // This column contains levels higher than what's expected for this interaction type
                }


                bool match = true;
                for (int runIdx = 0; runIdx < numRuns; runIdx++) {
                    if (oaMatrix[runIdx, colIdx] != patternToMatch[runIdx]) {
                        match = false;
                        break;
                    }
                }
                if (match) {
                    return colIdx; // Found a matching, unused, and level-consistent column
                }
            }
            return -1; // No suitable match found
        }

        private static HashSet<int> GetReservedInteractionColumnsFromLinearGraph(
            LinearGraph linearGraph,
            IReadOnlyList<ParameterInteraction> interactionsToConsider,
            IReadOnlyList<FactorToAssignForDesign> allFactors,
            Dictionary<string, int> currentAssignments) { // Pass current assignments to resolve factor names to columns

            var reservedColumns = new HashSet<int>();
            if (linearGraph == null || interactionsToConsider == null || !interactionsToConsider.Any()) {
                return reservedColumns;
            }

            var factorNameToCol0BasedMap = currentAssignments
                .Where(kvp => !kvp.Key.Contains('*')) // Only main factors
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            foreach (var userInteraction in interactionsToConsider) {
                // Try to find this userInteraction in the LinearGraph
                // This requires knowing which OA columns the userInteraction's main factors are assigned to.
                // This is a bit of a chicken-and-egg problem if called before main factor assignment.
                // For now, this method is more about identifying *potential* interaction columns from LG.

                // A simpler interpretation: if any interaction is requested, all columns listed in *any* LG interaction are "softly" reserved.
                // This is a broad reservation.
            }
            // If user wants to study any interactions, all columns listed in the LG's interactions list are candidates.
            if (interactionsToConsider.Any()) {
                foreach (var lgInteraction in linearGraph.Interactions) {
                    foreach (var col1Based in lgInteraction.InteractionColumns) {
                        reservedColumns.Add(col1Based - 1); // Convert to 0-based
                    }
                }
            }
            if (reservedColumns.Any()) {
                 Logger.Debug("OA_FACTORY", "Identified {Count} columns from Linear Graph as potentially reserved for interactions: [{Columns}]",
                             reservedColumns.Count, string.Join(", ", reservedColumns.Select(c => c + 1)));
            }
            return reservedColumns;
        }
        
        private static void AssignFactorToColumnIfMatches(
            List<FactorToAssignForDesign> factorsRemaining,
            OrthogonalArrayInfo arrayInfo,
            int col0Based, // Candidate column in the OA matrix (0-based)
            Dictionary<string, int> columnAssignments, // Accumulates assignments
            HashSet<int> usedPhysicalColumns) { // Tracks used OA matrix columns

            if (col0Based < 0 || col0Based >= arrayInfo.LevelCounts.Count) {
                 Logger.Warning("OA_FACTORY", "Attempted to assign factor to column {ColumnIndex}, which is out of bounds for array {ArrayName}'s defined LevelCounts.", col0Based + 1, arrayInfo.Designation);
                return; // Column index is out of bounds for LevelCounts
            }

            int levelOfThisColumn = arrayInfo.LevelCounts[col0Based];

            for (int i = 0; i < factorsRemaining.Count; i++) {
                FactorToAssignForDesign factor = factorsRemaining[i];
                if (factor.Levels == levelOfThisColumn) { // Match factor levels with column capability
                    columnAssignments[factor.Name] = col0Based;
                    usedPhysicalColumns.Add(col0Based);
                    factorsRemaining.RemoveAt(i);
                    Logger.Debug("OA_FACTORY", "Assigned factor '{FactorName}' ({FactorLevels}L) to OA column {OAColIdx} ({ColumnLevels}L).",
                                 factor.Name, factor.Levels, col0Based + 1, levelOfThisColumn);
                    return; // Factor assigned
                }
            }
        }

        private static void AssignRemainingFactorsIteratively(
            List<FactorToAssignForDesign> factorsRemaining,
            OrthogonalArrayInfo arrayInfo,
            HashSet<int> interactionColumnsToPotentiallyAvoid, // Columns that might be used by LG interactions
            Dictionary<string, int> columnAssignments,
            HashSet<int> usedPhysicalColumns,
            bool preferUnreserved) {

            // Sort factors to assign, e.g., by number of levels (descending) to fit complex ones first.
            var sortedFactorsToAssign = factorsRemaining.OrderByDescending(f => f.Levels).ToList();

            foreach (var factor in sortedFactorsToAssign) {
                if (!factorsRemaining.Contains(factor)) { continue; } // Already assigned in this iteration by another call

                int assignedCol = -1;
                // Iterate through all physical columns of the array
                for (int colIdx0Based = 0; colIdx0Based < arrayInfo.MaximumFactors; colIdx0Based++) {
                    if (colIdx0Based >= arrayInfo.LevelCounts.Count) { continue; } // Should not happen if MaxFactors matches LevelCounts length

                    bool isPotentiallyReservedForInteraction = interactionColumnsToPotentiallyAvoid.Contains(colIdx0Based);

                    if (preferUnreserved && isPotentiallyReservedForInteraction) {
                        continue; // In "prefer unreserved" mode, skip columns potentially used by LG interactions
                    }
                    if (!preferUnreserved && !isPotentiallyReservedForInteraction && interactionColumnsToPotentiallyAvoid.Any()) {
                        // In "use reserved if necessary" mode, if there are any reserved columns,
                        // skip unreserved ones to prioritize filling reserved ones if they match.
                        // This logic might need refinement based on how "soft" the reservation is.
                        // A simpler approach: if preferUnreserved is false, consider all columns.
                        // The current logic tries to use reserved ones if !preferUnreserved AND reserved ones exist.
                        continue;
                    }

                    if (!usedPhysicalColumns.Contains(colIdx0Based) && arrayInfo.LevelCounts[colIdx0Based] == factor.Levels) {
                        columnAssignments[factor.Name] = colIdx0Based;
                        usedPhysicalColumns.Add(colIdx0Based);
                        factorsRemaining.Remove(factor); // Remove from the original list being modified
                        assignedCol = colIdx0Based;
                        Logger.Debug("OA_FACTORY", "Assigned remaining factor '{FactorName}' ({FactorLevels}L) to OA column {OAColIdx} ({ColumnLevels}L). ReservedForInteraction: {IsReserved}, Mode: {Mode}",
                                     factor.Name, factor.Levels, colIdx0Based + 1, arrayInfo.LevelCounts[colIdx0Based], isPotentiallyReservedForInteraction, preferUnreserved ? "PreferUnreserved" : "UseReservedIfNeeded");
                        break; // Factor assigned, move to next factor
                    }
                }
            }
        }



        public static List<Dictionary<string, Level>> CreateParameterConfigurations(
            OrthogonalArrayDesign design,
            IReadOnlyDictionary<string, ParameterLevelSet> parameterDefinitions // Name -> ParameterLevelSet from EngineConfiguration.ControlFactors
            ) {
            ArgumentNullException.ThrowIfNull(design);
            ArgumentNullException.ThrowIfNull(parameterDefinitions);

            var (arrayDesignation, oaMatrix, columnAssignments) = design;
            int runs = oaMatrix.GetLength(0);
            int maxColsInMatrix = oaMatrix.GetLength(1);
            var configurations = new List<Dictionary<string, Level>>();

            // Filter columnAssignments to only include main control factors that have definitions
            var mainFactorAssignments = columnAssignments
                .Where(kvp => !kvp.Key.Contains('*') && parameterDefinitions.ContainsKey(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            for (int run = 0; run < runs; run++) {
                var currentConfig = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
                foreach (var (paramName, assignedColumnIndex0Based) in mainFactorAssignments) {
                    if (assignedColumnIndex0Based < 0 || assignedColumnIndex0Based >= maxColsInMatrix) {
                        throw new DesignException($"Column index {assignedColumnIndex0Based + 1} for parameter '{paramName}' is outside the bounds of OA '{arrayDesignation}' (max cols: {maxColsInMatrix}).");
                    }
                    if (!parameterDefinitions.TryGetValue(paramName, out ParameterLevelSet paramLevelSet)) {
                        // This should ideally be caught earlier during validation if a factor in design doesn't have definitions.
                        throw new DesignException($"No level definitions found for parameter '{paramName}' which is assigned to column {assignedColumnIndex0Based + 1}.");
                    }

                    OALevel oaLevelFromMatrixCell = oaMatrix[run, assignedColumnIndex0Based];

                    if (!paramLevelSet.TryGetValue(oaLevelFromMatrixCell, out Level actualLevelObject)) {
                        // This indicates a mismatch between the OA cell's symbol and the defined levels for the factor.
                        // e.g., OA cell has '3', but factor only has OALevels 1 and 2 defined.
                        string definedOALevelsStr = string.Join(", ", paramLevelSet.Keys.Select(k => k.ToString()));
                        throw new DesignException(
                            $"Orthogonal array cell [{run + 1},{assignedColumnIndex0Based + 1}] for factor '{paramName}' contains symbol '{oaLevelFromMatrixCell}', " +
                            $"but this factor only has defined OALevels: [{definedOALevelsStr}]. " +
                            $"Ensure factor levels in configuration match the capabilities of the OA column it's assigned to.");
                    }
                    currentConfig[paramName] = actualLevelObject;
                }
                configurations.Add(currentConfig);
            }
            return configurations;
        }

        public static OALevel[] Calculate2LevelInteractionColumn(OALevel[,] array, int column1_0based, int column2_0based) {
            ArgumentNullException.ThrowIfNull(array);
            int runs = array.GetLength(0);
            int maxCols = array.GetLength(1);

            if (column1_0based < 0 || column1_0based >= maxCols) { throw new ArgumentOutOfRangeException(nameof(column1_0based)); }
            if (column2_0based < 0 || column2_0based >= maxCols) { throw new ArgumentOutOfRangeException(nameof(column2_0based)); }
            if (column1_0based == column2_0based) { throw new ArgumentException("Interaction columns must be different."); }

            var interactionCol = new OALevel[runs];
            for (int i = 0; i < runs; i++) {
                OALevel val1 = array[i, column1_0based];
                OALevel val2 = array[i, column2_0based];
                if (val1.Level > 2 || val2.Level > 2) { throw new ArgumentException($"Columns {column1_0based+1} and {column2_0based+1} must be 2-level for this calculation."); }
                interactionCol[i] = (val1 == val2) ? OALevel.One : OALevel.Two;
            }
            return interactionCol;
        }

        public static (OALevel[] interactionCol1, OALevel[] interactionCol2) Calculate3LevelInteractionColumns(OALevel[,] array, int column1_0based, int column2_0based) {
            ArgumentNullException.ThrowIfNull(array);
            int runs = array.GetLength(0);
            int maxCols = array.GetLength(1);

            if (column1_0based < 0 || column1_0based >= maxCols) { throw new ArgumentOutOfRangeException(nameof(column1_0based)); }
            if (column2_0based < 0 || column2_0based >= maxCols) { throw new ArgumentOutOfRangeException(nameof(column2_0based)); }
            if (column1_0based == column2_0based) { throw new ArgumentException("Interaction columns must be different."); }

            var intCol1 = new OALevel[runs];
            var intCol2 = new OALevel[runs];

            for (int i = 0; i < runs; i++) {
                int levelA_0based = array[i, column1_0based].Level - 1;
                int levelB_0based = array[i, column2_0based].Level - 1;

                if (levelA_0based < 0 || levelA_0based > 2 || levelB_0based < 0 || levelB_0based > 2) {
                    throw new ArgumentException($"Invalid level found in array at run {i + 1}, columns {column1_0based + 1}/{column2_0based + 1}. Expected OALevels 1, 2, or 3 for 3-level interaction calculation.");
                }
                intCol1[i] = OALevel.Parse(((levelA_0based + levelB_0based) % 3 + 1).ToString());
                intCol2[i] = OALevel.Parse(((levelA_0based + 2 * levelB_0based) % 3 + 1).ToString()); // Standard formula for second component: A + 2B (mod 3)
            }
            return (intCol1, intCol2);
        }
    }

    // OrthogonalArrayFactoryHelper contains static data (array definitions, L32 col map, etc.)
    // It remains unchanged from the original, assuming it's correctly defined.
    // For brevity, its content is not repeated here.
    public static class OrthogonalArrayFactoryHelper {
        public static readonly int[] L32_ColMap = new int[] { 1, 2, 4, 8, 16, 3, 5, 9, 17, 6, 10, 18, 12, 20, 24, 7, 11, 19, 13, 21, 25, 14, 22, 26, 28, 15, 23, 27, 29, 30, 31 };
        
        // L32 Linear Graph Interactions (example, needs to be comprehensive if used)
        // This is a subset for demonstration. A full L32 LG is extensive.
        // Standard L32 interactions are usually derived as needed rather than fully listed.
        // The Phadke approach uses specific base columns and derives interactions.
        // For this factory, if L32 interactions are to be explicitly assigned from LG, this list needs to be accurate and complete for the chosen model.
        // Typically, for L32, one might only define LG for a few key interactions or rely on dynamic calculation for 2-level factors.
        public static readonly List<ColumnInteraction> L32_LinearGraphInteractions = new() {
            new(1, 2, 3), new(1, 4, 5), new(2, 4, 6), // Interactions between first 3 base columns (1,2,4)
            new(1, 8, 9), new(2, 8, 10), new(4, 8, 12), // Interactions with 4th base column (8)
            new(1, 16, 17), new(2, 16, 18), new(4, 16, 20), new(8, 16, 24), // Interactions with 5th base column (16)
            // ... many more 2-factor interactions ...
            // Selected 3-factor:
            new(1,2, [4,7]), // (1x2)x4 -> 3x4 -> 7
            // ... and so on.
        };

        public static readonly OALevel[,] L16_2_12_4_1_Data = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2 }, { 1, 2, 2, 2, 2, 1, 1, 1, 1, 2, 2, 2, 2 }, { 1, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1 }, { 2, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2 }, { 2, 1, 1, 2, 2, 2, 2, 1, 1, 2, 2, 1, 1 }, { 2, 2, 2, 1, 1, 1, 1, 2, 2, 2, 2, 1, 1 }, { 2, 2, 2, 1, 1, 2, 2, 1, 1, 1, 1, 2, 2 }, { 3, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2 }, { 3, 1, 2, 1, 2, 2, 1, 2, 1, 2, 1, 2, 1 }, { 3, 2, 1, 2, 1, 1, 2, 2, 1, 2, 1, 2, 1 }, { 3, 2, 1, 2, 1, 2, 1, 1, 2, 1, 2, 1, 2 }, { 4, 1, 2, 2, 1, 1, 2, 2, 1, 2, 2, 1, 1 }, { 4, 1, 2, 2, 1, 2, 1, 1, 2, 1, 1, 2, 2 }, { 4, 2, 1, 1, 2, 1, 2, 1, 2, 2, 1, 2, 2 }, { 4, 2, 1, 1, 2, 2, 1, 2, 1, 1, 2, 1, 1 } };
        public static readonly OALevel[,] L16_2_9_4_2_Data = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2 }, { 1, 2, 1, 1, 2, 2, 2, 1, 1, 2, 2 }, { 1, 2, 2, 2, 1, 1, 1, 2, 2, 1, 1 }, { 2, 1, 1, 2, 1, 2, 2, 1, 2, 2, 1 }, { 2, 1, 2, 1, 2, 1, 1, 2, 1, 1, 2 }, { 2, 2, 1, 2, 2, 2, 1, 2, 1, 1, 2 }, { 2, 2, 2, 1, 1, 1, 2, 1, 2, 2, 1 }, { 3, 1, 2, 1, 2, 2, 1, 2, 2, 1, 2 }, { 3, 1, 1, 2, 1, 1, 2, 1, 1, 2, 1 }, { 3, 2, 2, 1, 2, 1, 2, 2, 2, 1, 1 }, { 3, 2, 1, 2, 1, 2, 1, 1, 1, 2, 2 }, { 4, 1, 2, 2, 1, 2, 1, 1, 1, 1, 1 }, { 4, 1, 1, 1, 2, 1, 1, 2, 2, 2, 2 }, { 4, 2, 2, 2, 2, 1, 2, 1, 2, 2, 2 }, { 4, 2, 1, 1, 1, 2, 1, 2, 1, 1, 1 } };
        public static readonly OALevel[,] L16_2_6_4_3_Data = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 1, 2, 2, 2, 2, 2, 2 }, { 1, 2, 2, 1, 1, 2, 2, 1, 1 }, { 1, 2, 2, 2, 2, 1, 1, 2, 2 }, { 2, 1, 2, 1, 2, 1, 1, 2, 2 }, { 2, 1, 2, 2, 1, 2, 2, 1, 1 }, { 2, 2, 1, 1, 2, 1, 2, 2, 1 }, { 2, 2, 1, 2, 1, 2, 1, 1, 2 }, { 3, 1, 3, 1, 2, 2, 1, 1, 2 }, { 3, 1, 3, 2, 1, 1, 2, 2, 1 }, { 3, 2, 4, 1, 2, 1, 2, 2, 1 }, { 3, 2, 4, 2, 1, 2, 1, 1, 2 }, { 4, 1, 4, 1, 1, 2, 2, 1, 1 }, { 4, 1, 4, 2, 2, 1, 1, 2, 2 }, { 4, 2, 3, 1, 1, 1, 1, 1, 2 }, { 4, 2, 3, 2, 2, 2, 2, 2, 1 } };
        public static readonly OALevel[,] L16_2_3_4_4_Data = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 1, 2, 2, 2, 2 }, { 1, 2, 2, 1, 1, 2, 2 }, { 1, 2, 2, 2, 2, 1, 1 }, { 2, 1, 2, 1, 2, 1, 2 }, { 2, 1, 2, 2, 1, 2, 1 }, { 2, 2, 1, 1, 2, 2, 1 }, { 2, 2, 1, 2, 1, 1, 2 }, { 3, 3, 1, 1, 2, 1, 2 }, { 3, 3, 1, 2, 1, 2, 1 }, { 3, 4, 2, 1, 2, 2, 1 }, { 3, 4, 2, 2, 1, 1, 2 }, { 4, 3, 2, 1, 1, 2, 1 }, { 4, 3, 2, 2, 2, 1, 2 }, { 4, 4, 1, 1, 1, 1, 2 }, { 4, 4, 1, 2, 2, 2, 1 } };
        public static readonly OALevel[,] L18_6_1_3_6_Data = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1 }, { 1, 2, 2, 2, 2, 2, 2 }, { 1, 3, 3, 3, 3, 3, 3 }, { 2, 1, 1, 2, 2, 3, 3 }, { 2, 2, 2, 3, 3, 1, 1 }, { 2, 3, 3, 1, 1, 2, 2 }, { 3, 1, 2, 1, 3, 2, 3 }, { 3, 2, 3, 2, 1, 3, 1 }, { 3, 3, 1, 3, 2, 1, 2 }, { 4, 1, 3, 3, 2, 2, 1 }, { 4, 2, 1, 1, 3, 3, 2 }, { 4, 3, 2, 2, 1, 1, 3 }, { 5, 1, 2, 3, 1, 3, 2 }, { 5, 2, 3, 1, 2, 1, 3 }, { 5, 3, 1, 2, 3, 2, 1 }, { 6, 1, 3, 2, 3, 1, 2 }, { 6, 2, 1, 3, 1, 2, 3 }, { 6, 3, 2, 1, 2, 3, 1 } };
        public static readonly OALevel[,] L25_5_6_Data = new OALevel[,] { { 1, 1, 1, 1, 1, 1 }, { 1, 2, 2, 2, 2, 2 }, { 1, 3, 3, 3, 3, 3 }, { 1, 4, 4, 4, 4, 4 }, { 1, 5, 5, 5, 5, 5 }, { 2, 1, 2, 3, 4, 5 }, { 2, 2, 3, 4, 5, 1 }, { 2, 3, 4, 5, 1, 2 }, { 2, 4, 5, 1, 2, 3 }, { 2, 5, 1, 2, 3, 4 }, { 3, 1, 3, 5, 2, 4 }, { 3, 2, 4, 1, 3, 5 }, { 3, 3, 5, 2, 4, 1 }, { 3, 4, 1, 3, 5, 2 }, { 3, 5, 2, 4, 1, 3 }, { 4, 1, 4, 2, 5, 3 }, { 4, 2, 5, 3, 1, 4 }, { 4, 3, 1, 4, 2, 5 }, { 4, 4, 2, 5, 3, 1 }, { 4, 5, 3, 1, 4, 2 }, { 5, 1, 5, 4, 3, 2 }, { 5, 2, 1, 5, 4, 3 }, { 5, 3, 2, 1, 5, 4 }, { 5, 4, 3, 2, 1, 5 }, { 5, 5, 4, 3, 2, 1 } };
        public static readonly OALevel[,] L32_2_1_4_9_Data = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2 }, { 1, 1, 1, 2, 2, 1, 1, 1, 2, 2 }, { 1, 1, 1, 2, 2, 2, 2, 2, 1, 1 }, { 1, 1, 2, 1, 2, 1, 2, 2, 1, 2 }, { 1, 1, 2, 1, 2, 2, 1, 1, 2, 1 }, { 1, 1, 2, 2, 1, 1, 2, 2, 2, 1 }, { 1, 1, 2, 2, 1, 2, 1, 1, 1, 2 }, { 1, 2, 1, 1, 2, 2, 1, 2, 1, 2 }, { 1, 2, 1, 1, 2, 1, 2, 1, 2, 1 }, { 1, 2, 1, 2, 1, 2, 1, 2, 2, 1 }, { 1, 2, 1, 2, 1, 1, 2, 1, 1, 2 }, { 1, 2, 2, 1, 1, 2, 2, 1, 1, 1 }, { 1, 2, 2, 1, 1, 1, 1, 2, 2, 2 }, { 1, 2, 2, 2, 2, 2, 2, 1, 2, 2 }, { 1, 2, 2, 2, 2, 1, 1, 2, 1, 1 }, { 2, 1, 2, 2, 1, 2, 1, 1, 2, 1 }, { 2, 1, 2, 2, 1, 1, 2, 2, 1, 2 }, { 2, 1, 2, 1, 2, 2, 1, 1, 1, 2 }, { 2, 1, 2, 1, 2, 1, 2, 2, 2, 1 }, { 2, 1, 1, 2, 2, 2, 2, 2, 2, 2 }, { 2, 1, 1, 2, 2, 1, 1, 1, 1, 1 }, { 2, 1, 1, 1, 1, 2, 2, 2, 1, 1 }, { 2, 1, 1, 1, 1, 1, 1, 1, 2, 2 }, { 2, 2, 2, 2, 2, 1, 1, 2, 2, 2 }, { 2, 2, 2, 2, 2, 2, 2, 1, 1, 1 }, { 2, 2, 2, 1, 1, 1, 1, 2, 1, 1 }, { 2, 2, 2, 1, 1, 2, 2, 1, 2, 2 }, { 2, 2, 1, 2, 1, 1, 2, 1, 2, 1 }, { 2, 2, 1, 2, 1, 2, 1, 2, 1, 2 }, { 2, 2, 1, 1, 2, 1, 2, 1, 1, 2 }, { 2, 2, 1, 1, 2, 2, 1, 2, 2, 1 } };
        public static readonly OALevel[,] L36_2_11_3_12_Data = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 }, { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 }, { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3 }, { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 1, 1, 1, 1 }, { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 1, 1, 1, 1, 2, 2, 2, 2 }, { 1, 2, 2, 2, 2, 1, 1, 1, 2, 2, 2, 1, 1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 2 }, { 1, 2, 2, 2, 2, 1, 1, 1, 2, 2, 2, 2, 2, 3, 1, 2, 3, 1, 2, 3, 1, 3, 1 }, { 1, 2, 2, 2, 2, 1, 1, 1, 2, 2, 2, 3, 3, 1, 2, 3, 1, 2, 3, 1, 2, 2, 3 }, { 1, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 2, 3, 2, 3, 1, 3, 1, 2, 2, 3 }, { 1, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 2, 2, 3, 1, 3, 1, 2, 1, 2, 3, 3, 1 }, { 1, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 3, 3, 1, 2, 1, 2, 3, 2, 3, 1, 1, 2 }, { 2, 1, 2, 2, 1, 1, 2, 2, 1, 2, 1, 1, 2, 1, 3, 1, 3, 2, 2, 3, 1, 3, 2 }, { 2, 1, 2, 2, 1, 1, 2, 2, 1, 2, 1, 2, 3, 2, 1, 2, 1, 3, 3, 1, 2, 1, 3 }, { 2, 1, 2, 2, 1, 1, 2, 2, 1, 2, 1, 3, 1, 3, 2, 3, 2, 1, 1, 2, 3, 2, 1 }, { 2, 1, 2, 2, 1, 2, 1, 1, 2, 1, 2, 1, 2, 1, 3, 3, 2, 1, 1, 2, 3, 2, 3 }, { 2, 1, 2, 2, 1, 2, 1, 1, 2, 1, 2, 2, 3, 2, 1, 1, 3, 2, 2, 3, 1, 3, 1 }, { 2, 1, 2, 2, 1, 2, 1, 1, 2, 1, 2, 3, 1, 3, 2, 2, 1, 3, 3, 1, 2, 1, 2 }, { 2, 2, 1, 1, 2, 1, 2, 1, 2, 2, 1, 1, 2, 1, 3, 2, 1, 3, 3, 2, 1, 1, 3 }, { 2, 2, 1, 1, 2, 1, 2, 1, 2, 2, 1, 2, 3, 2, 1, 3, 2, 1, 1, 3, 2, 2, 1 }, { 2, 2, 1, 1, 2, 1, 2, 1, 2, 2, 1, 3, 1, 3, 2, 1, 3, 2, 2, 1, 3, 3, 2 }, { 2, 2, 1, 1, 2, 2, 1, 2, 1, 1, 2, 1, 2, 1, 3, 1, 3, 2, 3, 1, 2, 3, 2 }, { 2, 2, 1, 1, 2, 2, 1, 2, 1, 1, 2, 2, 3, 2, 1, 2, 1, 3, 1, 2, 3, 1, 3 }, { 2, 2, 1, 1, 2, 2, 1, 2, 1, 1, 2, 3, 1, 3, 2, 3, 2, 1, 2, 3, 1, 2, 1 } };
        public static readonly OALevel[,] L36_2_3_3_13_Data = new OALevel[,] { { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }, { 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2 }, { 1, 1, 1, 1, 1, 1, 1, 3, 3, 3, 3, 3, 3, 3, 3, 3 }, { 1, 1, 1, 2, 2, 2, 2, 1, 1, 1, 1, 2, 2, 2, 2, 3 }, { 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 1 }, { 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 1, 1, 1, 1, 2 }, { 1, 1, 1, 3, 3, 3, 3, 1, 1, 1, 1, 3, 3, 3, 3, 2 }, { 1, 1, 1, 3, 3, 3, 3, 2, 2, 2, 2, 1, 1, 1, 1, 3 }, { 1, 1, 1, 3, 3, 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 1 }, { 1, 2, 2, 1, 2, 3, 1, 1, 2, 3, 1, 2, 3, 1, 2, 3 }, { 1, 2, 2, 1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 3, 1, 2 }, { 1, 2, 2, 1, 2, 3, 1, 3, 1, 2, 3, 1, 2, 2, 3, 1 }, { 1, 2, 2, 2, 3, 1, 2, 1, 2, 3, 2, 3, 1, 2, 3, 1 }, { 1, 2, 2, 2, 3, 1, 2, 2, 3, 1, 3, 1, 2, 1, 2, 3 }, { 1, 2, 2, 2, 3, 1, 2, 3, 1, 2, 1, 2, 3, 3, 1, 2 }, { 1, 2, 2, 3, 1, 2, 3, 1, 2, 3, 3, 1, 2, 3, 1, 2 }, { 1, 2, 2, 3, 1, 2, 3, 2, 3, 1, 1, 2, 3, 2, 3, 1 }, { 1, 2, 2, 3, 1, 2, 3, 3, 1, 2, 2, 3, 1, 1, 2, 3 }, { 2, 1, 2, 3, 1, 2, 3, 1, 3, 2, 1, 3, 2, 1, 3, 2 }, { 2, 1, 2, 3, 1, 2, 3, 2, 1, 3, 2, 1, 3, 3, 2, 1 }, { 2, 1, 2, 3, 1, 2, 3, 3, 2, 1, 3, 2, 1, 2, 1, 3 }, { 2, 1, 2, 1, 2, 3, 1, 1, 3, 2, 3, 2, 1, 2, 1, 3 }, { 2, 1, 2, 1, 2, 3, 1, 2, 1, 3, 1, 3, 2, 3, 2, 1 }, { 2, 1, 2, 1, 2, 3, 1, 3, 2, 1, 2, 1, 3, 1, 3, 2 }, { 2, 1, 2, 2, 3, 1, 2, 1, 3, 2, 2, 1, 3, 3, 2, 1 }, { 2, 1, 2, 2, 3, 1, 2, 2, 1, 3, 3, 2, 1, 1, 3, 2 }, { 2, 1, 2, 2, 3, 1, 2, 3, 2, 1, 1, 3, 2, 2, 1, 3 }, { 2, 2, 3, 1, 3, 2, 1, 1, 2, 3, 2, 3, 1, 1, 2, 3 }, { 2, 2, 3, 1, 3, 2, 1, 2, 3, 1, 3, 1, 2, 2, 3, 1 }, { 2, 2, 3, 1, 3, 2, 1, 3, 1, 2, 1, 2, 3, 3, 1, 2 }, { 2, 2, 3, 2, 1, 3, 2, 1, 2, 3, 3, 1, 2, 2, 3, 1 }, { 2, 2, 3, 2, 1, 3, 2, 2, 3, 1, 1, 2, 3, 3, 1, 2 }, { 2, 2, 3, 2, 1, 3, 2, 3, 1, 2, 2, 3, 1, 1, 2, 3 }, { 2, 2, 3, 3, 2, 1, 3, 1, 2, 3, 1, 2, 3, 3, 1, 2 }, { 2, 2, 3, 3, 2, 1, 3, 2, 3, 1, 2, 3, 1, 1, 2, 3 }, { 2, 2, 3, 3, 2, 1, 3, 3, 1, 2, 3, 1, 2, 2, 3, 1 } };
    }


    /// <summary>
    /// Custom exception type for errors encountered during orthogonal array design or generation.
    /// </summary>
    public class DesignException : InvalidOperationException {
        public DesignException(string message) : base(message) { }
        public DesignException(string message, Exception innerException) : base(message, innerException) { }
    }
}