using SearXngSearch.Models;

namespace SearXngSearch.Services;

/// <summary>
/// SearXNG 검색 결과를 LLM이 읽기 좋은 텍스트/JSON 형식으로 변환합니다.
/// </summary>
public static class SearchResultFormatter
{
    /// <summary>
    /// 결과를 구조화된 텍스트로 포맷합니다.
    /// </summary>
    public static string ToText(SearXngSearchResponse response, string query, int maxResults)
    {
        var results = response.Results.Take(maxResults).ToList();

        if (results.Count == 0)
        {
            return $"\"{query}\"에 대한 검색 결과가 없습니다. 검색어를 변경해 보세요.";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"검색어: {query}");
        sb.AppendLine($"결과 수: {results.Count}");
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
            sb.AppendLine($"    {Truncate(top.Content, 300)}");
        }
        sb.AppendLine(new string('-', 60));

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {r.Title ?? "(제목 없음)"}");
            sb.AppendLine($"    URL: {r.Url}");

            if (!string.IsNullOrWhiteSpace(r.Content))
            {
                sb.AppendLine($"    요약: {Truncate(r.Content, 300)}");
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
    public static string ToJson(SearXngSearchResponse response, string query, int maxResults)
    {
        var results = response.Results.Take(maxResults).ToList();

        var top = results.Count > 0 ? results[0] : null;

        var payload = new
        {
            query,
            resultCount = results.Count,
            tookMs = response.Took,
            topResult = top is null ? null : new
            {
                title = top.Title,
                url = top.Url,
                content = top.Content is null ? null : Truncate(top.Content, 500),
            },
            results = results
                .Select(r => new
                {
                    title = r.Title,
                    url = r.Url,
                    content = r.Content is null ? null : Truncate(r.Content, 500),
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
