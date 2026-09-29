namespace SearXngSearch.Services;

/// <summary>
/// SearXNG API 호출 중 발생한 오류.
/// </summary>
/// <param name="message">오류 설명.</param>
/// <param name="statusCode">HTTP 상태 코드 (해당하는 경우).</param>
public sealed class SearXngApiException : Exception
{
    public int? StatusCode { get; }

    /// <summary>
    /// 서버가 지정한 재시도 대기 시간(초). 429 응답의 <c>Retry-After</c> 헤더가
    /// 있을 때 설정됩니다.
    /// </summary>
    public double? RetryAfterSeconds { get; }

    public SearXngApiException(string message, int? statusCode = null, double? retryAfterSeconds = null)
        : base(message)
    {
        StatusCode = statusCode;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public SearXngApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
