using System.Text.Json.Serialization;

namespace CsCallGraph.LanguageServer;

public class JsonRpcMessage
{
    [JsonPropertyName("jsonrpc")] public string Jsonrpc { get; set; } = "2.0";

    [JsonPropertyName("id")] public JsonRpcId? Id { get; set; }

    [JsonPropertyName("method")] public string? Method { get; set; }

    [JsonPropertyName("params")] public System.Text.Json.JsonElement? Params { get; set; }

    [JsonPropertyName("result")] public System.Text.Json.JsonElement? Result { get; set; }

    [JsonPropertyName("error")] public JsonRpcError? Error { get; set; }
}

public class JsonRpcId
{
    public long? IntVal { get; set; }
    public string? StrVal { get; set; }

    public static implicit operator JsonRpcId(long id) => new() { IntVal = id };
    public static implicit operator JsonRpcId(string id) => new() { StrVal = id };
}

public class JsonRpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }

    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

public class InitializeParams
{
    [JsonPropertyName("processId")] public int? ProcessId { get; set; }

    [JsonPropertyName("rootUri")] public string? RootUri { get; set; }

    [JsonPropertyName("capabilities")] public System.Text.Json.JsonElement? Capabilities { get; set; }
}

public class InitializeResult
{
    [JsonPropertyName("capabilities")] public ServerCapabilities Capabilities { get; set; } = new();
}

public class ServerCapabilities
{
    [JsonPropertyName("textDocumentSync")]
    public int TextDocumentSync { get; set; } = 1; // Full

    [JsonPropertyName("callHierarchyProvider")]
    public bool CallHierarchyProvider { get; set; } = true;
}

public class TextDocumentItem
{
    [JsonPropertyName("uri")] public string Uri { get; set; } = "";

    [JsonPropertyName("languageId")] public string LanguageId { get; set; } = "";

    [JsonPropertyName("version")] public int Version { get; set; }

    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

public class Position
{
    [JsonPropertyName("line")] public int Line { get; set; }

    [JsonPropertyName("character")] public int Character { get; set; }
}

public class Range
{
    [JsonPropertyName("start")] public Position Start { get; set; } = new();

    [JsonPropertyName("end")] public Position End { get; set; } = new();
}

public class CallHierarchyItem
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("kind")] public int Kind { get; set; }

    [JsonPropertyName("detail")] public string? Detail { get; set; }

    [JsonPropertyName("uri")] public string Uri { get; set; } = "";

    [JsonPropertyName("range")] public Range Range { get; set; } = new();

    [JsonPropertyName("selectionRange")] public Range SelectionRange { get; set; } = new();

    [JsonPropertyName("data")] public string? Data { get; set; }
}

public class CallHierarchyIncomingCall
{
    [JsonPropertyName("from")] public CallHierarchyItem From { get; set; } = new();

    [JsonPropertyName("fromRanges")] public Range[] FromRanges { get; set; } = [];
}

public class CallHierarchyOutgoingCall
{
    [JsonPropertyName("to")] public CallHierarchyItem To { get; set; } = new();

    [JsonPropertyName("fromRanges")] public Range[] FromRanges { get; set; } = [];
}
