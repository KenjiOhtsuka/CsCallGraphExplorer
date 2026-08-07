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

        var item = ToLspItem(desc);
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
        var calls = result.Roots.Select(ToIncomingCall).OfType<CallHierarchyIncomingCall>().ToArray();
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
        var calls = result.Roots.Select(ToOutgoingCall).OfType<CallHierarchyOutgoingCall>().ToArray();
        return new JsonRpcMessage
        {
            Id = msg.Id,
            Result = JsonSerializer.SerializeToElement(calls, JsonOpts),
        };
    }

    private static CallHierarchyItem? ToLspItem(SymbolDescriptor? desc)
    {
        if (desc == null) return null;

        var declaration = desc.DeclarationLocations.FirstOrDefault();
        if (declaration == null) return null;

        var selection = desc.IdentifierLocations.Count > 0
            ? desc.IdentifierLocations[0]
            : declaration;

        return new CallHierarchyItem
        {
            Name = desc.DisplayString,
            Kind = ToLspSymbolKind(desc.Kind),
            Detail = desc.FullyQualifiedName,
            Uri = PathToUri(declaration.FilePath),
            Range = ToRange(declaration),
            SelectionRange = ToRange(selection),
            Data = desc.FullyQualifiedName,
        };
    }

    private static CallHierarchyIncomingCall? ToIncomingCall(CallGraphNode node)
    {
        var from = ToLspItem(node.Symbol);
        if (from == null) return null;

        return new CallHierarchyIncomingCall
        {
            From = from,
            FromRanges = node.CallSites.Select(ToRange).ToArray(),
        };
    }

    private static CallHierarchyOutgoingCall? ToOutgoingCall(CallGraphNode node)
    {
        var to = ToLspItem(node.Symbol);
        if (to == null) return null;

        return new CallHierarchyOutgoingCall
        {
            To = to,
            FromRanges = node.CallSites.Select(ToRange).ToArray(),
        };
    }

    private static Range ToRange(CallSite site) => new()
    {
        Start = new Position { Line = site.LineNumber, Character = site.Column },
        End = new Position { Line = site.EndLineNumber, Character = site.EndColumn },
    };

    private static int ToLspSymbolKind(SymbolKind kind) => kind switch
    {
        Core.Models.SymbolKind.Method => 6,
        Core.Models.SymbolKind.Constructor => 9,
        Core.Models.SymbolKind.Property => 7,
        Core.Models.SymbolKind.Field => 8,
        Core.Models.SymbolKind.Event => 24,
        Core.Models.SymbolKind.Indexer => 7,
        Core.Models.SymbolKind.Operator => 25,
        Core.Models.SymbolKind.Lambda => 6,
        Core.Models.SymbolKind.LocalFunction => 6,
        _ => 6,
    };

    private static string UriToPath(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var uriObj) || !uriObj.IsFile)
            return uri;
        // LocalPath on Windows mishandles the percent-encoded drive letter
        // produced by VS Code (file:///c%3A/...) and returns a path with a
        // leading slash, so derive the path from AbsolutePath instead.
        var path = Uri.UnescapeDataString(uriObj.AbsolutePath);
        if (path.Length >= 3 && path[2] == ':' && path[0] == '/')
            path = path.Substring(1);
        return path.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string PathToUri(string path) =>
        string.IsNullOrEmpty(path) ? "" : new Uri(path).AbsoluteUri;

    private static JsonRpcMessage Result(JsonRpcId? id, object value) =>
        new() { Id = id, Result = JsonSerializer.SerializeToElement(value, JsonOpts) };

    private static JsonRpcMessage Error(JsonRpcId? id, int code, string message) =>
        new() { Id = id, Error = new JsonRpcError { Code = code, Message = message } };

    public void Dispose()
    {
        _engine.Dispose();
    }
}
