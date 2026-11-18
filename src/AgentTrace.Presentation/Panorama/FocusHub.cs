namespace AgentTrace.Presentation.Panorama;

/// <summary>
/// Holds the current focus of the Panorama and decides whether a published focus must be applied. It ignores everything while it is not
/// <see cref="Active"/> (the separate tabs keep working on their own), a focus equal to the current one, and anything published while it is
/// applying one (a change caused by a link never re-publishes). The panes that receive it are those other than its origin.
/// </summary>
public sealed class FocusHub
{
    /// <summary>The focus being shown, or null.</summary>
    public TraceFocus? Current { get; private set; }

    /// <summary>True while the Panorama is shown: only then do selections link.</summary>
    public bool Active { get; set; }

    /// <summary>True while the <c>apply</c> action of <see cref="Publish"/> runs.</summary>
    public bool Applying { get; private set; }

    /// <summary>The panes that receive a focus from <paramref name="origin"/>.</summary>
    public static IReadOnlyList<FocusOrigin> ConsumersOf(FocusOrigin origin) =>
        [.. Enum.GetValues<FocusOrigin>().Where(o => o != origin)];

    /// <summary>
    /// A user action: a new focus (or null: nothing is selected any more). Runs <paramref name="apply"/> with it and returns true, only when
    /// the hub accepted it as a change.
    /// </summary>
    public bool Publish(TraceFocus? focus, Action<TraceFocus?> apply)
    {
        if (!Active || Applying || Equals(focus, Current)) return false;
        Current = focus;
        Applying = true;
        try
        {
            apply(focus);
        }
        finally
        {
            Applying = false;
        }
        return true;
    }

    /// <summary>Forgets the focus without applying anything (leaving the Panorama).</summary>
    public void Clear() => Current = null;
}
