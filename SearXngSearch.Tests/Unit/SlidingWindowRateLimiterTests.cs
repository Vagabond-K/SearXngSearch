using SearXngSearch.Services;
using Xunit;

namespace SearXngSearch.Tests.Unit;

public class SlidingWindowRateLimiterTests
{
    [Fact]
    public void TryAcquire_한도_이내_성공()
    {
        var limiter = new SlidingWindowRateLimiter();

        for (var i = 0; i < 30; i++)
        {
            Assert.True(limiter.TryAcquire(30));
        }
    }

    [Fact]
    public void TryAcquire_한도_초과_실패()
    {
        var limiter = new SlidingWindowRateLimiter();

        for (var i = 0; i < 2; i++)
        {
            Assert.True(limiter.TryAcquire(2));
        }

        Assert.False(limiter.TryAcquire(2));
    }

    [Fact]
    public void TryAcquire_윈도우_만료_후_재획득_가능()
    {
        var limiter = new SlidingWindowRateLimiter(windowDuration: TimeSpan.FromMilliseconds(100));

        Assert.True(limiter.TryAcquire(1));
        Assert.False(limiter.TryAcquire(1));

        Thread.Sleep(150);

        Assert.True(limiter.TryAcquire(1));
    }

    [Fact]
    public void TryAcquire_동시_호출_시_한도_엄격_준수()
    {
        var limiter = new SlidingWindowRateLimiter();
        var successes = 0;
        var lockObj = new object();

        Parallel.For(0, 100, _ =>
        {
            if (limiter.TryAcquire(10))
            {
                lock (lockObj) successes++;
            }
        });

        Assert.Equal(10, successes);
    }

    [Fact]
    public async Task WaitAsync_한도_이내_즉시_획득()
    {
        var limiter = new SlidingWindowRateLimiter();

        Assert.True(await limiter.WaitAsync(1, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task WaitAsync_윈도우_만료_대기_후_획득()
    {
        var limiter = new SlidingWindowRateLimiter(windowDuration: TimeSpan.FromMilliseconds(150));

        Assert.True(await limiter.WaitAsync(1, TimeSpan.FromSeconds(2)));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(await limiter.WaitAsync(1, TimeSpan.FromSeconds(2)));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 100, $"대기 시간 부족: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitAsync_대기_시간_초과_시_false()
    {
        var limiter = new SlidingWindowRateLimiter(windowDuration: TimeSpan.FromMinutes(1));

        Assert.True(await limiter.WaitAsync(1, TimeSpan.FromSeconds(1)));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(await limiter.WaitAsync(1, TimeSpan.FromMilliseconds(100)));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5000, $"대기 초과: {sw.ElapsedMilliseconds}ms");
    }
}
