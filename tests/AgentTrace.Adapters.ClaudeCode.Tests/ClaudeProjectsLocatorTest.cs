using System.Text.Json;
using AgentTrace.Core;

namespace AgentTrace.Adapters.ClaudeCode.Tests;

public sealed class ClaudeProjectsLocatorTest : TempDirTest
{
    [Fact]
    public void EncodeReplacesEveryNonAlphanumericCharacter()
    {
        Assert.Equal("D--Work-projects-agenttrace-studio",
            ClaudeProjectsLocator.Encode("D:\\Work\\projects\\agenttrace-studio"));
        Assert.Equal("D--Work-projects-1-sample", ClaudeProjectsLocator.Encode("D:\\Work\\projects\\1_sample"));
        Assert.Equal("-home-u-my-app", ClaudeProjectsLocator.Encode("/home/u/my_app"));
    }

    /// <summary>New test: Java's <c>agent-.+\.jsonl</c> requires at least one character and the dot does not match line terminators.</summary>
    [Fact]
    public void SubagentFileNamesFollowJavasMatchesRules()
    {
        Assert.True(ClaudeProjectsLocator.IsSubagentFileName("agent-abc123.jsonl"));
        Assert.True(ClaudeProjectsLocator.IsSubagentFileName("agent-a b.jsonl"));
        Assert.False(ClaudeProjectsLocator.IsSubagentFileName("agent-.jsonl"));
        Assert.False(ClaudeProjectsLocator.IsSubagentFileName("agent-abc.meta.json"));
        Assert.False(ClaudeProjectsLocator.IsSubagentFileName("x-agent-abc.jsonl"));
        Assert.False(ClaudeProjectsLocator.IsSubagentFileName("agent-a\nb.jsonl"));
        Assert.False(ClaudeProjectsLocator.IsSubagentFileName("agent-a" + (char)0x2028 + "b.jsonl"));
        Assert.False(ClaudeProjectsLocator.IsSubagentFileName("agent-abc.jsonl\n"));
    }

    [Fact]
    public void FindsFolderByExactName()
    {
        FilePath project = Tmp.Combine("my_project");
        Directory.CreateDirectory(project.Value);
        FilePath folder = Tmp.Combine("root", ClaudeProjectsLocator.Encode(project.Value));
        Directory.CreateDirectory(folder.Value);
        Assert.Equal(folder, new ClaudeProjectsLocator(Tmp.Combine("root"), false).ProjectFolder(project));
    }

    [Fact]
    public void IgnoresLetterCaseWhenAllowed()
    {
        FilePath project = Tmp.Combine("p");
        Directory.CreateDirectory(project.Value);
        string name = ClaudeProjectsLocator.Encode(project.Value);
        FilePath folder = Tmp.Combine("root", name.ToLowerInvariant());
        Directory.CreateDirectory(folder.Value);
        // On case-sensitive file systems the name search resolves it; on the others (Windows) the exact
        // match already does. Either way it must be the same folder.
        FilePath found = new ClaudeProjectsLocator(Tmp.Combine("root"), true).ProjectFolder(project)
            ?? throw new Xunit.Sdk.XunitException("folder not found");
        Assert.Equal(folder, found);
    }

    [Fact]
    public void FallsBackToCwdDeclaredInTheTraces()
    {
        FilePath project = Tmp.Combine("real project");
        Directory.CreateDirectory(project.Value);
        FilePath folder = Tmp.Combine("root", "name-that-does-not-match");
        Directory.CreateDirectory(folder.Value);
        string line = JsonSerializer.Serialize(new Dictionary<string, string> { ["type"] = "user", ["cwd"] = project.Value });
        File.WriteAllText(folder.Combine("x.jsonl").Value, "{\"type\":\"mode\"}\n" + line + "\n");
        Assert.Equal(folder, new ClaudeProjectsLocator(Tmp.Combine("root"), false).ProjectFolder(project));
    }

    [Fact]
    public void UnknownProjectOrMissingRootGivesNothing()
    {
        FilePath project = Tmp.Combine("other");
        Directory.CreateDirectory(project.Value);
        Assert.Empty(new ClaudeProjectsLocator(Tmp.Combine("does-not-exist"), false).FindSessionFiles(project));
        Directory.CreateDirectory(Tmp.Combine("root").Value);
        Assert.Empty(new ClaudeProjectsLocator(Tmp.Combine("root"), false).FindSessionFiles(project));
    }

    [Fact]
    public void ListsSessionsNewestFirstAndSubagentsSeparately()
    {
        FilePath project = Tmp.Combine("proj");
        Directory.CreateDirectory(project.Value);
        FilePath folder = Tmp.Combine("root", ClaudeProjectsLocator.Encode(project.Value));
        Directory.CreateDirectory(folder.Value);
        FilePath old = folder.Combine("aaaaaaaa-0000-0000-0000-000000000001.jsonl");
        File.WriteAllText(old.Value, "{}\n");
        FilePath recent = folder.Combine("bbbbbbbb-0000-0000-0000-000000000002.jsonl");
        File.WriteAllText(recent.Value, "{}\n");
        FilePath subDir = folder.Combine("aaaaaaaa-0000-0000-0000-000000000001", "subagents");
        Directory.CreateDirectory(subDir.Value);
        FilePath sub = subDir.Combine("agent-x1.jsonl");
        File.WriteAllText(sub.Value, "{}\n");
        File.WriteAllText(subDir.Combine("agent-x1.meta.json").Value, "{}");
        File.WriteAllText(folder.Combine("notes.txt").Value, "not a trace");
        File.SetLastWriteTimeUtc(old.Value, DateTimeOffset.FromUnixTimeMilliseconds(1_000_000).UtcDateTime);
        File.SetLastWriteTimeUtc(sub.Value, DateTimeOffset.FromUnixTimeMilliseconds(2_000_000).UtcDateTime);
        File.SetLastWriteTimeUtc(recent.Value, DateTimeOffset.FromUnixTimeMilliseconds(3_000_000).UtcDateTime);

        IReadOnlyList<FilePath> files = new ClaudeProjectsLocator(Tmp.Combine("root"), false).FindSessionFiles(project);
        Assert.Equal(new[] { recent, old }, files);
        var locator = new ClaudeProjectsLocator(Tmp.Combine("root"), false);
        Assert.Equal(new[] { sub }, locator.FindSubagentFiles(old));
        Assert.Empty(locator.FindSubagentFiles(recent));
    }
}
