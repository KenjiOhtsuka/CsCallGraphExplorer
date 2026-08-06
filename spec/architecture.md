# Architecture

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| `CsCallGraph.Core` | ✅ **Done** | `CallGraphEngine`, `CallersQuery`, `CalleesQuery`, `SymbolResolver`, models |
| `CsCallGraph.Cli` | ✅ **Done** | 3 commands, tree/json output, manual arg parsing, structured errors |
| `CsCallGraph.LanguageServer` | ✅ **Done** | LSP server with CallHierarchyProvider (stdin/stdout JSON-RPC) |
| VS Code Extension | ✅ **Done** | `extensions/vscode/` — LSP client, CallHierarchyProvider, output panel commands |

## High-Level Overview (Target Architecture)

```
┌─────────────────────────────────────────────────┐
│                  VS Code Host                    │
│  ┌───────────────────────────────────────────┐   │
│  │           Extension (TypeScript)           │   │  ← ✅ Done (LSP client)
│  │  ┌──────────┐  ┌─────────────────────┐   │   │
│  │  │ Commands │  │  CallHierarchyTree   │   │   │
│  │  │ (right-  │  │  DataProvider        │   │   │
│  │  │  click)  │  └─────────────────────┘   │   │
│  │  └──────────┘                             │   │
│  └──────────────────┬────────────────────────┘   │
│                     │ LSP / JSON-RPC              │
│  ┌──────────────────▼────────────────────────┐   │
│  │        Roslyn Language Server (C#)         │   │  ← ✅ Done
│  │  ┌──────────┐ ┌──────────────────────┐   │   │
│  │  │ Analysis │ │   Call Graph Engine   │   │   │  ← ✅ Core exists
│  │  │ Session  │ │   (workspace cache)   │   │   │
│  │  └──────────┘ └──────────────────────┘   │   │
│  │  ┌──────────────────────────────────────┐ │   │
│  │  │         Roslyn Workspace             │ │   │
│  │  │  (Microsoft.CodeAnalysis.Workspaces) │ │   │
│  │  └──────────────────────────────────────┘ │   │
│  └───────────────────────────────────────────┘   │
└─────────────────────────────────────────────────┘
```

The CLI, LSP server, and VS Code extension are implemented and wired, though with known interoperability gaps (see [Known LSP Implementation Gaps](#known-lsp-implementation-gaps)).

## Extension Layer (TypeScript) — ✅ Done

- **Activation**: `onLanguage:csharp` + `onCommand:csCallGraph.showCallers`
- **Commands**: Registers `csCallGraph.showCallers` and `csCallGraph.showCallees`
- **Call Hierarchy Provider**: Implements VS Code's native `CallHierarchyProvider` via LSP client
- **LSP Client**: Connects to the standalone Roslyn language server; sends `textDocument/prepareCallHierarchy`, `callHierarchy/incomingCalls`, and `callHierarchy/outgoingCalls`
- **Output Panel**: Fallback commands for tree view in output panel
- **Known gaps**: `maxDepth`/`searchScope` settings declared in `package.json` but not yet wired to server; LSP frame header uses UTF-16 string length instead of UTF-8 byte count (breaks on non-ASCII)

## Analysis Layer (C# / Roslyn) — ✅ Core complete

- **Language Server**: A .NET console application with manual JSON-RPC framing (Content-Length headers) → ✅ Implemented in `CsCallGraph.LanguageServer`
- **Workspace Manager**: Opens the solution or project; creates a `Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace` or `AdhocWorkspace` → ✅ Implemented in `CallGraphEngine.OpenSolutionAsync`
- **Call Graph Engine**: Core analysis logic (see [roslyn-integration.md](roslyn-integration.md)) → ✅ Implemented
- **Cache**: In-memory solution, workspace, and compilation caches → ✅ Implemented. LRU result cache keyed by `(documentUri, symbolId, direction)` with TTL → ❌ Not started (planned for large-solution support)

## Communication Protocol

Two options, listed by preference:

### Option A: Reuse C# Dev Kit's Roslyn Host (Recommended)

C# Dev Kit already manages a Roslyn workspace in-process. It exposes a Language Server. We can contribute a new LSP handler (`textDocument/callHierarchy`) to the existing C# Dev Kit language server.

**Pros**: No need to host a second process; shares workspace state (no re-parsing); uses existing project loading infrastructure.

**Cons**: Tight coupling to C# Dev Kit internals; requires coordination with Microsoft.

### Option B: Standalone Roslyn Language Server

We ship a small .NET executable that loads the solution independently.

**Pros**: Decoupled, works without C# Dev Kit.

**Cons**: Duplicate project loading; memory overhead; slower startup; must handle project configuration by itself.

**Recommendation**: Start with Option B for independence, but design the API surface to align with the LSP CallHierarchy specification so migration to Option A is straightforward.

## LSP Protocol Mapping

We follow the [LSP 3.16 Call Hierarchy](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#textDocument_callHierarchy) specification:

| LSP Method | Direction | Description |
|---|---|---|
| `textDocument/prepareCallHierarchy` | - | Given a position, return the symbol at that location |
| `callHierarchy/incomingCalls` | Callers | Who calls this symbol? |
| `callHierarchy/outgoingCalls` | Callees | Whom does this symbol call? |

## Known LSP Implementation Gaps

| # | File | Issue | Impact |
|---|------|-------|--------|
| 1 | `LspModels.cs` | `JsonRpcId` lacks `JsonConverter`; numeric IDs serialize as objects `{"id":{}}` instead of `{"id":1}` | **Fixed** in PR #14 — `JsonRpcIdConverter` reads/writes scalar string or integer ids |
| 2 | `LspModels.cs` | `TextDocumentSync` advertises `Full` (1) but server ignores `didOpen`/`didChange` | Client may send unnecessary document sync traffic |
| 3 | `extension.ts` | `Content-Length` uses UTF-16 `string.length` instead of UTF-8 byte count | Breaks on non-ASCII characters split across TCP chunks |
| 4 | `extension.ts` | `csCallGraph.maxDepth` and `csCallGraph.searchScope` settings declared but never sent to server | Settings have no effect |
| 5 | `CallHierarchyHandler.cs` | `ToLspItem`/`ToIncomingCall`/`ToOutgoingCall` range fields may mix declaration and call-site positions | **Fixed** in PR #21 — every item uses its symbol's declaration file, full-span `range`, and identifier-only `selectionRange`; call-site positions live only in `FromRanges` |
