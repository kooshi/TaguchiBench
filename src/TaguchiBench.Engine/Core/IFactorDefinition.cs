// TaguchiBench.Engine/Core/IFactorDefinition.cs

namespace TaguchiBench.Engine.Core {
    /// <summary>
    /// Defines the essential properties of a factor needed by the ResultAnalyzer.
    /// This allows the analyzer to be agnostic of the specific Factor class implementation.
    /// </summary>
    public interface IFactorDefinition {
        /// <summary>
        /// Gets the unique name of the factor.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the set of levels defined for this factor.
        /// The ParameterLevelSet maps OA symbols (1, 2, ...) to actual Level objects (which include string values).
        /// </summary>
        ParameterLevelSet Levels { get; }
    }
}