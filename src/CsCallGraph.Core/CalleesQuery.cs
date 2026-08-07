using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CsCallGraph.Core.Models;

namespace CsCallGraph.Core;

public static class CalleesQuery
{
    public static async Task<List<CallGraphNode>> BuildCalleesTreeAsync(
        Solution solution, ISymbol target, int maxDepth, CancellationToken ct)
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        return await BuildCalleesTreeCoreAsync(solution, target, maxDepth, visited, ct);
    }

    private static async Task<List<CallGraphNode>> BuildCalleesTreeCoreAsync(
        Solution solution, ISymbol target, int maxDepth,
        HashSet<ISymbol> visited, CancellationToken ct)
    {
        var results = new List<CallGraphNode>();
        if (maxDepth == 0) maxDepth = visited.Count + 100;
        if (maxDepth < 0) return results;
        if (!visited.Add(target)) return results;

        var callees = await FindDirectCalleesAsync(solution, target, ct);
        foreach (var (callee, locations) in callees)
        {
            var node = new CallGraphNode
            {
                Symbol = SymbolResolver.CreateDescriptor(callee),
                CallSites = locations
                    .Select(SymbolResolver.ToCallSite)
                    .ToList(),
            };

            if (maxDepth > 1)
            {
                node.Children.AddRange(
                    await BuildCalleesTreeCoreAsync(solution, callee, maxDepth - 1, visited, ct));
            }

            results.Add(node);
        }

        return results;
    }

    private static async Task<List<(ISymbol symbol, List<Microsoft.CodeAnalysis.Location> locations)>> FindDirectCalleesAsync(
        Solution solution, ISymbol target, CancellationToken ct)
    {
        var calleeMap = new Dictionary<ISymbol, List<Microsoft.CodeAnalysis.Location>>(SymbolEqualityComparer.Default);

        foreach (var loc in target.Locations)
        {
            if (loc.SourceTree == null) continue;
            var document = solution.GetDocument(loc.SourceTree);
            if (document == null) continue;

            var root = await loc.SourceTree.GetRootAsync(ct);
            var semanticModel = await document.GetSemanticModelAsync(ct);
            if (semanticModel == null) continue;

            var tokenParent = root.FindToken(loc.SourceSpan.Start).Parent;
            var containingMember = tokenParent?
                .AncestorsAndSelf().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault()
                ?? tokenParent?
                .AncestorsAndSelf().OfType<AccessorDeclarationSyntax>().FirstOrDefault()
                as SyntaxNode;

            // Primary constructors (C# 12 records/classes) have no method or accessor
            // declaration; fall back to the enclosing type declaration. Exclude
            // implicitly-declared constructors (e.g. the record copy constructor),
            // which share the primary constructor's location but are not in source.
            if (containingMember == null
                && target is IMethodSymbol { MethodKind: MethodKind.Constructor }
                && !target.IsImplicitlyDeclared)
            {
                containingMember = tokenParent?
                    .AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            }

            if (containingMember == null) continue;

            // Primary constructor scope: the base list is the only callee source.
            if (containingMember is TypeDeclarationSyntax typeDecl)
            {
                if (typeDecl.BaseList is { } baseList)
                {
                    foreach (var primaryBase in baseList.Types
                        .OfType<PrimaryConstructorBaseTypeSyntax>())
                    {
                        var info = semanticModel.GetSymbolInfo(primaryBase, ct);
                        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                        if (symbol == null) continue;
                        AddToMap(calleeMap, symbol, primaryBase.GetLocation());
                    }
                }
                continue;
            }

            // Method calls
            foreach (var invocation in containingMember.DescendantNodes()
                .OfType<InvocationExpressionSyntax>())
            {
                var info = semanticModel.GetSymbolInfo(invocation, ct);
                if (info.Symbol == null) continue;
                AddToMap(calleeMap, info.Symbol, invocation.GetLocation());
            }

            // Constructor calls
            foreach (var creation in containingMember.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>())
            {
                var info = semanticModel.GetSymbolInfo(creation, ct);
                if (info.Symbol == null) continue;
                AddToMap(calleeMap, info.Symbol, creation.GetLocation());
            }

            // Constructor initializer calls (: this(...) / : base(...))
            foreach (var initializer in containingMember.DescendantNodes()
                .OfType<ConstructorInitializerSyntax>())
            {
                var info = semanticModel.GetSymbolInfo(initializer, ct);
                if (info.Symbol == null) continue;
                AddToMap(calleeMap, info.Symbol, initializer.GetLocation());
            }

            // Property/event accesses (excluding those inside invocations/creations)
            foreach (var memberAccess in containingMember.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>())
            {
                if (memberAccess.Parent is InvocationExpressionSyntax
                    || memberAccess.Parent is ObjectCreationExpressionSyntax
                    || memberAccess.Parent is ElementAccessExpressionSyntax)
                    continue;

                var info = semanticModel.GetSymbolInfo(memberAccess, ct);
                if (info.Symbol is IPropertySymbol or IEventSymbol)
                    AddToMap(calleeMap, info.Symbol, memberAccess.GetLocation());
            }

            // Indexer accesses
            foreach (var elementAccess in containingMember.DescendantNodes()
                .OfType<ElementAccessExpressionSyntax>())
            {
                if (elementAccess.Parent is AssignmentExpressionSyntax assign
                    && assign.Left == elementAccess)
                    continue;

                var info = semanticModel.GetSymbolInfo(elementAccess, ct);
                if (info.Symbol is IPropertySymbol { IsIndexer: true })
                    AddToMap(calleeMap, info.Symbol, elementAccess.GetLocation());
            }
        }

        return calleeMap
            .Where(kv => kv.Key.Locations.Any(l => l.IsInSource))
            .Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private static void AddToMap(
        Dictionary<ISymbol, List<Microsoft.CodeAnalysis.Location>> map,
        ISymbol symbol,
        Microsoft.CodeAnalysis.Location location)
    {
        if (!map.TryGetValue(symbol, out var list))
        {
            list = [];
            map[symbol] = list;
        }
        list.Add(location);
    }
}
