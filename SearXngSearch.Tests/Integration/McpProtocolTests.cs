using System.Text.Json;
using SearXngSearch.Tests.TestHelpers;
using Xunit;

namespace SearXngSearch.Tests.Integration;

/// <summary>
/// MCP Streamable HTTP 프로토콜 엔드포인트(/mcp) 통합 테스트.
/// raw JSON-RPC 요청으로 initialize → tools/list → tools/call 흐름을 검증합니다.
/// </summary>
public class McpProtocolTests : IAsyncLifetime
{
    private TestServerFactory _factory = null!;
    private McpTestClient _mcp = null!;

    public async Task InitializeAsync()
    {
        _factory = new TestServerFactory();
        _factory.Handler.SetJson("fake", SearXngFixtures.SearchJson, path: "/search");
        _factory.Handler.SetJson("fake", SearXngFixtures.ConfigDictCategoriesJson, path: "/config");
        _mcp = new McpTestClient(_factory.CreateClient());
        await _mcp.SendAsync("initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" },
        });
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Initialize_서버_정보_반환()
    {
        var (success, result, _, status) = await _mcp.SendAsync("initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" },
        });

        Assert.True(success);
        Assert.Equal(200, status);
        Assert.NotNull(result);
        Assert.Equal("searxng-search-mcp-server", result!.Value.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal("1.0.0", result.Value.GetProperty("serverInfo").GetProperty("version").GetString());
    }

    [Fact]
    public async Task ToolsList_5개_도구_노출()
    {
        var (success, result, _, _) = await _mcp.SendAsync("tools/list");

        Assert.True(success);
        var tools = result!.Value.GetProperty("tools");
        Assert.Equal(5, tools.GetArrayLength());

        var names = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!)
            .ToHashSet();

        Assert.Equal(
            new HashSet<string> { "web_search", "news_search", "check_searxng_status", "get_instance_info", "get_search_suggestions" },
            names);
    }

    [Fact]
    public async Task WebSearch_텍스트_포맷_결과()
    {
        var (success, result, error, _) = await _mcp.SendAsync("tools/call", new
        {
            name = "web_search",
            arguments = new { query = "test", maxResults = 5 },
        });

        Assert.True(success);
        Assert.Null(error);
        var text = McpTestClient.ExtractToolText(result);

        Assert.Contains("최상위 결과 (Direct Answer): 첫 번째 결과", text);
        Assert.Contains("https://example.com/1", text);
        Assert.Contains("결과 수: 2", text);
    }

    [Fact]
    public async Task WebSearch_JSON_포맷_결과()
    {
        var (success, result, _, _) = await _mcp.SendAsync("tools/call", new
        {
            name = "web_search",
            arguments = new { query = "test", outputFormat = "json" },
        });

        Assert.True(success);
        var text = McpTestClient.ExtractToolText(result);
        using var doc = JsonDocument.Parse(text);

        Assert.Equal("test", doc.RootElement.GetProperty("query").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("resultCount").GetInt32());
        Assert.Equal("첫 번째 결과", doc.RootElement.GetProperty("topResult").GetProperty("title").GetString());
    }

    [Fact]
    public async Task NewsSearch_결과_반환()
    {
        var (success, result, _, _) = await _mcp.SendAsync("tools/call", new
        {
            name = "news_search",
            arguments = new { query = "test" },
        });

        Assert.True(success);
        var text = McpTestClient.ExtractToolText(result);
        Assert.Contains("첫 번째 결과", text);
    }

    [Fact]
    public async Task CheckSearXngStatus_정상_연결_메시지()
    {
        var (success, result, _, _) = await _mcp.SendAsync("tools/call", new
        {
            name = "check_searxng_status",
        });

        Assert.True(success);
        var text = McpTestClient.ExtractToolText(result);
        Assert.Contains("정상적으로 연결되었습니다", text);
    }

    [Fact]
    public async Task GetInstanceInfo_버전_카테고리_반환()
    {
        var (success, result, _, _) = await _mcp.SendAsync("tools/call", new
        {
            name = "get_instance_info",
        });

        Assert.True(success);
        var text = McpTestClient.ExtractToolText(result);
        Assert.Contains("버전: 2026.9.23", text);
        Assert.Contains("general, news, it", text);
        Assert.Contains("활성화된 엔진: 2개", text);
    }

    [Fact]
    public async Task GetSearchSuggestions_제안어_반환()
    {
        var (success, result, _, _) = await _mcp.SendAsync("tools/call", new
        {
            name = "get_search_suggestions",
            arguments = new { query = "test" },
        });

        Assert.True(success);
        var text = McpTestClient.ExtractToolText(result);
        Assert.Contains("test query", text);
        Assert.Contains("test example", text);
    }

    [Fact]
    public async Task 존재_않는_도구_호출_시_JSONRPC_에러()
    {
        var (_, result, error, _) = await _mcp.SendAsync("tools/call", new
        {
            name = "no_such_tool",
        });

        // SDK는 200 + JSON-RPC error 객체를 반환합니다.
        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task SearXNG_다운_시_web_search는_SDK_에러_결과_반환()
    {
        // 새 팩토리: SearXNG 응답 미등록(=인스턴스 미실행)
        await using var factory = new TestServerFactory();
        var mcp = new McpTestClient(factory.CreateClient());

        var (success, result, _, _) = await mcp.SendAsync("tools/call", new
        {
            name = "web_search",
            arguments = new { query = "test" },
        });

        // web_search는 예외를 잡지 않아 SDK가 isError 도구 결과로 감싸 반환합니다.
        Assert.True(success);
        var text = McpTestClient.ExtractToolText(result);
        Assert.Contains("An error occurred invoking 'web_search'", text);
    }

    [Fact]
    public async Task SearXNG_다운_시_check_searxng_status는_친절_한_오류_텍스트_반환()
    {
        // 새 팩토리: SearXNG 응답 미등록(=인스턴스 미실행)
        await using var factory = new TestServerFactory();
        var mcp = new McpTestClient(factory.CreateClient());

        var (success, result, _, _) = await mcp.SendAsync("tools/call", new
        {
            name = "check_searxng_status",
        });

        // check_searxng_status는 예외를 잡아 원인을 설명하는 텍스트를 반환합니다.
        Assert.True(success);
        var text = McpTestClient.ExtractToolText(result);
        Assert.Contains("연결", text);
    }
}
