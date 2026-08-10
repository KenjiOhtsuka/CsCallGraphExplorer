# CsCallGraph for Visual Studio Code

Call-hierarchy exploration for C# using Roslyn. Find who calls a method or what a method calls, across all projects in a solution — right from the editor.

> Beta release. Reports issues at [GitHub Repository](https://github.com/KenjiOhtsuka/CsCallGraphExplorer/issues).

## Features

- **Show Callers / Show Callees** — right-click any C# method in the editor and pick `CsCallGraph ▸ Show Callers` (or Show Callees). The sidebar tree is populated from a full Roslyn analysis of your solution.
- **Direction toggle** — switch the current root between callers and callees.
- **Copy Call Tree** — copy the tree to the clipboard in tree format or indented JSON (`Format: Tree` / `Format: JSON`).
- **Open Location** — click a call site to jump to the source line.
- **Empty state** — placeholder nodes (`No callers` / `No callees`) when a method has no incoming/outgoing calls.

## Requirements

- Visual Studio Code `^1.96.0`
- [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) — the extension bundles the Roslyn analysis server, which runs on the .NET 10 runtime.

## Usage

1. Open a folder (or window) that contains a `.sln` file. The extension auto-detects the solution, or set `csCallGraph.solutionPath` to override.
2. Open a C# file, place the cursor on a method, and right-click → `CsCallGraph ▸ Show Callers` / `Show Callees`.
3. Use the buttons in the sidebar title bar to toggle direction, copy the tree, or switch copy format.

## Settings

| Setting | Default | Description |
|---|---|---|
| `csCallGraph.solutionPath` | *(empty)* | Path to the `.sln` file (relative to workspace root). Auto-detected when empty. |
| `csCallGraph.maxDepth` | `10` | Maximum depth of the call tree (`0` = unlimited). |
| `csCallGraph.searchScope` | `solution` | Search scope: `solution`, `project`, `project-with-dependencies`. |
| `csCallGraph.debugLogging` | `true` | Log LSP traffic and server stderr to the CsCallGraph output panel. |

## Troubleshooting

If the sidebar stays empty, open the **CsCallGraph** output panel (View → Output, then pick "CsCallGraph" from the dropdown). It shows:

- which solution file was picked up (or why none was found),
- which server build is being used,
- LSP errors and the server's stderr when `csCallGraph.debugLogging` is on.

## Limitations

- **Callable members only — not types.** Analysis targets methods, constructors, properties, fields, events, indexers, operators, lambdas, and local functions. Right-clicking a **class or interface name** (or any type) produces no callers/callees — place the cursor on a member instead.
- **Requires the .NET 10 runtime.** The analysis server is bundled inside the VSIX but runs on the .NET 10 runtime — it is not self-contained.
- **Solution-wide load.** Each session loads the whole solution through Roslyn's MSBuild workspace. Very large solutions can take a while to load and use significant memory.
- **Bodyless symbols.** Methods without a body — interface declarations, `abstract` methods, partial method declarations — have no callers/callees to analyze, so the view shows placeholder nodes (`No callers` / `No callees`).
- **First `.sln` wins.** The extension auto-detects a solution by scanning the workspace (and parent folders) and uses the first `.sln` it finds. Projects outside that solution are not analyzed; set `csCallGraph.solutionPath` to point at another one.
- **C# only.** Analysis uses Roslyn's C# compiler; Visual Basic is not supported.
- **Beta.** Tested on Windows only; macOS and Linux are untested (but should work with the .NET 10 runtime installed).

## Project

The analysis engine and CLI live in the [CsCallGraphExplorer](https://github.com/KenjiOhtsuka/CsCallGraphExplorer) repository. The extension ships with the compiled analysis server bundled inside the VSIX.
