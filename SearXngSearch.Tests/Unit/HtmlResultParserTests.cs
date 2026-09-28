using SearXngSearch.Services;
using SearXngSearch.Tests.TestHelpers;
using Xunit;

namespace SearXngSearch.Tests.Unit;

public class HtmlResultParserTests
{
    [Fact]
    public void Parse_결과_추출()
    {
        var response = HtmlResultParser.Parse(SearXngFixtures.SearchHtml);

        Assert.Equal(2, response.Results.Count);

        var first = response.Results[0];
        Assert.Equal("HTML 결과 1", first.Title);
        Assert.Equal("https://example.com/html1", first.Url);
        Assert.Equal("HTML에서 파싱된 첫 번째 내용", first.Content);
        Assert.Equal("google", first.Engine);

        var second = response.Results[1];
        Assert.Equal("HTML 결과 2", second.Title);
        Assert.Equal("duckduckgo", second.Engine);
    }

    [Fact]
    public void Parse_결과_없는_페이지_시_빈_목록()
    {
        var response = HtmlResultParser.Parse("<html><body><p>결과 없음</p></body></html>");

        Assert.Empty(response.Results);
    }
}
