using System.Text.Json;
using SearXngSearch.Models;
using SearXngSearch.Services;
using Xunit;

namespace SearXngSearch.Tests.Unit;

public class SearchResultFormatterTests
{
    private static SearXngSearchResponse MakeResponse(int resultCount, string? content = null)
    {
        var response = new SearXngSearchResponse { Took = 42 };
        for (var i = 0; i < resultCount; i++)
        {
            response.Results.Add(new SearXngResult
            {
                Title = $"결과 {i + 1}",
                Url = $"https://example.com/{i + 1}",
                Content = content ?? $"결과 {i + 1}의 요약입니다.",
                Engine = "google",
                Category = "general",
                Score = 90.0,
                PublishedDate = "2026-09-01",
            });
        }
        return response;
    }

    [Fact]
    public void ToText_결과_0건_시_안내_메시지()
    {
        var text = SearchResultFormatter.ToText(new SearXngSearchResponse(), "없는 검색어", 10);

        Assert.Contains("검색 결과가 없습니다", text);
    }

    [Fact]
    public void ToText_최상위_결과_강조_표시()
    {
        var text = SearchResultFormatter.ToText(MakeResponse(3), "쿼리", 10);

        Assert.Contains("최상위 결과 (Direct Answer): 결과 1", text);
        Assert.Contains("https://example.com/1", text);
        Assert.Contains("[2] 결과 2", text);
        Assert.Contains("검색 소요 시간: 42ms", text);
    }

    [Fact]
    public void ToText_maxResults_만큼만_포함()
    {
        var text = SearchResultFormatter.ToText(MakeResponse(5), "쿼리", 2);

        Assert.Contains("결과 수: 2", text);
        Assert.Contains("결과 1", text);
        Assert.Contains("결과 2", text);
        Assert.DoesNotContain("결과 3", text);
    }

    [Fact]
    public void ToText_긴_요약_300자_단절()
    {
        var longContent = new string('a', 400);
        var text = SearchResultFormatter.ToText(MakeResponse(1, longContent), "쿼리", 10);

        Assert.Contains("…", text);
        Assert.DoesNotContain(longContent, text);
    }

    [Fact]
    public void ToText_제안어_포함()
    {
        var response = MakeResponse(1);
        response.Suggestions = ["제안 1", "제안 2"];

        var text = SearchResultFormatter.ToText(response, "쿼리", 10);

        Assert.Contains("추천 검색어: 제안 1, 제안 2", text);
    }

    [Fact]
    public void ToJson_구조_검증()
    {
        var json = SearchResultFormatter.ToJson(MakeResponse(2), "쿼리", 10);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("쿼리", root.GetProperty("query").GetString());
        Assert.Equal(2, root.GetProperty("resultCount").GetInt32());
        Assert.Equal(42, root.GetProperty("tookMs").GetInt32());
        Assert.Equal("결과 1", root.GetProperty("topResult").GetProperty("title").GetString());
        Assert.Equal(2, root.GetProperty("results").GetArrayLength());
        Assert.Equal("google", root.GetProperty("results")[0].GetProperty("engine").GetString());
    }

    [Fact]
    public void ToJson_결과_0건_시_topResult_null()
    {
        var json = SearchResultFormatter.ToJson(new SearXngSearchResponse(), "쿼리", 10);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(0, root.GetProperty("resultCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("topResult").ValueKind);
    }
}
