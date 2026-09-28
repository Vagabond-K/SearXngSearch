using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using SearXngSearch.Ai;
using SearXngSearch.Options;
using SearXngSearch.Services;
using System.ClientModel;

// Microsoft Agent Framework(MAF) 에이전트에 SearXNG 검색 도구를
// 인 프로세스(AITool)로 연결하는 예제입니다.
// MCP 서버/프로세스 없이 SearXngSearch 라이브러리를 직접 사용합니다.
//
// 환경 변수:
//   OPENAI_API_KEY   — OpenAI API 키 (필수)
//   OPENAI_BASE_URL  — (선택) OpenAI 호환 엔드포인트. vLLM 등 로컬 LLM 사용 시
//                      예: http://localhost:8000/v1
//   OPENAI_MODEL     — (선택) 모델 이름. 기본: gpt-4o-mini
//   SEARXNG_BASE_URL — (선택) SearXNG 인스턴스 주소. 기본: http://localhost:8080
//
// 실행:
//   $env:OPENAI_API_KEY = "sk-..."; dotnet run --project Samples/MicrosoftAgentFrameworkSample

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("OPENAI_API_KEY 환경 변수가 설정되어 있지 않습니다.");
var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
var searXngUrl = Environment.GetEnvironmentVariable("SEARXNG_BASE_URL") ?? "http://localhost:8080";

// 1) SearXNG 검색 클라이언트 + AI 도구 등록
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    }).SetMinimumLevel(LogLevel.Warning));
services.AddMemoryCache();
services.Configure<SearXngOptions>(options => options.BaseUrl = searXngUrl);
services.AddSearXngClient();
services.AddSingleton<SearXngClient>();
services.AddSingleton<SearXngAiTools>();

using var provider = services.BuildServiceProvider();
AITool[] searchTools = provider.GetSearXngTools();

// 2) OpenAI(호환) 채팅 클라이언트 생성
var openAiClient = string.IsNullOrWhiteSpace(baseUrl)
    ? new OpenAIClient(apiKey)
    : new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });

// 3) MAF 에이전트 생성 — SearXNG 도구를 인 프로세스 함수 도구로 연결
AIAgent agent = openAiClient.GetChatClient(model).AsAIAgent(
    name: "SearXngSearchAgent",
    instructions:
        "웹 검색 도구를 사용해 사용자의 질문에 답하는 에이전트입니다. " +
        "최신 정보, 뉴스, 사실 확인이 필요할 때 web_search 또는 news_search 도구를 사용하세요. " +
        "답변에는 출처 URL을 포함하세요.",
    tools: searchTools);

Console.WriteLine("=== Microsoft Agent Framework + SearXNG 검색 에이전트 ===");
Console.WriteLine($"모델: {model}");
Console.WriteLine($"SearXNG: {searXngUrl}");
Console.WriteLine($"도구: {string.Join(", ", searchTools.Select(t => t.Name))}");
Console.WriteLine("질문을 입력하세요. (종료: exit / quit / q)");
Console.WriteLine();

while (true)
{
    Console.Write("질문> ");
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
        var response = await agent.RunAsync(input);
        Console.WriteLine();
        Console.WriteLine(response.Text);
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"[오류] {ex.Message}");
    }

    Console.WriteLine();
}
