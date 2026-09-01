using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Tests;

public sealed class CoreArchitectureTests
{
    [Fact]
    public void CoreAssembly_DoesNotReferenceAvalonia()
    {
        var references = typeof(AppPaths).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        references.Must().BeEmpty();
    }
}
