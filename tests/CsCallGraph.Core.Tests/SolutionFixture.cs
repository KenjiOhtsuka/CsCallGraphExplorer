using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;

namespace CsCallGraph.Core.Tests;

public sealed class SolutionFixture : IAsyncLifetime
{
    public string SolutionPath { get; }
    public Solution Solution { get; private set; } = null!;
    public List<Compilation> Compilations { get; private set; } = [];

    public SolutionFixture()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "samples", "SampleProject.sln");
            if (File.Exists(candidate))
            {
                SolutionPath = Path.GetFullPath(candidate);
                return;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            $"SampleProject.sln not found. Searched from {AppContext.BaseDirectory}");
    }

    public async Task InitializeAsync()
    {
        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                throw new InvalidOperationException($"Workspace failure: {e.Diagnostic.Message}");
        };
        Solution = await workspace.OpenSolutionAsync(SolutionPath);

        foreach (var project in Solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation != null)
                Compilations.Add(compilation);
        }
    }

    public Task DisposeAsync()
    {
        (Solution.Workspace as MSBuildWorkspace)?.Dispose();
        return Task.CompletedTask;
    }
}
