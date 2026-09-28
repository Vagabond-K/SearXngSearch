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
}
