using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CsCallGraph.Core.Models;

namespace CsCallGraph.Core;

public static class SymbolResolver
{
    public static SymbolDescriptor CreateDescriptor(ISymbol symbol)
    {
        var method = symbol as IMethodSymbol;
        var field = symbol as IFieldSymbol;
        var prop = symbol as IPropertySymbol;
        var namedType = symbol as INamedTypeSymbol;

        Models.SymbolKind kind;
        switch (symbol)
        {
            case IMethodSymbol m when m.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor:
                kind = Models.SymbolKind.Constructor; break;
            case IMethodSymbol m when m.MethodKind == MethodKind.Ordinary:
                kind = Models.SymbolKind.Method; break;
            case IMethodSymbol m when m.MethodKind == MethodKind.LambdaMethod:
                kind = Models.SymbolKind.Lambda; break;
            case IMethodSymbol m when m.MethodKind == MethodKind.LocalFunction:
                kind = Models.SymbolKind.LocalFunction; break;
            case IMethodSymbol m when m.MethodKind == MethodKind.UserDefinedOperator:
                kind = Models.SymbolKind.Operator; break;
            case IPropertySymbol { IsIndexer: true }:
                kind = Models.SymbolKind.Indexer; break;
            case IPropertySymbol:
                kind = Models.SymbolKind.Property; break;
            case IFieldSymbol:
                kind = Models.SymbolKind.Field; break;
            case IEventSymbol:
                kind = Models.SymbolKind.Event; break;
            default:
                kind = Models.SymbolKind.Method; break;
        }

        var parameters = method?.Parameters.Select(p => new ParameterInfo
        {
            Name = p.Name,
            TypeName = p.Type.ToDisplayString(),
            IsRef = p.RefKind == RefKind.Ref,
            IsOut = p.RefKind == RefKind.Out,
        }).ToList() ?? [];

        var declLocs = GetDeclarationLocations(symbol);

        var identLocs = GetIdentifierLocations(symbol);

        return new SymbolDescriptor
        {
            Name = symbol.Name,
            FullyQualifiedName = symbol.ToDisplayString(),
            ContainingType = symbol.ContainingType?.ToDisplayString() ?? "",
            ContainingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
            Kind = kind,
            IsStatic = symbol.IsStatic,
            Arity = namedType?.Arity ?? method?.Arity ?? 0,
            Parameters = parameters,
            DeclarationLocations = declLocs,
            IdentifierLocations = identLocs,
            DisplayString = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        };
    }

    private static List<CallSite> GetDeclarationLocations(ISymbol symbol)
    {
        var syntaxLocs = symbol.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax().GetLocation())
            .Where(l => l.IsInSource)
            .Select(ToCallSite)
            .ToList();
        if (syntaxLocs.Count > 0) return syntaxLocs;

        return symbol.Locations
            .Where(l => l.IsInSource)
            .Select(ToCallSite)
            .ToList();
    }

    public static CallSite ToCallSite(Location location)
    {
        var span = location.GetLineSpan();
        return new CallSite
        {
            FilePath = location.SourceTree?.FilePath ?? "",
            LineNumber = span.StartLinePosition.Line,
            Column = span.StartLinePosition.Character,
            EndLineNumber = span.EndLinePosition.Line,
            EndColumn = span.EndLinePosition.Character,
        };
    }

    private static List<CallSite> GetIdentifierLocations(ISymbol symbol)
    {
        var token = GetIdentifierToken(symbol);
        if (token is not { } id || id.Span.IsEmpty)
            return [];

        var lineSpan = id.SyntaxTree.GetLineSpan(id.Span);
        return
        [
            new CallSite
            {
                FilePath = id.SyntaxTree.FilePath,
                LineNumber = lineSpan.StartLinePosition.Line,
                Column = lineSpan.StartLinePosition.Character,
                EndLineNumber = lineSpan.EndLinePosition.Line,
                EndColumn = lineSpan.EndLinePosition.Character,
            },
        ];
    }

    private static SyntaxToken? GetIdentifierToken(ISymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax())
            {
                case MethodDeclarationSyntax m: return m.Identifier;
                case LocalFunctionStatementSyntax l: return l.Identifier;
                case ConstructorDeclarationSyntax c: return c.Identifier;
                case DestructorDeclarationSyntax d: return d.Identifier;
                case PropertyDeclarationSyntax p: return p.Identifier;
                case EventDeclarationSyntax e: return e.Identifier;
                case VariableDeclaratorSyntax v: return v.Identifier;
                case OperatorDeclarationSyntax o: return o.OperatorToken;
                case IndexerDeclarationSyntax i: return i.ThisKeyword;
                case RecordDeclarationSyntax r: return r.Identifier;
                case TypeDeclarationSyntax t: return t.Identifier;
                case EnumDeclarationSyntax e: return e.Identifier;
                case DelegateDeclarationSyntax d: return d.Identifier;
                default: continue;
            }
        }
        return null;
    }

    public static INamedTypeSymbol? FindType(Compilation compilation, string qualifiedTypeName)
    {
        var type = compilation.GetTypeByMetadataName(qualifiedTypeName);
        if (type != null) return type;

        foreach (var module in compilation.Assembly.Modules)
        {
            foreach (var ns in GetNamespaces(module.GlobalNamespace))
            {
                foreach (var member in ns.GetTypeMembers())
                {
                    if (member.ToDisplayString() == qualifiedTypeName)
                        return member;

                    var nested = FindNestedType(member, qualifiedTypeName);
                    if (nested != null) return nested;
                }
            }
        }

        type = compilation.GetTypeByMetadataName(qualifiedTypeName);
        return type;
    }

    public static IReadOnlyList<ISymbol> FindMembersByName(INamedTypeSymbol type, string memberName)
    {
        return type.GetMembers(memberName)
            .Where(m => m is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)
            .ToList();
    }

    public static IMethodSymbol? FindMethodByParams(IEnumerable<ISymbol> members, string? paramSpec)
    {
        var methods = members.OfType<IMethodSymbol>().ToList();
        if (methods.Count == 0) return null;

        if (paramSpec == null) return null;

        if (paramSpec.Length == 0)
            return methods.FirstOrDefault(m => m.Parameters.Length == 0);

        var paramTypes = SplitParamTypes(paramSpec);
        var matched = new List<IMethodSymbol>();

        foreach (var m in methods)
        {
            if (m.Parameters.Length != paramTypes.Length)
                continue;

            bool match = true;
            for (int i = 0; i < paramTypes.Length; i++)
            {
                // Strip ref/out/in modifier from the expected parameter spec for type comparison
                var expectedType = paramTypes[i];
                var isRefExpected = expectedType.StartsWith("ref ", StringComparison.OrdinalIgnoreCase);
                var isOutExpected = expectedType.StartsWith("out ", StringComparison.OrdinalIgnoreCase);
                var isInExpected = expectedType.StartsWith("in ", StringComparison.OrdinalIgnoreCase);
                if (isRefExpected || isOutExpected)
                    expectedType = expectedType[4..];
                else if (isInExpected)
                    expectedType = expectedType[3..];

                var pType = m.Parameters[i].Type.ToDisplayString();
                if (!pType.EndsWith(expectedType, StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }

                // Validate ref/out match
                var actualRefKind = m.Parameters[i].RefKind;
                if (isRefExpected && actualRefKind != RefKind.Ref)
                {
                    match = false;
                    break;
                }
                if (isOutExpected && actualRefKind != RefKind.Out)
                {
                    match = false;
                    break;
                }
                if (isInExpected && actualRefKind != RefKind.In)
                {
                    match = false;
                    break;
                }
                if ((actualRefKind is RefKind.Ref or RefKind.Out or RefKind.In) && !isRefExpected && !isOutExpected && !isInExpected)
                {
                    match = false;
                    break;
                }
            }
            if (match)
                matched.Add(m);
        }

        if (matched.Count == 1)
            return matched[0];

        return null;
    }

    private static string[] SplitParamTypes(string paramSpec)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < paramSpec.Length; i++)
        {
            switch (paramSpec[i])
            {
                case '<':
                case '[':
                    depth++;
                    break;
                case '>':
                case ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(paramSpec[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }
        result.Add(paramSpec[start..].Trim());
        return [.. result];
    }

    private static IEnumerable<INamespaceSymbol> GetNamespaces(INamespaceSymbol root)
    {
        yield return root;
        foreach (var member in root.GetMembers())
        {
            if (member is INamespaceSymbol child)
            {
                foreach (var nested in GetNamespaces(child))
                    yield return nested;
            }
        }
    }

    private static INamedTypeSymbol? FindNestedType(INamedTypeSymbol type, string fullName)
    {
        if (type.ToDisplayString() == fullName) return type;
        foreach (var nested in type.GetTypeMembers())
        {
            var result = FindNestedType(nested, fullName);
            if (result != null) return result;
        }
        return null;
    }
}
