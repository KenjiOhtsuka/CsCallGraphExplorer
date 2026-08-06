using System.Text.Json;
using Xunit;

namespace CsCallGraph.LanguageServer.Tests;

public class LspModelsTests
{
    [Fact]
    public void ServerCapabilities_AdvertisesTextDocumentSyncNone()
    {
        var result = new InitializeResult();
        var json = JsonSerializer.SerializeToElement(result);

        Assert.Equal(0, json.GetProperty("capabilities").GetProperty("textDocumentSync").GetInt32());
    }
}
