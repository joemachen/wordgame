using Crossword.Core.Domain;

namespace Crossword.Tests.Architecture;

[Trait("Category", "Architecture")]
public class CoreDependencyTests
{
    [Fact]
    public void Core_ReferencesOnlyBaseClassLibrary()
    {
        var offenders = typeof(RunState).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => !(name == "System" || name.StartsWith("System.") || name == "netstandard" || name == "mscorlib"))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Crossword.Core must stay engine-agnostic, but references: {string.Join(", ", offenders)}");
    }
}
