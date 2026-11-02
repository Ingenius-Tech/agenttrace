using System.Diagnostics;
using AgentTrace.Presentation.Tree;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Presentation.Text;

/// <summary>
/// The icons the application uses, by name (the codicons of VS Code). Presentation knows no fonts or glyphs:
/// Desktop translates each name to its character of the <c>codicon.ttf</c> font.
/// </summary>
public enum Codicon
{
    FileSymlinkDirectory, RootFolder, FolderOpened, Folder, FileSymlinkFile, FileCode, Gear, Markdown, FileMedia, FileZip, FilePdf,
    FileBinary, File, Eye, NewFile, Edit, Terminal, Search, RepoForked, Tools, Pass, Error, Sync, Question,
    ArrowDown, ArrowRight, ChevronDown, ChevronRight, Close, CollapseAll, Comment, CommentDiscussion, Database, Diff, ExpandAll,
    EyeClosed, Files, Filter, GoToFile, Info, Link, Person, Pulse, ScreenFull, ZoomIn, ZoomOut,
}

/// <summary>Which icon each tree entry carries (Java's <c>FileIcons.ikon</c>, without the graphic control).</summary>
public static class FileIconNames
{
    public static Codicon Of(FileNode node, bool expanded, bool projectRoot)
    {
        if (node.Directory)
        {
            if (node.Symlink) return Codicon.FileSymlinkDirectory;
            if (projectRoot) return Codicon.RootFolder;
            return expanded ? Codicon.FolderOpened : Codicon.Folder;
        }
        if (node.Symlink) return Codicon.FileSymlinkFile;
        return FileKinds.Of(node.Name) switch
        {
            FileKind.Code => Codicon.FileCode,
            FileKind.Config => Codicon.Gear,
            FileKind.Markup => Codicon.Markdown,
            FileKind.Image => Codicon.FileMedia,
            FileKind.Archive => Codicon.FileZip,
            FileKind.Pdf => Codicon.FilePdf,
            FileKind.Binary => Codicon.FileBinary,
            FileKind.Text => Codicon.File,
            _ => throw new UnreachableException(),
        };
    }
}

/// <summary>Which icon each graph step carries (Java's <c>StepIcons.of</c>, without the drawing).</summary>
public static class StepIconNames
{
    public static Codicon Of(ToolKind kind, bool creates) => kind switch
    {
        ToolKind.Read => Codicon.Eye,
        ToolKind.Write => creates ? Codicon.NewFile : Codicon.Edit,
        ToolKind.Execute => Codicon.Terminal,
        ToolKind.Search => Codicon.Search,
        ToolKind.Delegate => Codicon.RepoForked,
        ToolKind.Other => Codicon.Tools,
        _ => throw new UnreachableException(),
    };

    public static Codicon Of(StepState state) => state switch
    {
        StepState.Ok => Codicon.Pass,
        StepState.Error => Codicon.Error,
        StepState.Running => Codicon.Sync,
        StepState.NoResult => Codicon.Question,
        _ => throw new UnreachableException(),
    };

    public static bool Creates(ChangeKind kind) => kind == ChangeKind.Created;
}

/// <summary>Which icon each status bar counter carries (Java's <c>StatusBar.icon</c>).</summary>
public static class StatusIconNames
{
    public static Codicon Of(StatusSegments.SegmentKind kind) => kind switch
    {
        StatusSegments.SegmentKind.Events => Codicon.Pulse,
        StatusSegments.SegmentKind.Turns => Codicon.CommentDiscussion,
        StatusSegments.SegmentKind.Tools => Codicon.Tools,
        StatusSegments.SegmentKind.Errors => Codicon.Error,
        StatusSegments.SegmentKind.Files => Codicon.Files,
        StatusSegments.SegmentKind.External => Codicon.Person,
        StatusSegments.SegmentKind.Subagents => Codicon.RepoForked,
        StatusSegments.SegmentKind.Tokens => Codicon.Database,
        _ => throw new UnreachableException(),
    };
}
