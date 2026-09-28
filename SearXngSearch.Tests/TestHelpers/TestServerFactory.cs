using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SearXngSearch.Options;

namespace SearXngSearch.Tests.TestHelpers;

/// <summary>
/// 실제 SearXNG 인스턴스 없이 MCP 서버 전체를 인메모리에서 띄우는
/// <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// <see cref="IHttpClientFactory"/>를 <see cref="FakeHttpClientFactory"/>로 교체합니다.
/// </summary>
public class TestServerFactory : WebApplicationFactory<Program>
{
    /// <summary>테스트에서 SearXNG 응답을 등록하는 공용 핸들러.</summary>
    public FakeSearXngHandler Handler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("SearXng:BaseUrl", "http://fake:8080");

        builder.ConfigureTestServices(services =>
        {
            // AddSearXngClient()의 IHttpClientFactory 등록보다 늦게 등록되어
            // GetRequiredService 시 이 팩토리가 우선 사용됩니다.
            services.AddSingleton<IHttpClientFactory>(sp =>
                new FakeHttpClientFactory(Handler, sp.GetRequiredService<IOptions<SearXngOptions>>().Value));
        });
    }
}

/// <summary>Bearer 토큰 인증이 활성화된 서버 팩토리 (AuthToken = "test-token").</summary>
public sealed class AuthenticatedTestServerFactory : TestServerFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("SearXng:AuthToken", "test-token");
    }
}
