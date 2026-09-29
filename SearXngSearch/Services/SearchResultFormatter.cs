using SearXngSearch.Models;

namespace SearXngSearch.Services;

/// <summary>
/// SearXNG 검색 결과를 LLM이 읽기 좋은 텍스트/JSON 형식으로 변환합니다.
/// 요약 길이(<c>maxSnippetLength</c>)와 전체 출력 크기 상한(<c>maxOutputChars</c>)으로
/// LLM 컨텍스트에 투입되는 텍스트 크기를 제어할 수 있습니다.
/// </summary>
public static class SearchResultFormatter
{
    /// <summary>텍스트 형식 기본 요약 길이(자).</summary>
    public const int DefaultTextSnippetLength = 300;

    /// <summary>JSON 형식 기본 요약 길이(자).</summary>
    public const int DefaultJsonSnippetLength = 500;

    /// <summary>
    /// 결과를 구조화된 텍스트로 포맷합니다.
    /// </summary>
    /// <param name="response">SearXNG 검색 응답.</param>
    /// <param name="query">검색어.</param>
    /// <param name="maxResults">포함할 최대 결과 수.</param>
    /// <param name="maxSnippetLength">결과 요약(content) 최대 길이(자). null 또는 0 이하이면 300자.</param>
    /// <param name="maxOutputChars">전체 출력 최대 크기(자). 초과 시 하위 결과부터 생략. null 또는 0 이하이면 제한 없음.</param>
    public static string ToText(
        SearXngSearchResponse response, string query, int maxResults,
        int? maxSnippetLength = null, int? maxOutputChars = null)
    {
        var snippetLength = maxSnippetLength is > 0 ? maxSnippetLength.Value : DefaultTextSnippetLength;
        var results = response.Results.Take(maxResults).ToList();

        if (results.Count == 0)
        {
            return $"\"{query}\"에 대한 검색 결과가 없습니다. 검색어를 변경해 보세요.";
        }

        int? limit = maxOutputChars is > 0 ? maxOutputChars.Value : null;
        var included = results.Count;

        while (true)
        {
            var text = BuildText(response, query, results, included, snippetLength);
            if (limit is null || text.Length <= limit || included <= 1)
            {
                return text;
            }

            included--;
        }
    }

    private static string BuildText(
        SearXngSearchResponse response, string query,
        IReadOnlyList<SearXngResult> results, int included, int snippetLength)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"검색어: {query}");
        sb.AppendLine($"결과 수: {included}");
        if (response.Took is not null)
        {
            sb.AppendLine($"검색 소요 시간: {response.Took}ms");
        }
        sb.AppendLine(new string('=', 60));

        // Direct answer: 최상위 결과를 강조 표시
        var top = results[0];
        sb.AppendLine();
        sb.AppendLine($"🎯 최상위 결과 (Direct Answer): {top.Title ?? "(제목 없음)"}");
        sb.AppendLine($"    URL: {top.Url}");
        if (!string.IsNullOrWhiteSpace(top.Content))
        {
            sb.AppendLine($"    {Truncate(top.Content, snippetLength)}");
        }
        sb.AppendLine(new string('-', 60));

        for (var i = 0; i < included; i++)
        {
            var r = results[i];
            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {r.Title ?? "(제목 없음)"}");
            sb.AppendLine($"    URL: {r.Url}");

            if (!string.IsNullOrWhiteSpace(r.Content))
            {
                sb.AppendLine($"    요약: {Truncate(r.Content, snippetLength)}");
            }

            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.Engine))
            {
                meta.Add($"엔진: {r.Engine}");
            }
            if (!string.IsNullOrWhiteSpace(r.Category))
            {
                meta.Add($"카테고리: {r.Category}");
            }
            if (!string.IsNullOrWhiteSpace(r.PublishedDate))
            {
                meta.Add($"날짜: {r.PublishedDate}");
            }
            if (r.Score is not null)
            {
                meta.Add($"점수: {r.Score}");
            }

            if (meta.Count > 0)
            {
                sb.AppendLine($"    {string.Join(" | ", meta)}");
            }
        }

        if (included < results.Count)
        {
            sb.AppendLine();
            sb.AppendLine($"(출력 크기 상한 초과로 {results.Count - included}개 결과 생략)");
        }

        if (response.Suggestions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"추천 검색어: {string.Join(", ", response.Suggestions)}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 결과를 JSON 문자열로 포맷합니다.
    /// </summary>
    /// <param name="response">SearXNG 검색 응답.</param>
    /// <param name="query">검색어.</param>
    /// <param name="maxResults">포함할 최대 결과 수.</param>
    /// <param name="maxSnippetLength">결과 요약(content) 최대 길이(자). null 또는 0 이하이면 500자.</param>
    /// <param name="maxOutputChars">전체 출력 최대 크기(자). 초과 시 하위 결과부터 생략. null 또는 0 이하이면 제한 없음.</param>
    public static string ToJson(
        SearXngSearchResponse response, string query, int maxResults,
        int? maxSnippetLength = null, int? maxOutputChars = null)
    {
        var snippetLength = maxSnippetLength is > 0 ? maxSnippetLength.Value : DefaultJsonSnippetLength;
        var results = response.Results.Take(maxResults).ToList();

        int? limit = maxOutputChars is > 0 ? maxOutputChars.Value : null;
        var included = results.Count;

        while (true)
        {
            var json = BuildJson(response, query, results, included, snippetLength);
            if (limit is null || json.Length <= limit || included <= 1)
            {
                return json;
            }

            included--;
        }
    }

    private static string BuildJson(
        SearXngSearchResponse response, string query,
        IReadOnlyList<SearXngResult> results, int included, int snippetLength)
    {
        var includedResults = results.Take(included).ToList();
        var top = includedResults.Count > 0 ? includedResults[0] : null;

        var payload = new
        {
            query,
            resultCount = includedResults.Count,
            omittedCount = results.Count - includedResults.Count,
            tookMs = response.Took,
            topResult = top is null ? null : new
            {
                title = top.Title,
                url = top.Url,
                content = top.Content is null ? null : Truncate(top.Content, snippetLength),
            },
            results = includedResults
                .Select(r => new
                {
                    title = r.Title,
                    url = r.Url,
                    content = r.Content is null ? null : Truncate(r.Content, snippetLength),
                    engine = r.Engine,
                    category = r.Category,
                    publishedDate = r.PublishedDate,
                    score = r.Score,
                })
                .ToList(),
            suggestions = response.Suggestions,
        };

        return System.Text.Json.JsonSerializer.Serialize(
            payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
