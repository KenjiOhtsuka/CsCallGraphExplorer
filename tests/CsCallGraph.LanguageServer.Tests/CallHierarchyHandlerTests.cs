using System.Text.Json;
using Xunit;

namespace CsCallGraph.LanguageServer.Tests;

[Collection("Handler")]
public class CallHierarchyHandlerTests
{
    private const string CallersFile = "file:///C:/Users/user/project/CsCallGraphExplorer/samples/SampleConsoleApp/Callers.cs";

    private readonly HandlerFixture _fixture;

    public CallHierarchyHandlerTests(HandlerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void PrepareCallHierarchy_UsesDeclarationFileAndPreciseRanges()
    {
        var response = _fixture.Handler.PrepareCallHierarchy(
            PrepareRequest(CallersFile, line: 37, character: 22));

        var items = Deserialize<CallHierarchyItem[]>(response);
        var item = Assert.Single(items);

        Assert.Equal("SampleLibrary.PublicMethods.StaticMethod(string)", item.Data);
        Assert.EndsWith("/SampleLibrary/PublicMethods.cs", item.Uri);

        AssertRange(item.Range, startLine: 10, startChar: 4, endLine: 13, endChar: 5);
        AssertRange(item.SelectionRange, startLine: 10, startChar: 23, endLine: 10, endChar: 35);
        ContainsWithin(item.Range, item.SelectionRange);
    }

    [Fact]
    public void PrepareCallHierarchy_DeclarationSpanCoversSelectionSpan()
    {
        var response = _fixture.Handler.PrepareCallHierarchy(
            PrepareRequest(CallersFile, line: 11, character: 18));

        var items = Deserialize<CallHierarchyItem[]>(response);
        var item = Assert.Single(items);

        Assert.Equal("SampleConsoleApp.Callers.RunAll()", item.Data);
        Assert.EndsWith("/SampleConsoleApp/Callers.cs", item.Uri);
        Assert.Equal(11, item.Range.Start.Line);
        Assert.Equal(4, item.Range.Start.Character);
        Assert.True(IsAfterOrEqual(item.Range.End, item.Range.Start));
        ContainsWithin(item.Range, item.SelectionRange);
    }

    [Fact]
    public void IncomingCalls_ItemsPointAtDeclarationsWithCallSiteRanges()
    {
        var response = _fixture.Handler.IncomingCalls(IncomingRequest(
            "SampleLibrary.PublicMethods.StaticMethod(string)"));

        var calls = Deserialize<CallHierarchyIncomingCall[]>(response);
        Assert.NotEmpty(calls);

        foreach (var call in calls)
        {
            Assert.NotEmpty(call.From.Uri);
            Assert.StartsWith("file:///", call.From.Uri);
            Assert.True(IsAfterOrEqual(call.From.Range.End, call.From.Range.Start));
            ContainsWithin(call.From.Range, call.From.SelectionRange);
            Assert.NotEmpty(call.FromRanges);
            Assert.All(call.FromRanges, r =>
            {
                Assert.True(r.Start.Line >= 0);
                Assert.True(IsAfterOrEqual(r.End, r.Start));
            });
        }

        var callStatic = calls.First(c => c.From.Name == "CallStaticMethod");
        Assert.EndsWith("/SampleConsoleApp/Callers.cs", callStatic.From.Uri);
    }

    [Fact]
    public void OutgoingCalls_ItemsPointAtCalleeDeclarations()
    {
        var response = _fixture.Handler.OutgoingCalls(OutgoingRequest(
            "SampleConsoleApp.Callers.RunAll"));

        var calls = Deserialize<CallHierarchyOutgoingCall[]>(response);
        Assert.NotEmpty(calls);

        foreach (var call in calls)
        {
            Assert.NotEmpty(call.To.Uri);
            Assert.StartsWith("file:///", call.To.Uri);
            Assert.True(IsAfterOrEqual(call.To.Range.End, call.To.Range.Start));
            ContainsWithin(call.To.Range, call.To.SelectionRange);
            Assert.NotEmpty(call.FromRanges);
            Assert.All(call.FromRanges, r =>
            {
                Assert.True(r.Start.Line >= 0);
                Assert.True(IsAfterOrEqual(r.End, r.Start));
            });
        }

        var internalCall = calls.First(c => c.To.Name == "CallInternal");
        Assert.EndsWith("/SampleLibrary/Internals.cs", internalCall.To.Uri);

        var ctor = calls.First(c => c.To.Kind == 9);
        ContainsWithin(ctor.To.Range, ctor.To.SelectionRange);
    }

    private static JsonRpcMessage PrepareRequest(string uri, int line, int character) =>
        new()
        {
            Id = (JsonRpcId)2,
            Params = JsonDocument.Parse(
                $"{{\"textDocument\":{{\"uri\":\"{uri}\"}},\"position\":{{\"line\":{line},\"character\":{character}}}}}").RootElement,
        };

    private static JsonRpcMessage IncomingRequest(string data) =>
        new()
        {
            Id = (JsonRpcId)3,
            Params = JsonDocument.Parse(
                $"{{\"item\":{{\"data\":\"{data}\"}}}}").RootElement,
        };

    private static JsonRpcMessage OutgoingRequest(string data) =>
        new()
        {
            Id = (JsonRpcId)4,
            Params = JsonDocument.Parse(
                $"{{\"item\":{{\"data\":\"{data}\"}}}}").RootElement,
        };

    private static T Deserialize<T>(JsonRpcMessage? response)
    {
        Assert.NotNull(response);
        Assert.NotNull(response.Result);
        return JsonSerializer.Deserialize<T>(response.Result!.Value.GetRawText())!;
    }

    private static void AssertRange(Range range, int startLine, int startChar, int endLine, int endChar)
    {
        Assert.Equal(startLine, range.Start.Line);
        Assert.Equal(startChar, range.Start.Character);
        Assert.Equal(endLine, range.End.Line);
        Assert.Equal(endChar, range.End.Character);
    }

    private static void ContainsWithin(Range outer, Range inner) =>
        Assert.True(IsContained(outer, inner),
            $"Expected selectionRange {inner} within range {outer}");

    private static bool IsContained(Range outer, Range inner) =>
        IsAfterOrEqual(inner.Start, outer.Start) && IsAfterOrEqual(outer.End, inner.End);

    private static bool IsAfterOrEqual(Position a, Position b) =>
        a.Line > b.Line || (a.Line == b.Line && a.Character >= b.Character);
}
