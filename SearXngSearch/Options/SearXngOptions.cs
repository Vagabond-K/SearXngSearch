namespace SearXngSearch.Options;

/// <summary>
/// SearXNG 인스턴스 연결 옵션. appsettings.json의 "SearXng" 섹션에서 바인딩됩니다.
/// </summary>
public sealed class SearXngOptions
{
    /// <summary>설정 섹션 이름.</summary>
    public const string SectionName = "SearXng";

    /// <summary>
    /// SearXNG 인스턴스 기본 URL (예: https://searx.be 또는 http://localhost:8080).
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>
    /// API 키 (선택). 인스턴스에 인증이 활성화된 경우에만 필요합니다.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>HTTP 요청 타임아웃(초).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>기본 반환 결과 수.</summary>
    public int DefaultMaxResults { get; set; } = 10;

    /// <summary>요청할 수 있는 최대 결과 수.</summary>
    public int MaxResultsLimit { get; set; } = 30;

    /// <summary>기본 검색 언어 (예: "ko", "en", "ja", "all").</summary>
    public string DefaultLanguage { get; set; } = "all";

    /// <summary>기본 시간 범위 필터 (day/week/month/year, null이면 필터 없음).</summary>
    public string? DefaultTimeRange { get; set; }

    /// <summary>기본 검색 카테고리 (예: "general", "news", "it", "science").</summary>
    public string? DefaultCategories { get; set; }

    /// <summary>
    /// MCP 엔드포인트 인증용 Bearer 토큰. 설정하면 /mcp 요청에
    /// <c>Authorization: Bearer &lt;token&gt;</c> 헤더가 필요합니다. (null = 인증 없음)
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>검색 결과 캐싱 활성화 여부.</summary>
    public bool CacheEnabled { get; set; } = true;

    /// <summary>캐시 유지 시간(초).</summary>
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>인스턴스당 분당 최대 요청 수 (슬라이딩 윈도우 레이트 리미트).</summary>
    public int RateLimitPerMinute { get; set; } = 30;

    /// <summary>
    /// HTML 폴백 활성화 여부. JSON API가 비활성화된 인스턴스(403/404)에서
    /// HTML 응답을 파싱합니다 (best-effort, 메타데이터 제한).
    /// </summary>
    public bool HtmlFallbackEnabled { get; set; } = false;
}
