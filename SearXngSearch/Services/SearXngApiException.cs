namespace SearXngSearch.Services;

/// <summary>
/// SearXNG API 호출 중 발생한 오류.
/// </summary>
/// <param name="message">오류 설명.</param>
/// <param name="statusCode">HTTP 상태 코드 (해당하는 경우).</param>
public sealed class SearXngApiException : Exception
{
    public int? StatusCode { get; }

    public SearXngApiException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public SearXngApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
