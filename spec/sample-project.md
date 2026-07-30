# Sample Project

> **Status: ✅ All files created and building.** Verified against `callers`, `callees`, and `list-symbols` commands.

A deliberately structured C# solution used as a reference target for integration tests and manual CLI/extension validation.

## Solution Structure

The sample lives in `samples/` with its own solution file, completely independent from the tool's solution.

```
samples/
├── SampleProject.sln                                      ✅
├── SampleConsoleApp/
│   ├── SampleConsoleApp.csproj                            ✅
│   ├── Program.cs                      # Entry point      ✅
│   ├── Callers.cs                      # Exercising calls  ✅
│   └── OtherCalls.cs                   # Additional callers ✅
├── SampleLibrary/
│   ├── SampleLibrary.csproj                                ✅
│   ├── PublicMethods.cs                                    ✅
│   ├── FieldsAndProperties.cs                              ✅
│   ├── CtorsAndStatics.cs                                  ✅
│   ├── NestedAndInner.cs                                   ✅
│   ├── StaticClass.cs                                      ✅
│   ├── Overloads.cs                                        ✅
│   ├── Inheritance.cs                                      ✅
│   ├── Generics.cs                                         ✅
│   ├── LambdasAndDelegates.cs                              ✅
│   ├── AsyncStuff.cs                                       ✅
│   └── Internals.cs                                        ✅
```

## Project Dependencies

```
SampleConsoleApp ──> SampleLibrary  (project reference)
```

Both projects are in the same solution (`SampleProject.sln`). This lets integration tests verify cross-project call resolution.

## C# Elements Covered

### Methods (all in SampleLibrary, called from SampleConsoleApp)

| File | Elements | Modifiers |
|---|---|---|
| `PublicMethods.cs` | Instance method, static method, method with ref/out, method returning void | `public`, `private`, `internal`, `protected` |
| `CtorsAndStatics.cs` | Instance constructor, static constructor, constructor chaining (`: this()`, `: base()`) | `public`, `static` |
| `Overloads.cs` | Overloaded methods by parameter count, type, and ref-kind | `public` |
| `Inheritance.cs` | Virtual method, override, interface method implementation, `base.Method()` call | `public`, `virtual`, `override` |
| `Generics.cs` | Generic method `<T>`, generic class `<TKey, TValue>`, constraints | `public` |
| `LambdasAndDelegates.cs` | Lambda assigned to `Func<>`, delegate declaration + invocation, local function, anonymous method | — |
| `AsyncStuff.cs` | `async Task`, `async Task<T>`, `async void` (event handler), `ValueTask` | `public`, `async` |
| `Internals.cs` | Internal method called from same assembly, private method called within class | `internal`, `private` |

### Fields & Properties

| File | Elements |
|---|---|
| `FieldsAndProperties.cs` | Static field, instance field, `readonly` field, `const`, auto-property, property with get/set body, expression-bodied property, static property, init-only setter |
| `CtorsAndStatics.cs` | Static field initialized in static ctor |

### Types

| File | Elements |
|---|---|
| `NestedAndInner.cs` | Class with inner (nested) class, inner class calling outer class members, inner static class |
| `StaticClass.cs` | `static class` with static methods and fields |
| `Inheritance.cs` | Base class, derived class, interface, struct implementing interface |

### Variables & Constants

- Local variables (in methods across multiple files)
- `const` fields
- `static readonly` fields
- Captured variables in lambdas

## Verification Matrix

Each element in the sample must produce a predictable call graph that tests verify against:

| Source | Expected Callers | Expected Callees | Verified |
|---|---|---|---|
| `SampleLibrary.PublicMethods.InstanceMethod` | `CallInstanceMethod`, `CallPrivate` | `PrivateMethod` | ✅ |
| `SampleLibrary.PublicMethods.StaticMethod` | `InstanceMethod`, `CallStaticMethod`, `ExtraCaller` | — | ✅ |
| `SampleLibrary.CtorsAndStatics..ctor` | `new` in `CallConstructors` | `: this()` chain | ✅ |
| `SampleLibrary.LambdasAndDelegates.LocalFunctionExample` | `CallDelegates` | `Multiply` (local function) | ✅ |
| `SampleLibrary.Inheritance.DerivedClass.Greet` | `CallInheritance`, `Greet` (base call) | `Greet` (base) | ✅ |
| `SampleConsoleApp.Callers.RunAll` | entry point | all category methods | ✅ |

## Usage in Tests

```csharp
// Core tests — reference the solution path as a test fixture
var solutionPath = Path.Combine(TestData.Root, "samples", "SampleProject.sln");
var engine = new CallGraphEngine();
var result = await engine.GetCallersAsync(solutionPath, "SampleLibrary.PublicMethods.InstanceMethod");

// CLI tests — launch process
var (exitCode, stdout, stderr) = ProcessRunner.Run(
    "dotnet", $"run --project src/CsCallGraph.Cli -- callers --solution \"{solutionPath}\" --symbol \"SampleLibrary.PublicMethods.InstanceMethod\" --output json");
```
