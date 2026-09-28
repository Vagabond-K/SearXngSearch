using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SearXngSearch.Options;
using SearXngSearch.Services;

namespace SearXngSearch.Tests.TestHelpers;

/// <summary>
/// <see cref="SearXngClient"/>를 <see cref="FakeSearXngHandler"/>와 함께 생성하는 헬퍼.
/// </summary>
public static class ClientFactory
{
    public static (SearXngClient Client, FakeSearXngHandler Handler) Create(SearXngOptions? options = null)
    {
        options ??= new SearXngOptions();

        // BaseUrl을 명시하지 않은 테스트는 "fake" 호스트(핸들러 응답 등록 대상)를 사용하도록 통일.
        // failover 테스트처럼 BaseUrl을 명시한 경우(리플리카/다운 호스트)는 그대로 유지.
        if (options.BaseUrl == "http://localhost:8080")
        {
            options.BaseUrl = "http://fake:8080";
        }
        var handler = new FakeSearXngHandler();
        var factory = new FakeHttpClientFactory(handler, options);
        var client = new SearXngClient(
            factory,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<SearXngClient>.Instance,
            new MemoryCache(new MemoryCacheOptions()));

        return (client, handler);
    }
}
