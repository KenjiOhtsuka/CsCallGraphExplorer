# Testing Guide

## Prerequisites

- .NET 10 SDK
- Node.js 22+ (v24.18.0 installed)
- VS Code (any recent version)
- `src/CsCallGraph.Cli` builds successfully
- `src/CsCallGraph.LanguageServer` builds successfully

## 1. Build the VS Code extension

```powershell
cd extensions\vscode
npm install
npm run compile
```

Output goes to `extensions/vscode/out/extension.js`.

## 2. Test the LSP server manually (without VS Code)

You can send raw LSP messages to the language server via PowerShell to verify it works.

### Start the server

```powershell
# Run in a separate terminal — it will block waiting for stdin
dotnet run --project src\CsCallGraph.LanguageServer -- --solution samples\SampleProject.sln
```

### Send frames with the helper below

`Content-Length` is the **UTF-8 byte count** of the JSON body, not the character count.
The `Send-Lsp` helper below computes it automatically, so the frames are always correct:

```powershell
function Send-Lsp {
  param([string]$Body)
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
  $frame = "Content-Length: $($bytes.Length)`r`n`r`n$Body"
  $frame
  $frame | & dotnet run --project src\CsCallGraph.LanguageServer -- --solution samples\SampleProject.sln
}
```

> Pipe one request per invocation — the server reads a single frame and exits at EOF.
> For reference: the `initialize` body below is **107 bytes** (`Content-Length: 107`).

### Send an initialize request

```powershell
Send-Lsp '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":null,"rootUri":null,"capabilities":{}}}'
```

Expected response:
```json
{"jsonrpc":"2.0","id":1,"result":{"capabilities":{"textDocumentSync":1,"callHierarchyProvider":true}}}
```

### Send prepareCallHierarchy

With the server still running, send:

```powershell
Send-Lsp '{"jsonrpc":"2.0","id":2,"method":"textDocument/prepareCallHierarchy","params":{"textDocument":{"uri":"file:///C:/Users/user/project/CsCallGraphExplorer/samples/SampleConsoleApp/Callers.cs"},"position":{"line":37,"character":22}}}'
```

Replace the file path with your actual absolute path. `line` and `character` are 0-based;
`37:22` points at the `StaticMethod` identifier on 1-based line 38 (chars 8–20 are the
`PublicMethods` type name, which resolves to the *type* instead of the method).

### Send incomingCalls (callers)

```powershell
Send-Lsp '{"jsonrpc":"2.0","id":3,"method":"callHierarchy/incomingCalls","params":{"item":{"name":"StaticMethod","kind":6,"uri":"file:///C:/Users/user/project/CsCallGraphExplorer/samples/SampleConsoleApp/Callers.cs","range":{"start":{"line":0,"character":0},"end":{"line":0,"character":0}},"selectionRange":{"start":{"line":36,"character":0},"end":{"line":36,"character":0}},"data":"SampleLibrary.PublicMethods.StaticMethod(string)"}}}'
```

`data` is copied verbatim from the item returned by prepareCallHierarchy.

### Send outgoingCalls (callees)

```powershell
Send-Lsp '{"jsonrpc":"2.0","id":4,"method":"callHierarchy/outgoingCalls","params":{"item":{"name":"RunAll","kind":6,"uri":"file:///C:/Users/user/project/CsCallGraphExplorer/samples/SampleConsoleApp/Callers.cs","range":{"start":{"line":0,"character":0},"end":{"line":0,"character":0}},"selectionRange":{"start":{"line":12,"character":0},"end":{"line":12,"character":0}},"data":"SampleConsoleApp.Callers.RunAll"}}}'
```

### Shutdown

```powershell
Send-Lsp '{"jsonrpc":"2.0","id":5,"method":"shutdown","params":null}'
```

```powershell
Send-Lsp '{"jsonrpc":"2.0","method":"exit"}'
```

> **Tip:** Pipe test messages from a file:
> `Get-Content test-request.txt | dotnet run --project src\CsCallGraph.LanguageServer -- --solution samples\SampleProject.sln`

## 3. Test the VS Code extension

### Launch Extension Development Host

```powershell
cd extensions\vscode
code --extensionDevelopmentPath=.
```

This opens a new VS Code window (the "Extension Development Host") with the extension loaded.

### Load the sample project

In the Extension Development Host:
1. **File > Open Folder...** → open `CsCallGraphExplorer/samples/SampleConsoleApp`
2. Open `Callers.cs`
3. Set the `csCallGraph.solutionPath` setting:
   - **File > Preferences > Settings**
   - Search for `csCallGraph`
   - Set `Solution Path` to the relative path of `SampleProject.sln`, e.g. `../SampleProject.sln`

### Run a command

1. Place cursor on a method call (e.g., line 38 `PublicMethods.StaticMethod("world")`)
2. **Ctrl+Shift+P** → `CsCallGraph: Show Callers`
3. Check the **CsCallGraph** output panel (View > Output > CsCallGraph)

### Test with the context menu

1. Right-click on a symbol in a C# file
2. Go to **CsCallGraph > Show Callers** (or Show Callees)
3. Results appear in the output panel

### Debug the extension

1. In the main VS Code window, open `extensions/vscode/src/extension.ts`
2. Press **F5** — this launches the Extension Development Host with the debugger attached
3. Set breakpoints in `extension.ts`
4. Run commands in the host window

## 4. Test after LSP wiring (Step 2)

When the extension is updated to use LSP instead of shell-out:

1. The LSP server starts automatically when VS Code opens a C# file
2. No manual solution path configuration needed (auto-detected)
3. Use VS Code's built-in **Peek > Call Hierarchy** (Ctrl+Shift+H) or right-click
4. Results appear in the native call hierarchy tree view

## Troubleshooting

| Problem | Likely fix |
|---|---|
| `dotnet` command not found | Install .NET 10 SDK |
| Extension not activating | Check `npm run compile` succeeded; check VS Code Developer Tools console (Help > Toggle Developer Tools) |
| LSP server crashes silently | Run it manually (section 2) to see error output |
| "No .sln file found" | Set `csCallGraph.solutionPath` in settings |
| Symbol not resolved | Check `line` and `character` are 0-based and point inside the method-name identifier |
| LSP frame malformed / server stalls | Use `Send-Lsp` (section 2) so `Content-Length` is computed from UTF-8 byte count |
| Slow response (~30s) | First load — Roslyn is parsing the solution. Subsequent calls are faster if using LSP |