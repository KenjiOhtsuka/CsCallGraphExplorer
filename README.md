# CsCallGraphExplorer

> Call-hierarchy exploration for C#, powered by Roslyn. Discover **who calls a method** and **what a method calls** across every project in a solution.

![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET%2010-512BD4?logo=dotnet&logoColor=white)
![Language](https://img.shields.io/badge/language-C%23-239120)
![VS Code](https://img.shields.io/badge/VS%20Code-extension-007ACC?logo=visualstudiocode&logoColor=white)

Two ways to explore your call graph:

| | **CLI** (`CsCallGraph.Cli`) | **VS Code extension** (`cs-call-graph`) |
|---|---|---|
| Where | Terminal / scripts | Editor sidebar |
| Query | `callers` / `callees` / `list-symbols` by symbol name | Right-click any C# method |
| Output | Text tree or JSON | Interactive tree with click-through navigation |
| Best for | CI checks, batch analysis, scripting | Day-to-day code exploration |

## Features

- **Roslyn-powered** — full static analysis of your solution (MSBuild workspace), not regex.
- **Both directions** — callers, callees, or toggle between them.
- **Tree or JSON output** — human-readable or machine-consumable.
- **Depth limiting** — cap the tree (`--depth`, default 10; `0` = unlimited).
- **Scope filtering** — `solution`, `project`, or `project-with-dependencies`.
- **Extension niceties** — direction toggle, copy tree / JSON to clipboard, click call sites to jump to source, empty-state placeholders for bodyless symbols.

## Quick start (CLI)

```powershell
dotnet run --project src\CsCallGraph.Cli -- callers --solution samples\SampleProject.sln --symbol "SampleLibrary.PublicMethods.StaticMethod"
```

```text
Callers of StaticMethod
├─ [M] CallStaticMethod (static)  —  1 call site(s)
│    at samples\SampleConsoleApp\Callers.cs:38,23
│  └─ [M] RunAll  —  1 call site(s)
│       at samples\SampleConsoleApp\Callers.cs:15,9
│     └─ [M] <top-level-statements-entry-point> (static)  —  1 call site(s)
│          at samples\SampleConsoleApp\Program.cs:5,6
├─ [M] ExtraCaller  —  1 call site(s)
│    at samples\SampleConsoleApp\OtherCalls.cs:22,23
└─ [M] InstanceMethod  —  1 call site(s)
     at samples\SampleLibrary\PublicMethods.cs:7,9
   ├─ [M] CallInstanceMethod  —  1 call site(s)
   ...
```

## VS Code extension

The repo also ships a VS Code extension (`extensions/vscode`, package name `cs-call-graph`).

**Install**
- From the Marketplace (publisher `KenjiOtsuka`), or
- Build a `.vsix` locally: `cd extensions\vscode; npm install; npm run package` — then `code --install-extension cs-call-graph-<version>.vsix`.

**Use**
1. Open a folder that contains a `.sln` (auto-detected, or set `csCallGraph.solutionPath`).
2. Place the cursor on a method, right-click → **CsCallGraph ▸ Show Callers** (or Show Callees).
3. Explore the sidebar tree — toggle direction, copy the tree (tree/JSON), click a call site to jump to it.

**Requirements:** VS Code `^1.96.0` and the **.NET 10 runtime** (the analysis server ships inside the VSIX but is not self-contained).

Full extension docs, settings, and known limitations: [`extensions/vscode/README.md`](extensions/vscode/README.md).

## Requirements

| Component | Requires |
|---|---|
| CLI + tests | .NET 10 SDK |
| VS Code extension | VS Code `^1.96.0` + .NET 10 runtime |

## Build & test

```powershell
dotnet build CsCallGraphExplorer.sln
dotnet test CsCallGraphExplorer.sln
```

69 tests — 55 in `CsCallGraph.Core.Tests`, 14 in `CsCallGraph.LanguageServer.Tests`.

For the extension:

```powershell
cd extensions\vscode
npm install
npm run compile     # or npm run watch
npm run lint
```

## Usage (CLI reference)

```text
cs-call-graph <command> [options]

Commands:
  callers       Show who calls the specified symbol
  callees       Show what the specified symbol calls
  list-symbols  List all callable symbols in the solution

Global options:
  --solution <path>          Path to the solution file (required)
  --symbol <name>            Fully qualified symbol name
  --symbol-at <file:ln:col>  Resolve symbol from source location
  --output <format>          Output format: tree (default) or json
  --depth <n>                Max depth (default: 10, 0 = unlimited)
  --scope <scope>            solution (default), project, project-with-dependencies
  --help / -h / -?           Show this usage
```

> Run via `dotnet run --project src\CsCallGraph.Cli -- <args>`. The `--` separates `dotnet` options from program arguments.

### List symbols

```
dotnet run --project src\CsCallGraph.Cli -- list-symbols --solution samples\SampleProject.sln
```

One fully-qualified symbol name per line:

```text
SampleLibrary.PublicMethods.InstanceMethod
SampleLibrary.PublicMethods.StaticMethod
SampleLibrary.Overloads.Compute
SampleLibrary.GenericClass<TKey, TValue>.Add
```

### Resolve symbol from source location

Line and column are 1-based (matching editor display):

```
dotnet run --project src\CsCallGraph.Cli -- callers --solution samples\SampleProject.sln --symbol-at samples\SampleConsoleApp\Callers.cs:38:23
```

### JSON output

```
dotnet run --project src\CsCallGraph.Cli -- callers --solution samples\SampleProject.sln --symbol "SampleLibrary.PublicMethods.StaticMethod" --output json
```

```json
{
  "Target": {
    "Name": "StaticMethod",
    "FullyQualifiedName": "SampleLibrary.PublicMethods.StaticMethod(string)",
    "ContainingType": "SampleLibrary.PublicMethods",
    "ContainingNamespace": "SampleLibrary",
    "Kind": "Method",
    "IsStatic": true,
    "Arity": 0,
    "Parameters": [{ "Name": "input", "TypeName": "string", "IsRef": false, "IsOut": false }],
    "DeclarationLocations": [{ "File": "C:\\...\\PublicMethods.cs", "Line": 11, "Column": 24 }],
    "DisplayString": "StaticMethod",
    "Direction": "Callers"
  },
  "Roots": [
    {
      "Symbol": "CallStaticMethod",
      "DisplayString": "CallStaticMethod",
      "ContainingType": "SampleConsoleApp.Callers",
      "Kind": "Method",
      "IsStatic": true,
      "CallCount": 1,
      "CallSites": [{ "File": "C:\\...\\Callers.cs", "Line": 38, "Column": 23 }],
      "Children": []
    }
  ]
}
```

### Limit depth

```
dotnet run --project src\CsCallGraph.Cli -- callees --solution samples\SampleProject.sln --symbol "SampleConsoleApp.Callers.RunAll" --depth 3
```

### Scope filtering

```
dotnet run --project src\CsCallGraph.Cli -- callees --solution samples\SampleProject.sln --symbol "SampleLibrary.PublicMethods.InstanceMethod" --scope solution
dotnet run --project src\CsCallGraph.Cli -- callees --solution samples\SampleProject.sln --symbol "SampleLibrary.PublicMethods.InstanceMethod" --scope project
dotnet run --project src\CsCallGraph.Cli -- callees --solution samples\SampleProject.sln --symbol "SampleLibrary.PublicMethods.InstanceMethod" --scope project-with-dependencies
```

- `solution` (default) — full solution
- `project` — only the symbol's own project
- `project-with-dependencies` — symbol's project and its direct references

## Symbol name format

Use fully qualified names as shown by `list-symbols`. Exact match only — no fuzzy resolution.

| Input | Behavior |
|---|---|
| `Foo.Bar.Compute` | Single match → proceed |
| `Foo.Bar.Compute` | Multiple overloads → `AMBIGUOUS_SYMBOL` error |
| `Foo.Bar.Compute(int,string)` | Parameter list disambiguates |
| `GenericClass<TKey, TValue>.Add` | Generic type with arity-2 |
| `GenericClass<,>.Add` | Same, shorthand with commas |
| `GenericMethods.Swap<>` | Generic method by arity |

Constructors use `.ctor`:

```
SampleLibrary.CtorsAndStatics..ctor
SampleLibrary.CtorsAndStatics..ctor(string)
```

## Error format

Errors are written to **stderr** as structured JSON:

```json
{
  "error": {
    "code": "SYMBOL_NOT_FOUND",
    "message": "Symbol 'Does.Not.Exist' not found in solution",
    "details": { "symbol": "Does.Not.Exist" }
  }
}
```

| Code | Meaning | Exit code |
|---|---|---|
| `SOLUTION_NOT_FOUND` | Solution file not found | 2 |
| `SOLUTION_LOAD_FAILED` | Roslyn failed to load solution | 2 |
| `SYMBOL_NOT_FOUND` | Symbol not found in solution | 1 |
| `AMBIGUOUS_SYMBOL` | Multiple matching symbols | 1 |
| `INTERNAL_ERROR` | Unexpected error | 2 |
| Usage validation | Missing args, etc. | 3 |

## Project structure

```text
CsCallGraphExplorer.sln            Tool + extension workspace
src/
  CsCallGraph.Core/                Analysis engine (Roslyn wrapping)
  CsCallGraph.Cli/                 CLI frontend
  CsCallGraph.LanguageServer/      LSP server used by the VS Code extension
extensions/
  vscode/                          VS Code extension (cs-call-graph)
    src/extension.ts               Extension host, LSP client, sidebar tree
    server/                        Bundled analysis server (built artifact)
    resources/icon.svg             Activity-bar icon
    test/TESTING.md                Manual testing checklist
samples/
  SampleProject.sln                Standalone sample solution
  SampleLibrary/                   C# library with constructs
  SampleConsoleApp/                Console app exercising the library
tests/
  CsCallGraph.Core.Tests/          Unit tests (xUnit, 55 tests)
  CsCallGraph.LanguageServer.Tests/  LSP handler tests (xUnit, 14 tests)
spec/                              Design documents
```

## License

[MIT](LICENSE)
