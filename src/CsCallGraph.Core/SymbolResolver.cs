using Microsoft.CodeAnalysis;
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
            case IMethodSymbol m when m.MethodKind == MethodKind.Constructor:
                kind = Models.SymbolKind.Constructor; break;
            case IMethodSymbol m when m.MethodKind == MethodKind.Ordinary:
                kind = Models.SymbolKind.Method; break;
            case IMethodSymbol m when m.MethodKind == MethodKind.LambdaMethod:
                kind = Models.SymbolKind.Lambda; break;
            case IMethodSymbol m when m.MethodKind == MethodKind.LocalFunction:
                kind = Models.SymbolKind.LocalFunction; break;
            case IMethodSymbol m when m.MethodKind == MethodKind.UserDefinedOperator:
                kind = Models.SymbolKind.Operator; break;
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

        var declLocs = symbol.Locations
            .Where(l => l.IsInSource)
            .Select(l => new CallSite
            {
                FilePath = l.SourceTree?.FilePath ?? "",
                LineNumber = l.GetLineSpan().StartLinePosition.Line,
                Column = l.GetLineSpan().StartLinePosition.Character,
            })
            .ToList();

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
            DisplayString = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        };
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

        // No param spec — return null to signal ambiguity
        if (paramSpec == null) return null;

        // Empty string from "Method()" — match parameterless
        if (paramSpec.Length == 0)
            return methods.FirstOrDefault(m => m.Parameters.Length == 0);

        var paramTypes = paramSpec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return methods.FirstOrDefault(m =>
        {
            if (paramTypes.Length == 0)
                return m.Parameters.Length == 0;
            if (paramTypes.Length != m.Parameters.Length)
                return false;

            for (int i = 0; i < paramTypes.Length; i++)
            {
                var pType = m.Parameters[i].Type.ToDisplayString();
                if (!pType.EndsWith(paramTypes[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        });
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
