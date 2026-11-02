using System.Globalization;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Presentation.Text;

/// <summary>Text of the header of the file viewer (details and warnings).</summary>
public static class FileHeader
{
    /// <summary>One line with the essentials: encoding, line ending, number of lines and size. Empty if there is no content to show.</summary>
    public static string Details(FileContent content)
    {
        if (!content.Readable) return content.Binary ? string.Format(CultureInfo.CurrentCulture, TextStrings.FileBinaryDetailsFormat, Size(content.Size)) : "";
        int lines = content.Lines.Count;
        List<string> parts =
        [
            ReasonText.Encoding(content.Encoding),
            LineEndingText(content.LineEnding),
            string.Format(CultureInfo.CurrentCulture, Plural.Pick(lines, TextStrings.FileLines_One, TextStrings.FileLines_Other), LocalNumbers.Grouped(lines)),
            Size(content.Size),
        ];
        return string.Join(" · ", parts);
    }

    /// <summary>Warnings the user must see (truncated, unexpected encoding, link, mixed line endings).</summary>
    public static IReadOnlyList<string> Warnings(FileContent content)
    {
        List<string> warnings = [];
        if (content.Truncated)
        {
            warnings.Add(string.Format(CultureInfo.CurrentCulture, TextStrings.FileTruncatedWarningFormat, FileContentReader.MaxBytes / (1024 * 1024)));
        }
        if (content.EncodingNote is { } note) warnings.Add(ReasonText.Describe(note, null));
        if (content.LinkTarget is not null) warnings.Add(string.Format(CultureInfo.CurrentCulture, TextStrings.FileLinkWarningFormat, content.LinkTarget));
        if (content.Readable && content.LineEnding == LineEnding.Mixed) warnings.Add(TextStrings.FileMixedEndingsWarning);
        return warnings;
    }

    /// <summary>What is shown instead of the text when it cannot be shown, or null if there is text.</summary>
    public static string? Placeholder(FileContent content)
    {
        if (content.Problem is { } problem) return ReasonText.Describe(problem, content.ProblemDetail);
        if (content.Binary) return string.Format(CultureInfo.CurrentCulture, TextStrings.FileBinaryPlaceholderFormat, Size(content.Size));
        if (content.Lines.Count == 0) return TextStrings.FileEmptyPlaceholder;
        return null;
    }

    public static string Size(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024 * 1024) return LocalNumbers.OneDecimal(bytes / 1024m) + " KB";
        return LocalNumbers.OneDecimal(bytes / (1024m * 1024m)) + " MB";
    }

    private static string LineEndingText(LineEnding ending) => ending switch
    {
        LineEnding.None => TextStrings.LineEndingNone,
        LineEnding.Lf => "LF",
        LineEnding.CrLf => "CRLF",
        LineEnding.Cr => "CR",
        LineEnding.Mixed => TextStrings.LineEndingMixed,
        _ => throw new System.Diagnostics.UnreachableException(),
    };
}
