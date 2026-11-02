namespace AgentTrace.Presentation.Heatmap;

/// <summary>Which kinds of activity count as "heat" in the map: reads, writes (create, modify, delete, external changes) and errors.</summary>
public readonly record struct ActivityFilter(bool Reads, bool Writes, bool Errors)
{
    public static ActivityFilter All { get; } = new(true, true, true);

    /// <summary>Active kinds.</summary>
    public int Count => (Reads ? 1 : 0) + (Writes ? 1 : 0) + (Errors ? 1 : 0);

    /// <summary>
    /// From <c>reads,writes,errors</c> (case-insensitive; the Spanish names "lecturas", "escrituras", "errores" are also accepted).
    /// With no valid name, all are active. Whatever is not understood is ignored.
    /// </summary>
    public static ActivityFilter Parse(string? text)
    {
        bool reads = false, writes = false, errors = false;
        foreach (string part in (text ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "reads" or "lecturas": reads = true; break;
                case "writes" or "escrituras": writes = true; break;
                case "errors" or "errores": errors = true; break;
            }
        }
        return reads || writes || errors ? new ActivityFilter(reads, writes, errors) : All;
    }
}
