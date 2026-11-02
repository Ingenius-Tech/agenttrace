namespace AgentTrace.Presentation.Heatmap;

/// <summary>
/// What has happened to a file (or, summed, to a folder): reads, agent changes, external changes and errors.
/// <c>Writes</c>: create + modify + delete + external.
/// </summary>
public readonly record struct FileActivity(int Reads, int Created, int Modified, int Deleted, int External, int Errors)
{
    public int Writes => Created + Modified + Deleted + External;

    /// <summary>Everything, unfiltered.</summary>
    public int Total => Reads + Writes + Errors;

    public FileActivity Plus(FileActivity other) => new(Reads + other.Reads, Created + other.Created, Modified + other.Modified,
        Deleted + other.Deleted, External + other.External, Errors + other.Errors);

    /// <summary>The heat: the sum of the active kinds.</summary>
    public int Value(ActivityFilter filter) => (filter.Reads ? Reads : 0) + (filter.Writes ? Writes : 0) + (filter.Errors ? Errors : 0);
}
