using CsCallGraph.Core;
using CsCallGraph.Core.Models;
using CsCallGraph.Cli.Output;

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (cliArgs.Length == 0)
{
    PrintUsage();
    return 0;
}

var command = cliArgs[0].ToLowerInvariant();

switch (command)
{
    case "callers":
    case "callees":
        return await RunAnalysisCommand(command, cliArgs[1..]);
    case "list-symbols":
        return await RunListSymbolsCommand(cliArgs[1..]);
    case "--help":
    case "-h":
    case "-?":
        PrintUsage();
        return 0;
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 3;
}

static int PrintUsage()
{
    Console.Error.WriteLine("Usage: cs-call-graph <command> [options]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  callers       Show who calls the specified symbol");
    Console.Error.WriteLine("  callees       Show what the specified symbol calls");
    Console.Error.WriteLine("  list-symbols  List all callable symbols in the solution");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Global options:");
    Console.Error.WriteLine("  --solution <path>       Path to the solution file (required)");
    Console.Error.WriteLine("  --symbol <name>         Fully qualified symbol name");
    Console.Error.WriteLine("  --symbol-at <file:ln:col>  Resolve symbol from source location");
    Console.Error.WriteLine("  --scope <scope>         Search scope: solution (default), project, project-with-dependencies");
    Console.Error.WriteLine("  --output <format>       Output format: tree (default) or json");
    Console.Error.WriteLine("  --depth <n>             Max depth (default: 10, 0 = unlimited)");
    return 0;
}

static async Task<CallGraphResult> RunAnalysisByNameAsync(CallGraphEngine engine, string command, string solution, string symbol, int depth, SearchScope scope)
{
    return command == "callers"
        ? await engine.GetCallersAsync(solution, symbol, depth, scope)
        : await engine.GetCalleesAsync(solution, symbol, depth, scope);
}

static async Task<CallGraphResult> RunAnalysisAtAsync(CallGraphEngine engine, string command, string solution, string symbolAt, int depth, SearchScope scope)
{
    var parts = symbolAt.Split(':');
    if (parts.Length < 3 || !int.TryParse(parts[^2], out var line) || !int.TryParse(parts[^1], out var col))
        throw new InvalidOperationException("--symbol-at must be in format <filepath>:<line>:<col>");

    var filePath = string.Join(":", parts[..^2]);
    // Roslyn uses 0-based line/column; CLI input is 1-based
    line--;
    col--;

    return command == "callers"
        ? await engine.GetCallersAtAsync(solution, filePath, line, col, depth, scope)
        : await engine.GetCalleesAtAsync(solution, filePath, line, col, depth, scope);
}

static string? GetOption(IReadOnlyList<string> args, string longName, string? shortName)
{
    for (int i = 0; i < args.Count; i++)
    {
        if (args[i] == longName || (shortName != null && args[i] == shortName))
        {
            if (i + 1 < args.Count)
                return args[i + 1];
        }
    }
    return null;
}

static async Task<int> RunAnalysisCommand(string command, string[] cmdArgs)
{
    var solution = GetOption(cmdArgs, "--solution", "-s");
    var symbol = GetOption(cmdArgs, "--symbol", "-m");
    var symbolAt = GetOption(cmdArgs, "--symbol-at", null);
    var depthStr = GetOption(cmdArgs, "--depth", "-d");
    var output = GetOption(cmdArgs, "--output", "-o") ?? "tree";
    var scopeStr = GetOption(cmdArgs, "--scope", null);

    if (solution == null)
    {
        Console.Error.WriteLine("Error: --solution is required");
        return 3;
    }
    if (symbol == null && symbolAt == null)
    {
        Console.Error.WriteLine("Error: --symbol or --symbol-at is required");
        return 3;
    }
    if (symbol != null && symbolAt != null)
    {
        Console.Error.WriteLine("Error: --symbol and --symbol-at cannot be used together");
        return 3;
    }

    var depth = 10;
    if (depthStr != null && !int.TryParse(depthStr, out depth))
    {
        Console.Error.WriteLine("Error: --depth must be a number");
        return 3;
    }

    SearchScope scope;
    if (scopeStr == null)
    {
        scope = SearchScope.Solution;
    }
    else
    {
        scope = scopeStr.ToLowerInvariant() switch
        {
            "project" => SearchScope.Project,
            "project-with-dependencies" => SearchScope.ProjectWithDependencies,
            "solution" => SearchScope.Solution,
            _ => (SearchScope)(-1), // invalid sentinel
        };
        if ((int)scope < 0)
        {
            Console.Error.WriteLine($"Error: unrecognized --scope value '{scopeStr}'. Valid values: solution, project, project-with-dependencies");
            return 3;
        }
    }

    IOutputFormatter formatter = output.ToLowerInvariant() switch
    {
        "json" => new JsonFormatter(),
        "tree" => new TreeFormatter(),
        _ => null!,
    };
    if (formatter == null)
    {
        Console.Error.WriteLine($"Error: unrecognized --output value '{output}'. Valid values: tree, json");
        return 3;
    }

    try
    {
        using var engine = new CallGraphEngine();
        var result = symbolAt != null
            ? await RunAnalysisAtAsync(engine, command, solution, symbolAt, depth, scope)
            : await RunAnalysisByNameAsync(engine, command, solution, symbol!, depth, scope);

        Console.WriteLine(formatter.Format(result));
        return 0;
    }
    catch (AmbiguousSymbolException ex)
    {
        WriteError("AMBIGUOUS_SYMBOL", ex.Message, new { symbol = ex.SymbolName });
        return 1;
    }
    catch (SymbolNotFoundException ex)
    {
        WriteError("SYMBOL_NOT_FOUND", ex.Message, new { symbol = ex.SymbolName });
        return 1;
    }
    catch (FileNotFoundException ex)
    {
        WriteError("SOLUTION_NOT_FOUND", ex.Message, new { path = ex.FileName });
        return 2;
    }
    catch (SolutionLoadFailedException ex)
    {
        WriteError("SOLUTION_LOAD_FAILED", ex.Message, new { path = ex.SolutionPath });
        return 2;
    }
    catch (Exception ex)
    {
        WriteError("INTERNAL_ERROR", ex.Message);
        return 2;
    }
}

static async Task<int> RunListSymbolsCommand(string[] cmdArgs)
{
    var solution = GetOption(cmdArgs, "--solution", "-s");
    if (solution == null)
    {
        Console.Error.WriteLine("Error: --solution is required");
        return 3;
    }

    try
    {
        using var engine = new CallGraphEngine();
        var symbols = await engine.ListSymbolsAsync(solution);
        foreach (var s in symbols.OrderBy(x => x))
            Console.WriteLine(s);
        return 0;
    }
    catch (FileNotFoundException ex)
    {
        WriteError("SOLUTION_NOT_FOUND", ex.Message, new { path = ex.FileName });
        return 2;
    }
    catch (SolutionLoadFailedException ex)
    {
        WriteError("SOLUTION_LOAD_FAILED", ex.Message, new { path = ex.SolutionPath });
        return 2;
    }
    catch (Exception ex)
    {
        WriteError("INTERNAL_ERROR", ex.Message);
        return 2;
    }
}

static void WriteError(string code, string message, object? details = null)
{
    var error = new Dictionary<string, object?>
    {
        ["code"] = code,
        ["message"] = message,
    };
    if (details != null)
        error["details"] = details;

    var payload = new Dictionary<string, object?> { ["error"] = error };
    Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}
