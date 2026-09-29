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
            MaxRetries = 0, // 재시도 없이 즉시 failover 시나리오
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
    public async Task SearchAsync_429_시_백오프_재시도_후_성공()
    {
        var options = new SearXngOptions
        {
            MaxRetries = 2,
            RetryBaseDelaySeconds = 0.01,
            RetryMaxDelaySeconds = 0.05,
        };
        var (client, handler) = ClientFactory.Create(options);
        var callCount = 0;
        handler.SetHandler("fake", "/search", _ =>
        {
            callCount++;
            return callCount < 3
                ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
                    { Content = new System.Net.Http.StringContent("") })
                : Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    { Content = new System.Net.Http.StringContent(SearXngFixtures.SearchJson, System.Text.Encoding.UTF8, "application/json") });
        });

        var response = await client.SearchAsync("test");

        Assert.Equal(2, response.Results.Count);
        Assert.Equal(3, handler.CallCount("fake")); // 429 두 번 + 성공
    }

    [Fact]
    public async Task SearchAsync_429_RetryAfter_헤더_지정_지연_준수()
    {
        var options = new SearXngOptions
        {
            MaxRetries = 1,
            RetryBaseDelaySeconds = 0.01, // 백오프(0.01초)와 Retry-After(0.5초)를 구분 가능하게
            RetryMaxDelaySeconds = 5.0,   // Retry-After가 상한에 걸리지 않도록
        };
        var (client, handler) = ClientFactory.Create(options);
        var callCount = 0;
        handler.SetHandler("fake", "/search", _ =>
        {
            callCount++;
            if (callCount == 1)
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
                {
                    Content = new System.Net.Http.StringContent(""),
                };
                response.Headers.RetryAfter =
                    new System.Net.Http.Headers.RetryConditionHeaderValue(System.TimeSpan.FromSeconds(0.5));
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(SearXngFixtures.SearchJson, System.Text.Encoding.UTF8, "application/json"),
            });
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.SearchAsync("test");
        sw.Stop();

        Assert.Equal(2, response.Results.Count);
        Assert.Equal(2, handler.CallCount("fake"));
        // Retry-After(0.5초)가 기본 백오프(0.01초)보다 우선 적용되었는지 검증
        Assert.True(sw.Elapsed >= System.TimeSpan.FromMilliseconds(400),
            $"Retry-After 지연 미준수: {sw.Elapsed.TotalMilliseconds:0}ms");
    }

    [Fact]
    public async Task SearchAsync_재시도_소진_시_다음_리플리카로_failover()
    {
        var options = new SearXngOptions
        {
            BaseUrl = "http://replica1:8080;http://replica2:8080",
            MaxRetries = 2,
            RetryBaseDelaySeconds = 0.01,
            RetryMaxDelaySeconds = 0.05,
        };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetStatus("replica1", 429);
        handler.SetJson("replica2", SearXngFixtures.SearchJson);

        var response = await client.SearchAsync("test");

        Assert.Equal(2, response.Results.Count);
        Assert.Equal(3, handler.CallCount("replica1")); // 최초 1회 + 재시도 2회
        Assert.Equal(1, handler.CallCount("replica2"));
    }

    [Fact]
    public async Task SearchAsync_연결_실패_시_다음_리플리카로_failover()
    {
        var options = new SearXngOptions
        {
            BaseUrl = "http://down:8080;http://up:8080",
            MaxRetries = 0, // 재시도 없이 즉시 failover 시나리오
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
            MaxRetries = 0, // 테스트 속도 위해 재시도 비활성
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
            MaxRetries = 0,
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
    public async Task SearchAsync_레이트_리미트_초과_대기_비활성_시_예외()
    {
        var options = new SearXngOptions
        {
            RateLimitPerMinute = 2,
            RateLimitWaitEnabled = false,
        };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        await client.SearchAsync("q1");
        await client.SearchAsync("q2");

        var ex = await Assert.ThrowsAsync<SearXngApiException>(() => client.SearchAsync("q3"));
        Assert.Contains("레이트 리미트 초과", ex.Message);
        Assert.Equal(2, handler.CallCount("fake"));
    }

    [Fact]
    public async Task SearchAsync_레이트_리미트_초과_시_슬롯_대기_후_성공()
    {
        var options = new SearXngOptions
        {
            RateLimitPerMinute = 1,
            RateLimitWindowSeconds = 0.3,
            RateLimitWaitEnabled = true,
            RateLimitWaitMaxSeconds = 5,
        };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        await client.SearchAsync("q1");
        var response = await client.SearchAsync("q2"); // 윈도우 만료 대기 후 성공

        Assert.Equal(2, response.Results.Count);
        Assert.Equal(2, handler.CallCount("fake"));
    }

    [Fact]
    public async Task SearchAsync_레이트_리미트_대기_시간_초과_시_예외()
    {
        var options = new SearXngOptions
        {
            RateLimitPerMinute = 1,
            RateLimitWindowSeconds = 60,
            RateLimitWaitEnabled = true,
            RateLimitWaitMaxSeconds = 0.2,
        };
        var (client, handler) = ClientFactory.Create(options);
        handler.SetJson("fake", SearXngFixtures.SearchJson);

        await client.SearchAsync("q1");

        var ex = await Assert.ThrowsAsync<SearXngApiException>(() => client.SearchAsync("q2"));
        Assert.Contains("레이트 리미트 초과", ex.Message);
        Assert.Equal(1, handler.CallCount("fake"));
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
