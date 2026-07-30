using System.Text;
using CsCallGraph.Core.Models;

namespace CsCallGraph.Cli.Output;

public class TreeFormatter : IOutputFormatter
{
    public string Format(CallGraphResult result)
    {
        var sb = new StringBuilder();
        var dir = result.Direction == CallDirection.Callers ? "Callers" : "Callees";
        sb.AppendLine($"{dir} of {result.Target.DisplayString}");

        if (result.Roots.Count > 0)
        {
            for (int i = 0; i < result.Roots.Count; i++)
                FormatNode(sb, result.Roots[i], "", i == result.Roots.Count - 1);
        }
        else
        {
            sb.AppendLine("  (none)");
        }

        return sb.ToString();
    }

    private static void FormatNode(StringBuilder sb, CallGraphNode node, string indent, bool isLast)
    {
        var prefix = isLast ? "└─ " : "├─ ";
        var staticTag = node.Symbol.IsStatic ? " (static)" : "";
        var kindIcon = node.Symbol.Kind switch
        {
            SymbolKind.Method => "M",
            SymbolKind.Constructor => "C",
            SymbolKind.Property => "P",
            SymbolKind.Field => "F",
            SymbolKind.Indexer => "I",
            SymbolKind.Operator => "O",
            SymbolKind.Lambda => "λ",
            SymbolKind.LocalFunction => "L",
            _ => "?",
        };

        var targetInfo = node.CallCount > 0
            ? $"  —  {node.CallCount} call site(s)"
            : "";

        sb.AppendLine($"{indent}{prefix}[{kindIcon}] {node.Symbol.DisplayString}{staticTag}{targetInfo}");

        foreach (var site in node.CallSites)
        {
            var filePart = Path.IsPathRooted(site.FilePath)
                ? GetRelativePath(site.FilePath)
                : site.FilePath;
            sb.AppendLine($"{indent}{(isLast ? "   " : "│  ")}  at {filePart}:{site.LineNumber + 1},{site.Column + 1}");
        }

        var childIndent = indent + (isLast ? "   " : "│  ");
        for (int i = 0; i < node.Children.Count; i++)
            FormatNode(sb, node.Children[i], childIndent, i == node.Children.Count - 1);
    }

    private static string GetRelativePath(string fullPath)
    {
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            if (fullPath.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
                return fullPath[(cwd.Length + 1)..];
        }
        catch { }
        return fullPath;
    }
}
