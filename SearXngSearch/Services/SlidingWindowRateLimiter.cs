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

    /// <summary>
    /// 슬롯을 획득하기 위해 대기합니다. 한도 초과 시 가장 오래된 요청이 윈도우에서
    /// 밀려날 때까지(최대 <paramref name="maxWait"/>) 기다린 후 획득합니다.
    /// </summary>
    /// <returns>획득 성공 여부. 대기 시간 초과 시 false.</returns>
    public async Task<bool> WaitAsync(int limitPerMinute, TimeSpan maxWait, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + maxWait;

        while (true)
        {
            if (TryAcquire(limitPerMinute))
            {
                return true;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            // 슬롯이 비어질 때까지는 대기해도 소용이 없으므로, 가장 오래된 요청이
            // 윈도우에서 만료되는 시점(±여유)까지만 잠든 뒤 재시도합니다.
            var wait = ComputeWaitUntilSlotFrees(limitPerMinute);
            if (wait > remaining)
            {
                wait = remaining;
            }

            try
            {
                await Task.Delay(wait, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
        }
    }

    private TimeSpan ComputeWaitUntilSlotFrees(int limitPerMinute)
    {
        lock (_lock)
        {
            if (_timestamps.Count < limitPerMinute)
            {
                return TimeSpan.FromMilliseconds(50);
            }

            // 큐의 (limitPerMinute - 1)번째 항목이 윈도우 밖으로 나가는 시점
            var oldest = _timestamps.ElementAt(limitPerMinute - 1);
            var wait = _window - (DateTime.UtcNow - oldest);
            return wait > TimeSpan.Zero ? wait + TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(50);
        }
    }
}
