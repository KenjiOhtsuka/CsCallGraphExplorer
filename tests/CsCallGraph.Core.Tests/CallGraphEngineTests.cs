using CsCallGraph.Core.Models;
using Xunit;

namespace CsCallGraph.Core.Tests;

[Collection("Solution")]
public class CallGraphEngineTests
{
    private readonly SolutionFixture _fixture;
    private readonly CallGraphEngine _engine;

    public CallGraphEngineTests(SolutionFixture fixture)
    {
        _fixture = fixture;
        _engine = new CallGraphEngine();
    }

    #region ListSymbols

    [Fact]
    public async Task ListSymbols_ReturnsOnlyUserSymbols()
    {
        var symbols = await _engine.ListSymbolsAsync(_fixture.SolutionPath);

        Assert.NotEmpty(symbols);
        Assert.All(symbols, s =>
            Assert.True(s.StartsWith("SampleLibrary") || s.StartsWith("SampleConsoleApp") || s.StartsWith("Program"),
                $"Symbol '{s}' is not in user code namespace"));
    }

    [Fact]
    public async Task ListSymbols_IncludesMethods()
    {
        var symbols = await _engine.ListSymbolsAsync(_fixture.SolutionPath);

        Assert.Contains(symbols, s => s == "SampleLibrary.PublicMethods.InstanceMethod");
        Assert.Contains(symbols, s => s == "SampleLibrary.PublicMethods.StaticMethod");
    }

    [Fact]
    public async Task ListSymbols_IncludesConstructors()
    {
        var symbols = await _engine.ListSymbolsAsync(_fixture.SolutionPath);

        Assert.Contains(symbols, s => s == "SampleLibrary.CtorsAndStatics..ctor");
    }

    [Fact]
    public async Task ListSymbols_IncludesProperties()
    {
        var symbols = await _engine.ListSymbolsAsync(_fixture.SolutionPath);

        Assert.Contains(symbols, s => s == "SampleLibrary.FieldsAndProperties.AutoProperty");
    }

    [Fact]
    public async Task ListSymbols_IncludesFields()
    {
        var symbols = await _engine.ListSymbolsAsync(_fixture.SolutionPath);

        Assert.Contains(symbols, s => s == "SampleLibrary.FieldsAndProperties.InstanceField");
        Assert.Contains(symbols, s => s == "SampleLibrary.FieldsAndProperties.StaticField");
    }

    [Fact]
    public async Task ListSymbols_ExcludesCompilerGenerated()
    {
        var symbols = await _engine.ListSymbolsAsync(_fixture.SolutionPath);

        Assert.DoesNotContain(symbols, s => s.Contains("k__BackingField"));
        Assert.DoesNotContain(symbols, s => s.Contains("BeginInvoke"));
        Assert.DoesNotContain(symbols, s => s.Contains("EndInvoke"));
    }

    [Fact]
    public async Task ListSymbols_IncludesGenericTypesWithArity()
    {
        var symbols = await _engine.ListSymbolsAsync(_fixture.SolutionPath);

        Assert.Contains(symbols, s => s.Contains("GenericClass<TKey, TValue>"));
    }

    #endregion

    #region GetCallers

    [Fact]
    public async Task GetCallers_InstanceMethod_ReturnsCallers()
    {
        var result = await _engine.GetCallersAsync(
            _fixture.SolutionPath,
            "SampleLibrary.PublicMethods.InstanceMethod");

        Assert.Equal(CallDirection.Callers, result.Direction);
        Assert.NotEmpty(result.Roots);

        var names = result.Roots.Select(r => r.Symbol.Name).ToList();
        Assert.Contains("CallInstanceMethod", names);
        Assert.Contains("ExtraCaller", names);
    }

    [Fact]
    public async Task GetCallers_StaticMethod_ReturnsCallers()
    {
        var result = await _engine.GetCallersAsync(
            _fixture.SolutionPath,
            "SampleLibrary.PublicMethods.StaticMethod");

        Assert.NotEmpty(result.Roots);

        var names = result.Roots.Select(r => r.Symbol.Name).ToList();
        Assert.Contains("InstanceMethod", names);
        Assert.Contains("CallStaticMethod", names);
    }

    [Fact]
    public async Task GetCallers_WithDepth1_ReturnsDirectCallersOnly()
    {
        var result = await _engine.GetCallersAsync(
            _fixture.SolutionPath,
            "SampleLibrary.PublicMethods.InstanceMethod",
            maxDepth: 1);

        Assert.NotEmpty(result.Roots);
        Assert.All(result.Roots, r => Assert.Empty(r.Children));
    }

    [Fact]
    public async Task GetCallers_CallSites_HaveFileAndLine()
    {
        var result = await _engine.GetCallersAsync(
            _fixture.SolutionPath,
            "SampleLibrary.PublicMethods.InstanceMethod");

        foreach (var root in result.Roots)
        {
            Assert.NotEmpty(root.CallSites);
            Assert.All(root.CallSites, cs =>
            {
                Assert.NotEmpty(cs.FilePath);
                Assert.True(cs.LineNumber >= 0);
                Assert.True(cs.Column >= 0);
                Assert.True(cs.EndLineNumber >= cs.LineNumber);
                Assert.True(cs.EndColumn >= 0);
            });
        }
    }

    [Fact]
    public async Task GetCallers_UnknownSymbol_ThrowsSymbolNotFound()
    {
        var ex = await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => _engine.GetCallersAsync(_fixture.SolutionPath, "Does.Not.Exist"));

        Assert.Equal("Does.Not.Exist", ex.SymbolName);
    }

    #endregion

    #region GetCallees

    [Fact]
    public async Task GetCallees_MethodWithCalls_ReturnsCallees()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleLibrary.PublicMethods.InstanceMethod");

        Assert.Equal(CallDirection.Callees, result.Direction);
        Assert.NotEmpty(result.Roots);

        var names = result.Roots.Select(r => r.Symbol.Name).ToList();
        Assert.Contains("PrivateMethod", names);
    }

    [Fact]
    public async Task GetCallees_EntryPoint_ReturnsAllCallees()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleConsoleApp.Callers.RunAll");

        Assert.NotEmpty(result.Roots);
        var names = result.Roots.Select(r => r.Symbol.Name).ToList();
        Assert.Contains("CallInstanceMethod", names);
        Assert.Contains("CallStaticMethod", names);
        Assert.Contains("CallInheritance", names);
        Assert.Contains("CallConstructors", names);
        Assert.Contains("CallDelegates", names);
        Assert.Contains("CallGenerics", names);
    }

    [Fact]
    public async Task GetCallees_WithDepth1_ReturnsDirectCalleesOnly()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleConsoleApp.Callers.RunAll",
            maxDepth: 1);

        Assert.NotEmpty(result.Roots);
        Assert.All(result.Roots, r => Assert.Empty(r.Children));
    }

    [Fact]
    public async Task GetCallees_TransitiveClosure_ReturnsNestedCalls()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleConsoleApp.Callers.CallInstanceMethod");

        var allNames = FlattenNames(result.Roots);
        Assert.Contains("InstanceMethod", allNames);
    }

    #endregion

    #region Specific Symbol Types

    [Fact]
    public async Task GetCallees_AsyncMethod_Resolves()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleLibrary.AsyncStuff.ComputeAsync");

        Assert.NotNull(result);
        Assert.Equal("ComputeAsync", result.Target.Name);
    }

    [Fact]
    public async Task GetCallers_Constructor_ReturnsNewExpressions()
    {
        var result = await _engine.GetCallersAsync(
            _fixture.SolutionPath,
            "SampleLibrary.CtorsAndStatics..ctor()",
            maxDepth: 1);

        Assert.NotEmpty(result.Roots);
    }

    [Fact]
    public async Task GetCallees_Constructor_Resolves()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleLibrary.CtorsAndStatics..ctor()",
            maxDepth: 1);

        Assert.NotNull(result);
        Assert.Equal(".ctor", result.Target.Name);
    }

    #endregion

    #region Constructor Chaining

    [Fact]
    public async Task GetCallees_ThisInitializer_RecordsChainedConstructor()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleLibrary.CtorsAndStatics..ctor()");

        Assert.Single(result.Roots);
        var root = result.Roots[0];
        Assert.Equal(".ctor", root.Symbol.Name);
        Assert.Equal("SampleLibrary.CtorsAndStatics", root.Symbol.ContainingType);
        Assert.Single(root.Symbol.Parameters);
        Assert.Equal("string", root.Symbol.Parameters[0].TypeName);
        Assert.Single(root.CallSites);
        Assert.EndsWith("CtorsAndStatics.cs", root.CallSites[0].FilePath);
    }

    [Fact]
    public async Task GetCallers_ThisInitializer_ReturnsChainingConstructor()
    {
        var result = await _engine.GetCallersAsync(
            _fixture.SolutionPath,
            "SampleLibrary.CtorsAndStatics..ctor(string)");

        Assert.Contains(result.Roots, r =>
            r.Symbol.Name == ".ctor" &&
            r.Symbol.ContainingType == "SampleLibrary.CtorsAndStatics" &&
            r.Symbol.Parameters.Count == 0);
    }

    [Fact]
    public async Task GetCallees_BaseInitializer_RecordsBaseConstructor()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleLibrary.DerivedClass..ctor()");

        Assert.Contains(result.Roots, r =>
            r.Symbol.Name == ".ctor" &&
            r.Symbol.ContainingType == "SampleLibrary.BaseClass" &&
            r.Symbol.Parameters.Count == 1 &&
            r.Symbol.Parameters[0].TypeName == "string");
    }

    [Fact]
    public async Task GetCallees_RecordPrimaryConstructor_RecordsBaseConstructor()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleLibrary.DerivedRecord..ctor(string)");

        Assert.Single(result.Roots);
        var root = result.Roots[0];
        Assert.Equal(".ctor", root.Symbol.Name);
        Assert.Equal("SampleLibrary.BaseRecord", root.Symbol.ContainingType);
        Assert.Single(root.Symbol.Parameters);
        Assert.Equal("string", root.Symbol.Parameters[0].TypeName);
        Assert.Single(root.CallSites);
        Assert.EndsWith("Records.cs", root.CallSites[0].FilePath);
    }

    [Fact]
    public async Task GetCallers_BaseConstructor_ReturnsDerivedCtorInitializer()
    {
        var result = await _engine.GetCallersAsync(
            _fixture.SolutionPath,
            "SampleLibrary.BaseClass..ctor(string)");

        Assert.Contains(result.Roots, r =>
            r.Symbol.Name == ".ctor" &&
            r.Symbol.ContainingType == "SampleLibrary.DerivedClass");
    }

    #endregion

    #region Mixed Call Forms

    [Fact]
    public async Task GetCallers_StaticClassMethod_ReturnsCallers()
    {
        var result = await _engine.GetCallersAsync(
            _fixture.SolutionPath,
            "SampleLibrary.StaticClass.Increment");

        Assert.NotEmpty(result.Roots);
        var names = result.Roots.Select(r => r.Symbol.Name).ToList();
        Assert.Contains("CallStaticClass", names);
    }

    [Fact]
    public async Task GetCallees_LocalFunction_Resolves()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleLibrary.LambdasAndDelegates.LocalFunctionExample");

        var names = FlattenNames(result.Roots);
        Assert.Contains("Multiply", names);
    }

    [Fact]
    public async Task GetCallees_GenericMethod_Resolves()
    {
        var result = await _engine.GetCalleesAsync(
            _fixture.SolutionPath,
            "SampleLibrary.GenericMethods.Swap<int>");

        Assert.NotNull(result);
        Assert.Equal("Swap", result.Target.Name);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Engine_NonexistentSolution_ThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _engine.ListSymbolsAsync("C:\\nonexistent\\path\\Foo.sln"));
    }

    [Fact]
    public async Task Engine_NonexistentSymbol_ThrowsSymbolNotFound()
    {
        var ex = await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => _engine.GetCalleesAsync(_fixture.SolutionPath, "SampleLibrary.NonexistentClass.NonexistentMethod"));
        Assert.Equal("SampleLibrary.NonexistentClass.NonexistentMethod", ex.SymbolName);
    }

    [Fact]
    public async Task Engine_CancelledFirstCall_DoesNotPoisonSolutionCache()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _engine.ListSymbolsAsync(_fixture.SolutionPath, cts.Token));

        var symbols = await _engine.ListSymbolsAsync(_fixture.SolutionPath);
        Assert.NotEmpty(symbols);
    }

    #endregion

    private static List<string> FlattenNames(List<CallGraphNode> nodes)
    {
        var results = new List<string>();
        foreach (var node in nodes)
        {
            results.Add(node.Symbol.Name);
            results.AddRange(FlattenNames(node.Children));
        }
        return results;
    }
}
