using Avalonia.Media;
using AgentTrace.Presentation.Text;

namespace AgentTrace.Desktop.Theme;

/// <summary>The <c>codicon.ttf</c> icon font (CC BY 4.0) and the character that corresponds to each <see cref="Codicon"/>.</summary>
public static class Codicons
{
    public static readonly FontFamily Font = new("avares://AgentTrace.Desktop/Assets#codicon");

    public static string Glyph(Codicon icon) => char.ConvertFromUtf32(Code(icon));

    public static int Code(Codicon icon) => icon switch
    {
        Codicon.FileSymlinkDirectory => 0xeaed, Codicon.RootFolder => 0xeb46, Codicon.FolderOpened => 0xeaf7,
        Codicon.Folder => 0xea83, Codicon.FileSymlinkFile => 0xeaee, Codicon.FileCode => 0xeae9, Codicon.Gear => 0xeaf8,
        Codicon.Markdown => 0xeb1d, Codicon.FileMedia => 0xeaea, Codicon.FileZip => 0xeaef, Codicon.FilePdf => 0xeaeb,
        Codicon.FileBinary => 0xeae8, Codicon.File => 0xea7b, Codicon.Eye => 0xea70, Codicon.NewFile => 0xea7f,
        Codicon.Edit => 0xea73, Codicon.Terminal => 0xea85, Codicon.Search => 0xea6d, Codicon.RepoForked => 0xea63,
        Codicon.Tools => 0xeb6d, Codicon.Pass => 0xeba4, Codicon.Error => 0xea87, Codicon.Sync => 0xea77,
        Codicon.Question => 0xeb32, Codicon.ArrowDown => 0xea9a, Codicon.ArrowRight => 0xea9c, Codicon.ChevronDown => 0xeab4,
        Codicon.ChevronRight => 0xeab6, Codicon.Close => 0xea76, Codicon.CollapseAll => 0xeac5, Codicon.Comment => 0xea6b,
        Codicon.CommentDiscussion => 0xeac7, Codicon.Database => 0xeace, Codicon.Diff => 0xeae1, Codicon.ExpandAll => 0xeb95,
        Codicon.EyeClosed => 0xeae7, Codicon.Files => 0xeaf0, Codicon.Filter => 0xeaf1, Codicon.GoToFile => 0xea94,
        Codicon.Info => 0xea74, Codicon.Link => 0xeb15, Codicon.Person => 0xea67, Codicon.Pulse => 0xeb31,
        Codicon.ScreenFull => 0xeb4c, Codicon.ZoomIn => 0xeb81, Codicon.ZoomOut => 0xeb82,
        _ => throw new System.Diagnostics.UnreachableException(),
    };
}
