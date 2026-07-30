using CsCallGraph.LanguageServer;

if (args.Length < 2 || args[0] != "--solution")
{
    Console.Error.WriteLine("Usage: CsCallGraph.LanguageServer --solution <path-to-sln>");
    return 1;
}

using var server = new LspServer(args[1]);
server.Run();
return 0;
