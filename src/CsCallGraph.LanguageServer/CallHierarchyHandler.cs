using System.Text.Json;
using CsCallGraph.Core;
using CsCallGraph.Core.Models;

namespace CsCallGraph.LanguageServer;

public class CallHierarchyHandler : IDisposable
{
    private readonly CallGraphEngine _engine;
    private readonly string _solutionPath;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public CallHierarchyHandler(CallGraphEngine engine, string solutionPath)
    {
        _engine = engine;
        _solutionPath = solutionPath;
    }

    public JsonRpcMessage? PrepareCallHierarchy(JsonRpcMessage msg)
    {
        if (msg.Params == null) return Error(msg.Id, -32602, "Missing params");
        var txtDoc = msg.Params.Value.GetProperty("textDocument");
        var position = msg.Params.Value.GetProperty("position");
        var uri = txtDoc.GetProperty("uri").GetString() ?? "";
        var line = position.GetProperty("line").GetInt32();
        var character = position.GetProperty("character").GetInt32();

        var filePath = UriToPath(uri);
        var desc = _engine.ResolveSymbolAtAsync(_solutionPath, filePath, line, character).GetAwaiter().GetResult();

        var item = ToLspItem(desc, uri);
        var items = item != null ? new[] { item } : [];
        return new JsonRpcMessage
        {
            Id = msg.Id,
            Result = JsonSerializer.SerializeToElement(items, JsonOpts),
        };
    }

    public JsonRpcMessage? IncomingCalls(JsonRpcMessage msg)
    {
        if (msg.Params == null) return Error(msg.Id, -32602, "Missing params");
        var item = msg.Params.Value.GetProperty("item");
        var symbol = item.GetProperty("data").GetString();
        if (symbol == null) return Result(msg.Id, Array.Empty<CallHierarchyIncomingCall>());

        var result = _engine.GetCallersAsync(_solutionPath, symbol).GetAwaiter().GetResult();
        var calls = result.Roots.Select(n => ToIncomingCall(n, item)).ToArray();
        return new JsonRpcMessage
        {
            Id = msg.Id,
            Result = JsonSerializer.SerializeToElement(calls, JsonOpts),
        };
    }

    public JsonRpcMessage? OutgoingCalls(JsonRpcMessage msg)
    {
        if (msg.Params == null) return Error(msg.Id, -32602, "Missing params");
        var item = msg.Params.Value.GetProperty("item");
        var symbol = item.GetProperty("data").GetString();
        if (symbol == null) return Result(msg.Id, Array.Empty<CallHierarchyOutgoingCall>());

        var result = _engine.GetCalleesAsync(_solutionPath, symbol).GetAwaiter().GetResult();
        var calls = result.Roots.Select(n => ToOutgoingCall(n, item)).ToArray();
        return new JsonRpcMessage
        {
            Id = msg.Id,
            Result = JsonSerializer.SerializeToElement(calls, JsonOpts),
        };
    }

    private static CallHierarchyItem? ToLspItem(SymbolDescriptor? desc, string uri)
    {
        if (desc == null) return null;
        return new CallHierarchyItem
        {
            Name = desc.DisplayString,
            Kind = ToLspSymbolKind(desc.Kind),
            Detail = desc.FullyQualifiedName,
            Uri = uri,
            Range = new Range
            {
                Start = new Position(),
                End = new Position(),
            },
            SelectionRange = new Range
            {
                Start = new Position(),
                End = new Position(),
            },
            Data = desc.FullyQualifiedName,
        };
    }

    private static CallHierarchyIncomingCall ToIncomingCall(CallGraphNode node, System.Text.Json.JsonElement parentItem)
    {
        var file = node.CallSites.FirstOrDefault()?.FilePath ?? "";
        var line = node.CallSites.FirstOrDefault()?.LineNumber ?? 0;
        var col = node.CallSites.FirstOrDefault()?.Column ?? 0;

        return new CallHierarchyIncomingCall
        {
            From = new CallHierarchyItem
            {
                Name = node.Symbol.DisplayString,
                Kind = ToLspSymbolKind(node.Symbol.Kind),
                Detail = node.Symbol.FullyQualifiedName,
                Uri = PathToUri(file),
                Range = new Range { Start = new Position(), End = new Position() },
                SelectionRange = new Range { Start = new Position { Line = line, Character = col }, End = new Position { Line = line, Character = col } },
                Data = node.Symbol.FullyQualifiedName,
            },
            FromRanges = [new Range { Start = new Position { Line = line, Character = col }, End = new Position { Line = line, Character = col } }],
        };
    }

    private static CallHierarchyOutgoingCall ToOutgoingCall(CallGraphNode node, System.Text.Json.JsonElement parentItem)
    {
        var file = node.CallSites.FirstOrDefault()?.FilePath ?? "";
        var line = node.CallSites.FirstOrDefault()?.LineNumber ?? 0;
        var col = node.CallSites.FirstOrDefault()?.Column ?? 0;

        return new CallHierarchyOutgoingCall
        {
            To = new CallHierarchyItem
            {
                Name = node.Symbol.DisplayString,
                Kind = ToLspSymbolKind(node.Symbol.Kind),
                Detail = node.Symbol.FullyQualifiedName,
                Uri = PathToUri(file),
                Range = new Range { Start = new Position(), End = new Position() },
                SelectionRange = new Range { Start = new Position { Line = line, Character = col }, End = new Position { Line = line, Character = col } },
                Data = node.Symbol.FullyQualifiedName,
            },
            FromRanges = [new Range { Start = new Position { Line = line, Character = col }, End = new Position { Line = line, Character = col } }],
        };
    }

    private static int ToLspSymbolKind(SymbolKind kind) => kind switch
    {
        Core.Models.SymbolKind.Method => 6,
        Core.Models.SymbolKind.Constructor => 9,
        Core.Models.SymbolKind.Property => 9,
        Core.Models.SymbolKind.Field => 8,
        Core.Models.SymbolKind.Event => 10,
        Core.Models.SymbolKind.Indexer => 9,
        Core.Models.SymbolKind.Operator => 6,
        Core.Models.SymbolKind.Lambda => 6,
        Core.Models.SymbolKind.LocalFunction => 6,
        _ => 6,
    };

    private static string UriToPath(string uri) =>
        Uri.UnescapeDataString(uri.Replace("file:///", "").Replace('/', '\\'));

    private static string PathToUri(string path) =>
        new Uri(path).AbsoluteUri;

    private static JsonRpcMessage Result(JsonRpcId? id, object value) =>
        new() { Id = id, Result = JsonSerializer.SerializeToElement(value, JsonOpts) };

    private static JsonRpcMessage Error(JsonRpcId? id, int code, string message) =>
        new() { Id = id, Error = new JsonRpcError { Code = code, Message = message } };

    public void Dispose()
    {
        _engine.Dispose();
    }
}
