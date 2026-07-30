using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using CsCallGraph.Core.Models;

namespace CsCallGraph.Core;

public static class CallersQuery
{
    public static async Task<List<CallGraphNode>> BuildCallersTreeAsync(
        Solution solution, ISymbol target, int maxDepth, CancellationToken ct)
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        return await BuildCallersTreeCoreAsync(solution, target, maxDepth, visited, ct);
    }

    private static async Task<List<CallGraphNode>> BuildCallersTreeCoreAsync(
        Solution solution, ISymbol target, int maxDepth,
        HashSet<ISymbol> visited, CancellationToken ct)
    {
        var results = new List<CallGraphNode>();
        if (maxDepth == 0) maxDepth = visited.Count + 100;
        if (maxDepth < 0) return results;
        if (!visited.Add(target)) return results;

        var callers = await SymbolFinder.FindCallersAsync(target, solution, ct);
        var grouped = callers
            .Where(c => c.CallingSymbol != null && c.CallingSymbol.Locations.Any(l => l.IsInSource))
            .GroupBy(c => c.CallingSymbol!, SymbolEqualityComparer.Default)
            .ToList();

        foreach (var group in grouped)
        {
            var node = BuildNode(group.Key, group.SelectMany(c => c.Locations));
            if (maxDepth > 1)
            {
                node.Children.AddRange(
                    await BuildCallersTreeCoreAsync(solution, group.Key, maxDepth - 1, visited, ct));
            }
            results.Add(node);
        }

        return results;
    }

    private static CallGraphNode BuildNode(ISymbol symbol, IEnumerable<Microsoft.CodeAnalysis.Location> locations)
    {
        return new CallGraphNode
        {
            Symbol = SymbolResolver.CreateDescriptor(symbol),
            CallSites = locations
                .Where(l => l.IsInSource)
                .Select(l => new CallSite
                {
                    FilePath = l.SourceTree?.FilePath ?? "",
                    LineNumber = l.GetLineSpan().StartLinePosition.Line,
                    Column = l.GetLineSpan().StartLinePosition.Character,
                })
                .ToList(),
        };
    }
}
