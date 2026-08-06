using System.Text.Json;
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

public class JsonRpcIdConverter : JsonConverter<JsonRpcId>
{
    public override JsonRpcId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new JsonRpcId { StrVal = reader.GetString() };

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out var intVal))
                return new JsonRpcId { IntVal = intVal };
            throw new JsonException("JSON-RPC id must be an integer or a string.");
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for JSON-RPC id.");
    }

    public override void Write(Utf8JsonWriter writer, JsonRpcId value, JsonSerializerOptions options)
    {
        if (value.IntVal.HasValue) writer.WriteNumberValue(value.IntVal.Value);
        else if (value.StrVal != null) writer.WriteStringValue(value.StrVal);
        else writer.WriteNullValue();
    }
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
    public int TextDocumentSync { get; set; } // 0 = None (server resolves from disk; no didOpen/didChange)

    [JsonPropertyName("callHierarchyProvider")]
    public bool CallHierarchyProvider { get; set; } = true;
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
