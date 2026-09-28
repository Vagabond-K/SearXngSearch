using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SearXngSearch.Mcp.Tools;
using SearXngSearch.Options;
using SearXngSearch.Services;

// 공통 MCP 서버 구성 (stdio / Streamable HTTP 모드 공유)
static IMcpServerBuilder AddMcpServerCore(IServiceCollection services, IConfiguration configuration)
{
    // SearXNG 옵션 바인딩 (appsettings.json의 "SearXng" 섹션)
    services.Configure<SearXngOptions>(
        configuration.GetSection(SearXngOptions.SectionName));

    // SearXNG API용 HttpClient 팩토리
    services.AddSearXngClient();

    // 검색 결과 캐싱용 메모리 캐시
    services.AddMemoryCache();

    // SearXNG 클라이언트
    services.AddSingleton<SearXngClient>();

    return services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "searxng-search-mcp-server",
                Version = "1.0.0",
            };
            options.ServerInstructions =
                "SearXNG 메타 검색 엔진을 통한 웹 검색 도구입니다. " +
                "web_search로 일반 웹 검색, news_search로 뉴스 검색을 수행합니다.";
        })
        .WithTools<SearchTools>();
}

// stdio 모드: MCP 클라이언트(Claude Desktop, Cursor, VS Code 등)의 자식 프로세스로 실행
// stdout은 MCP 프로토콜 채널이므로 모든 로그는 stderr로 출력해야 합니다.
if (args.Contains("--stdio"))
{
    // MCP 클라이언트가 다른 작업 디렉터리에서 프로세스를 시작해도
    // appsettings.json이 로드되도록 content root를 앱 디렉터리로 고정합니다.
    var stdioBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

    AddMcpServerCore(stdioBuilder.Services, stdioBuilder.Configuration)
        .WithStdioServerTransport();

    // 기본 콘솔 프로바이더(stdout 출력)를 제거하고 stderr 전용으로 재등록합니다.
    // stdout은 MCP 프로토콜 채널이므로 로그가 섞이면 안 됩니다.
    stdioBuilder.Logging.ClearProviders();
    stdioBuilder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    await stdioBuilder.Build().RunAsync();
    return;
}

// Windows 서비스로 실행되면 작업 디렉터리가 C:\Windows\System32가 되어
// appsettings.json이 로드되지 않으므로 content root를 앱 디렉터리로 고정합니다.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Windows 서비스로 실행되도록 구성 (서비스로 등록되지 않으면 일반 프로세스로 동작)
// ServiceName은 install-service.ps1이 등록하는 서비스명과 일치해야 합니다.
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "SearXngSearchMcpServer";
});

// MCP 서버 등록 (Streamable HTTP 전송)
AddMcpServerCore(builder.Services, builder.Configuration)
    .WithHttpTransport(options =>
    {
        // Stateless 모드: 세션 상태가 필요 없는 검색 서버에 적합하며
        // 수평 확장 및 세션 어피니티 없이 동작합니다.
        options.SessionMode = HttpServerSessionMode.Stateless;
    });

// 브라우저 기반 클라이언트(MCP Inspector 등)를 위한 CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

// Bearer 토큰 인증 (SearXng:AuthToken 설정 시에만 활성화)
// /health는 모니터링을 위해 항상 열려 있습니다.
var authToken = builder.Configuration.GetSection(SearXngOptions.SectionName)["AuthToken"];
if (!string.IsNullOrWhiteSpace(authToken))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path == "/health")
        {
            await next();
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (!string.Equals(header, $"Bearer {authToken}", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsync("Unauthorized: 유효한 Bearer 토큰이 필요합니다.");
            return;
        }

        await next();
    });

    app.Logger.LogInformation("Bearer 토큰 인증이 활성화되었습니다.");
}

// 헬스 체크 엔드포인트
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "searxng-search-mcp-server" }));

// MCP Streamable HTTP 엔드포인트
app.MapMcp("/mcp");

// 바인딩 URL: --urls 인자/appsettings의 "Urls" 설정 → app.Urls → 기본값 순으로 확인
// (app.Urls는 UseUrls()/app.Urls.Add()로만 채워지므로 --urls 인자는 설정에서 확인해야 함)
// 여러 URL은 ';'로 구분되어 올 수 있으므로 각각의 /mcp 엔드포인트를 로그에 남긴다.
var urls = builder.Configuration["Urls"]
    ?? string.Join(";", app.Urls.Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase)));

if (string.IsNullOrWhiteSpace(urls))
{
    urls = "http://localhost:5000";
}

foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
{
    app.Logger.LogInformation("SearXNG MCP 서버 시작: {Url}/mcp", url.TrimEnd('/'));
}

app.Run();
