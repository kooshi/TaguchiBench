// TaguchiBench.Engine/Core/Level.cs

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis; // For SetsRequiredMembers
using System.Linq;

namespace TaguchiBench.Engine.Core {
    /// <summary>
    /// Represents a level within an orthogonal array column, typically 1, 2, 3, etc.
    /// This record ensures type safety and clear semantics for OA level indexing.
    /// </summary>
    public record OALevel {
        public static OALevel One { get; } = new(1);
        public static OALevel Two { get; } = new(2);
        public static OALevel Three { get; } = new(3);
        public static OALevel Four { get; } = new(4);
        public static OALevel Five { get; } = new(5);
        public static OALevel Six { get; } = new(6);
        // Consider adding more if standard arrays require them, though 6 is common for many published OAs.

        public required int Level { get; init; }

        [SetsRequiredMembers]
        private OALevel(int level) {
            // A pragmatic limit, extendable if necessary. Most standard OAs use up to 5 or so.
            if (level < 1 || level > 27) { // L27 has 3^13, implying up to 3 levels. L36(6^1) needs 6. Max standard OA level is often low.
                                           // Let's allow for more flexibility if custom arrays or higher-level factors are ever introduced.
                                           // However, for standard Taguchi, levels rarely exceed 5. Let's keep it reasonable.
                                           // Re-evaluating: The *value* of the OA cell is 1,2,3... The *number of levels* a factor has can be different.
                                           // This OALevel represents the symbol in the OA cell.
                throw new ArgumentOutOfRangeException(nameof(level), $"Orthogonal array cell level must be positive and within a practical range (e.g., 1-27 for L27). Value was: {level}.");
            }
            Level = level;
        }

        public static implicit operator OALevel(int level) {
            return new OALevel(level);
        }

        public static implicit operator int(OALevel oaLevel) {
            return oaLevel.Level;
        }

        public static bool operator <(OALevel left, OALevel right) {
            if (left is null || right is null) { return false; } // Or throw, depending on desired null handling.
            return left.Level < right.Level;
        }
        public static bool operator >(OALevel left, OALevel right) {
            if (left is null || right is null) { return false; }
            return left.Level > right.Level;
        }
        public static bool operator <=(OALevel left, OALevel right) {
            if (left is null || right is null) { return left is null && right is null; }
            return left.Level <= right.Level;
        }
        public static bool operator >=(OALevel left, OALevel right) {
            if (left is null || right is null) { return left is null && right is null; }
            return left.Level >= right.Level;
        }

        // Arithmetic operators on OALevels are generally not meaningful in the context of OA symbols.
        // They were present before, but their use cases are questionable and can lead to invalid levels.
        // If specific OA algebra requires them (e.g., for interaction column calculation),
        // it should be handled explicitly with integer arithmetic on the .Level property.
        // Removing ++, --, +, - operators to prevent misuse.

        public static OALevel Parse(string value) {
            if (int.TryParse(value, out int level)) {
                return new OALevel(level); // Validation occurs in the constructor.
            }
            throw new FormatException($"Invalid OALevel string representation: '{value}'. Must be an integer.");
        }

        public override string ToString() {
            return Level.ToString();
        }
    }

    /// <summary>
    /// Represents a specific level of a <see cref="Factor"/>,
    /// associating an orthogonal array level (<see cref="OALevel"/>) with its actual string value.
    /// </summary>
    public record Level {
        /// <summary>
        /// The symbolic level (e.g., 1, 2, 3) as it appears in the orthogonal array matrix cell.
        /// </summary>
        public required OALevel OALevel { get; init; }

        /// <summary>
        /// The actual string value of the factor at this level (e.g., "0.5", "true", "config_A").
        /// </summary>
        public required string Value { get; init; }

        // Default constructor for deserialization if ever needed, though records are usually fine.
        public Level() { }

        [SetsRequiredMembers]
        public Level(OALevel oaLevel, string value) {
            OALevel = oaLevel ?? throw new ArgumentNullException(nameof(oaLevel));
            Value = value ?? throw new ArgumentNullException(nameof(value)); // Value itself can be "null" if that's a valid level string.
        }

        // Implicit conversion from a tuple for conciseness in definition.
        public static implicit operator Level((OALevel oaLevel, string value) tuple) {
            return new Level(tuple.oaLevel, tuple.value);
        }

        public override string ToString() {
            return $"OA{OALevel.Level}:{Value}";
        }
    }

    /// <summary>
    /// Represents the set of defined levels for a single <see cref="Factor"/>.
    /// This dictionary maps an <see cref="OALevel"/> (the symbol in the OA matrix, e.g., 1, 2)
    /// to the corresponding <see cref="Level"/> object, which contains the actual string value.
    /// </summary>
    public class ParameterLevelSet : Dictionary<OALevel, Level> {
        public ParameterLevelSet() : base() { }

        public ParameterLevelSet(IEnumerable<Level> levels) {
            ArgumentNullException.ThrowIfNull(levels);
            foreach (Level level in levels) {
                if (level == null) {
                    throw new ArgumentException("Collection cannot contain null Level objects.", nameof(levels));
                }
                if (ContainsKey(level.OALevel)) {
                    // More descriptive error, showing the problematic OALevel and its associated value.
                    throw new ArgumentException($"Duplicate OALevel {level.OALevel} (mapping to value '{level.Value}') detected. Each OALevel within a set must be unique.", nameof(levels));
                }
                this[level.OALevel] = level;
            }
        }

        /// <summary>
        /// Adds a new level to the set.
        /// </summary>
        /// <param name="oaLevel">The orthogonal array symbolic level (e.g., OALevel.One).</param>
        /// <param name="value">The string value for this level.</param>
        public void Add(OALevel oaLevel, string value) {
            Level newLevel = new(oaLevel, value);
            if (ContainsKey(newLevel.OALevel)) {
                throw new ArgumentException($"Duplicate OALevel {newLevel.OALevel} (attempting to add value '{newLevel.Value}'). An entry for this OALevel already exists with value '{this[newLevel.OALevel].Value}'.", nameof(oaLevel));
            }
            this[newLevel.OALevel] = newLevel;
        }
    }

    // This class was previously in BenchmarkConfiguration.cs, moved here as it's core to OA design.
    /// <summary>
    /// Represents an interaction between two parameters (factors) that should be considered in the analysis.
    /// The names correspond to the <see cref="Factor.Name"/>.
    /// </summary>
    public record ParameterInteraction {
        /// <summary>
        /// Gets the name of the first parameter (factor) in the interaction.
        /// </summary>
        public string FirstParameterName { get; }

        /// <summary>
        /// Gets the name of the second parameter (factor) in the interaction.
        /// </summary>
        public string SecondParameterName { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ParameterInteraction"/> class.
        /// Ensures that parameter names are not null or whitespace and are not identical (case-insensitive).
        /// </summary>
        /// <param name="firstParameterName">The name of the first parameter.</param>
        /// <param name="secondParameterName">The name of the second parameter.</param>
        public ParameterInteraction(string firstParameterName, string secondParameterName) {
            if (string.IsNullOrWhiteSpace(firstParameterName)) {
                throw new ArgumentException("First parameter name cannot be null or whitespace.", nameof(firstParameterName));
            }
            if (string.IsNullOrWhiteSpace(secondParameterName)) {
                throw new ArgumentException("Second parameter name cannot be null or whitespace.", nameof(secondParameterName));
            }
            if (string.Equals(firstParameterName, secondParameterName, StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException($"Interaction parameters must be different. Attempted to interact '{firstParameterName}' with itself.", nameof(secondParameterName));
            }

            // Canonical ordering for consistent hashing and comparison if needed, though not strictly required by record equality.
            if (string.Compare(firstParameterName, secondParameterName, StringComparison.Ordinal) > 0) {
                (firstParameterName, secondParameterName) = (secondParameterName, firstParameterName);
            }

            FirstParameterName = firstParameterName;
            SecondParameterName = secondParameterName;
        }
    }
}