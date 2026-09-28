using SearXngSearch.Options;

namespace SearXngSearch.Tests.TestHelpers;

/// <summary>
/// <see cref="SearXngClient"/>가 실제 네트워크 대신 <see cref="FakeSearXngHandler"/>를
/// 사용하도록 하는 <see cref="IHttpClientFactory"/> 대체 구현.
/// </summary>
public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly FakeSearXngHandler _handler;
    private readonly SearXngOptions _options;

    public FakeHttpClientFactory(FakeSearXngHandler handler, SearXngOptions? options = null)
    {
        _handler = handler;
        _options = options ?? new SearXngOptions();
    }

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(_handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        return client;
    }
}
