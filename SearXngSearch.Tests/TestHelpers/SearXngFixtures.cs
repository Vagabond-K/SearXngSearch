namespace SearXngSearch.Tests.TestHelpers;

/// <summary>
/// SearXNG API 응답 흉내용 고정 JSON/HTML 픽스처.
/// 실제 SearXNG 인스턴스 응답 구조와 일치합니다.
/// </summary>
public static class SearXngFixtures
{
    /// <summary>/search?format=json 응답 (결과 2건 + 제안어).</summary>
    public const string SearchJson = """
        {
          "query": "test",
          "results": [
            {
              "title": "첫 번째 결과",
              "url": "https://example.com/1",
              "content": "첫 번째 결과의 요약 내용입니다.",
              "engine": "google",
              "score": 99.5,
              "category": "general",
              "publishedDate": "2026-09-01"
            },
            {
              "title": "두 번째 결과",
              "url": "https://example.com/2",
              "content": "두 번째 결과의 요약 내용입니다.",
              "engine": "duckduckgo",
              "score": 88.0,
              "category": "general"
            }
          ],
          "suggestions": ["test query", "test example"],
          "took": 42
        }
        """;

    /// <summary>/config 응답 (categories가 dict 형식 — 2026.9.x 인스턴스).</summary>
    public const string ConfigDictCategoriesJson = """
        {
          "version": "2026.9.23",
          "categories": { "general": {}, "news": {}, "it": {} },
          "engines": [
            { "name": "google", "short_name": "google", "enabled": true },
            { "name": "duckduckgo", "short_name": "ddg", "enabled": true },
            { "name": "brave", "short_name": "brave", "enabled": false }
          ]
        }
        """;

    /// <summary>/config 응답 (categories가 array 형식 — 2026.8.x 인스턴스).</summary>
    public const string ConfigArrayCategoriesJson = """
        {
          "version": "2026.8.22",
          "categories": ["general", "news", "it"],
          "engines": [
            { "name": "google", "short_name": "google", "enabled": true }
          ]
        }
        """;

    /// <summary>JSON API 비활성화 인스턴스의 HTML 검색 결과 페이지.</summary>
    public const string SearchHtml = """
        <html><body>
          <article class="result" data-engine="google">
            <h3><a href="https://example.com/html1">HTML 결과 1</a></h3>
            <p class="content">HTML에서 파싱된 첫 번째 내용</p>
          </article>
          <article class="result" data-engine="duckduckgo">
            <h3><a href="https://example.com/html2">HTML 결과 2</a></h3>
            <p class="content">HTML에서 파싱된 두 번째 내용</p>
          </article>
        </body></html>
        """;
}
