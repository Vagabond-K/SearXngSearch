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
    /// 리플리카 목록을 순서대로 시도합니다. 각 리플리카에서 429/503 또는 연결 실패 시
    /// 지수 백오프로 재시도(<see cref="SearXngOptions.MaxRetries"/>)한 뒤,
    /// 여전히 실패하면 다음 리플리카로 failover합니다.
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
                if (!await AcquireRateLimitSlotAsync(baseUrl, cancellationToken))
                {
                    throw new SearXngApiException(
                        _options.RateLimitWaitEnabled
                            ? $"레이트 리미트 초과: {baseUrl} (분당 {_options.RateLimitPerMinute}회 제한, " +
                              $"{_options.RateLimitWaitMaxSeconds:0.#}초 대기에도 슬롯을 확보하지 못했습니다). 잠시 후 다시 시도하세요."
                            : $"레이트 리미트 초과: {baseUrl} (분당 {_options.RateLimitPerMinute}회 제한). 잠시 후 다시 시도하세요.");
                }

                return await ExecuteWithRetryAsync(baseUrl, action, cancellationToken);
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

    /// <summary>
    /// 단일 리플리카에서 429/503 또는 연결 실패 시 지수 백오프(±25% jitter)로 재시도합니다.
    /// 응답에 <c>Retry-After</c> 헤더가 있으면 그 값을 우선 사용합니다.
    /// 재시도 횟수 소진 시 예외를 던져 상위 failover 루프로 전달합니다.
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(
        string baseUrl, Func<string, Task<T>> action, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action(baseUrl);
            }
            catch (SearXngApiException ex) when (ex.StatusCode is 429 or 503)
            {
                if (attempt >= _options.MaxRetries)
                {
                    throw;
                }

                var delay = ComputeRetryDelay(attempt, ex.RetryAfterSeconds);
                _logger.LogWarning(
                    "리플리카 {Url}이 {Status}를 반환했습니다. {Delay}ms 후 재시도합니다 ({Attempt}/{Max}).",
                    baseUrl, ex.StatusCode, (int)delay.TotalMilliseconds, attempt + 1, _options.MaxRetries);
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < _options.MaxRetries)
            {
                var delay = ComputeRetryDelay(attempt, null);
                _logger.LogWarning(
                    "리플리카 {Url}에 연결할 수 없습니다. {Delay}ms 후 재시도합니다 ({Attempt}/{Max}).",
                    baseUrl, (int)delay.TotalMilliseconds, attempt + 1, _options.MaxRetries);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 재시도 지연을 계산합니다. <paramref name="retryAfterSeconds"/>(서버 지정)이 있으면
    /// 그것을, 없으면 <c>RetryBaseDelaySeconds * 2^attempt</c>에 ±25% jitter를 적용합니다.
    /// <see cref="SearXngOptions.RetryMaxDelaySeconds"/>(또는 Retry-After)를 초과하지 않습니다.
    /// </summary>
    private TimeSpan ComputeRetryDelay(int attempt, double? retryAfterSeconds)
    {
        var maxDelay = TimeSpan.FromSeconds(_options.RetryMaxDelaySeconds);

        if (retryAfterSeconds is > 0)
        {
            return TimeSpan.FromSeconds(Math.Min(retryAfterSeconds.Value, _options.RetryMaxDelaySeconds));
        }

        var delay = TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds * Math.Pow(2, attempt));
        if (delay > maxDelay)
        {
            delay = maxDelay;
        }

        // ±25% jitter: 동시 재시도가 같은 시점에 몰리는 것을 분산
        var jitter = 1.0 + (Random.Shared.NextDouble() * 0.5 - 0.25);
        return TimeSpan.FromMilliseconds(delay.TotalMilliseconds * jitter);
    }

    /// <summary>
    /// 인스턴스별 레이트 리미트 슬롯을 획득합니다. <see cref="SearXngOptions.RateLimitWaitEnabled"/>
    /// 이 true면 한도 초과 시 슬롯이 비어질 때까지(최대 <see cref="SearXngOptions.RateLimitWaitMaxSeconds"/>)
    /// 대기하고, false면 즉시 true/false를 반환합니다.
    /// </summary>
    private async Task<bool> AcquireRateLimitSlotAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var limiter = _rateLimiters.GetOrAdd(
            baseUrl, _ => new SlidingWindowRateLimiter(TimeSpan.FromSeconds(_options.RateLimitWindowSeconds)));

        if (!_options.RateLimitWaitEnabled)
        {
            return limiter.TryAcquire(_options.RateLimitPerMinute);
        }

        return await limiter.WaitAsync(
            _options.RateLimitPerMinute,
            TimeSpan.FromSeconds(_options.RateLimitWaitMaxSeconds),
            cancellationToken);
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
            (int)response.StatusCode,
            ParseRetryAfterSeconds(response));
    }

    /// <summary>
    /// 429/503 응답의 <c>Retry-After</c> 헤더(초 단위 delta 또는 날짜)를 파싱합니다.
    /// </summary>
    private static double? ParseRetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta.TotalSeconds;
        }

        if (retryAfter?.Date is { } date)
        {
            var seconds = (date - DateTimeOffset.UtcNow).TotalSeconds;
            return seconds > 0 ? seconds : null;
        }

        return null;
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
