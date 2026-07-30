# UI / UX Design — ❌ Not started (VS Code extension phase)

> All content in this document is a **future specification** for the VS Code extension. None of it has been implemented yet.

## Context Menu

**Location**: Editor context menu (right-click on symbol)
**Label**: `CsCallGraph: Show Call Hierarchy`
**Submenu**:
```
CsCallGraph
├─ Show Callers          (Alt+F12)
└─ Show Callees          (Shift+Alt+F12)
```

Keyboard shortcuts are configurable.

## Tree View Container

- **Location**: Explorer view container (custom viewlet), titled "Call Hierarchy"
- **Default side**: Right sidebar (or left, per user preference)
- **Empty state**: "Select a symbol and run 'CsCallGraph: Show Call Hierarchy'"

## Tree Node Design

```
▶ MethodA(string) : void                  [icon: method]
  ▶ CalledBy(SomeClass.cs : 42)          [icon: method, count badge: 2]
    ▶ AlsoCallsA(Other.cs : 15)          [icon: method]
  ▶ AnotherCaller(Foo.cs : 88)           [icon: method]
```

Each row shows:
1. Expand/collapse chevron (if children exist)
2. Symbol kind icon (colored, matching VS Code convention)
3. Symbol name with parameter list (truncated with ellipsis if > 80 chars)
4. Badge: call-site count (if > 1)
5. Secondary text: file path and line number (gray, monospace, after the name)

### Symbol Icons (FileIconProvider)

| Symbol Kind | VS Code Icon |
|---|---|
| Method | `symbol-method` |
| Constructor | `symbol-constructor` |
| Property | `symbol-property` |
| Field | `symbol-field` |
| Variable | `symbol-variable` |
| Constant | `symbol-constant` |
| Class | `symbol-class` |
| Interface | `symbol-interface` |
| Struct | `symbol-struct` |
| Enum | `symbol-enum` |
| Delegate | `symbol-event` |
| Lambda | `symbol-function` (or `symbol-key` for distinction) |

### Color Coding

- **Root symbol**: bold text
- **Caller nodes**: one shade (blue-ish)
- **Callee nodes**: another shade (green-ish) if dual-pane mode is active

## Interaction

| Action | Behavior |
|---|---|
| **Click** on node | Open file at symbol's declaration location |
| **Double-click** | Same as click |
| **Right-click** → Show Callers | Refresh root with this symbol as the new root |
| **Right-click** → Show Callees | Same as above but callee direction |
| **Right-click** → Copy | Copy this node's text to clipboard |
| **Expand** | Lazy-load: ask language server for children, then render |
| **Collapse** | Cache children in memory; re-display on re-expand without re-fetch |
| **Tooltip** | Full signature + containing type + XML doc summary (first line) |

## Clipboard Copy

The entire visible tree or a selected subtree must be copyable to clipboard as plain text.

### Copy Entire Tree

- **Button** in toolbar: "Copy Tree" (clipboard icon)
- **Keyboard shortcut**: `Ctrl+C` when focus is on an empty area of the tree
- Right-click on empty space → "Copy All"
- Output format:

```
Call Hierarchy — Show Callers of MethodA(string): void
  ├─ CalledBy(int): bool  — SomeClass.cs:42
  │  └─ AlsoCallsA(): void  — Other.cs:15
  └─ AnotherCaller(): void  — Foo.cs:88
```

### Copy Single Branch

- Right-click on a node → "Copy Branch"
- Output includes the selected node and all its visible descendants with the same indentation format

### Copy Single Node

- Right-click on a node → "Copy"
- Output: `MethodA(string): void  — SomeClass.cs:42`

### Text Format Rules

- Indentation uses `├─ ` (branch), `└─ ` (last child), `│  ` (continuation)
- Each line: symbol name + parameter list + return type + ` — ` + relative file path + `:` + line number
- Max line width: no truncation (full signature)
- Encoding: UTF-8 without BOM

## Toolbar Actions

In the view header:
- **Refresh** (🔄): re-run analysis for current root symbol
- **Direction toggle**: "Callers" ↔ "Callees" (default: Callers)
- **Scope selector**: "Solution" / "Project" / "Project + Dependencies"
- **Copy Tree** (📋): copy entire visible tree to clipboard as plain text
- **Clear** (✕): reset tree to empty state

## Keyboard Shortcuts

| Command | Default Keybinding |
|---|---|
| Show Callers | `Alt+F12` |
| Show Callees | `Shift+Alt+F12` |
| Focus Call Hierarchy panel | `Ctrl+Shift+F12` |
| Go to symbol (navigate) | `Enter` or left-click |

## Settings (contributed.json)

```jsonc
{
  "csCallGraph.maxDepth": {
    "type": "number",
    "default": 10,
    "description": "Maximum depth of call hierarchy tree expansion (0 = unlimited)"
  },
  "csCallGraph.defaultDirection": {
    "type": "string",
    "enum": ["callers", "callees"],
    "default": "callers",
    "description": "Default direction when opening call hierarchy"
  },
  "csCallGraph.searchScope": {
    "type": "string",
    "enum": ["solution", "project", "projectWithDependencies"],
    "default": "solution",
    "description": "Scope for symbol search"
  },
  "csCallGraph.useIndex": {
    "type": "boolean",
    "default": true,
    "description": "Use precomputed index for faster results"
  },
  "csCallGraph.includePropertyAccessors": {
    "type": "boolean",
    "default": true,
    "description": "Show implicit property get/set calls as separate nodes"
  }
}
```

## Responsiveness UX

- **Loading state**: Show a spinner / progress bar in the tree while analysis is running
- **Partial results**: If the analysis takes >2 seconds, show results incrementally as each batch finishes
- **Cancellation**: If the user clicks a different symbol, cancel the previous in-flight request
- **Error state**: If analysis fails (e.g., solution not loaded), show a descriptive error message inline
