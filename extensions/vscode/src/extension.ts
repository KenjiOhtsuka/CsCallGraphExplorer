import * as vscode from 'vscode';
import * as path from 'path';
import * as cp from 'child_process';
import * as fs from 'fs';

// ---------------------------------------------------------------------------
// Lightweight LSP client — JSON-RPC over stdin/stdout
// ---------------------------------------------------------------------------
type LogFn = (line: string) => void;

class LspClient {
  private _child?: cp.ChildProcess;
  private _msgId = 1;
  private _pending = new Map<number, { resolve: (v: any) => void; reject: (e: any) => void }>();
  private _buffer = Buffer.alloc(0);

  constructor(private _log: LogFn = () => {}) {}

  get isRunning(): boolean {
    return !!this._child && !this._child.killed;
  }

  start(command: string, args: string[]): Promise<void> {
    return new Promise((resolve, reject) => {
      this._log(`[LSP] spawn: ${command} ${args.join(' ')}`);
      this._child = cp.spawn(command, args, { stdio: ['pipe', 'pipe', 'pipe'] });

      this._child.stdout!.on('data', (chunk: Buffer) => this._onData(chunk));
      this._child.stderr!.on('data', (chunk: Buffer) => {
        const text = chunk.toString();
        for (const line of text.split(/\r?\n/)) {
          const trimmed = line.trim();
          if (trimmed.length > 0) this._log(`[LSP stderr] ${trimmed}`);
        }
      });
      this._child.on('exit', (code) => {
        this._log(`[LSP] exited with code ${code}`);
        this._child = undefined;
        this._rejectPending(new Error(`LSP server exited with code ${code}`));
      });
      this._child.on('error', (err) => {
        this._log(`[LSP] spawn error: ${err.message}`);
        this._child = undefined;
        this._rejectPending(err);
        reject(err);
      });

      const t0 = Date.now();
      this.request('initialize', {
        processId: process.pid,
        rootUri: null,
        capabilities: {}
      }).then(() => {
        this._log(`[LSP] initialize OK in ${Date.now() - t0} ms`);
        resolve();
      }, reject);
    });
  }

  stop(): void {
    try { this.notify('shutdown'); } catch {}
    try { this.notify('exit'); } catch {}
    if (this._child && !this._child.killed) {
      this._child.kill();
    }
    this._child = undefined;
    this._rejectPending(new Error('LSP server stopped'));
  }

  request(method: string, params: any): Promise<any> {
    if (!this.isRunning) {
      this._log(`[LSP !] ${method} rejected: LSP server is not running`);
      return Promise.reject(new Error('LSP server is not running'));
    }
    const id = this._msgId++;
    const t0 = Date.now();
    this._log(`[LSP ->] ${method} (id ${id})`);
    return new Promise((resolve, reject) => {
      this._pending.set(id, {
        resolve: (v: any) => {
          this._log(`[LSP <-] ${method} (id ${id}) in ${Date.now() - t0} ms`);
          resolve(v);
        },
        reject: (e: any) => {
          this._log(`[LSP <-] ${method} (id ${id}) ERROR in ${Date.now() - t0} ms: ${e.message}`);
          reject(e);
        },
      });
      this._send({ jsonrpc: '2.0', id, method, params });
    });
  }

  notify(method: string, params?: any): void {
    this._log(`[LSP ->] ${method} (notify)`);
    this._send({ jsonrpc: '2.0', method, params });
  }

  private _rejectPending(err: Error): void {
    for (const cb of this._pending.values()) cb.reject(err);
    this._pending.clear();
  }

  private _send(msg: any): void {
    const json = JSON.stringify(msg);
    const label = msg.id != null ? `id ${msg.id}` : 'notify';
    this._log(`[LSP send] ${msg.method ?? '?'} (${label}) ${json.slice(0, 400)}`);
    const header = `Content-Length: ${Buffer.byteLength(json, 'utf8')}\r\n\r\n`;
    this._child?.stdin?.write(header + json);
  }

  private _onData(chunk: Buffer): void {
    this._buffer = Buffer.concat([this._buffer, chunk]);
    while (true) {
      const sep = this._buffer.indexOf('\r\n\r\n');
      if (sep === -1) break;
      const match = /Content-Length: (\d+)/.exec(this._buffer.subarray(0, sep).toString('utf8'));
      if (!match) break;
      const bodyLen = parseInt(match[1], 10);
      if (this._buffer.length < sep + 4 + bodyLen) break;

      const body = this._buffer.subarray(sep + 4, sep + 4 + bodyLen).toString('utf8');
      this._buffer = Buffer.from(this._buffer.subarray(sep + 4 + bodyLen));

      try {
        const msg = JSON.parse(body);
        if (msg.id != null && this._pending.has(msg.id)) {
          const cb = this._pending.get(msg.id)!;
          this._pending.delete(msg.id);
          if (msg.error) cb.reject(new Error(msg.error.message));
          else cb.resolve(msg.result);
        }
      } catch { /* ignore malformed messages */ }
    }
  }
}

// ---------------------------------------------------------------------------
// Types for LSP call hierarchy items (with data field)
// ---------------------------------------------------------------------------
interface LspCallHierarchyItem {
  name: string;
  kind: number;
  detail?: string;
  uri: string;
  range: Range;
  selectionRange: Range;
  data?: string;
}

interface Range {
  start: { line: number; character: number };
  end: { line: number; character: number };
}

interface LspIncomingCall {
  from: LspCallHierarchyItem;
  fromRanges: Range[];
}

interface LspOutgoingCall {
  to: LspCallHierarchyItem;
  fromRanges: Range[];
}

// ---------------------------------------------------------------------------
// Extension entry point
// ---------------------------------------------------------------------------
let lspClient: LspClient | undefined;

export async function activate(context: vscode.ExtensionContext) {
  const outputChannel = vscode.window.createOutputChannel('CsCallGraph');
  context.subscriptions.push(outputChannel);

  const debugEnabled =
    process.env.CSCALLGRAPH_LSP_DEBUG === 'true' ||
    vscode.workspace.getConfiguration('csCallGraph').get<boolean>('debugLogging', false) === true;
  const log: LogFn = (line) => {
    if (debugEnabled) outputChannel.appendLine(line);
  };
  outputChannel.appendLine(`[CsCallGraph] LSP debug logging: ${debugEnabled ? 'ON' : 'OFF'}`);

  const slnPath = await resolveSolutionPath();
  if (!slnPath) {
    outputChannel.appendLine('[CsCallGraph] No .sln file found.');
    outputChannel.appendLine('Set csCallGraph.solutionPath or open a folder containing a .sln file.');
    registerFallbackCommands(context, outputChannel);
    return;
  }

  outputChannel.appendLine(`[CsCallGraph] Solution: ${slnPath}`);

  const projectRoot = path.resolve(__dirname, '..', '..', '..');
  const lspProject = path.join(projectRoot, 'src', 'CsCallGraph.LanguageServer');

  const lspDll = path.join(lspProject, 'bin', 'Debug', 'net10.0', 'CsCallGraph.LanguageServer.dll');
  const isPublished = fs.existsSync(lspDll);

  let command: string;
  let args: string[];
  if (isPublished) {
    command = 'dotnet';
    args = [lspDll, '--solution', slnPath];
  } else {
    command = 'dotnet';
    args = ['run', '--project', lspProject, '--', '--solution', slnPath];
  }

  lspClient = new LspClient(log);
  try {
    await lspClient.start(command, args);
    outputChannel.appendLine('[CsCallGraph] LSP server started.');
  } catch (err: any) {
    outputChannel.appendLine(`[CsCallGraph] Failed to start LSP server: ${err.message}`);
    lspClient = undefined;
    registerFallbackCommands(context, outputChannel);
    return;
  }

  context.subscriptions.push(
    vscode.languages.registerCallHierarchyProvider('csharp', new LspCallHierarchyProvider(lspClient))
  );

  context.subscriptions.push(
    vscode.commands.registerCommand('csCallGraph.showCallers', () =>
      showInOutputPanel(outputChannel, lspClient!, 'callers')
    ),
    vscode.commands.registerCommand('csCallGraph.showCallees', () =>
      showInOutputPanel(outputChannel, lspClient!, 'callees')
    ),
    vscode.commands.registerCommand('csCallGraph.listSymbols', () =>
      listSymbolsInOutputPanel(outputChannel)
    )
  );

  outputChannel.appendLine('[CsCallGraph] Extension activated with LSP server.');
}

// ---------------------------------------------------------------------------
// CallHierarchyProvider — uses LSP server
// ---------------------------------------------------------------------------
const _itemData = new WeakMap<vscode.CallHierarchyItem, string>();

class LspCallHierarchyProvider implements vscode.CallHierarchyProvider {
  constructor(private _client: LspClient) {}

  async prepareCallHierarchy(
    document: vscode.TextDocument,
    position: vscode.Position,
    token: vscode.CancellationToken
  ): Promise<vscode.CallHierarchyItem[]> {
    const snapped = snapToWord(document, position);
    const params = {
      textDocument: { uri: document.uri.toString() },
      position: { line: snapped.line, character: snapped.character },
    };
    const result: LspCallHierarchyItem[] = await this._client.request('textDocument/prepareCallHierarchy', params);
    if (!result || result.length === 0) return [];

    return result.map((r) => {
      const item = new vscode.CallHierarchyItem(
        toVscodeSymbolKind(r.kind),
        r.name,
        r.detail ?? '',
        vscode.Uri.parse(r.uri),
        toVscodeRange(r.range),
        toVscodeRange(r.selectionRange)
      );
      if (r.data) {
        _itemData.set(item, r.data);
      }
      return item;
    });
  }

  async provideCallHierarchyIncomingCalls(
    item: vscode.CallHierarchyItem,
    token: vscode.CancellationToken
  ): Promise<vscode.CallHierarchyIncomingCall[]> {
    const params = { item: toLspItem(item) };
    const result: LspIncomingCall[] = await this._client.request('callHierarchy/incomingCalls', params);
    return (result ?? []).map((r) => new vscode.CallHierarchyIncomingCall(
      fromLspItem(r.from),
      (r.fromRanges ?? []).map((rr) => toVscodeRange(rr))
    ));
  }

  async provideCallHierarchyOutgoingCalls(
    item: vscode.CallHierarchyItem,
    token: vscode.CancellationToken
  ): Promise<vscode.CallHierarchyOutgoingCall[]> {
    const params = { item: toLspItem(item) };
    const result: LspOutgoingCall[] = await this._client.request('callHierarchy/outgoingCalls', params);
    return (result ?? []).map((r) => new vscode.CallHierarchyOutgoingCall(
      fromLspItem(r.to),
      (r.fromRanges ?? []).map((rr) => toVscodeRange(rr))
    ));
  }
}

function toLspItem(item: vscode.CallHierarchyItem): LspCallHierarchyItem {
  return {
    name: item.name,
    kind: item.kind,
    detail: item.detail,
    uri: item.uri.toString(),
    range: fromVscodeRange(item.range),
    selectionRange: fromVscodeRange(item.selectionRange),
    data: _itemData.get(item),
  };
}

function fromLspItem(r: LspCallHierarchyItem): vscode.CallHierarchyItem {
  const item = new vscode.CallHierarchyItem(
    toVscodeSymbolKind(r.kind),
    r.name,
    r.detail ?? '',
    vscode.Uri.parse(r.uri),
    toVscodeRange(r.range),
    toVscodeRange(r.selectionRange)
  );
  if (r.data) {
    _itemData.set(item, r.data);
  }
  return item;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
function toVscodeSymbolKind(lspKind: number): vscode.SymbolKind {
  const map: Record<number, vscode.SymbolKind> = {
    1: vscode.SymbolKind.File,
    2: vscode.SymbolKind.Module,
    3: vscode.SymbolKind.Namespace,
    4: vscode.SymbolKind.Package,
    5: vscode.SymbolKind.Class,
    6: vscode.SymbolKind.Method,
    7: vscode.SymbolKind.Property,
    8: vscode.SymbolKind.Field,
    9: vscode.SymbolKind.Constructor,
    10: vscode.SymbolKind.Enum,
    11: vscode.SymbolKind.Interface,
    12: vscode.SymbolKind.Function,
    13: vscode.SymbolKind.Variable,
    14: vscode.SymbolKind.Constant,
    15: vscode.SymbolKind.String,
    16: vscode.SymbolKind.Number,
    17: vscode.SymbolKind.Boolean,
    18: vscode.SymbolKind.Array,
    19: vscode.SymbolKind.Object,
    20: vscode.SymbolKind.Key,
    21: vscode.SymbolKind.Null,
    22: vscode.SymbolKind.EnumMember,
    23: vscode.SymbolKind.Struct,
    24: vscode.SymbolKind.Event,
    25: vscode.SymbolKind.Operator,
    26: vscode.SymbolKind.TypeParameter,
  };
  return map[lspKind] ?? vscode.SymbolKind.Method;
}

function toVscodeRange(r: Range): vscode.Range {
  if (!r) return new vscode.Range(0, 0, 0, 0);
  return new vscode.Range(r.start.line, r.start.character, r.end.line, r.end.character);
}

function fromVscodeRange(r: vscode.Range): Range {
  return {
    start: { line: r.start.line, character: r.start.character },
    end: { line: r.end.line, character: r.end.character },
  };
}

function snapToWord(
  document: vscode.TextDocument,
  position: vscode.Position
): vscode.Position {
  const candidates = [position];
  if (position.character > 0) candidates.push(position.translate(0, -1));
  for (const pos of candidates) {
    const word = document.getWordRangeAtPosition(pos);
    if (word) return word.start;
  }
  return position;
}

async function resolveSolutionPath(): Promise<string | undefined> {
  const config = vscode.workspace.getConfiguration('csCallGraph');
  const configured = config.get<string>('solutionPath');
  if (configured) {
    if (path.isAbsolute(configured)) return configured;
    const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (root) return path.resolve(root, configured);
    return undefined;
  }

  const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  if (!root) return undefined;

  const files = await vscode.workspace.findFiles('**/*.sln', '**/node_modules/**', 5);
  if (files.length > 0) return files[0].fsPath;

  let dir = root;
  while (true) {
    try {
      const entries = fs.readdirSync(dir);
      const sln = entries.find(e => e.endsWith('.sln'));
      if (sln) return path.join(dir, sln);
    } catch {}
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  return undefined;
}

async function showInOutputPanel(
  channel: vscode.OutputChannel,
  client: LspClient,
  direction: 'callers' | 'callees'
): Promise<void> {
  const editor = vscode.window.activeTextEditor;
  if (!editor || editor.document.languageId !== 'csharp') {
    vscode.window.showErrorMessage('Open a C# file first');
    return;
  }

  channel.clear();
  const rawPos = editor.selection.active;
  const pos = snapToWord(editor.document, rawPos);
  const params = {
    textDocument: { uri: editor.document.uri.toString() },
    position: { line: pos.line, character: pos.character },
  };
  const snapped = pos.line !== rawPos.line || pos.character !== rawPos.character
    ? ` (snapped from ${rawPos.line + 1}:${rawPos.character + 1})`
    : '';
  channel.appendLine(`[Query] ${direction} at ${editor.document.fileName}:${pos.line + 1}:${pos.character + 1} (0-based ${pos.line}:${pos.character})${snapped}`);

  try {
    const items: LspCallHierarchyItem[] = await client.request('textDocument/prepareCallHierarchy', params);
    if (!items || items.length === 0) {
      channel.appendLine('No symbol found at cursor.');
      channel.appendLine(`[Hint] The cursor was at line ${pos.line + 1}, column ${pos.character + 1}. Click directly on the method name (not the parentheses/arguments) and re-run.`);
      channel.show();
      return;
    }

    const data = items[0].data ?? items[0].detail ?? items[0].name;
    channel.appendLine(`[Resolved] ${data}`);
    const method = direction === 'callers' ? 'callHierarchy/incomingCalls' : 'callHierarchy/outgoingCalls';

    const itemKey = { data: items[0].data };
    const calls: any[] = await client.request(method, { item: itemKey });

    channel.appendLine(`=== ${direction === 'callers' ? 'Callers' : 'Callees'} of ${data} ===`);
    channel.appendLine('');
    for (const call of calls ?? []) {
      const child = call.from ?? call.to;
      const ranges = call.fromRanges ?? [];
      const file = child.uri
        ? vscode.workspace.asRelativePath(vscode.Uri.parse(child.uri).fsPath, false)
        : '';
      const loc = ranges.length > 0
        ? `  at ${file ? `${file}:` : ''}${ranges[0].start.line + 1}:${ranges[0].start.character + 1}`
        : file
          ? `  at ${file}`
          : '';
      channel.appendLine(`  ${child.data ?? child.name}${loc}`);
      channel.appendLine('');
    }
  } catch (err: any) {
    channel.appendLine(`[Error] ${err.message}`);
  }
  channel.show();
}

async function listSymbolsInOutputPanel(channel: vscode.OutputChannel): Promise<void> {
  channel.appendLine('Use the CLI: dotnet run --project src/CsCallGraph.Cli -- list-symbols --solution <path>');
  channel.show();
}

function registerFallbackCommands(
  context: vscode.ExtensionContext,
  channel: vscode.OutputChannel
): void {
  context.subscriptions.push(
    vscode.commands.registerCommand('csCallGraph.showCallers', () => {
      channel.show();
    }),
    vscode.commands.registerCommand('csCallGraph.showCallees', () => {
      channel.show();
    }),
    vscode.commands.registerCommand('csCallGraph.listSymbols', () => {
      channel.show();
    })
  );
}

export function deactivate(): void {
  lspClient?.stop();
  lspClient = undefined;
}
