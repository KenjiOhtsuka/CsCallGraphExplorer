# CsCallGraphExplorer

> **Status: CLI MVP complete.** Core analysis engine (`CsCallGraph.Core`) and CLI tool (`CsCallGraph.Cli`) are implemented. LSP server and VS Code extension are planned for future phases.

A call-hierarchy exploration tool for C# using Roslyn analysis. Delivered first as a CLI, with a VS Code extension planned for later.

## Problem Statement

Visual Studio Enterprise provides a "Call Hierarchy" feature that allows developers to right-click on a function and see an interactive tree of callers and callees. Visual Studio Code lacks this capability out of the box. This project fills that gap for C# developers working in VS Code.

## Goals

### ✅ Implemented (CLI MVP)
- ✅ `list-symbols` — enumerate all callable symbols in a solution
- ✅ `callers` — show who calls a given symbol (tree, depth-limited)
- ✅ `callees` — show what a given symbol calls (tree, depth-limited)
- ✅ Tree and JSON output formats
- ✅ Depth limiting for recursive call graphs
- ✅ Structured JSON errors on stderr
- ✅ Scoped to user-code symbols only (not framework)

### 📅 Planned (Phase 2+)
- 🏗️ VS Code extension scaffolded in `extensions/vscode/` — structure, commands, webview panel
- ⏳ LSP server for incremental analysis
- ⏳ Precomputed index for large-solution performance
- ⏳ Clipboard copy (tree, branch, single node)
- ⏳ Fuzzy symbol search
- ⏳ Field / variable / constant reference tracking

## Non-Goals

- Cross-language call hierarchy (C# only)
- Full static analysis tool (e.g., finding all possible call paths through interfaces)
- Real-time call graph visualization (static snapshot on demand)
- Decompilation of external code

## Key Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Language | C# | Roslyn is the de facto C# compiler platform |
| Analysis backend | Roslyn Workspaces (Microsoft.CodeAnalysis.Workspaces) | Same infrastructure used by Visual Studio and C# Dev Kit |
| Extension host | VS Code extension API (TypeScript) | VS Code extensions are written in TS/JS |
| IPC | Language Server Protocol (LSP) via custom language server | Or reuse C# Dev Kit's Roslyn host; TBD |
| Caching | LRU cache + precomputed symbol-level index | Minimize repeated full-solution analysis |
| UI | TreeViewProvider in VS Code | Native VS Code tree widget supports lazy loading |

## Prior Art

- **Visual Studio Call Hierarchy** (`Ctrl+K, Ctrl+T`): the reference implementation
- **VS Code call hierarchy API**: VS Code 1.43+ has a built-in CallHierarchyProvider API, but no C# extension implements it with Roslyn today (C# Dev Kit may add it in future)
