using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace SearXngSearch.Tests.TestHelpers;

/// <summary>
/// SearXNG 인스턴스를 흉내내는 <see cref="HttpMessageHandler"/>.
/// 호스트+경로별로 응답을 등록하고, 미등록 조합에는 연결 실패(<see cref="HttpRequestException"/>)를
/// 발생시켜 failover 시나리오를 오프라인에서 검증할 수 있습니다.
/// </summary>
public sealed class FakeSearXngHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _handlers = new();
    private readonly ConcurrentDictionary<string, int> _callCounts = new();
    private readonly ConcurrentDictionary<string, Uri> _lastUris = new();

    /// <summary>호스트+경로에 커스텀 응답 핸들러를 등록합니다.</summary>
    public void SetHandler(string host, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _handlers[$"{host}{path}"] = handler;
    }

    /// <summary>호스트+경로에 JSON 응답을 등록합니다.</summary>
    public void SetJson(string host, string json, string path = "/search", int statusCode = 200)
    {
        SetHandler(host, path, _ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));
    }

    /// <summary>호스트+경로에 HTML 응답을 등록합니다.</summary>
    public void SetHtml(string host, string html, string path = "/search", int statusCode = 200)
    {
        SetHandler(host, path, _ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        }));
    }

    /// <summary>호스트+경로에 상태 코드만 반환하는 응답을 등록합니다.</summary>
    public void SetStatus(string host, int statusCode, string path = "/search", string body = "")
    {
        SetHandler(host, path, _ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain"),
        }));
    }

    /// <summary>호스트에 대한 요청 횟수를 반환합니다.</summary>
    public int CallCount(string host) => _callCounts.TryGetValue(host, out var count) ? count : 0;

    /// <summary>호스트에 대한 마지막 요청 URI를 반환합니다.</summary>
    public Uri? LastRequestUri(string host) => _lastUris.TryGetValue(host, out var uri) ? uri : null;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri!.Host;
        var path = request.RequestUri.AbsolutePath;
        _callCounts.AddOrUpdate(host, 1, (_, v) => v + 1);
        _lastUris[host] = request.RequestUri;

        if (!_handlers.TryGetValue($"{host}{path}", out var handler))
        {
            // 미등록 호스트/경로 = 인스턴스 미실행/네트워크 오류 시뮬레이션
            throw new HttpRequestException($"Fake SearXNG: '{host}{path}'에 응답이 등록되어 있지 않습니다.");
        }

        return await handler(request);
    }
}
