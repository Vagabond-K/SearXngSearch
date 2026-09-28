using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SearXngSearch.Models;

namespace SearXngSearch.Services;

/// <summary>
/// SearXNG HTML 검색 결과 페이지를 best-effort로 파싱합니다.
/// JSON API가 비활성화된 인스턴스용 폴백으로 사용되며,
/// 메타데이터(엔진, 점수, 날짜 등)는 제한적으로 추출됩니다.
/// </summary>
public static class HtmlResultParser
{
    private static readonly HtmlParser Parser = new();

    public static SearXngSearchResponse Parse(string html)
    {
        var document = Parser.ParseDocument(html);
        var response = new SearXngSearchResponse();

        foreach (var article in document.QuerySelectorAll("article.result"))
        {
            var link = article.QuerySelector("h3 a, h4 a, a.url_header, a");
            if (link is null)
            {
                continue;
            }

            var content = article.QuerySelector("p.content, .content");

            response.Results.Add(new SearXngResult
            {
                Title = link.Text().Trim(),
                Url = link.GetAttribute("href"),
                Content = content is null ? null : content.Text().Trim(),
                Engine = article.GetAttribute("data-engine"),
            });
        }

        return response;
    }
}
