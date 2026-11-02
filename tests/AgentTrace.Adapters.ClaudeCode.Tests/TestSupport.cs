using AgentTrace.Core;

namespace AgentTrace.Adapters.ClaudeCode.Tests;

internal static class TestSupport
{
    /// <summary>Test resource copied to the output directory (<c>TestData/claude/...</c>, the equivalent of <c>src/test/resources</c>).</summary>
    public static FilePath Resource(string relative) =>
        new FilePath(AppContext.BaseDirectory).Combine("TestData").Combine(relative.Split('/'));
}

/// <summary>Temporary folder per test (JUnit's <c>@TempDir</c>): xUnit creates a new instance of the class for each test.</summary>
public abstract class TempDirTest : AgentTrace.Testing.TempDirTestBase
{
    protected FilePath Tmp => Dir;
}
