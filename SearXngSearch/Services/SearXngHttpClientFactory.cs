using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SearXngSearch.Options;

namespace SearXngSearch.Services;

/// <summary>
/// SearXNG API용 <see cref="HttpClient"/> 팩토리 등록.
/// </summary>
public static class SearXngHttpClientFactory
{
    public const string ClientName = "searxng";

    public static IServiceCollection AddSearXngClient(this IServiceCollection services)
    {
        services.AddHttpClient(ClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SearXngOptions>>().Value;

            // BaseAddress를 설정하지 않습니다. 리플리카 failover를 위해
            // 각 요청마다 절대 URI를 직접 구성하기 때문입니다.
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

            // SearXNG는 User-Agent가 비어 있거나 기본 .NET UA인 경우
            // 일부 인스턴스에서 403을 반환할 수 있습니다.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SearXngSearch/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }
        });

        return services;
    }
}
