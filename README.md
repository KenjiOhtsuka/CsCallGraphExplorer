# CsCallGraphExplorer

Call-hierarchy exploration for C# using Roslyn analysis. Discover who calls a method or what a method calls, across projects in a solution.

## Requirements

- .NET 10 SDK

## Build & Test

```
dotnet build CsCallGraphExplorer.sln
dotnet test tests\CsCallGraph.Core.Tests
```

## Usage

```
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

Output is one fully-qualified symbol name per line:
```
SampleLibrary.PublicMethods.InstanceMethod
SampleLibrary.PublicMethods.StaticMethod
SampleLibrary.Overloads.Compute
SampleLibrary.GenericClass<TKey, TValue>.Add
```

### Find callers

```
dotnet run --project src\CsCallGraph.Cli -- callers --solution samples\SampleProject.sln --symbol "SampleLibrary.PublicMethods.StaticMethod"
```

```
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
   │    at samples\SampleConsoleApp\Callers.cs:33,18
   │  └─ [M] RunAll  —  1 call site(s)
   │       at samples\SampleConsoleApp\Callers.cs:14,9
   │     └─ [M] <top-level-statements-entry-point> (static)  —  1 call site(s)
   │          at samples\SampleConsoleApp\Program.cs:5,6
   ├─ [M] Execute  —  1 call site(s)
   │    at samples\SampleConsoleApp\OtherCalls.cs:11,17
   │  └─ [M] <top-level-statements-entry-point> (static)  —  1 call site(s)
   │       at samples\SampleConsoleApp\Program.cs:8,7
   └─ [M] ExtraCaller  —  1 call site(s)
        at samples\SampleConsoleApp\OtherCalls.cs:21,17
```

### Find callees

```
dotnet run --project src\CsCallGraph.Cli -- callees --solution samples\SampleProject.sln --symbol "SampleConsoleApp.Callers.RunAll"
```

### Resolve symbol from source location

```
dotnet run --project src\CsCallGraph.Cli -- callers --solution samples\SampleProject.sln --symbol-at samples\SampleConsoleApp\Callers.cs:38:23
```

Line and column are 1-based (matching editor display).

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

## Sample project

The `samples/` directory contains `SampleLibrary` (library with various C# constructs: inheritance, generics, overloads, async, delegates, properties, constructors, statics, nested types) and `SampleConsoleApp` (exercises each feature).

```
dotnet run --project src\CsCallGraph.Cli -- list-symbols --solution samples\SampleProject.sln
```

## Project structure

```
CsCallGraphExplorer.sln           — Tool solution (Core + CLI + tests)
src/
  CsCallGraph.Core/               — Analysis engine (Roslyn wrapping)
  CsCallGraph.Cli/                — CLI frontend
samples/
  SampleProject.sln               — Standalone sample solution
  SampleLibrary/                  — C# library with constructs
  SampleConsoleApp/               — Console app exercising the library
tests/
  CsCallGraph.Core.Tests/         — Unit tests (xUnit, 44 tests)
spec/                             — Design documents
```
