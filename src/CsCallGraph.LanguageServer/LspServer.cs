using System.Text.Json;
using System.Text.RegularExpressions;
using CsCallGraph.Core;

namespace CsCallGraph.LanguageServer;

public partial class LspServer : IDisposable
{
    private readonly CallHierarchyHandler _handler;
    private bool _running = true;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    static LspServer()
    {
        JsonOpts.Converters.Add(new JsonRpcIdConverter());
    }

    public LspServer(string solutionPath)
    {
        var engine = new CallGraphEngine();
        _handler = new CallHierarchyHandler(engine, solutionPath);
    }

    public void Run()
    {
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();

        while (_running)
        {
            var message = ReadMessage(stdin);
            if (message == null) break;

            var response = HandleMessage(message);
            if (response != null)
                WriteMessage(stdout, response);
        }
    }

    private JsonRpcMessage? ReadMessage(Stream stream)
    {
        var header = "";
        while (true)
        {
            var line = ReadLine(stream);
            if (line == null) return null;
            if (line == "") break;
            header += line + "\r\n";
        }

        var match = ContentLength().Match(header);
        if (!match.Success) return null;
        var length = int.Parse(match.Groups[1].Value);

        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = stream.Read(buffer, offset, length - offset);
            if (read <= 0) return null;
            offset += read;
        }

        var json = System.Text.Encoding.UTF8.GetString(buffer);
        return JsonSerializer.Deserialize<JsonRpcMessage>(json, JsonOpts);
    }

    private static string? ReadLine(Stream stream)
    {
        var bytes = new List<byte>();
        while (true)
        {
            var b = stream.ReadByte();
            if (b == -1) return bytes.Count > 0 ? System.Text.Encoding.UTF8.GetString(bytes.ToArray()) : null;
            if (b == '\r') continue;
            if (b == '\n') break;
            bytes.Add((byte)b);
        }
        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static void WriteMessage(Stream stream, JsonRpcMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOpts);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {bytes.Length}\r\n\r\n";
        var headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
        stream.Write(headerBytes);
        stream.Write(bytes);
        stream.Flush();
    }

    private JsonRpcMessage? HandleMessage(JsonRpcMessage msg)
    {
        if (msg.Method == null) return null;

        try
        {
            return msg.Method switch
            {
                "initialize" => HandleInitialize(msg),
                "shutdown" => HandleShutdown(msg),
                "exit" => HandleExit(msg),
                "textDocument/prepareCallHierarchy" => _handler.PrepareCallHierarchy(msg),
                "callHierarchy/incomingCalls" => _handler.IncomingCalls(msg),
                "callHierarchy/outgoingCalls" => _handler.OutgoingCalls(msg),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            return Error(msg.Id, -32603, $"Internal error: {ex.Message}");
        }
    }

    private JsonRpcMessage HandleInitialize(JsonRpcMessage msg)
    {
        var result = new InitializeResult();
        return new JsonRpcMessage
        {
            Id = msg.Id,
            Result = JsonSerializer.SerializeToElement(result, JsonOpts),
        };
    }

    private JsonRpcMessage HandleShutdown(JsonRpcMessage msg)
    {
        return new JsonRpcMessage { Id = msg.Id, Result = JsonSerializer.Deserialize<System.Text.Json.JsonElement>("null") };
    }

    private JsonRpcMessage? HandleExit(JsonRpcMessage msg)
    {
        _running = false;
        return null;
    }

    private static JsonRpcMessage Error(JsonRpcId? id, int code, string message)
    {
        return new JsonRpcMessage
        {
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message },
        };
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    [GeneratedRegex("Content-Length: (\\d+)")]
    private static partial Regex ContentLength();
}
