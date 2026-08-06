using System.Text.Json;
using System.Text.Json.Serialization;
using CsCallGraph.Core.Models;

namespace CsCallGraph.Cli.Output;

public class JsonFormatter : IOutputFormatter
{
    public string Format(CallGraphResult result)
    {
        var json = new JsonResult
        {
            Target = MapDescriptor(result.Target, result.Direction),
            Roots = result.Roots.Select(MapNode).ToList(),
        };

        return JsonSerializer.Serialize(json, _options);
    }

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static JsonTarget MapDescriptor(SymbolDescriptor desc, CallDirection dir)
    {
        var locations = desc.IdentifierLocations.Count > 0
            ? desc.IdentifierLocations
            : desc.DeclarationLocations;

        return new JsonTarget
        {
            Name = desc.Name,
            FullyQualifiedName = desc.FullyQualifiedName,
            ContainingType = desc.ContainingType,
            ContainingNamespace = desc.ContainingNamespace,
            Kind = desc.Kind,
            IsStatic = desc.IsStatic,
            Arity = desc.Arity,
            Parameters = desc.Parameters.Select(p => new JsonParam
            {
                Name = p.Name,
                TypeName = p.TypeName,
                IsRef = p.IsRef,
                IsOut = p.IsOut,
            }).ToList(),
            DeclarationLocations = locations.Select(l => new JsonLocation
            {
                File = l.FilePath,
                Line = l.LineNumber + 1,
                Column = l.Column + 1,
            }).ToList(),
            DisplayString = desc.DisplayString,
            Direction = dir,
        };
    }

    private static JsonNode MapNode(CallGraphNode node)
    {
        return new JsonNode
        {
            Symbol = node.Symbol.Name,
            DisplayString = node.Symbol.DisplayString,
            ContainingType = node.Symbol.ContainingType,
            Kind = node.Symbol.Kind,
            IsStatic = node.Symbol.IsStatic,
            CallCount = node.CallCount,
            CallSites = node.CallSites.Select(s => new JsonLocation
            {
                File = s.FilePath,
                Line = s.LineNumber + 1,
                Column = s.Column + 1,
            }).ToList(),
            Children = node.Children.Select(MapNode).ToList(),
        };
    }

    private record JsonResult
    {
        public JsonTarget Target { get; init; } = null!;
        public List<JsonNode> Roots { get; init; } = [];
    }

    private record JsonTarget
    {
        public string Name { get; init; } = "";
        public string FullyQualifiedName { get; init; } = "";
        public string ContainingType { get; init; } = "";
        public string ContainingNamespace { get; init; } = "";
        public SymbolKind Kind { get; init; }
        public bool IsStatic { get; init; }
        public int Arity { get; init; }
        public List<JsonParam> Parameters { get; init; } = [];
        public List<JsonLocation> DeclarationLocations { get; init; } = [];
        public string DisplayString { get; init; } = "";
        public CallDirection Direction { get; init; }
    }

    private record JsonNode
    {
        public string Symbol { get; init; } = "";
        public string DisplayString { get; init; } = "";
        public string ContainingType { get; init; } = "";
        public SymbolKind Kind { get; init; }
        public bool IsStatic { get; init; }
        public int CallCount { get; init; }
        public List<JsonLocation> CallSites { get; init; } = [];
        public List<JsonNode> Children { get; init; } = [];
    }

    private record JsonParam
    {
        public string Name { get; init; } = "";
        public string TypeName { get; init; } = "";
        public bool IsRef { get; init; }
        public bool IsOut { get; init; }
    }

    private record JsonLocation
    {
        public string File { get; init; } = "";
        public int Line { get; init; }
        public int Column { get; init; }
    }
}
