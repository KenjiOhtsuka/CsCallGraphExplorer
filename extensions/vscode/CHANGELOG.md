# Change Log

## [0.1.1] - Beta

- Bundled the Roslyn analysis server inside the VSIX (requires the .NET 10 runtime).
- Added marketplace metadata (`repository`, `keywords`), README, LICENSE, and CHANGELOG.
- Documented known limitations (callable members only, bodyless symbols, first-`.sln` detection, Windows-tested).

## [0.1.0] - Beta

- Call-hierarchy exploration for C# using Roslyn: show callers and callees of any method in the solution.
- Sidebar tree view with direction toggle.
- Copy call tree to clipboard in tree or JSON format.
- Click-through navigation to call sites.
- Empty-state placeholders for methods with no incoming/outgoing calls.
- Bundled Roslyn analysis server (requires the .NET 10 runtime).
