using Microsoft.CodeAnalysis;
using CsCallGraph.Core.Models;
using Xunit;

using Models = CsCallGraph.Core.Models;

namespace CsCallGraph.Core.Tests;

[Collection("Solution")]
public class SymbolResolverTests
{
    private readonly SolutionFixture _fixture;

    public SymbolResolverTests(SolutionFixture fixture)
    {
        _fixture = fixture;
    }

    #region CreateDescriptor

    private SymbolDescriptor ResolveDescriptor(string symbolName)
    {
        var engine = new CallGraphEngine();
        var sym = ResolveSymbol(symbolName);
        return SymbolResolver.CreateDescriptor(sym);
    }

    private ISymbol ResolveSymbol(string symbolName)
    {
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, symbolName);
            if (type != null)
            {
                var lastDot = symbolName.LastIndexOf('.');
                var memberName = lastDot >= 0 ? symbolName[(lastDot + 1)..] : symbolName;

                if (lastDot > 0 && symbolName[lastDot - 1] == '.')
                    memberName = symbolName[lastDot..];

                var members = SymbolResolver.FindMembersByName(type, memberName);
                if (members.Count == 1) return members[0];
            }

            var nsStart = symbolName.IndexOf('.');
            if (nsStart > 0)
            {
                foreach (var nst in GetAllTypes(comp.Assembly.GlobalNamespace))
                {
                    var full = nst.ToDisplayString();
                    if (symbolName.StartsWith(full))
                    {
                        var rest = symbolName[full.Length..];
                        if (rest.StartsWith('.'))
                            rest = rest[1..];
                        if (!rest.Contains('.') || rest is ".ctor" or ".cctor")
                        {
                            var members = SymbolResolver.FindMembersByName(nst, rest);
                            if (members.Count == 1) return members[0];
                        }
                    }
                }
            }
        }
        throw new InvalidOperationException($"Symbol '{symbolName}' not found");
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamedTypeSymbol type) yield return type;
            if (member is INamespaceOrTypeSymbol sub)
            {
                foreach (var t in GetAllTypes(sub)) yield return t;
            }
        }
    }

    [Fact]
    public void CreateDescriptor_OrdinaryMethod_ProducesCorrectKind()
    {
        var desc = ResolveDescriptor("SampleLibrary.PublicMethods.InstanceMethod");
        Assert.Equal(Models.SymbolKind.Method, desc.Kind);
        Assert.Equal("InstanceMethod", desc.Name);
        Assert.Equal("SampleLibrary.PublicMethods", desc.ContainingType);
        Assert.False(desc.IsStatic);
    }

    [Fact]
    public void CreateDescriptor_StaticMethod_IsStaticTrue()
    {
        var desc = ResolveDescriptor("SampleLibrary.PublicMethods.StaticMethod");
        Assert.Equal(Models.SymbolKind.Method, desc.Kind);
        Assert.True(desc.IsStatic);
    }

    [Fact]
    public void CreateDescriptor_Constructor_ProducesConstructorKind()
    {
        var desc = ResolveDescriptor("SampleLibrary.OuterClass..ctor");
        Assert.Equal(Models.SymbolKind.Constructor, desc.Kind);
        Assert.Equal(".ctor", desc.Name);
    }

    [Fact]
    public void CreateDescriptor_Property_ProducesPropertyKind()
    {
        var desc = ResolveDescriptor("SampleLibrary.FieldsAndProperties.AutoProperty");
        Assert.Equal(Models.SymbolKind.Property, desc.Kind);
        Assert.Equal("AutoProperty", desc.Name);
    }

    [Fact]
    public void CreateDescriptor_Field_ProducesFieldKind()
    {
        var desc = ResolveDescriptor("SampleLibrary.FieldsAndProperties.InstanceField");
        Assert.Equal(Models.SymbolKind.Field, desc.Kind);
        Assert.Equal("InstanceField", desc.Name);
        Assert.False(desc.IsStatic);
    }

    [Fact]
    public void CreateDescriptor_StaticField_IsStaticTrue()
    {
        var desc = ResolveDescriptor("SampleLibrary.FieldsAndProperties.StaticField");
        Assert.True(desc.IsStatic);
    }

    [Fact]
    public void CreateDescriptor_MethodWithParameters_IncludesParams()
    {
        var desc = ResolveDescriptor("SampleLibrary.PublicMethods.MethodWithRefOut");
        Assert.Contains(desc.Parameters, p => p.Name == "x" && p.IsRef);
        Assert.Contains(desc.Parameters, p => p.Name == "y" && p.IsOut);
    }

    [Fact]
    public void CreateDescriptor_DeclarationLocations_NotEmpty()
    {
        var desc = ResolveDescriptor("SampleLibrary.PublicMethods.InstanceMethod");
        Assert.NotEmpty(desc.DeclarationLocations);
        Assert.All(desc.DeclarationLocations, l => Assert.NotEmpty(l.FilePath));
    }

    [Fact]
    public async Task CreateDescriptor_LocalFunction_HasLocalFunctionKind()
    {
        var engine = new CallGraphEngine();
        var result = await engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleLibrary.LambdasAndDelegates.LocalFunctionExample");

        var multiplyNode = result.Roots
            .SelectMany(r => Flatten(r))
            .FirstOrDefault(n => n.Symbol.Name == "Multiply");

        Assert.NotNull(multiplyNode);
        Assert.Equal(Models.SymbolKind.LocalFunction, multiplyNode.Symbol.Kind);
    }

    #endregion

    #region FindType

    [Fact]
    public void FindType_ByNamespaceQualifiedName_ReturnsType()
    {
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, "SampleLibrary.PublicMethods");
            if (type != null)
            {
                Assert.Equal("SampleLibrary.PublicMethods", type.ToDisplayString());
                return;
            }
        }
        Assert.Fail("Type not found in any compilation");
    }

    [Fact]
    public void FindType_GenericTypeWithArity_ReturnsType()
    {
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, "SampleLibrary.GenericClass<TKey, TValue>");
            if (type != null)
            {
                Assert.Equal(2, type.Arity);
                return;
            }
        }
        Assert.Fail("Generic type not found");
    }

    [Fact]
    public void FindType_NestedType_ReturnsType()
    {
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, "SampleLibrary.OuterClass.InnerClass");
            if (type != null)
            {
                Assert.Equal("InnerClass", type.Name);
                return;
            }
        }
        Assert.Fail("Nested type not found");
    }

    [Fact]
    public void FindType_Nonexistent_ReturnsNull()
    {
        Assert.NotEmpty(_fixture.Compilations);
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, "SampleLibrary.NonexistentType");
            Assert.Null(type);
        }
    }

    #endregion

    #region FindMembersByName

    [Fact]
    public void FindMembersByName_ExistingMember_ReturnsSymbols()
    {
        Assert.NotEmpty(_fixture.Compilations);
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, "SampleLibrary.PublicMethods");
            if (type != null)
            {
                var members = SymbolResolver.FindMembersByName(type, "InstanceMethod");
                Assert.Single(members);
                Assert.Equal("InstanceMethod", members[0].Name);
                return;
            }
        }
        Assert.Fail("Expected type not found in any compilation");
    }

    [Fact]
    public void FindMembersByName_OverloadedMethod_ReturnsAllOverloads()
    {
        Assert.NotEmpty(_fixture.Compilations);
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, "SampleLibrary.Overloads");
            if (type != null)
            {
                var members = SymbolResolver.FindMembersByName(type, "Compute");
                Assert.Equal(5, members.Count);
                Assert.All(members, m => Assert.Equal("Compute", m.Name));
                return;
            }
        }
        Assert.Fail("Expected type not found in any compilation");
    }

    [Fact]
    public void FindMembersByName_NonexistentMember_ReturnsEmpty()
    {
        Assert.NotEmpty(_fixture.Compilations);
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, "SampleLibrary.PublicMethods");
            if (type != null)
            {
                var members = SymbolResolver.FindMembersByName(type, "DoesNotExist");
                Assert.Empty(members);
                return;
            }
        }
        Assert.Fail("Expected type not found in any compilation");
    }

    #endregion

    #region FindMethodByParams

    [Fact]
    public void FindMethodByParams_EmptyParams_MatchesParameterless()
    {
        Assert.NotEmpty(_fixture.Compilations);
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, "SampleLibrary.Overloads");
            if (type != null)
            {
                var members = SymbolResolver.FindMembersByName(type, "Compute");
                var result = SymbolResolver.FindMethodByParams(members, "");
                Assert.NotNull(result);
                Assert.Empty(result!.Parameters);
                return;
            }
        }
        Assert.Fail("Expected type not found in any compilation");
    }

    [Fact]
    public void FindMethodByParams_IntParam_MatchesSingleInt()
    {
        Assert.NotEmpty(_fixture.Compilations);
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, "SampleLibrary.Overloads");
            if (type != null)
            {
                var members = SymbolResolver.FindMembersByName(type, "Compute");
                var result = SymbolResolver.FindMethodByParams(members, "int");
                Assert.NotNull(result);
                Assert.Single(result!.Parameters);
                Assert.Equal("int", result.Parameters[0].Type.ToDisplayString());
                return;
            }
        }
        Assert.Fail("Expected type not found in any compilation");
    }

    [Fact]
    public void FindMethodByParams_IntStringParams_MatchesCorrectOverload()
    {
        Assert.NotEmpty(_fixture.Compilations);
        foreach (var comp in _fixture.Compilations)
        {
            var type = SymbolResolver.FindType(comp, "SampleLibrary.Overloads");
            if (type != null)
            {
                var members = SymbolResolver.FindMembersByName(type, "Compute");
                var result = SymbolResolver.FindMethodByParams(members, "int, string");
                Assert.NotNull(result);
                Assert.Equal(2, result!.Parameters.Length);
                return;
            }
        }
        Assert.Fail("Expected type not found in any compilation");
    }

    [Fact]
    public void FindMethodByParams_NullParamSpec_ReturnsNull()
    {
        var members = new List<ISymbol>();
        var result = SymbolResolver.FindMethodByParams(members, null);
        Assert.Null(result);
    }

    #endregion

    private static List<CallGraphNode> Flatten(CallGraphNode node)
    {
        var results = new List<CallGraphNode> { node };
        foreach (var child in node.Children)
            results.AddRange(Flatten(child));
        return results;
    }
}
