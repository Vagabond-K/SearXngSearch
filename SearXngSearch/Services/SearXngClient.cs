using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SearXngSearch.Models;
using SearXngSearch.Options;

namespace SearXngSearch.Services;

/// <summary>
/// SearXNG JSON API(<c>/search?format=json</c>)를 호출하는 클라이언트.
/// 리플리카 failover, 레이트 리미트, 결과 캐싱, HTML 폴백을 지원합니다.
/// </summary>
public sealed class SearXngClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SearXngOptions _options;
    private readonly ILogger<SearXngClient> _logger;
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, SlidingWindowRateLimiter> _rateLimiters = new();

    public SearXngClient(
        IHttpClientFactory httpClientFactory,
        IOptions<SearXngOptions> options,
        ILogger<SearXngClient> logger,
        IMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// SearXNG 검색 API를 호출하고 결과를 반환합니다.
    /// </summary>
    /// <param name="query">검색어.</param>
    /// <param name="language">검색 언어 (예: "ko", "en", "all").</param>
    /// <param name="categories">검색 카테고리 (예: "general", "news", "it").</param>
    /// <param name="timeRange">시간 범위 (day/week/month/year).</param>
    /// <param name="maxResults">반환할 최대 결과 수.</param>
    /// <param name="page">페이지 번호 (1부터).</param>
    /// <param name="safesearch">안전 검색 수준 (0/1/2).</param>
    /// <param name="cancellationToken">취소 토큰.</param>
    public async Task<SearXngSearchResponse> SearchAsync(
        string query,
        string? language = null,
        string? categories = null,
        string? timeRange = null,
        int? maxResults = null,
        int page = 1,
        int safesearch = 1,
        CancellationToken cancellationToken = default)
    {
        var effectiveMaxResults = Math.Clamp(
            maxResults ?? _options.DefaultMaxResults,
            1,
            _options.MaxResultsLimit);

        var effectiveLanguage = string.IsNullOrWhiteSpace(language) ? _options.DefaultLanguage : language;
        var effectiveCategories = string.IsNullOrWhiteSpace(categories) ? _options.DefaultCategories : categories;
        var effectiveTimeRange = string.IsNullOrWhiteSpace(timeRange) ? _options.DefaultTimeRange : timeRange;

        var cacheKey = BuildCacheKey(query, effectiveLanguage, effectiveCategories, effectiveTimeRange, effectiveMaxResults, page, safesearch);

        if (_options.CacheEnabled && _cache.TryGetValue<SearXngSearchResponse>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogDebug("캐시 히트: {Query}", query);
            return cached;
        }

        var response = await ExecuteWithFailoverAsync(
            baseUrl => SendSearchRequestAsync(
                baseUrl, query, effectiveLanguage, effectiveCategories, effectiveTimeRange, effectiveMaxResults, page, safesearch,
                cancellationToken),
            cancellationToken);

        if (_options.CacheEnabled)
        {
            _cache.Set(cacheKey, response, TimeSpan.FromSeconds(_options.CacheTtlSeconds));
        }

        _logger.LogInformation(
            "SearXNG 검색 완료: query={Query}, results={Count}, took={Took}ms",
            query,
            response.Results.Count,
            response.Took);

        return response;
    }

    /// <summary>
    /// SearXNG 인스턴스 정보(버전, 카테고리, 엔진 목록)를 가져옵니다.
    /// </summary>
    public async Task<SearXngInstanceInfo> GetInstanceInfoAsync(CancellationToken cancellationToken = default)
    {
        var config = await ExecuteWithFailoverAsync(
            baseUrl => SendConfigRequestAsync(baseUrl, cancellationToken),
            cancellationToken);

        return new SearXngInstanceInfo
        {
            BaseUrl = _options.BaseUrl,
            Version = config.Version,
            Categories = ParseConfigCategories(config.Categories),
            Engines = config.Engines is null
                ? []
                : config.Engines.Select(e => e.Name ?? "").Where(n => n.Length > 0).ToList(),
            EnabledEngineCount = config.Engines is null ? 0 : config.Engines.Count(e => e.Enabled),
        };
    }

    /// <summary>
    /// 검색어 자동완성 후보를 가져옵니다.
    /// </summary>
    public async Task<List<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default)
    {
        var response = await SearchAsync(query, cancellationToken: cancellationToken);
        return response.Suggestions ?? [];
    }

    /// <summary>
    /// 리플리카 목록을 순서대로 시도하며, 429/503 또는 연결 실패 시 다음 리플리카로 failover합니다.
    /// </summary>
    private async Task<T> ExecuteWithFailoverAsync<T>(
        Func<string, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var baseUrls = GetBaseUrls();
        Exception? lastException = null;

        for (var i = 0; i < baseUrls.Count; i++)
        {
            var baseUrl = baseUrls[i];
            try
            {
                if (!TryAcquireRateLimit(baseUrl))
                {
                    throw new SearXngApiException(
                        $"레이트 리미트 초과: {baseUrl} (분당 {_options.RateLimitPerMinute}회 제한). 잠시 후 다시 시도하세요.");
                }

                return await action(baseUrl);
            }
            catch (SearXngApiException ex) when (ex.StatusCode is 429 or 503)
            {
                lastException = ex;
                _logger.LogWarning(
                    "리플리카 {Index}/{Count} ({Url})이 {Status}를 반환했습니다. 다음 리플리카로 전환합니다.",
                    i + 1, baseUrls.Count, baseUrl, ex.StatusCode);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(
                    "리플리카 {Index}/{Count} ({Url})에 연결할 수 없습니다. 다음 리플리카로 전환합니다.",
                    i + 1, baseUrls.Count, baseUrl);
            }
        }

        throw lastException ?? new SearXngApiException("사용 가능한 SearXNG 리플리카가 없습니다.");
    }

    private async Task<SearXngSearchResponse> SendSearchRequestAsync(
        string baseUrl, string query, string? language, string? categories, string? timeRange,
        int maxResults, int page, int safesearch, CancellationToken cancellationToken)
    {
        var requestUri = BuildSearchUri(
            baseUrl, query, language, categories, timeRange, maxResults, page, safesearch);
        _logger.LogDebug("SearXNG 검색 요청: {Uri}", requestUri);

        var client = _httpClientFactory.CreateClient(SearXngHttpClientFactory.ClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(requestUri, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SearXngApiException(
                $"SearXNG 요청이 {_options.TimeoutSeconds}초 내에 완료되지 않았습니다. " +
                $"인스턴스가 느리거나 응답이 큰 경우 TimeoutSeconds를 늘려보세요.");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            SearXngSearchResponse? result;
            try
            {
                result = JsonSerializer.Deserialize<SearXngSearchResponse>(content, SearXngJson.Options);
            }
            catch (JsonException ex)
            {
                throw new SearXngApiException(
                    "SearXNG API 응답을 파싱할 수 없습니다. JSON 형식이 예상과 다릅니다.", ex);
            }

            return result ?? throw new SearXngApiException("SearXNG API 응답이 비어 있습니다.");
        }

        // JSON API 비활성화(403/404) 시 HTML 폴백
        if (_options.HtmlFallbackEnabled &&
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "JSON API가 비활성화된 것으로 보입니다 ({Status}). HTML 폴백을 시도합니다.",
                (int)response.StatusCode);

            var parsed = HtmlResultParser.Parse(content);
            if (parsed.Results.Count > 0)
            {
                return parsed;
            }
        }

        _logger.LogError("SearXNG API 오류: {StatusCode}", response.StatusCode);
        throw new SearXngApiException(
            $"SearXNG API 요청 실패 (HTTP {(int)response.StatusCode} {response.StatusCode}). " +
            $"인스턴스가 실행 중인지, JSON API(format=json)가 활성화되어 있는지 확인하세요. " +
            $"(settings.yml: search.formats: [html, json] 또는 HtmlFallbackEnabled: true)",
            (int)response.StatusCode);
    }

    private async Task<SearXngConfigResponse> SendConfigRequestAsync(
        string baseUrl, CancellationToken cancellationToken)
    {
        var requestUri = new Uri($"{baseUrl}/config");
        var client = _httpClientFactory.CreateClient(SearXngHttpClientFactory.ClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(requestUri, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SearXngApiException(
                $"SearXNG 요청이 {_options.TimeoutSeconds}초 내에 완료되지 않았습니다.");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SearXngApiException(
                $"SearXNG /config 요청 실패 (HTTP {(int)response.StatusCode} {response.StatusCode})",
                (int)response.StatusCode);
        }

        try
        {
            return JsonSerializer.Deserialize<SearXngConfigResponse>(content, SearXngJson.Options)
                   ?? throw new SearXngApiException("SearXNG /config 응답이 비어 있습니다.");
        }
        catch (JsonException ex)
        {
            throw new SearXngApiException("SearXNG /config 응답을 파싱할 수 없습니다.", ex);
        }
    }

    /// <summary>
    /// /config의 categories는 인스턴스 버전에 따라 dict 또는 array일 수 있어
    /// 두 형식 모두 지원합니다.
    /// </summary>
    private static List<string> ParseConfigCategories(JsonElement? categories)
    {
        if (categories is null || categories.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        var names = new List<string>();
        if (categories.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in categories.Value.EnumerateObject())
            {
                names.Add(property.Name);
            }
        }
        else if (categories.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in categories.Value.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    names.Add(element.GetString() ?? "");
                }
                else if (element.ValueKind == JsonValueKind.Object &&
                         element.TryGetProperty("name", out var nameProp))
                {
                    names.Add(nameProp.GetString() ?? "");
                }
            }
        }

        return names.Where(n => n.Length > 0).ToList();
    }

    private bool TryAcquireRateLimit(string baseUrl)
    {
        var limiter = _rateLimiters.GetOrAdd(baseUrl, _ => new SlidingWindowRateLimiter());
        return limiter.TryAcquire(_options.RateLimitPerMinute);
    }

    private List<string> GetBaseUrls()
    {
        return _options.BaseUrl
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(u => u.TrimEnd('/'))
            .Distinct()
            .ToList();
    }

    private static string BuildCacheKey(
        string query, string? language, string? categories, string? timeRange,
        int maxResults, int page, int safesearch)
    {
        return string.Join('|',
            query.ToLowerInvariant(),
            language ?? "all",
            categories ?? "general",
            timeRange ?? "none",
            maxResults.ToString(),
            page.ToString(),
            safesearch.ToString());
    }

    private static Uri BuildSearchUri(
        string baseUrl, string? query, string? language, string? categories, string? timeRange,
        int maxResults, int page, int safesearch)
    {
        var parameters = new StringBuilder();
        AppendParam(parameters, "q", query);
        AppendParam(parameters, "format", "json");
        AppendParam(parameters, "language", language);
        AppendParam(parameters, "categories", categories);
        AppendParam(parameters, "time_range", timeRange);
        AppendParam(parameters, "max_results", maxResults.ToString());
        AppendParam(parameters, "pageno", page.ToString());
        AppendParam(parameters, "safesearch", safesearch.ToString());

        return new Uri($"{baseUrl}/search?{parameters}");
    }

    private static void AppendParam(StringBuilder sb, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append('&');
        }

        sb.Append(name).Append('=').Append(Uri.EscapeDataString(value));
    }
}
