namespace CsCallGraph.Core.Models;

public enum CallDirection
{
    Callers,
    Callees
}

public enum SearchScope
{
    Solution,
    Project,
    ProjectWithDependencies
}

public enum SymbolKind
{
    Method,
    Constructor,
    Property,
    Field,
    Event,
    Indexer,
    Operator,
    Lambda,
    LocalFunction
}

public class ParameterInfo
{
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public bool IsRef { get; init; }
    public bool IsOut { get; init; }
}

public class CallSite
{
    public string FilePath { get; init; } = "";
    public int LineNumber { get; init; }
    public int Column { get; init; }
}

public class SymbolDescriptor
{
    public string Name { get; init; } = "";
    public string FullyQualifiedName { get; init; } = "";
    public string ContainingType { get; init; } = "";
    public string ContainingNamespace { get; init; } = "";
    public SymbolKind Kind { get; init; }
    public bool IsStatic { get; init; }
    public int Arity { get; init; }
    public List<ParameterInfo> Parameters { get; init; } = [];
    public List<CallSite> DeclarationLocations { get; init; } = [];
    public string DisplayString { get; init; } = "";
}

public class CallGraphNode
{
    public SymbolDescriptor Symbol { get; init; } = null!;
    public List<CallSite> CallSites { get; init; } = [];
    public int CallCount => CallSites.Count;
    public List<CallGraphNode> Children { get; init; } = [];
}

public class CallGraphResult
{
    public SymbolDescriptor Target { get; init; } = null!;
    public CallDirection Direction { get; init; }
    public List<CallGraphNode> Roots { get; init; } = [];
}
