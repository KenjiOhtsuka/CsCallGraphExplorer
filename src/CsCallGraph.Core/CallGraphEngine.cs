using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using CsCallGraph.Core.Models;

namespace CsCallGraph.Core;

public class CallGraphEngine : IDisposable
{
    private const int DefaultMaxDepth = 10;
    private const SearchScope DefaultScope = SearchScope.Solution;

    private readonly ConcurrentDictionary<string, Lazy<Task<Solution>>> _solutionCache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<Compilation?>>> _compilationCache = new();
    private readonly ConcurrentDictionary<string, MSBuildWorkspace> _workspaceCache = new();

    public async Task<CallGraphResult> GetCallersAsync(
        string solutionPath,
        string symbolName,
        int maxDepth = DefaultMaxDepth,
        SearchScope scope = DefaultScope,
        CancellationToken ct = default)
    {
        return await AnalyzeAsync(solutionPath, symbolName, CallDirection.Callers, maxDepth, scope, ct);
    }

    public async Task<CallGraphResult> GetCalleesAsync(
        string solutionPath,
        string symbolName,
        int maxDepth = DefaultMaxDepth,
        SearchScope scope = DefaultScope,
        CancellationToken ct = default)
    {
        return await AnalyzeAsync(solutionPath, symbolName, CallDirection.Callees, maxDepth, scope, ct);
    }

    public async Task<CallGraphResult> GetCallersAtAsync(
        string solutionPath,
        string filePath,
        int line,
        int column,
        int maxDepth = DefaultMaxDepth,
        SearchScope scope = DefaultScope,
        CancellationToken ct = default)
    {
        return await AnalyzeAtAsync(solutionPath, filePath, line, column, CallDirection.Callers, maxDepth, scope, ct);
    }

    public async Task<CallGraphResult> GetCalleesAtAsync(
        string solutionPath,
        string filePath,
        int line,
        int column,
        int maxDepth = DefaultMaxDepth,
        SearchScope scope = DefaultScope,
        CancellationToken ct = default)
    {
        return await AnalyzeAtAsync(solutionPath, filePath, line, column, CallDirection.Callees, maxDepth, scope, ct);
    }

    public async Task<SymbolDescriptor?> ResolveSymbolAtAsync(
        string solutionPath,
        string filePath,
        int line,
        int column,
        CancellationToken ct = default)
    {
        var solution = await GetSolutionAsync(solutionPath, ct);
        var target = await ResolveSymbolFromPositionAsync(solution, filePath, line, column, ct);
        return target != null ? SymbolResolver.CreateDescriptor(target) : null;
    }

    public async Task<List<string>> ListSymbolsAsync(
        string solutionPath,
        CancellationToken ct = default)
    {
        var solution = await GetSolutionAsync(solutionPath, ct);
        var symbols = new List<string>();

        foreach (var project in solution.Projects)
        {
            var compilation = await GetCompilationAsync(project, ct);
            if (compilation == null) continue;

            CollectCallableSymbols(compilation.Assembly.GlobalNamespace, symbols);
        }

        return symbols;
    }

    private async Task<CallGraphResult> AnalyzeAsync(
        string solutionPath,
        string symbolName,
        CallDirection direction,
        int maxDepth,
        SearchScope scope,
        CancellationToken ct)
    {
        var solution = await GetSolutionAsync(solutionPath, ct);

        var target = await ResolveTargetSymbolAsync(solution, symbolName, ct);
        if (target == null)
            throw new SymbolNotFoundException(symbolName);

        return await BuildResultAsync(solution, target, direction, maxDepth, scope, ct);
    }

    private async Task<CallGraphResult> AnalyzeAtAsync(
        string solutionPath,
        string filePath,
        int line,
        int column,
        CallDirection direction,
        int maxDepth,
        SearchScope scope,
        CancellationToken ct)
    {
        var solution = await GetSolutionAsync(solutionPath, ct);

        var target = await ResolveSymbolFromPositionAsync(solution, filePath, line, column, ct);
        if (target == null)
            throw new SymbolNotFoundException($"{filePath}:{line + 1},{column + 1}");

        return await BuildResultAsync(solution, target, direction, maxDepth, scope, ct);
    }

    private static async Task<CallGraphResult> BuildResultAsync(
        Solution solution, ISymbol target, CallDirection direction,
        int maxDepth, SearchScope scope, CancellationToken ct)
    {
        var scoped = ScopeSolution(solution, target, scope);
        var roots = direction == CallDirection.Callers
            ? await CallersQuery.BuildCallersTreeAsync(scoped, target, maxDepth, ct)
            : await CalleesQuery.BuildCalleesTreeAsync(scoped, target, maxDepth, ct);

        return new CallGraphResult
        {
            Target = SymbolResolver.CreateDescriptor(target),
            Direction = direction,
            Roots = roots,
        };
    }

    private static Solution ScopeSolution(Solution solution, ISymbol target, SearchScope scope)
    {
        if (scope == SearchScope.Solution) return solution;

        var containingProject = solution.Projects
            .FirstOrDefault(p => p.Language == "C#" && target.Locations.Any(l =>
                l.SourceTree != null && solution.GetDocument(l.SourceTree)?.Project.Id == p.Id));

        if (containingProject == null) return solution;

        var keepIds = new HashSet<ProjectId> { containingProject.Id };
        if (scope == SearchScope.ProjectWithDependencies)
        {
            var queue = new Queue<Project>();
            queue.Enqueue(containingProject);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var refProj in current.ProjectReferences)
                {
                    if (keepIds.Add(refProj.ProjectId))
                    {
                        var referenced = solution.GetProject(refProj.ProjectId);
                        if (referenced != null)
                            queue.Enqueue(referenced);
                    }
                }
            }
        }

        var toRemove = solution.Projects
            .Where(p => !keepIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToList();

        return toRemove.Aggregate(solution, (s, id) => s.RemoveProject(id));
    }

    private static async Task<ISymbol?> ResolveSymbolFromPositionAsync(
        Solution solution, string filePath, int line, int column, CancellationToken ct)
    {
        var document = FindDocumentByPath(solution, filePath);
        if (document == null) return null;

        var tree = await document.GetSyntaxTreeAsync(ct);
        if (tree == null) return null;

        var text = await document.GetTextAsync(ct);
        var position = text.Lines.GetPosition(new Microsoft.CodeAnalysis.Text.LinePosition(line, column));
        if (position < 0) return null;

        var semanticModel = await document.GetSemanticModelAsync(ct);
        if (semanticModel == null) return null;

        var root = await tree.GetRootAsync(ct);
        var token = root.FindToken(position, true);
        var parent = token.Parent;
        if (parent == null) return null;

        var declared = semanticModel.GetDeclaredSymbol(parent, ct);
        if (declared != null)
            return declared;

        var info = semanticModel.GetSymbolInfo(parent, ct);
        if (info.Symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction })
            return info.Symbol;

        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }

    private static Document? FindDocumentByPath(Solution solution, string filePath)
    {
        var normalized = filePath.Replace('/', '\\');
        return solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d =>
                d.FilePath != null &&
                (string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase) ||
                 d.FilePath.EndsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(d.FilePath.Replace('/', '\\'), normalized, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<Solution> GetSolutionAsync(string solutionPath, CancellationToken ct)
    {
        var lazy = _solutionCache.GetOrAdd(solutionPath, new Lazy<Task<Solution>>(async () =>
        {
            var workspace = await OpenSolutionAsync(solutionPath, ct);
            return workspace.CurrentSolution;
        }));
        return await lazy.Value;
    }

    private async Task<Compilation?> GetCompilationAsync(Project project, CancellationToken ct)
    {
        var key = project.FilePath ?? project.Name;
        var lazy = _compilationCache.GetOrAdd(key, new Lazy<Task<Compilation?>>(async () =>
        {
            return await project.GetCompilationAsync(ct);
        }));
        return await lazy.Value;
    }

    private async Task<ISymbol?> ResolveTargetSymbolAsync(
        Solution solution, string symbolName, CancellationToken ct)
    {
        string memberName;
        string? paramSpec = null;
        string typePart;

        var parenIdx = symbolName.IndexOf('(');
        if (parenIdx >= 0)
        {
            var closeParen = symbolName.IndexOf(')', parenIdx);
            paramSpec = closeParen > parenIdx + 1
                ? symbolName[(parenIdx + 1)..closeParen]
                : "";
            typePart = symbolName[..parenIdx];
        }
        else
        {
            typePart = symbolName;
        }

        var lastDot = typePart.LastIndexOf('.');
        memberName = lastDot >= 0 ? typePart[(lastDot + 1)..] : typePart;

        if (lastDot > 0 && typePart[lastDot - 1] == '.')
        {
            memberName = typePart[lastDot..];
            lastDot--;
        }

        var memberLookupName = memberName;
        var genericArgStart = memberName.IndexOf('<');
        if (genericArgStart >= 0)
            memberLookupName = memberName[..genericArgStart];

        var projects = solution.Projects.ToList();
        var foundSymbols = new List<ISymbol>();
        var isAmbiguous = false;
        var lockObj = new object();

        await Parallel.ForEachAsync(projects, ct, async (project, token) =>
        {
            var compilation = await GetCompilationAsync(project, token);
            if (compilation == null) return;

            var result = ResolveSymbolInCompilation(compilation, typePart,
                memberLookupName, memberName, paramSpec);
            if (result == null) return;
            var (sym, ambig) = result.Value;

            lock (lockObj)
            {
                if (ambig)
                    isAmbiguous = true;
                else if (sym != null)
                {
                    if (!foundSymbols.Any(s => SymbolEqualityComparer.Default.Equals(s, sym)))
                        foundSymbols.Add(sym);
                }
            }
        });

        if (isAmbiguous)
            throw new AmbiguousSymbolException(symbolName);

        if (foundSymbols.Count == 1)
            return foundSymbols[0];

        if (foundSymbols.Count > 1)
            throw new AmbiguousSymbolException(symbolName);

        return null;
    }

    private static (ISymbol? Symbol, bool IsAmbiguous)? ResolveSymbolInCompilation(
        Compilation compilation,
        string typePart, string memberLookupName, string memberName, string? paramSpec)
    {
        var type = SymbolResolver.FindType(compilation, typePart);
        if (type == null)
        {
            var typeQualifier = memberName.Length <= typePart.Length
                ? typePart[..^memberName.Length].TrimEnd('.')
                : typePart;
            type = SymbolResolver.FindType(compilation, typeQualifier);
        }

        if (type == null)
        {
            var nsStart = typePart.IndexOf('.');
            if (nsStart > 0)
            {
                var guessedNs = typePart[..nsStart];
                foreach (var nst in GetAllTypes(compilation.Assembly.GlobalNamespace))
                {
                    if (nst.ContainingNamespace?.ToDisplayString() != guessedNs &&
                        !nst.ToDisplayString().StartsWith(guessedNs))
                        continue;

                    var full = nst.ToDisplayString();
                    if (!typePart.StartsWith(full)) continue;

                    var rest = typePart[full.Length..];
                    if (rest.StartsWith('.'))
                        rest = rest[1..];

                    if (!rest.Contains('.') || rest is ".ctor" or ".cctor")
                    {
                        var members = SymbolResolver.FindMembersByName(nst, rest);
                        if (members.Count == 1) return (members[0], false);
                        if (members.Count > 1)
                        {
                            if (paramSpec == null)
                                return (null, true);
                            var byParams = SymbolResolver.FindMethodByParams(members, paramSpec);
                            if (byParams != null) return (byParams, false);
                        }
                    }
                }
            }
            return null;
        }

        var foundMembers = SymbolResolver.FindMembersByName(type, memberLookupName);
        if (foundMembers.Count == 1) return (foundMembers[0], false);
        if (foundMembers.Count > 1)
        {
            if (paramSpec == null)
                return (null, true);
            var byParams = SymbolResolver.FindMethodByParams(foundMembers, paramSpec);
            if (byParams != null) return (byParams, false);
        }

        return null;
    }

    private async Task<MSBuildWorkspace> OpenSolutionAsync(
        string solutionPath, CancellationToken ct)
    {
        if (_workspaceCache.TryGetValue(solutionPath, out var cached))
            return cached;

        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");

        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (e.Diagnostic.Kind == Microsoft.CodeAnalysis.WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine($"Workspace error: {e.Diagnostic.Message}");
        };

        try
        {
            await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            workspace.Dispose();
            throw new SolutionLoadFailedException(solutionPath, ex.Message);
        }

        _workspaceCache.TryAdd(solutionPath, workspace);
        return workspace;
    }

    private static void CollectCallableSymbols(
        INamespaceOrTypeSymbol container, List<string> results)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamedTypeSymbol type)
            {
                CollectTypeMembers(type, results);
            }
            else if (member is INamespaceSymbol ns)
            {
                CollectCallableSymbols(ns, results);
            }
        }
    }

    private static void CollectTypeMembers(INamedTypeSymbol type, List<string> results)
    {
        foreach (var sub in type.GetMembers())
        {
            if (sub.IsImplicitlyDeclared || !sub.Locations.Any(l => l.IsInSource))
                continue;

            if (sub is INamedTypeSymbol nested)
            {
                CollectTypeMembers(nested, results);
            }
            else
            {
                switch (sub)
                {
                    case IMethodSymbol m when m.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.StaticConstructor:
                        results.Add($"{type.ToDisplayString()}.{sub.Name}");
                        break;
                    case IPropertySymbol:
                    case IFieldSymbol:
                    case IEventSymbol:
                        results.Add($"{type.ToDisplayString()}.{sub.Name}");
                        break;
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (var workspace in _workspaceCache.Values)
            workspace.Dispose();
        _workspaceCache.Clear();
        _solutionCache.Clear();
        _compilationCache.Clear();
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamedTypeSymbol type)
                yield return type;
            if (member is INamespaceOrTypeSymbol sub)
            {
                foreach (var t in GetAllTypes(sub))
                    yield return t;
            }
        }
    }
}

public class SymbolNotFoundException : Exception
{
    public string SymbolName { get; }
    public SymbolNotFoundException(string symbolName)
        : base($"Symbol '{symbolName}' not found in solution")
    {
        SymbolName = symbolName;
    }
}

public class AmbiguousSymbolException : Exception
{
    public string SymbolName { get; }
    public AmbiguousSymbolException(string symbolName)
        : base($"Symbol '{symbolName}' is ambiguous — specify parameter list to disambiguate")
    {
        SymbolName = symbolName;
    }
}

public class SolutionLoadFailedException : Exception
{
    public string SolutionPath { get; }
    public SolutionLoadFailedException(string solutionPath, string reason)
        : base($"Failed to load solution '{solutionPath}': {reason}")
    {
        SolutionPath = solutionPath;
    }
}
