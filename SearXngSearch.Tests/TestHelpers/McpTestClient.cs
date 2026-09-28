using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SearXngSearch.Tests.TestHelpers;

/// <summary>
/// MCP Streamable HTTP 엔드포인트(<c>/mcp</c>)에 raw JSON-RPC 요청을 보내는 테스트 클라이언트.
/// 응답이 <c>application/json</c>이든 <c>text/event-stream</c>이든 모두 파싱합니다.
/// </summary>
public sealed class McpTestClient
{
    private readonly HttpClient _http;
    private int _nextId;

    public McpTestClient(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
    }

    /// <summary>Bearer 토큰을 설정합니다.</summary>
    public void SetBearerToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>JSON-RPC 요청을 보내고 응답을 파싱합니다.</summary>
    /// <returns>(HTTP 성공 여부, result, error, HTTP 상태 코드)</returns>
    public async Task<(bool Success, JsonElement? Result, JsonElement? Error, int StatusCode)> SendAsync(
        string method, object? @params = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ++_nextId,
            ["method"] = method,
        };
        if (@params is not null)
        {
            payload["params"] = @params;
        }

        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("/mcp", content);
        var body = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";

        var json = mediaType.Contains("event-stream", StringComparison.OrdinalIgnoreCase)
            ? ParseSse(body)
            : JsonSerializer.Deserialize<JsonElement>(body);

        JsonElement? result = null;
        JsonElement? error = null;
        if (json.ValueKind == JsonValueKind.Object)
        {
            if (json.TryGetProperty("result", out var r)) result = r;
            if (json.TryGetProperty("error", out var e)) error = e;
        }

        return (response.IsSuccessStatusCode, result, error, (int)response.StatusCode);
    }

    /// <summary>MCP tool 결과(<c>result.content[]</c>)의 텍스트를 합쳐서 반환합니다.</summary>
    public static string ExtractToolText(JsonElement? result)
    {
        if (result is not { ValueKind: JsonValueKind.Object })
        {
            return "";
        }

        var sb = new StringBuilder();
        if (result.Value.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var text))
                {
                    sb.Append(text.GetString());
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>SSE 본문에서 data: 줄의 JSON을 추출합니다.</summary>
    private static JsonElement ParseSse(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("data:", StringComparison.Ordinal))
            {
                var json = trimmed["data:".Length..].Trim();
                if (json.Length > 0)
                {
                    return JsonSerializer.Deserialize<JsonElement>(json);
                }
            }
        }

        throw new InvalidOperationException($"SSE 본문에서 data: 줄을 찾을 수 없습니다: {body}");
    }
}
