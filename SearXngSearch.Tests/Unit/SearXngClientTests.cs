using SearXngSearch.Options;
using SearXngSearch.Services;
using SearXngSearch.Tests.TestHelpers;
using Xunit;

namespace SearXngSearch.Tests.Unit;

public class SearXngClientTests
{
    [Fact]
    public async Task SearchAsync_기본_검색_성공()
    {
        var (client, handler) = ClientFactory.Create();
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        var response = await client.SearchAsync("test");

        Assert.Equal(2, response.Results.Count);
        Assert.Equal("첫 번째 결과", response.Results[0].Title);
        Assert.Equal(42, response.Took);
        Assert.Equal(2, response.Suggestions.Count);
    }

    [Fact]
    public async Task SearchAsync_요청_URI_파라미터_구성()
    {
        var (client, handler) = ClientFactory.Create();
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        await client.SearchAsync(
            query: "C# tutorial",
            language: "en",
            categories: "it",
            timeRange: "week",
            maxResults: 5,
            page: 2,
            safesearch: 2);

        var uri = handler.LastRequestUri("fake")!;
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        Assert.Equal("/search", uri.AbsolutePath);
        Assert.Equal("C# tutorial", query["q"]); // ParseQueryString은 디코딩된 값을 반환
        Assert.Equal("json", query["format"]);
        Assert.Equal("en", query["language"]);
        Assert.Equal("it", query["categories"]);
        Assert.Equal("week", query["time_range"]);
        Assert.Equal("5", query["max_results"]);
        Assert.Equal("2", query["pageno"]);
        Assert.Equal("2", query["safesearch"]);
    }

    [Fact]
    public async Task SearchAsync_maxResults_상한_클램핑()
    {
        var (client, handler) = ClientFactory.Create();
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        await client.SearchAsync("test", maxResults: 999);

        var query = System.Web.HttpUtility.ParseQueryString(handler.LastRequestUri("fake")!.Query);
        Assert.Equal("30", query["max_results"]);
    }

    [Fact]
    public async Task SearchAsync_캐시_히트_시_재요청_없음()
    {
        var (client, handler) = ClientFactory.Create();
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        await client.SearchAsync("test");
        await client.SearchAsync("test");

        Assert.Equal(1, handler.CallCount("fake"));
    }

    [Fact]
    public async Task SearchAsync_다른_쿼리_는_캐시_미적용()
    {
        var (client, handler) = ClientFactory.Create();
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        await client.SearchAsync("test");
        await client.SearchAsync("other");

        Assert.Equal(2, handler.CallCount("fake"));
    }

    [Fact]
    public async Task SearchAsync_캐시_비활성_시_매번_요청()
    {
        var (client, handler) = ClientFactory.Create(new SearXngOptions { CacheEnabled = false });
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        await client.SearchAsync("test");
        await client.SearchAsync("test");

        Assert.Equal(2, handler.CallCount("fake"));
    }

    [Fact]
    public async Task SearchAsync_429_시_다음_리플리카로_failover()
    {
        var options = new SearXngOptions
        {
            BaseUrl = "http://replica1:8080;http://replica2:8080",
        };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetStatus("replica1", 429);
        handler.SetJson("replica2", SearXngFixtures.SearchJson);

        var response = await client.SearchAsync("test");

        Assert.Equal(2, response.Results.Count);
        Assert.Equal(1, handler.CallCount("replica1"));
        Assert.Equal(1, handler.CallCount("replica2"));
    }

    [Fact]
    public async Task SearchAsync_연결_실패_시_다음_리플리카로_failover()
    {
        var options = new SearXngOptions
        {
            BaseUrl = "http://down:8080;http://up:8080",
        };
        var (client, handler) = ClientFactory.Create(options);
        // 'down' 호스트에 응답 미등록 = 연결 실패
        handler.SetJson("up", SearXngFixtures.SearchJson);

        var response = await client.SearchAsync("test");

        Assert.Equal(2, response.Results.Count);
        Assert.Equal(1, handler.CallCount("down"));
        Assert.Equal(1, handler.CallCount("up"));
    }

    [Fact]
    public async Task SearchAsync_모든_리플리카_실패_시_예외()
    {
        var options = new SearXngOptions
        {
            BaseUrl = "http://down1:8080;http://down2:8080",
        };
        var (client, _) = ClientFactory.Create(options);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync("test"));
        Assert.Contains("down2", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_503_도_failover_트리거()
    {
        var options = new SearXngOptions
        {
            BaseUrl = "http://replica1:8080;http://replica2:8080",
        };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetStatus("replica1", 503);
        handler.SetJson("replica2", SearXngFixtures.SearchJson);

        var response = await client.SearchAsync("test");

        Assert.Equal(2, response.Results.Count);
    }

    [Fact]
    public async Task SearchAsync_400_은_failover_없이_즉시_예외()
    {
        var options = new SearXngOptions
        {
            BaseUrl = "http://replica1:8080;http://replica2:8080",
        };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetStatus("replica1", 400);

        var ex = await Assert.ThrowsAsync<SearXngApiException>(() => client.SearchAsync("test"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, handler.CallCount("replica2"));
    }

    [Fact]
    public async Task SearchAsync_레이트_리미트_초과_시_예외()
    {
        var options = new SearXngOptions { RateLimitPerMinute = 2 };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        await client.SearchAsync("q1");
        await client.SearchAsync("q2");

        var ex = await Assert.ThrowsAsync<SearXngApiException>(() => client.SearchAsync("q3"));
        Assert.Contains("레이트 리미트 초과", ex.Message);
        Assert.Equal(2, handler.CallCount("fake"));
    }

    [Fact]
    public async Task SearchAsync_HTML_폴백_403_시_파싱_결과_반환()
    {
        var options = new SearXngOptions { HtmlFallbackEnabled = true };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetHtml("fake", SearXngFixtures.SearchHtml, statusCode: 403);

        var response = await client.SearchAsync("test");

        Assert.Equal(2, response.Results.Count);
        Assert.Equal("HTML 결과 1", response.Results[0].Title);
    }

    [Fact]
    public async Task SearchAsync_HTML_폴백_404_시_동작()
    {
        var options = new SearXngOptions { HtmlFallbackEnabled = true };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetHtml("fake", SearXngFixtures.SearchHtml, statusCode: 404);

        var response = await client.SearchAsync("test");

        Assert.Equal(2, response.Results.Count);
    }

    [Fact]
    public async Task SearchAsync_HTML_폴백_비활성_시_예외()
    {
        var (client, handler) = ClientFactory.Create(); // HtmlFallbackEnabled = false
        handler.SetHtml("fake", SearXngFixtures.SearchHtml, statusCode: 403);

        var ex = await Assert.ThrowsAsync<SearXngApiException>(() => client.SearchAsync("test"));

        Assert.Equal(403, ex.StatusCode);
        Assert.Contains("format=json", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_HTML_폴백_결과_없으면_예외()
    {
        var options = new SearXngOptions { HtmlFallbackEnabled = true };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetHtml("fake", "<html><body>결과 없음</body></html>", statusCode: 403);

        await Assert.ThrowsAsync<SearXngApiException>(() => client.SearchAsync("test"));
    }

    [Fact]
    public async Task SearchAsync_타임아웃_시_예외()
    {
        var options = new SearXngOptions { TimeoutSeconds = 1 };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetHandler("fake", "/search", async _ =>
        {
            await Task.Delay(3000);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });

        var ex = await Assert.ThrowsAsync<SearXngApiException>(() => client.SearchAsync("test"));
        Assert.Contains("완료되지 않았습니다", ex.Message);
    }

    [Fact]
    public async Task GetInstanceInfoAsync_categories_dict_형식_파싱()
    {
        var (client, handler) = ClientFactory.Create();
        handler.SetJson("fake", SearXngFixtures.ConfigDictCategoriesJson, path: "/config");

        var info = await client.GetInstanceInfoAsync();

        Assert.Equal("2026.9.23", info.Version);
        Assert.Equal(new[] { "general", "news", "it" }, info.Categories);
        Assert.Equal(3, info.Engines.Count);
        Assert.Equal(2, info.EnabledEngineCount);
    }

    [Fact]
    public async Task GetInstanceInfoAsync_categories_array_형식_파싱()
    {
        var (client, handler) = ClientFactory.Create();
        handler.SetJson("fake", SearXngFixtures.ConfigArrayCategoriesJson, path: "/config");

        var info = await client.GetInstanceInfoAsync();

        Assert.Equal("2026.8.22", info.Version);
        Assert.Equal(new[] { "general", "news", "it" }, info.Categories);
        Assert.Equal(1, info.EnabledEngineCount);
    }

    [Fact]
    public async Task GetSuggestionsAsync_제안어_반환()
    {
        var (client, handler) = ClientFactory.Create();
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        var suggestions = await client.GetSuggestionsAsync("test");

        Assert.Equal(new[] { "test query", "test example" }, suggestions);
    }

    [Fact]
    public async Task GetSuggestionsAsync_제안어_없으면_빈_목록()
    {
        var (client, handler) = ClientFactory.Create();
        handler.SetJson("fake", """{"results": [], "took": 1}""");

        var suggestions = await client.GetSuggestionsAsync("test");

        Assert.Empty(suggestions);
    }
}
