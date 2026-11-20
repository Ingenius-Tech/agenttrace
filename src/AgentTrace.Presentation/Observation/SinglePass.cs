namespace AgentTrace.Presentation.Observation;

/// <summary>
/// One background pass at a time. The result of a pass is delivered on the UI thread only if the pass was not cancelled meanwhile
/// (the data it computed belongs to a session that is gone), and a pass that throws frees the slot and delivers nothing, so the next
/// refresh can run and the pane keeps what it showed. Used from the UI thread only.
/// </summary>
public sealed class SinglePass
{
    private long _epoch;

    /// <summary>True from <see cref="TryRun{T}"/> until the pass has been delivered, has failed or has been cancelled.</summary>
    public bool Busy { get; private set; }

    /// <summary>
    /// Starts <paramref name="work"/> on another thread and hands its result to <paramref name="apply"/> through <paramref name="onUi"/>.
    /// Returns null, and does nothing, while another pass is running; otherwise the task of the background part (for tests).
    /// </summary>
    public Task? TryRun<T>(Func<T> work, Action<T> apply, Action<Action> onUi)
    {
        if (Busy) return null;
        Busy = true;
        long mine = _epoch;
        return Task.Run(() =>
        {
            T? result = default;
            bool ok = true;
            try
            {
                result = work();
            }
            catch (Exception)
            {
                ok = false; // a failed pass changes nothing; the pane keeps its previous state
            }
            onUi(() =>
            {
                if (mine != _epoch) return;
                Busy = false;
                if (ok) apply(result!);
            });
        });
    }

    /// <summary>Discards the pass in flight, if any (its result is never applied) and frees the slot.</summary>
    public void Cancel()
    {
        _epoch++;
        Busy = false;
    }
}
