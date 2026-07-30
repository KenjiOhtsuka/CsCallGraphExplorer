# CLI Interface

> **Implementation status:** ✅ All core commands and options are implemented. Extended options (`--verbose`) are planned.

## Command Structure

```
cs-call-graph [command] [options]
```

## Global Options

| Option | Description | Status |
|---|---|---|
| `--solution <path>` | Path to `.sln` file (required) | ✅ Implemented |
| `--symbol <name>` | Symbol name to analyze (e.g., `MyClass.MyMethod`) | ✅ Implemented |
| `--symbol-at <file:line:col>` | Alternative: resolve symbol from source location | ✅ Implemented |
| `--output <format>` | Output format: `tree` (default), `json` | ✅ Implemented (`yaml` ❌) |
| `--depth <n>` | Max depth (default: 10, 0 = unlimited) | ✅ Implemented |
| `--scope <scope>` | `solution` (default), `project`, `project-with-dependencies` | ✅ Implemented |
| `--verbose` | Print diagnostic/progress information to stderr | ❌ Not started |

## Commands

### `callers` — Show incoming calls (who calls this symbol) ✅

```
cs-call-graph callers --solution Foo.sln --symbol "Foo.Bar.MethodA"
```

Output (default `tree`):

```
Callers of InstanceMethod
├─ [M] CallInstanceMethod  —  1 call site(s)
│    at samples\SampleConsoleApp\Callers.cs:33,9
│  └─ [M] RunAll  —  1 call site(s)
│       at samples\SampleConsoleApp\Callers.cs:14,9
│     └─ [M] <top-level-statements-entry-point> (static)  —  1 call site(s)
│          at samples\SampleConsoleApp\Program.cs:5,6
└─ ...
```

### `callees` — Show outgoing calls (who this symbol calls) ✅

```
cs-call-graph callees --solution Foo.sln --symbol "Foo.Bar.MethodA"
```

Output:

```
Callees of RunAll
├─ [M] CallInstanceMethod  —  1 call site(s)
│    at samples\SampleConsoleApp\Callers.cs:14,9
│  └─ [M] InstanceMethod  —  1 call site(s)
│       at samples\SampleConsoleApp\Callers.cs:33,9
│     ├─ [M] StaticMethod (static)  —  1 call site(s)
│     │    at samples\SampleLibrary\PublicMethods.cs:7,9
│     └─ [M] PrivateMethod  —  1 call site(s)
│          at samples\SampleLibrary\PublicMethods.cs:8,9
├─ ...
```

### `list-symbols` — List all callable symbols in the solution ✅

```
cs-call-graph list-symbols --solution Foo.sln
```

Output (one symbol per line):

```
SampleLibrary.PublicMethods.InstanceMethod
SampleLibrary.PublicMethods.StaticMethod
SampleLibrary.Overloads.Compute
SampleLibrary.GenericClass<TKey, TValue>.Add
...
```

Useful for piping into other tools or discovering symbol names.

## JSON Output Format ✅

When `--output json` is specified, the tree is serialized to JSON. See `CallGraphResult`, `CallGraphNode`, `CallSite` in `Models/CallGraphModels.cs`.

```json
{
  "Target": {
    "Name": "RunAll",
    "FullyQualifiedName": "SampleConsoleApp.Callers.RunAll()",
    "ContainingType": "SampleConsoleApp.Callers",
    "Kind": "Method",
    "IsStatic": false,
    "DisplayString": "RunAll"
  },
  "Direction": "Callees",
  "Roots": [
    {
      "Symbol": "CallInstanceMethod",
      "DisplayString": "CallInstanceMethod",
      "ContainingType": "SampleConsoleApp.Callers",
      "Kind": "Method",
      "CallCount": 1,
      "CallSites": [
        {
          "File": "samples\\SampleConsoleApp\\Callers.cs",
          "Line": 14,
          "Column": 9
        }
      ],
      "Children": [ ... ]
    }
  ]
}
```

## Symbol Name Resolution (`--symbol`) ✅

The `--symbol` option accepts a fully-qualified symbol name.

### Resolution Rules (MVP)

1. **Exact match only** — the name must match a symbol's full metadata name (namespace + type + member) exactly.
2. **Overloads** — if the name matches multiple overloads, the CLI returns an `AMBIGUOUS_SYMBOL` error (⚠️ currently returns `SYMBOL_NOT_FOUND` — ambiguous resolution is a known gap). The user must disambiguate by including the parameter list in parentheses.
3. **Parameter list syntax** — to disambiguate overloads, append the parameter types:
   - `Foo.Bar.Method(int, string)`
   - `Foo.Bar.Method()`
   - `Foo.Bar.Method(int)`
4. **Generic types** — for generic types/methods, the name without type parameters matches the unbound generic (any arity). To disambiguate by arity, append `<...>` with comma-separated type parameter names or just commas:
   - `GenericClass` — matches `GenericClass<T>` if unique; ambiguous if both `GenericClass<T>` and `GenericClass<T,U>` exist
   - `GenericClass<TKey, TValue>` or `GenericClass<,>` — matches only arity 2
   - `GenericMethods.Swap<T>` or `GenericMethods.Swap<>` — matches the generic method by arity
5. **Nested types** — use `.` as separator between outer and inner type: `Outer.Inner.Method`.
6. **No fuzzy matching** in MVP. Fuzzy / partial name search is a post-MVP enhancement.

### Examples

| Input | Matches | Behavior | Status |
|---|---|---|---|
| `Foo.Bar.Compute` | Exact match | Proceed | ✅ Working |
| `Foo.Bar.Compute` | Multiple overloads | Return `AMBIGUOUS_SYMBOL` with candidates | ✅ Returns `AMBIGUOUS_SYMBOL` |
| `Foo.Bar.Compute(int, string)` | Single overload | Proceed | ✅ Working |
| `Foo.Bar.Compute(int)` | No match | Return `SYMBOL_NOT_FOUND` | ✅ Working |
| `Bar.Compute` | Partial name | Return `SYMBOL_NOT_FOUND` (fuzzy not supported) | ✅ Working |
| `SampleLibrary.GenericClass` | Unique generic class | Proceed (matches unbound) | ✅ Working |
| `SampleLibrary.GenericClass<,>` | Arity-2 generic class | Proceed | ✅ Working |
| `SampleLibrary.GenericMethods.Swap<>` | Unique generic method | Proceed | ✅ Working |

## Output Channels

| Stream | Purpose |
|---|---|
| **stdout** | Structured result data (JSON tree, plain-text tree, symbol list) |
| **stderr** | Errors, warnings, progress, diagnostics |

Errors always go to stderr, never to stdout. This allows piping JSON output to other tools without contamination.

## Error Format (Structured JSON)

All errors are serialized as JSON to stderr, even when `--output tree` is used. This ensures a machine-parseable error format regardless of display mode.

```json
{
  "error": {
    "code": "SYMBOL_NOT_FOUND",
    "message": "Symbol 'Foo.Bar.Nonexistent' not found in solution",
    "details": {
      "symbol": "Foo.Bar.Nonexistent",
      "solution": "src/Foo.sln",
      "suggestions": ["Foo.Bar.MethodA", "Foo.Bar.MethodB", "Foo.Bar.PropertyX"]
    }
  }
}
```

### Error Codes — Implementation Status

| Code | HTTP-equiv | Meaning | Status |
|---|---|---|---|
| `SOLUTION_NOT_FOUND` | 404 | Solution file does not exist | ✅ Implemented |
| `SOLUTION_LOAD_FAILED` | 500 | Roslyn failed to load the solution | ✅ Implemented |
| `SYMBOL_NOT_FOUND` | 404 | Symbol not found | ✅ Implemented |
| `AMBIGUOUS_SYMBOL` | 409 | Multiple matches | ✅ Implemented |
| `INVALID_ARGUMENT` | 400 | Missing/incorrect args | ✅ Implemented (via manual arg parsing) |
| `INTERNAL_ERROR` | 500 | Unhandled exception | ✅ Implemented |

The `details` field is flexible — different error codes may include situational fields (e.g., `suggestions`, `candidates`, `path`).

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Recoverable error (symbol not found, ambiguous) |
| 2 | Environment error (solution load failure) |
| 3 | Usage error (invalid arguments) |
