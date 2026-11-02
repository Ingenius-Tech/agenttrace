namespace AgentTrace.Core;

/// <summary>
/// Source of a project's file changes, compared with a baseline. Read-only: it never modifies the project.
/// Today there is one implementation (git); the interface allows testing without git and adding others, such as its own snapshot.
/// The methods may throw <see cref="IOException"/>.
/// </summary>
public interface IChangeSource : IDisposable
{
    /// <summary>Files changed with respect to the baseline, ordered by path.</summary>
    IReadOnlyList<FileChange> Changes();

    /// <summary>Previous and current versions of a file in <see cref="Changes"/>.</summary>
    FileVersions Versions(FilePath path);

    /// <summary>Takes the current state as the new baseline.</summary>
    void ResetBaseline();
}
