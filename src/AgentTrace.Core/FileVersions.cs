namespace AgentTrace.Core;

/// <summary>Why the text of a file cannot be compared. Presentation turns it into user text.</summary>
public enum UnavailableReason
{
    /// <summary>The content is binary.</summary>
    Binary,

    /// <summary>The file (or a diff over it) exceeds the size limits.</summary>
    TooLarge,

    /// <summary>The baseline snapshot of the file could not be kept.</summary>
    NoBaseline,

    /// <summary>The path is a folder or a submodule, not a file.</summary>
    SubmoduleOrFolder,

    /// <summary>The project has no repository, so there is no baseline to compare with.</summary>
    NoRepository,

    /// <summary>The file does not belong to any observed repository.</summary>
    OutsideObservedRepositories,
}

/// <summary>
/// The two versions of a file that are compared. <c>Before</c>: text at the baseline, or null if it did not exist.
/// <c>After</c>: current text, or null if it was deleted. <c>UnavailableReason</c>: why there is no text to compare
/// (binary, too large, no baseline...), or null if they can be compared.
/// </summary>
public sealed record FileVersions(FilePath Path, string? Before, string? After, UnavailableReason? UnavailableReason)
{
    public static FileVersions Unavailable(FilePath path, UnavailableReason reason) => new(path, null, null, reason);
}
