using CsCallGraph.Core;
using Xunit;

namespace CsCallGraph.LanguageServer.Tests;

[CollectionDefinition("Handler")]
public class HandlerCollection : ICollectionFixture<HandlerFixture>
{
}

public sealed class HandlerFixture : IDisposable
{
    public string SolutionPath { get; }
    public CallHierarchyHandler Handler { get; }

    public HandlerFixture()
    {
        SolutionPath = ResolveSolutionPath();
        Handler = new CallHierarchyHandler(new CallGraphEngine(), SolutionPath);
    }

    public void Dispose()
    {
        Handler.Dispose();
    }

    private static string ResolveSolutionPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "samples", "SampleProject.sln");
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            $"SampleProject.sln not found. Searched from {AppContext.BaseDirectory}");
    }
}
