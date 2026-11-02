using Xunit;

namespace AgentTrace.Testing;

/// <summary>Polls until a condition that depends on another thread (followers, watchers) is met.</summary>
public static class Eventually
{
    public static void Until(string what, Func<bool> condition, int timeoutMillis = 5_000, int pollMillis = 10)
    {
        long limit = Environment.TickCount64 + timeoutMillis;
        while (Environment.TickCount64 < limit)
        {
            if (condition()) return;
            Thread.Sleep(pollMillis);
        }
        Assert.Fail("Timed out waiting for: " + what);
    }
}
