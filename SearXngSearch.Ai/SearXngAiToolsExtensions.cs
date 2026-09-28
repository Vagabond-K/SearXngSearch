using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace SearXngSearch.Ai;

/// <summary>
/// <see cref="SearXngAiTools"/>를 <see cref="AITool"/> 배열로 변환하는 확장 메서드.
/// Microsoft Agent Framework의 <c>AsAIAgent(tools: ...)</c>에 그대로 전달할 수 있습니다.
/// </summary>
public static class SearXngAiToolsExtensions
{
    /// <summary>
    /// SearXNG 검색 도구(<c>web_search</c>, <c>news_search</c>, <c>check_searxng_status</c>,
    /// <c>get_instance_info</c>, <c>get_search_suggestions</c>)를 <see cref="AITool"/> 배열로 생성합니다.
    /// </summary>
    public static AITool[] AsSearXngTools(this SearXngAiTools tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return
        [
            AIFunctionFactory.Create(tools.WebSearch, name: "web_search"),
            AIFunctionFactory.Create(tools.NewsSearch, name: "news_search"),
            AIFunctionFactory.Create(tools.CheckSearXngStatus, name: "check_searxng_status"),
            AIFunctionFactory.Create(tools.GetInstanceInfo, name: "get_instance_info"),
            AIFunctionFactory.Create(tools.GetSearchSuggestions, name: "get_search_suggestions"),
        ];
    }

    /// <summary>
    /// DI 컨테이너에서 <see cref="SearXngAiTools"/>를 조회해 <see cref="AITool"/> 배열로 생성합니다.
    /// </summary>
    public static AITool[] GetSearXngTools(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<SearXngAiTools>().AsSearXngTools();
    }
}
