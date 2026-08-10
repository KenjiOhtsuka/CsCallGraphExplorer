# Change Log

## [0.1.2] - Beta

- Fixed activity-bar icon (`resources/icon.svg` instead of an unsupported codicon).
- Fixed sidebar buttons being hidden from the command palette (`focusedView` when-clauses).
- Fixed Format: Tree / Format: JSON buttons showing the wrong copy format state.
- Fixed node click opening the declaration instead of the call site.
- Validate `maxDepth` input in the callers/callees LSP handlers (`-32602` on invalid values).

## [0.1.1] - Beta

- Call-hierarchy exploration for C# using Roslyn: show callers and callees of any method in the solution.
- Sidebar tree view with direction toggle.
- Copy call tree to clipboard in tree or JSON format.
- Click-through navigation to call sites and declarations.
- Empty-state placeholders for methods with no incoming/outgoing calls.
- Bundled the Roslyn analysis server inside the VSIX (requires the .NET 10 runtime).
- Added marketplace metadata (`repository`, `keywords`), README, LICENSE, and CHANGELOG.
- Documented known limitations (callable members only, bodyless symbols, first-`.sln` detection, Windows-tested).
