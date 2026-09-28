using System.Text.Json;
using System.Text.Json.Serialization;

namespace SearXngSearch.Models;

/// <summary>
/// SearXNG <c>/search?format=json</c> 응답.
/// </summary>
public sealed class SearXngSearchResponse
{
    /// <summary>검색 결과 목록.</summary>
    [JsonPropertyName("results")]
    public List<SearXngResult> Results { get; set; } = [];

    /// <summary>인기 검색어 (선택).</summary>
    [JsonPropertyName("suggestions")]
    public List<string> Suggestions { get; set; } = [];

    /// <summary>검색에 걸린 시간(밀리초).</summary>
    [JsonPropertyName("took")]
    public int? Took { get; set; }

    /// <summary>사용된 검색 엔진 목록 (선택).</summary>
    [JsonPropertyName("number_of_results")]
    public int? NumberOfResults { get; set; }
}

/// <summary>
/// SearXNG 검색 결과 1건.
/// </summary>
public sealed class SearXngResult
{
    /// <summary>결과 제목.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>결과 URL.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>결과 요약/설명.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>검색 엔진 이름 (예: google, duckduckgo).</summary>
    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

    /// <summary>공신력 점수 (0~100, 선택).</summary>
    [JsonPropertyName("score")]
    public double? Score { get; set; }

    /// <summary>카테고리 (예: general, news).</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>게시/업로드 날짜 (선택).</summary>
    [JsonPropertyName("publishedDate")]
    public string? PublishedDate { get; set; }

    /// <summary>이미지 URL (선택).</summary>
    [JsonPropertyName("img_src")]
    public string? ImgSrc { get; set; }

    /// <summary>추가 메타데이터 (선택).</summary>
    [JsonPropertyName("metadata")]
    public JsonElement? Metadata { get; set; }
}

/// <summary>
/// SearXNG <c>/config</c> 응답 (인스턴스 정보).
/// </summary>
public sealed class SearXngConfigResponse
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("categories")]
    public JsonElement? Categories { get; set; }

    [JsonPropertyName("engines")]
    public List<SearXngConfigEngine>? Engines { get; set; }
}

/// <summary>SearXNG 설정의 검색 엔진.</summary>
public sealed class SearXngConfigEngine
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("short_name")]
    public string? ShortName { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

/// <summary>SearXNG 인스턴스 정보 요약.</summary>
public sealed class SearXngInstanceInfo
{
    public string BaseUrl { get; init; } = "";
    public string? Version { get; init; }
    public List<string> Categories { get; init; } = [];
    public List<string> Engines { get; init; } = [];
    public int EnabledEngineCount { get; init; }
}

/// <summary>
/// SearXNG 응답용 JSON 직렬화 옵션.
/// </summary>
public static class SearXngJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
