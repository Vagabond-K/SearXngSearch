namespace SearXngSearch.Services;

/// <summary>
/// 슬라이딩 윈도우(1분) 레이트 리미터. SearXNG 인스턴스별로 사용되어
/// 단일 인스턴스에 과도한 요청이 집중되는 것을 방지합니다.
/// </summary>
public sealed class SlidingWindowRateLimiter
{
    private readonly object _lock = new();
    private readonly Queue<DateTime> _timestamps = new();
    private readonly TimeSpan _window;

    /// <param name="windowDuration">슬라이딩 윈도우 기간 (기본 1분).</param>
    public SlidingWindowRateLimiter(TimeSpan? windowDuration = null)
    {
        _window = windowDuration ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// 슬롯을 시도 획득합니다. 최근 윈도우 기간 내 요청이 한도를 초과하면 false를 반환합니다.
    /// </summary>
    public bool TryAcquire(int limitPerMinute)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var windowStart = now - _window;

            while (_timestamps.Count > 0 && _timestamps.Peek() < windowStart)
            {
                _timestamps.Dequeue();
            }

            if (_timestamps.Count >= limitPerMinute)
            {
                return false;
            }

            _timestamps.Enqueue(now);
            return true;
        }
    }
}
