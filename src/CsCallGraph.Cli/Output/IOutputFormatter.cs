using CsCallGraph.Core.Models;

namespace CsCallGraph.Cli.Output;

public interface IOutputFormatter
{
    string Format(CallGraphResult result);
}
