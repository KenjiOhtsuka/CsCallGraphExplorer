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

// ---------------------------------------------------------------------------
// Types for LSP call-graph tree methods (csCallGraph/callers, csCallGraph/callees)
// ---------------------------------------------------------------------------
interface LspCallSite {
  filePath: string;
  lineNumber: number;
  column: number;
  endLineNumber: number;
  endColumn: number;
}

interface LspParameterInfo {
  name: string;
  typeName: string;
  isRef: boolean;
  isOut: boolean;
}

interface LspSymbolDescriptor {
  name: string;
  fullyQualifiedName: string;
  containingType: string;
  containingNamespace: string;
  kind: number;
  isStatic: boolean;
  arity: number;
  parameters: LspParameterInfo[];
  declarationLocations: LspCallSite[];
  identifierLocations: LspCallSite[];
  displayString: string;
}

interface LspCallGraphNode {
  symbol: LspSymbolDescriptor;
  callSites: LspCallSite[];
  callCount: number;
  children: LspCallGraphNode[];
}

interface LspCallGraphResult {
  target: LspSymbolDescriptor;
  direction: number; // 0 = Callers, 1 = Callees
  roots: LspCallGraphNode[];
}

type TreeDirection = 'callers' | 'callees';

// ---------------------------------------------------------------------------
// Extension entry point
// ---------------------------------------------------------------------------
let lspClient: LspClient | undefined;
let copyFormatIsTree = true;

async function setCopyFormat(isTree: boolean): Promise<void> {
  copyFormatIsTree = isTree;
  await vscode.commands.executeCommand('setContext', 'csCallGraph.copyFormatIsTree', isTree);
}

async function copyToClipboard(provider: CallGraphTreeProvider): Promise<void> {
  try {
    await vscode.env.clipboard.writeText(provider.copyTree());
    const fmt = copyFormatIsTree ? 'tree' : 'JSON';
    vscode.window.showInformationMessage(`CsCallGraph: call tree copied to clipboard (${fmt})`);
  } catch (err: any) {
    vscode.window.showErrorMessage(`CsCallGraph: ${err.message}`);
  }
}

export async function activate(context: vscode.ExtensionContext) {
  await vscode.commands.executeCommand('setContext', 'csCallGraph.copyFormatIsTree', copyFormatIsTree);
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
  const repoLspProject = path.join(projectRoot, 'src', 'CsCallGraph.LanguageServer');

  const resolveLspCommand = (): { command: string; args: string[] } => {
    const repoDll = path.join(repoLspProject, 'bin', 'Debug', 'net10.0', 'CsCallGraph.LanguageServer.dll');
    if (fs.existsSync(repoDll)) {
      log(`[LSP] using repo Debug build: ${repoDll}`);
      return { command: 'dotnet', args: [repoDll, '--solution', slnPath] };
    }
    const bundledDll = path.join(__dirname, '..', 'server', 'CsCallGraph.LanguageServer.dll');
    if (fs.existsSync(bundledDll)) {
      log(`[LSP] using bundled server: ${bundledDll}`);
      return { command: 'dotnet', args: [bundledDll, '--solution', slnPath] };
    }
    log(`[LSP] no published server found; falling back to 'dotnet run --project'`);
    return { command: 'dotnet', args: ['run', '--project', repoLspProject, '--', '--solution', slnPath] };
  };

  const { command, args } = resolveLspCommand();

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

  const provider = new CallGraphTreeProvider(lspClient);
  const treeView = vscode.window.createTreeView('csCallGraph.callHierarchy', {
    treeDataProvider: provider,
    showCollapseAll: true,
  });
  context.subscriptions.push(treeView);
  provider.onDidChangeTreeData(() => {
    treeView.description = provider.label || undefined;
  });

  context.subscriptions.push(
    vscode.commands.registerCommand('csCallGraph.showCallers', () =>
      showInSidebar(treeView, provider, lspClient!, 'callers')
    ),
    vscode.commands.registerCommand('csCallGraph.showCallees', () =>
      showInSidebar(treeView, provider, lspClient!, 'callees')
    ),
    vscode.commands.registerCommand('csCallGraph.toggleDirection', async () => {
      try {
        await provider.toggleDirection();
      } catch (err: any) {
        vscode.window.showErrorMessage(`CsCallGraph: ${err.message}`);
      }
    }),
    vscode.commands.registerCommand('csCallGraph.copyCallTree', () => copyToClipboard(provider)),
    vscode.commands.registerCommand('csCallGraph.formatTree', async () => {
      await setCopyFormat(false);
    }),
    vscode.commands.registerCommand('csCallGraph.formatJson', async () => {
      await setCopyFormat(true);
    }),
    vscode.commands.registerCommand('csCallGraph.openLocation', (site: LspCallSite) =>
      openLocation(site)
    ),
    vscode.commands.registerCommand('csCallGraph.listSymbols', () =>
      listSymbolsInOutputPanel(outputChannel)
    )
  );

  outputChannel.appendLine('[CsCallGraph] Extension activated with LSP server.');
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
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

async function showInSidebar(
  treeView: vscode.TreeView<CallTreeNode>,
  provider: CallGraphTreeProvider,
  client: LspClient,
  direction: TreeDirection
): Promise<void> {
  const editor = vscode.window.activeTextEditor;
  if (!editor || editor.document.languageId !== 'csharp') {
    vscode.window.showErrorMessage('CsCallGraph: open a C# file first');
    return;
  }

  const rawPos = editor.selection.active;
  const pos = snapToWord(editor.document, rawPos);
  const params = {
    textDocument: { uri: editor.document.uri.toString() },
    position: { line: pos.line, character: pos.character },
  };

  try {
    const items: LspCallHierarchyItem[] = await client.request('textDocument/prepareCallHierarchy', params);
    if (!items || items.length === 0) {
      vscode.window.showErrorMessage(
        'CsCallGraph: no symbol found at cursor. Click directly on the method name and re-run.');
      return;
    }

    const symbol = items[0].data ?? items[0].detail ?? items[0].name;
    const maxDepth = vscode.workspace.getConfiguration('csCallGraph').get<number>('maxDepth', 10);
    await provider.show(symbol, direction, maxDepth);
    await vscode.commands.executeCommand('csCallGraph.callHierarchy.focus');
    await provider.revealFirst(treeView);
  } catch (err: any) {
    vscode.window.showErrorMessage(`CsCallGraph: ${err.message}`);
  }
}

async function openLocation(site: LspCallSite): Promise<void> {
  if (!site?.filePath) return;
  const doc = await vscode.workspace.openTextDocument(site.filePath);
  const editor = await vscode.window.showTextDocument(doc);
  const sel = new vscode.Range(site.lineNumber, site.column, site.endLineNumber, site.endColumn);
  editor.selection = new vscode.Selection(sel.start, sel.end);
  editor.revealRange(sel, vscode.TextEditorRevealType.InCenter);
}

// ---------------------------------------------------------------------------
// Sidebar tree — renders the full call-graph tree returned by the server
// ---------------------------------------------------------------------------
const KIND_ICONS: Record<number, string> = {
  0: 'M', // Method
  1: 'C', // Constructor
  2: 'P', // Property
  3: 'F', // Field
  4: 'E', // Event
  5: 'I', // Indexer
  6: 'O', // Operator
  7: 'λ', // Lambda
  8: 'L', // LocalFunction
};

function kindIcon(kind: number): string {
  return KIND_ICONS[kind] ?? '?';
}

function relativeFilePath(filePath: string): string {
  if (!filePath) return '';
  if (!path.isAbsolute(filePath)) return filePath;
  const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  if (root) {
    const rel = path.relative(root, filePath);
    if (!rel.startsWith('..') && !path.isAbsolute(rel)) return rel.split(path.sep).join('/');
  }
  return filePath.split(path.sep).join('/');
}

class CallTreeNode extends vscode.TreeItem {
  constructor(
    public readonly node: LspCallGraphNode,
    public readonly parent: CallTreeNode | undefined
  ) {
    const sym = node.symbol;
    super(
      sym.displayString || sym.name,
      (node.children?.length ?? 0) > 0
        ? vscode.TreeItemCollapsibleState.Collapsed
        : vscode.TreeItemCollapsibleState.None
    );

    const first = sym.declarationLocations?.[0] ?? sym.identifierLocations?.[0];
    const parts: string[] = [];
    if (sym.isStatic) parts.push('(static)');
    const file = first ? relativeFilePath(first.filePath) : '';
    if (file) parts.push(file);
    if (parts.length > 0) this.description = parts.join(' ');

    const site = node.callSites?.[0];
    const tooltip = new vscode.MarkdownString();
    tooltip.appendCodeblock(sym.fullyQualifiedName || sym.displayString, 'csharp');
    if (node.callCount > 0) tooltip.appendMarkdown(`**${node.callCount}** call site(s)`);
    if (site) {
      tooltip.appendMarkdown(
        `\n\nFirst call site: \`${relativeFilePath(site.filePath)}:${site.lineNumber + 1},${site.column + 1}\``);
    }
    this.tooltip = tooltip;

    if (first) {
      this.command = {
        command: 'csCallGraph.openLocation',
        title: 'Open',
        arguments: [first],
      };
    }
  }

  static placeholder(label: string, description: string): CallTreeNode {
    const node = new CallTreeNode({
      symbol: {
        name: label,
        fullyQualifiedName: label,
        containingType: '',
        containingNamespace: '',
        kind: 0,
        isStatic: false,
        arity: 0,
        parameters: [],
        declarationLocations: [],
        identifierLocations: [],
        displayString: label,
      },
      callSites: [],
      callCount: 0,
      children: [],
    }, undefined);
    node.description = description;
    node.contextValue = 'csCallGraph.placeholder';
    return node;
  }
}

class CallGraphTreeProvider implements vscode.TreeDataProvider<CallTreeNode> {
  private readonly _onDidChangeTreeData = new vscode.EventEmitter<CallTreeNode | undefined>();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private _client: LspClient;
  private _result: LspCallGraphResult | undefined;
  private _symbol = '';
  private _direction: TreeDirection = 'callers';
  private _maxDepth = 10;
  private _roots: CallTreeNode[] = [];

  constructor(client: LspClient) {
    this._client = client;
  }

  get label(): string {
    if (!this._result) return '';
    const dir = this._direction === 'callers' ? 'Callers' : 'Callees';
    return `${dir} of ${this._result.target.displayString || this._result.target.name}`;
  }

  async show(symbol: string, direction: TreeDirection, maxDepth: number): Promise<void> {
    this._symbol = symbol;
    this._direction = direction;
    this._maxDepth = maxDepth;
    const method = direction === 'callers' ? 'csCallGraph/callers' : 'csCallGraph/callees';
    const result = (await this._client.request(method, { symbol, maxDepth })) as LspCallGraphResult;
    this._result = result;
    this._roots = (result.roots ?? []).map((n) => new CallTreeNode(n, undefined));
    this._onDidChangeTreeData.fire(undefined);
  }

  async toggleDirection(): Promise<void> {
    if (!this._result) throw new Error('run Show Callers or Show Callees first');
    await this.show(this._symbol, this._direction === 'callers' ? 'callees' : 'callers', this._maxDepth);
  }

  getTreeItem(element: CallTreeNode): vscode.TreeItem {
    return element;
  }

  getChildren(element?: CallTreeNode): CallTreeNode[] {
    if (!element) {
      if (!this._result) return [];
      if (this._roots.length === 0) {
        const label = this._direction === 'callers' ? 'No callers' : 'No callees';
        return [CallTreeNode.placeholder(label, 'Nothing found in the solution')];
      }
      return this._roots;
    }
    return (element.node.children ?? []).map((c) => new CallTreeNode(c, element));
  }

  getParent(element: CallTreeNode): CallTreeNode | undefined {
    return element.parent;
  }

  async revealFirst(treeView: vscode.TreeView<CallTreeNode>): Promise<void> {
    const root = this._roots[0];
    if (!root) return;
    try {
      await treeView.reveal(root, { expand: true, focus: false });
    } catch {
      // tree view may not be visible yet; ignore
    }
  }

  copyTree(): string {
    const result = this._result;
    if (!result) throw new Error('run Show Callers or Show Callees first');
    if (!copyFormatIsTree) {
      return JSON.stringify(result, null, 2);
    }

    const lines: string[] = [];
    const dir = this._direction === 'callers' ? 'Callers' : 'Callees';
    lines.push(`${dir} of ${result.target.displayString || result.target.name}`);
    if (result.roots.length === 0) {
      lines.push('  (none)');
    } else {
      result.roots.forEach((node, i) => this._formatNode(lines, node, '', i === result.roots.length - 1));
    }
    return lines.join('\n');
  }

  private _formatNode(lines: string[], node: LspCallGraphNode, indent: string, isLast: boolean): void {
    const prefix = isLast ? '└─ ' : '├─ ';
    const staticTag = node.symbol.isStatic ? ' (static)' : '';
    const targetInfo = node.callCount > 0 ? `  —  ${node.callCount} call site(s)` : '';
    const sites = (node.callSites ?? [])
      .map((s) => `at ${relativeFilePath(s.filePath)}:${s.lineNumber + 1},${s.column + 1}`)
      .join('; ');
    const sitesPart = sites ? `  ${sites}` : '';

    lines.push(
      `${indent}${prefix}[${kindIcon(node.symbol.kind)}] ${node.symbol.displayString || node.symbol.name}${staticTag}${targetInfo}${sitesPart}`
    );

    const childIndent = indent + (isLast ? '   ' : '│  ');
    (node.children ?? []).forEach((c, i) => this._formatNode(lines, c, childIndent, i === node.children.length - 1));
  }
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
    vscode.commands.registerCommand('csCallGraph.toggleDirection', () => {
      vscode.window.showErrorMessage('CsCallGraph: LSP server is not running');
    }),
    vscode.commands.registerCommand('csCallGraph.copyCallTree', () => {
      vscode.window.showErrorMessage('CsCallGraph: LSP server is not running');
    }),
    vscode.commands.registerCommand('csCallGraph.formatTree', () => {
      vscode.window.showErrorMessage('CsCallGraph: LSP server is not running');
    }),
    vscode.commands.registerCommand('csCallGraph.formatJson', () => {
      vscode.window.showErrorMessage('CsCallGraph: LSP server is not running');
    }),
    vscode.commands.registerCommand('csCallGraph.openLocation', () => {
      vscode.window.showErrorMessage('CsCallGraph: LSP server is not running');
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
