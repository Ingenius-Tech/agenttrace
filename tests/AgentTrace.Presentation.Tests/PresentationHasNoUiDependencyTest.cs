using AgentTrace.Presentation;

namespace AgentTrace.Presentation.Tests;

public sealed class PresentationHasNoUiDependencyTest
{
    [Fact]
    public void PresentationAssemblyDoesNotReferenceAvalonia()
    {
        var presentation = typeof(PresentationAssemblyMarker).Assembly;

        var avalonia = presentation.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(n => n.StartsWith("Avalonia", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(avalonia);
    }
}
