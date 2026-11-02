namespace AgentTrace.Tests;

// Shared by every *.Tests project (see tests/Directory.Build.props): checks that the
// project builds and starts under xUnit v3. It also avoids exit code 8 ("no tests")
// from Microsoft.Testing.Platform while a project has no tests of its own yet.
public sealed class BuildSanityTest
{
    [Fact]
    public void TestInfrastructureRuns() => Assert.True(true);
}
