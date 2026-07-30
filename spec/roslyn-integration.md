# Roslyn Integration

> **Implementation status:** Core symbol resolution, caller query (`SymbolFinder.FindCallersAsync`), and callee query (syntax tree walk) are **implemented** in `CsCallGraph.Core`. Compilation caching, parallel project iteration, and project-level scoping are **implemented**. Precomputed inverted index and LRU cache with TTL are **not yet implemented**.

## Symbol Resolution

### From source location (LSP path) → ✅ Implemented for CLI (`--symbol-at`)

For the CLI `--symbol-at` option, given a file path + line + column, resolve the symbol at that position via:

```csharp
var symbol = await document.GetSymbolAtPositionAsync(position, cancellationToken);
```

Returns `ISymbol` — the unified Roslyn abstraction for all declared symbols.

### Named Symbols (by fully-qualified name) → ✅ Implemented in `SymbolResolver.cs`

The CLI's `--symbol` option resolves by name. Internally, the engine iterates all projects in the solution and matches by:

1. **Type name**: `INamedTypeSymbol` with `ToDisplayString()` matching the namespace-qualified name. For generic types, arity (number of type parameters) is part of the identity.
2. **Member name**: Within a matched type, members are compared by `Name`. Overloads are disambiguated by parameter types.
3. **Generic matching**: A generic type/method's uninstantiated form is the `ISymbol` used for analysis. `FindCallersAsync` automatically resolves all constructed instantiations (e.g., `List<int>`, `List<string>`) back to the `List<T>` definition.

```csharp
// Finding a generic type by name
var types = solution.GetAllTypes()
    .Where(t => t.Name == "GenericClass" && t.Arity == 2);
```

## Finding Callers (Incoming Calls) → ✅ Implemented in `CallersQuery.cs`

Central API: `SymbolFinder.FindCallersAsync`

```csharp
var callers = await SymbolFinder.FindCallersAsync(
    symbol, solution, cancellationToken);
```

Returns `IEnumerable<CallerInfo>` where each entry contains:
- `CallingSymbol`: the `ISymbol` that makes the call
- `Locations`: the set of `DocumentSpan` where the call occurs
- `IsDirect`: whether this is a direct call

### Implementation details
- Results are filtered to source-only symbols (`Locations.Any(l => l.IsInSource)`)
- Grouped by `CallingSymbol` using `SymbolEqualityComparer.Default` to handle duplicates
- Recursive depth-limited traversal for transitive callers
- Tree node includes call-site count and file:line locations

## Finding Callees (Outgoing Calls) → ✅ Implemented in `CalleesQuery.cs`

No single Roslyn API returns all callees of a method. We must walk the syntax tree:

```csharp
var tree = await document.GetSyntaxTreeAsync(cancellationToken);
var root = await tree.GetRootAsync(cancellationToken);
var invocations = root.DescendantNodes()
    .OfType<InvocationExpressionSyntax>();
```

For each `InvocationExpressionSyntax`:
1. Get the `SemanticModel` for the document
2. Call `semanticModel.GetSymbolInfo(expression)` → `SymbolInfo.Symbol`
3. Resolve the target symbol via `SymbolInfo.Symbol` (or `CandidateSymbols` if ambiguous)

### Implementation details
- Walks `BaseMethodDeclarationSyntax` for the target method
- Detects `InvocationExpressionSyntax` (method calls) and `ObjectCreationExpressionSyntax` (constructor calls)
- Filters callees to source-only symbols
- Recursive depth-limited traversal

### Members Beyond Method Calls — ⏳ Partial

Currently detects:
- ✅ **Object creation**: `ObjectCreationExpressionSyntax` → constructor symbol
- ❌ **Constructor initializer**: `: this(...)` and `: base(...)` chaining (`ConstructorInitializerSyntax`) not yet recorded in the callee graph
- ✅ **Property access**: `MemberAccessExpressionSyntax` where the target is a property → property symbol
- ✅ **Indexer access**: `ElementAccessExpressionSyntax` → indexer symbol
- ❌ **Operator invocations**: `BinaryExpressionSyntax` / `PrefixUnaryExpressionSyntax` with custom operator overloads
- ❌ **Delegate invocations**: `InvocationExpressionSyntax` on a delegate-typed expression (only direct `Invoke()` calls resolved)
- ✅ **Local function calls**: simple `IdentifierNameSyntax` inside the containing method

## Performance — Achieving Rapid Response — ⏳ Partial

### Challenge

`SymbolFinder.FindCallersAsync` iterates the entire solution. For large solutions (>100 projects), this can take seconds.

### Strategies

#### 1. Precomputed Index (Recommended) — ❌ Not started

Build an inverted index on solution load:

| Index | Key | Value |
|---|---|---|
| `IncomingIndex` | Target Symbol ID | List of (Caller Symbol ID, Location) |
| `OutgoingIndex` | Source Symbol ID | List of (Callee Symbol ID, Location) |

Populate once when workspace is opened. Incrementally update on file save.

**Memory estimate**: For a solution with 500K symbols and 2M call edges, roughly 200-400 MB for the index. Acceptable for typical enterprise solutions.

#### 2. Compilation/Solution Cache — ✅ Implemented (with known gap)

The engine caches:
- `MSBuildWorkspace` instance per solution path (avoids re-parsing `.sln`/`.csproj` files)
- `Compilation` per project (avoids re-compiling)
- `Solution` snapshot per path

**Known gap**: `Lazy<Task<...>>` cache factory captures the caller's `CancellationToken`. If that token is cancelled, the cache entry is not evicted, so a subsequent caller might await a cancelled task. Fix: use a non-cancelable factory internally, then apply the caller's token only when awaiting.

#### 3. Project-Level Scoping — ✅ Implemented

`--scope project` limits analysis to the target's containing project. `--scope project-with-dependencies` includes transitive dependencies.

#### 4. Parallel Project Iteration — ✅ Implemented

`ResolveTargetSymbolAsync` iterates projects in parallel via `Parallel.ForEachAsync` with early-exit cancellation when the symbol is found.

#### 5. Cancellation — ✅ Implemented

`CancellationToken` is threaded through all async methods. The CLI does not currently expose a cancellation mechanism (Ctrl+C is handled by the runtime).

#### 6. Background Population — ❌ Not started

## Incremental Updates — ❌ Not started

## Handling Dynamic Resolution Limitations — ⏳ Partial

Not all calls can be statically resolved. Current implementation follows Roslyn's resolution without special fallbacks:

| Pattern | Roslyn Resolvability | Status |
|---|---|---|
| Direct method call | ✅ Resolved | ✅ Working |
| Virtual / override | ✅ Resolved to declaration | ✅ Working |
| Interface method | ✅ CandidateSymbols | ✅ Working |
| Delegate / event | Partial | ⏳ Basic support |
| `dynamic` keyword | ❌ Not resolved | ⏳ Skipped gracefully |
| Reflection (`MethodInfo.Invoke`) | ❌ Not resolved | ⏳ Skipped gracefully |
| Lambda assigned to delegate | ✅ If variable type is known | ✅ Working |
| `nameof()` | N/A | ✅ Not a call; skipped |

## Static vs. Instance Methods — ✅ Implemented

Both are handled identically by Roslyn's symbol model:
- `IMethodSymbol.IsStatic` distinguishes them
- Tree node icons differ (`[M]` vs `[M] (static)`)
- Indexing logic has no special branching; both are indexed the same way
