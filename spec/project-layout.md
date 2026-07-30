# Project Layout

## Repository Structure

```
CsCallGraphExplorer/
├── src/
│   ├── CsCallGraph.Core/               # Shared analysis engine  ✅ Done
│   │   ├── CallGraphEngine.cs          # Public API              ✅
│   │   ├── CallGraphIndex.cs           # Precomputed index       ❌ Not started
│   │   ├── CallersQuery.cs             # Find callers logic      ✅
│   │   ├── CalleesQuery.cs             # Find callees logic      ✅
│   │   ├── SymbolResolver.cs           # Resolve symbol by name  ✅
│   │   ├── SyntaxWalker.cs             # Walk syntax tree        ❌ Not started (logic inlined in CalleesQuery)
│   │   └── Models/
│   │       ├── CallGraphModels.cs      # All model types         ✅
│   ├── CsCallGraph.Cli/                # CLI tool                ✅ Done
│   │   ├── Program.cs                  # Manual arg parsing      ✅
│   │   └── Output/
│   │       ├── TreeFormatter.cs        # Plain-text tree output  ✅
│   │       └── JsonFormatter.cs        # JSON output             ✅
│   ├── CsCallGraph.LanguageServer/     # LSP server              ✅ Done
├── extensions/
│   └── vscode/                         # VS Code extension       ✅ Done
├── tests/
│   ├── CsCallGraph.Core.Tests/         # xUnit tests             ✅ 44 tests
│   └── CsCallGraph.Cli.Tests/          # CLI process tests       ❌ Not started
├── samples/
│   ├── SampleProject.sln               # Standalone solution     ✅
│   ├── SampleConsoleApp/               # Console app callers     ✅
│   └── SampleLibrary/                  # Library with patterns   ✅
├── spec/                               # Specification docs      ✅ Updated
├── CsCallGraphExplorer.sln             # Tool's own solution     ✅
└── README.md                           # Usage docs              ✅
```
```

## Component Dependency

```
CsCallGraph.Cli ──────────┐
                          ├──> CsCallGraph.Core ──> Microsoft.CodeAnalysis.Workspaces
CsCallGraph.LanguageServer ┘
                                      samples/
├── CsCallGraph.Core.Tests ────────┘    └── SampleConsoleApp (fixture)
└── CLI tests (process invocations) ──── reference via file path
```

## Solutions

Two separate solutions, never mixed:

### Tool Solution: `CsCallGraphExplorer.sln`

Contains all tool components and their tests:
- `src/CsCallGraph.Core/CsCallGraph.Core.csproj`
- `src/CsCallGraph.Cli/CsCallGraph.Cli.csproj`
- `src/CsCallGraph.LanguageServer/CsCallGraph.LanguageServer.csproj` (added later)
- `tests/CsCallGraph.Core.Tests/CsCallGraph.Core.Tests.csproj`

### Sample Solution: `samples/SampleProject.sln`

Contains the sample projects that serve as analysis targets:
- `samples/SampleConsoleApp/SampleConsoleApp.csproj`
- `samples/SampleLibrary/SampleLibrary.csproj`

This solution is never referenced by the tool's solution. Integration tests load it by file-system path only.

## Dependency Minimization

Keep external dependencies to an absolute minimum. Only pull in packages that are strictly necessary.

**Allowed:**
- `Microsoft.CodeAnalysis.*` — essential; Roslyn is the core analysis engine
- `xunit` / `Microsoft.NET.Test.Sdk` — testing only

**Avoid:**
- CLI argument parsing libraries (e.g., `System.CommandLine`) — use simple manual `string[] args` parsing. The CLI has few commands and flags; a 50-line manual parser is better than an external dependency.
- JSON serialization libraries — `System.Text.Json` is built into .NET 10 and requires no NuGet package.
- Logging frameworks — `Console.Error.WriteLine` is sufficient for the MVP.

## .NET Target

All .NET projects target **net10.0**.

NuGet dependencies:
| Package | Purpose |
|---|---|
| `Microsoft.CodeAnalysis.Workspaces.Common` | Solution/project loading, `Solution`, `Project`, `Document` |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | C# syntax/semantic model, `SymbolFinder` |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | Load `.sln`/`.csproj` files |

## Testing

### Core Tests (xUnit)

`CsCallGraph.Core.Tests` — unit tests for the analysis engine:
- Mock Roslyn workspaces or use the sample project as a fixture
- Verify callers/callees resolution, symbol matching, edge cases
- Quick, focused, no process spawning

### CLI Tests (Process Invocation)

CLI-specific behavior (argument parsing, exit codes, output formatting, error serialization) is tested by launching `cs-call-graph` as a child process against the sample project.

- Tests live in a separate directory (could be `tests/CsCallGraph.Cli.Tests/`)
- Use `Process.Start` / `dotnet run --project src/CsCallGraph.Cli` pointing at `samples/SampleConsoleApp/SampleConsoleApp.csproj`
- Verify stdout, stderr, exit codes, and JSON output structure
- These must NOT go through `CsCallGraph.Core` directly — the goal is to test the CLI layer end-to-end

### Sample Project

See [sample-project.md](sample-project.md) for full details.

The sample is a two-project solution (`SampleConsoleApp` → `SampleLibrary`) covering methods (static/instance/ctor/virtual/override/async/generic), fields (static/instance/readonly/const), properties, inner classes, static classes, lambdas, local functions, and all access modifiers.

## NuGet Packaging

`CsCallGraph.Core` is published as a NuGet package so that:
- It can be consumed independently by other tools (CI scripts, custom analyzers)
- The CLI and Language Server reference it via a project reference during development but could consume the published package in production

The CLI tool itself is NOT packaged as NuGet — it is distributed as a `dotnet tool` (`cs-call-graph` global tool) or as a self-contained executable.

Package ID: `CsCallGraph.Core`  
Versioning: [SemVer 2.0](https://semver.org/)
