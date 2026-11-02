using AgentTrace.Core;
using AgentTrace.Presentation.Text;
using AgentTrace.Presentation.Tree;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Presentation.Tests.Text;

/// <summary>New tests (no Java twin): the icon tables by name, the Java texts and the telemetry row record.</summary>
public sealed class IconNamesAndJavaTextTest
{
    private static FileNode Node(string name, bool directory = false, bool symlink = false) =>
        new(new FilePath("project").Combine(name), directory, false, symlink);

    [Fact]
    public void FileIconsFollowTheEntryKind()
    {
        Assert.Equal(Codicon.RootFolder, FileIconNames.Of(Node("p", directory: true), false, true));
        Assert.Equal(Codicon.Folder, FileIconNames.Of(Node("src", directory: true), false, false));
        Assert.Equal(Codicon.FolderOpened, FileIconNames.Of(Node("src", directory: true), true, false));
        Assert.Equal(Codicon.FileSymlinkDirectory, FileIconNames.Of(Node("l", directory: true, symlink: true), false, false));
        Assert.Equal(Codicon.FileSymlinkFile, FileIconNames.Of(Node("l", symlink: true), false, false));
        Assert.Equal(Codicon.FileCode, FileIconNames.Of(Node("A.java"), false, false));
        Assert.Equal(Codicon.Gear, FileIconNames.Of(Node("a.yml"), false, false));
        Assert.Equal(Codicon.Markdown, FileIconNames.Of(Node("README.md"), false, false));
        Assert.Equal(Codicon.FileMedia, FileIconNames.Of(Node("a.png"), false, false));
        Assert.Equal(Codicon.FileZip, FileIconNames.Of(Node("a.zip"), false, false));
        Assert.Equal(Codicon.FilePdf, FileIconNames.Of(Node("a.pdf"), false, false));
        Assert.Equal(Codicon.FileBinary, FileIconNames.Of(Node("a.exe"), false, false));
        Assert.Equal(Codicon.File, FileIconNames.Of(Node("a.txt"), false, false));
    }

    [Fact]
    public void StepIconsFollowTheToolKindAndTheState()
    {
        Assert.Equal(Codicon.Eye, StepIconNames.Of(ToolKind.Read, false));
        Assert.Equal(Codicon.NewFile, StepIconNames.Of(ToolKind.Write, true));
        Assert.Equal(Codicon.Edit, StepIconNames.Of(ToolKind.Write, false));
        Assert.Equal(Codicon.Terminal, StepIconNames.Of(ToolKind.Execute, false));
        Assert.Equal(Codicon.Search, StepIconNames.Of(ToolKind.Search, false));
        Assert.Equal(Codicon.RepoForked, StepIconNames.Of(ToolKind.Delegate, false));
        Assert.Equal(Codicon.Tools, StepIconNames.Of(ToolKind.Other, false));
        Assert.Equal(Codicon.Pass, StepIconNames.Of(StepState.Ok));
        Assert.Equal(Codicon.Error, StepIconNames.Of(StepState.Error));
        Assert.Equal(Codicon.Sync, StepIconNames.Of(StepState.Running));
        Assert.Equal(Codicon.Question, StepIconNames.Of(StepState.NoResult));
        Assert.True(StepIconNames.Creates(ChangeKind.Created));
        Assert.False(StepIconNames.Creates(ChangeKind.Modified));
    }

    [Fact]
    public void EveryKindOfChangeHasItsSymbolStyleAndLabels()
    {
        foreach (ChangeKind kind in Enum.GetValues<ChangeKind>())
        {
            Assert.False(string.IsNullOrEmpty(ChangeStyle.Symbol(kind)));
            Assert.StartsWith("status-", ChangeStyle.StyleClass(kind), StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(ChangeStyle.Label(kind)));
            Assert.False(string.IsNullOrEmpty(ChangeStyle.PluralLabel(kind)));
        }
        Assert.Equal("−", ChangeStyle.Symbol(ChangeKind.Deleted));
    }

    [Fact]
    public void JavaWhitespaceIsNotTheSameAsDotNetWhitespace()
    {
        // Java's replaceAll("\\s+", " ") only groups ASCII spaces: the non-breaking space stays
        Assert.Equal("a b c", JavaText.OneLine("a \t\n b\r\nc", 100));
        Assert.Equal("a b", JavaText.OneLine("a b", 100));
        // strip() does not remove the non-breaking space but does remove the U+001F separator
        Assert.Equal(" x ", JavaText.Strip(" x "));
        Assert.Equal("x", JavaText.Strip("\u001Fx\u001F"));
        Assert.Equal("abc…", JavaText.OneLine("abcdef", 3));
    }

    [Fact]
    public void ARowPrintsItsTextAndKeepsItsPath()
    {
        Assert.Equal("hello", TelemetryRow.Plain("hello").ToString());
        Assert.Null(TelemetryRow.Plain("hello").Path);
        var row = new TelemetryRow("x", new FilePath("a.txt"));
        Assert.Equal(new FilePath("a.txt"), row.Path);
    }
}
