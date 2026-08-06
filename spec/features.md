# Features

## Implementation Status

- ✅ **Implemented in CLI** — available in `CsCallGraph.Cli`
- ⏳ **Planned for CLI** — spec defined, not yet built
- ❌ **Not started** — future phase

## Phase 1 — Core Call Hierarchy (MVP)

### 1.1 CLI Commands → ✅ Implemented

- `callers --symbol` — "Who calls this?"
- `callees --symbol` — "Who does this call?"
- `list-symbols` — enumerate all callable symbols

### 1.2 Right-Click → "Show Call Hierarchy" → ✅ Done (VS Code extension wired to LSP server)

### 1.3 Tree View Output → ✅ Implemented (CLI)

- CLI tree output with box-drawing characters (`├─`, `└─`, `│`)
- Symbol name with kind icon (`[M]`, `[C]`, `[P]`, `[F]`, `[L]`)
- Call-site count per node
- File path + line number + column
- Depth-limited lazy expansion (`--depth`)

### 1.4 Supported Symbols (Callers & Callees)

| Symbol Kind | Caller Support | Callee Support |
|---|---|---|
| Static method | ✅ | ✅ |
| Instance method | ✅ | ✅ |
| Constructor | ✅ | ✅ |
| Property getter | ✅ | ✅ |
| Property setter | ✅ | ✅ |
| Indexer | ⏳ Not tested | ✅ |
| Operator overload | ⏳ Not tested | ⏳ Not tested |
| Local function | ✅ | ✅ |
| Lambda / delegate | ✅ | ✅ (callee for invocations) |
| Async methods | ✅ | ✅ |

### 1.5 Clipboard Copy → ❌ Not started (VS Code extension)

### 1.6 Transitive Call Paths → ✅ Implemented

Depth-limited transitive closure via `--depth` option. Default depth is 10, `--depth 0` for unlimited.

## Phase 2 — Symbols & References (High Priority)

### 2.1 Field / Variable / Constant References

- Right-click on a field, local variable, constant, or static field
- "Show References" option
- Tree view shows all locations where the symbol is read or written
- Nodes grouped by: reads vs writes (icon distinction)
- For fields: includes assignments, compound assignments, `out` / `ref` passthrough

### 2.2 Enum and Enum Member References

- Show all `switch` cases, comparisons, and conversions referencing the enum or its member

## Phase 3 — Class & Type Hierarchy (Medium Priority)

### 3.1 Type Hierarchy

- Show derived types (inheritance tree)
- Show base types
- Members that override / implement interface members

### 3.2 Interface Implementation

- For an interface method: show all implementors
- For a class method: show which interface member it implements (if any)

## Phase 4 — Advanced (Lower Priority)

### 4.1 Search Box

- Quick search bar at top of tree panel
- Fuzzy-search by symbol name across the solution
- Select a result → immediately show its call hierarchy

### 4.2 File Export (Secondary)

- Export the current tree as JSON or YAML for external use
- Lower priority than clipboard copy since clipboard covers most ad-hoc sharing needs

### 4.3 Cross-Reference Panel

- Side-by-side view: callers on left, callees on right

### 4.4 Asynchronous / Delegate Call Tracking

- Track method group conversions, event subscriptions, and `Invoke` patterns
- Best-effort: Roslyn cannot resolve all delegate targets statically

## Known Gaps & Deferred Issues (from PR #3 CodeRabbit Review)

Items acknowledged as valid but deferred for later resolution:

| # | Area | Issue | Severity | Status |
|---|------|-------|----------|--------|
| 1 | **LspModels.cs** | `JsonRpcId` serializes as object instead of JSON primitive; server cannot deserialize numeric `id` fields from client | Critical | **Fixed** in PR #14 (`JsonRpcIdConverter`) — numeric/string ids deserialize and echo correctly |
| 2 | **LSP extension.ts** | `Content-Length` computed via UTF-16 `string.length` instead of UTF-8 byte count; breaks on non-ASCII messages | Major | Deferred |
| 3 | **CalleesQuery.cs** | Constructor initializer calls (`: this(...)`, `: base(...)`) not recorded in callee graph | Major | Deferred |
| 4 | **VS Code settings** | `csCallGraph.maxDepth` and `csCallGraph.searchScope` declared in `package.json` but not wired to LSP server | Major | Deferred |
| 5 | **TESTING.md** | LSP frame examples have incorrect `Content-Length` byte counts | Major | **Fixed** in PR #16 — frames moved to a `Send-Lsp` helper that computes `Content-Length` from UTF-8 byte count |
| 6 | **TESTING.md** | Troubleshooting refers to `pnpm compile` instead of `npm run compile` | Minor | **Fixed** in PR #16 |
| 7 | **LspModels.cs** | Advertises `TextDocumentSync.Full` (1) but server does not consume `didOpen`/`didChange` events; should be `None` (0) | Minor | Deferred |
| 8 | **CallGraphEngine.cs** | Cache `Lazy<Task<...>>` factory captures caller's `CancellationToken`; on cancellation/retry, entry not evicted | Minor | Deferred |
| 9 | **CallHierarchyHandler.cs** | `ToLspItem`/`ToIncomingCall`/`ToOutgoingCall` range/selectionRange correctness for declaration vs call-site positions | Minor | **Fixed** in PR #21 — items use declaration file + full-span `range` + identifier `selectionRange`; call-site positions only in `FromRanges` |
