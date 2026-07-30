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

Currently the architecture is simpler: `CsCallGraph.Cli` directly references `CsCallGraph.Core` and invokes it synchronously via console commands. The VS Code extension + LSP server layers are future work.

## Extension Layer (TypeScript) — ❌ Not started

- **Activation**: `onLanguage:csharp` + `onCommand:csCallGraph.showCallers`
- **Commands**: Registers `csCallGraph.showCallers` and `csCallGraph.showCallees`
- **Tree View**: Implements `TreeDataProvider` for the side panel; lazy-loads children on expand
- **LSP Client**: Connects to the Roslyn language server; sends `textDocument/callHierarchy` requests

## Analysis Layer (C# / Roslyn) — ✅ Core complete

- **Language Server**: A .NET console application using StreamJsonRpc or built-in LSP libraries → ❌ Not started
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
