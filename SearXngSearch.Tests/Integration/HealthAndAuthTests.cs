using System.Net;
using System.Text.Json;
using SearXngSearch.Tests.TestHelpers;
using Xunit;

namespace SearXngSearch.Tests.Integration;

public class HealthEndpointTests
{
    [Fact]
    public async Task Health_200_및_상태_JSON()
    {
        await using var factory = new TestServerFactory();
        var http = factory.CreateClient();

        var response = await http.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("searxng-search-mcp-server", doc.RootElement.GetProperty("service").GetString());
    }
}

public class BearerAuthTests
{
    [Fact]
    public async Task 토큰_설정_시_무토큰_MCP_요청_401()
    {
        await using var factory = new AuthenticatedTestServerFactory();
        var http = factory.CreateClient();
        McpAccept(http);

        var response = await http.PostAsync("/mcp", JsonRpc("initialize"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task 토큰_설정_시_잘못된_토큰_401()
    {
        await using var factory = new AuthenticatedTestServerFactory();
        var http = factory.CreateClient();
        McpAccept(http);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong-token");

        var response = await http.PostAsync("/mcp", JsonRpc("initialize"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 토큰_설정_시_올바른_토큰_200()
    {
        await using var factory = new AuthenticatedTestServerFactory();
        var http = factory.CreateClient();
        McpAccept(http);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");

        var response = await http.PostAsync("/mcp", JsonRpc("initialize"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task 토큰_설정_시_Health_는_항상_열림()
    {
        await using var factory = new AuthenticatedTestServerFactory();
        var http = factory.CreateClient();

        var response = await http.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>MCP 엔드포인트는 Accept: application/json, text/event-stream을 요구합니다.</summary>
    private static void McpAccept(HttpClient http) =>
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");

    private static StringContent JsonRpc(string method) => new(
        $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}"}""",
        System.Text.Encoding.UTF8, "application/json");
}
