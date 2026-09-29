using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SearXngSearch.Options;
using SearXngSearch.Services;

// SearXngSearch 라이브러리를 AI 프레임워크 없이,
// 일반 .NET 콘솔 앱에서 직접 사용하는 예제입니다.
//
// 실행:
//   dotnet run --project Samples/SimpleSearchSample
//   (SearXNG 인스턴스 주소를 바꾸려면)
//   $env:SEARXNG_BASE_URL = "http://<searxng-host>:8080"; dotnet run --project Samples/SimpleSearchSample

var baseUrl = Environment.GetEnvironmentVariable("SEARXNG_BASE_URL") ?? "http://localhost:8080";

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    }).SetMinimumLevel(LogLevel.Warning));
services.AddMemoryCache();
services.Configure<SearXngOptions>(options => options.BaseUrl = baseUrl);
services.AddSearXngClient();
services.AddSingleton<SearXngClient>();

using var provider = services.BuildServiceProvider();
var searXng = provider.GetRequiredService<SearXngClient>();
var searXngOptions = provider.GetRequiredService<IOptions<SearXngOptions>>().Value;

Console.WriteLine("=== SearXNG 검색 샘플 (SearXngSearch 라이브러리) ===");
Console.WriteLine($"인스턴스: {baseUrl}");
Console.WriteLine("검색어를 입력하세요. (종료: exit / quit / q)");
Console.WriteLine();

while (true)
{
    Console.Write("검색어> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (input is "exit" or "quit" or "q")
    {
        break;
    }

    try
    {
        var response = await searXng.SearchAsync(input, maxResults: 5);
        Console.WriteLine();
        Console.WriteLine(SearchResultFormatter.ToText(response, input, 5, searXngOptions.MaxSnippetLength, searXngOptions.MaxOutputChars));
    }
    catch (SearXngApiException ex)
    {
        Console.WriteLine();
        Console.WriteLine($"[오류] {ex.Message}");
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine();
        Console.WriteLine($"[오류] SearXNG 인스턴스에 연결할 수 없습니다: {ex.Message}");
    }

    Console.WriteLine();
}
