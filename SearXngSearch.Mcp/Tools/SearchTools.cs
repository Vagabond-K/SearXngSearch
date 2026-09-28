using System.ComponentModel;
using ModelContextProtocol.Server;
using SearXngSearch.Services;

namespace SearXngSearch.Mcp.Tools;

/// <summary>
/// SearXNG 웹 검색 MCP 도구.
/// </summary>
[McpServerToolType]
public sealed class SearchTools
{
    private readonly SearXngClient _searXng;

    public SearchTools(SearXngClient searXng)
    {
        _searXng = searXng;
    }

    [McpServerTool(Name = "web_search"), Description(
        "SearXNG 메타 검색 엔진을 사용해 웹을 검색합니다. " +
        "일반 웹 검색, 문서 조사, 최신 정보 확인 등에 사용하세요. " +
        "결과에 제목, URL, 요약, 검색 엔진, 날짜가 포함됩니다.")]
    public async Task<string> WebSearch(
        [Description("검색할 쿼리 문자열.")] string query,
        [Description("검색 언어. 예: ko(한국어), en(영어), ja(일본어), all(모두). 기본값: all")]
        string? language = null,
        [Description("검색 카테고리. 예: general, news, it, science, images, files. 기본값: general")]
        string? categories = null,
        [Description("시간 범위 필터. day(1일), week(1주), month(1개월), year(1년) 중 하나. 지정하지 않으면 필터 없음.")]
        string? timeRange = null,
        [Description("반환할 최대 결과 수 (1~30). 기본값: 10")]
        int maxResults = 10,
        [Description("페이지 번호 (1부터). 기본값: 1")]
        int page = 1,
        [Description("출력 형식. text(가독성 높은 텍스트) 또는 json(구조화된 JSON). 기본값: text")]
        string outputFormat = "text",
        CancellationToken cancellationToken = default)
    {
        var response = await _searXng.SearchAsync(
            query: query,
            language: language,
            categories: categories,
            timeRange: timeRange,
            maxResults: maxResults,
            page: page,
            cancellationToken: cancellationToken);

        return outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? SearchResultFormatter.ToJson(response, query, maxResults)
            : SearchResultFormatter.ToText(response, query, maxResults);
    }

    [McpServerTool(Name = "news_search"), Description(
        "SearXNG를 사용해 뉴스 기사를 검색합니다. " +
        "최신 뉴스, 사건/사고, 트렌드 확인에 사용하세요. " +
        "timeRange로 day/week/month/year를 지정하면 해당 기간 내 기사만 반환됩니다.")]
    public async Task<string> NewsSearch(
        [Description("검색할 뉴스 쿼리 문자열.")] string query,
        [Description("검색 언어. 예: ko, en, all. 기본값: all")]
        string? language = null,
        [Description("시간 범위. day(1일), week(1주), month(1개월), year(1년). 기본값: week")]
        string? timeRange = "week",
        [Description("반환할 최대 결과 수 (1~30). 기본값: 10")]
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var response = await _searXng.SearchAsync(
            query: query,
            language: language,
            categories: "news",
            timeRange: timeRange,
            maxResults: maxResults,
            cancellationToken: cancellationToken);

        return SearchResultFormatter.ToText(response, query, maxResults);
    }

    [McpServerTool(Name = "check_searxng_status"), Description(
        "연결된 SearXNG 인스턴스의 상태를 확인합니다. " +
        "검색 도구가 오류를 반환할 때 먼저 이 도구로 인스턴스 가동 여부를 확인하세요.")]
    public async Task<string> CheckSearXngStatus(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _searXng.SearchAsync(
                query: "test",
                maxResults: 1,
                cancellationToken: cancellationToken);

            return $"SearXNG 인스턴스에 정상적으로 연결되었습니다. (테스트 검색 소요: {response.Took ?? 0}ms)";
        }
        catch (SearXngApiException ex)
        {
            return $"SearXNG 인스턴스 연결 실패: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"SearXNG 인스턴스에 네트워크 연결을 할 수 없습니다: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return "SearXNG 인스턴스 요청이 타임아웃되었습니다. 인스턴스가 응답하지 않습니다.";
        }
    }

    [McpServerTool(Name = "get_instance_info"), Description(
        "연결된 SearXNG 인스턴스의 정보를 가져옵니다. " +
        "버전, 사용 가능한 검색 카테고리 목록, 검색 엔진 목록을 반환합니다. " +
        "web_search의 categories 파라미터에 어떤 값을 쓸 수 있는지 확인할 때 사용하세요.")]
    public async Task<string> GetInstanceInfo(CancellationToken cancellationToken)
    {
        try
        {
            var info = await _searXng.GetInstanceInfoAsync(cancellationToken);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"SearXNG 인스턴스 정보 ({info.BaseUrl})");
            sb.AppendLine($"  버전: {info.Version ?? "알 수 없음"}");
            sb.AppendLine($"  활성화된 엔진: {info.EnabledEngineCount}개");
            sb.AppendLine();

            if (info.Categories.Count > 0)
            {
                sb.AppendLine($"사용 가능한 카테고리 ({info.Categories.Count}개):");
                sb.AppendLine("  " + string.Join(", ", info.Categories));
                sb.AppendLine();
            }

            if (info.Engines.Count > 0)
            {
                sb.AppendLine($"검색 엔진 ({info.Engines.Count}개):");
                sb.AppendLine("  " + string.Join(", ", info.Engines));
            }

            return sb.ToString().TrimEnd();
        }
        catch (SearXngApiException ex)
        {
            return $"SearXNG 인스턴스 정보 조회 실패: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"SearXNG 인스턴스에 네트워크 연결을 할 수 없습니다: {ex.Message}";
        }
    }

    [McpServerTool(Name = "get_search_suggestions"), Description(
        "검색어에 대한 자동완성 후보(제안어)를 가져옵니다. " +
        "검색어를 다듬거나 관련 검색어를 확인할 때 사용하세요.")]
    public async Task<string> GetSearchSuggestions(
        [Description("자동완성 후보를 받을 검색어.")] string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var suggestions = await _searXng.GetSuggestionsAsync(query, cancellationToken);

            if (suggestions.Count == 0)
            {
                return $"\"{query}\"에 대한 자동완성 후보가 없습니다.";
            }

            return $"\"{query}\"에 대한 자동완성 후보 ({suggestions.Count}개):\n" +
                   string.Join("\n", suggestions.Select((s, i) => $"  {i + 1}. {s}"));
        }
        catch (SearXngApiException ex)
        {
            return $"자동완성 후보 조회 실패: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"SearXNG 인스턴스에 네트워크 연결을 할 수 없습니다: {ex.Message}";
        }
    }
}
