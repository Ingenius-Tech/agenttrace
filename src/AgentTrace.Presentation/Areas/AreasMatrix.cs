using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Core.Flow;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Presentation.Areas;

/// <summary>What the matrix is built from: the flow (turns and prompts), the activity per file, and the rules of each repository.</summary>
public sealed record AreasSnapshot(FlowGraph Flow, AreaActivity Activity, AreaRepoSet Repos, FilePath Project)
{
    public static readonly AreasSnapshot Empty = new(new FlowBuilder().Snapshot(), AreaActivity.Empty, AreaRepoSet.Empty, new FilePath("."));

    /// <summary>The tasks of the flow, in order.</summary>
    public ValueList<AreaTask> Tasks { get; } = TaskGrouping.Of(Flow.Turns);

    /// <summary>Computes the activity per file from the flow and the git changes. Call it off the UI thread: it walks every step.</summary>
    public static AreasSnapshot Of(FlowGraph flow, IReadOnlyList<ChangeEntry> changes, AreaRepoSet repos, FilePath project) =>
        new(flow, AreaActivityBuilder.Build(flow, changes, project), repos, project);
}

/// <summary>A row is an area of the chosen dimension in one repository (<c>AreaId</c> null: the files that match none).</summary>
public readonly record struct AreaRowKey(FilePath Repo, string? AreaId);

/// <summary><c>Files</c>: files with activity in the row. <c>Total</c>: what the agent did there over all turns.</summary>
public sealed record AreaRow(AreaRowKey Key, string Name, FileActivity Total, int Files);

/// <summary>A file's activity in one cell. <c>Order</c>: position of its first touch within the turn; <c>WriteOrder</c>: that of its first change, if any.</summary>
public sealed record AreaFileCell(FilePath Path, FileActivity Activity, int Order, int? WriteOrder);

/// <summary>The activity of an area in a turn, with its files ordered by first touch.</summary>
public sealed record AreaCell(FileActivity Activity, ValueList<AreaFileCell> Files);

/// <summary>A visible turn. <c>Band</c>: index into <see cref="AreasMatrix.Bands"/>.</summary>
public sealed record AreaColumn(int Turn, int Band);

/// <summary>The columns of a task: <c>FirstColumn</c> and <c>Count</c> are column indexes (tasks without activity have no band).</summary>
public sealed record AreaBand(AreaTask Task, int FirstColumn, int Count);

/// <summary>
/// A line of the screen: an area row (<c>File</c> null), a file of an expanded row or the "+N more" line. <c>Row</c> is the area row it belongs to.
/// </summary>
public sealed record AreaDisplayRow(int Row, FilePath? File, string Label, FileActivity Total, bool IsMore);

/// <summary>
/// The Areas map: rows (areas of one dimension, per repository), columns (the turns with activity, grouped into tasks) and cells.
/// Built from an <see cref="AreasSnapshot"/>; it has no Avalonia dependency, so it is tested without a window.
/// </summary>
public sealed class AreasMatrix
{
    public const int FilesPerExpandedRow = 15;

    public static readonly AreasMatrix Empty = Build(AreasSnapshot.Empty, AreaDimension.Module, ActivityFilter.All);

    private readonly Dictionary<(int Row, int Column), AreaCell> _cells;
    private readonly AreasSnapshot _snapshot;

    private AreasMatrix(AreasSnapshot snapshot, AreaDimension dimension, ActivityFilter filter, IReadOnlyList<AreaRow> rows,
        IReadOnlyList<AreaColumn> columns, IReadOnlyList<AreaBand> bands, Dictionary<(int Row, int Column), AreaCell> cells)
    {
        _snapshot = snapshot;
        Dimension = dimension;
        Filter = filter;
        Rows = rows;
        Columns = columns;
        Bands = bands;
        _cells = cells;
        MaxCellValue = cells.Values.Select(c => c.Activity.Value(filter)).DefaultIfEmpty(0).Max();
        MaxRowValue = rows.Select(r => r.Total.Value(filter)).DefaultIfEmpty(0).Max();
    }

    public AreasSnapshot Snapshot => _snapshot;

    public AreaDimension Dimension { get; }

    public ActivityFilter Filter { get; }

    public IReadOnlyList<AreaRow> Rows { get; }

    public IReadOnlyList<AreaColumn> Columns { get; }

    public IReadOnlyList<AreaBand> Bands { get; }

    /// <summary>The highest value of a cell and of a row, for the heat scale.</summary>
    public int MaxCellValue { get; }

    public int MaxRowValue { get; }

    public bool IsEmpty => Rows.Count == 0;

    public AreaCell? Cell(int row, int column) => _cells.GetValueOrDefault((row, column));

    public int ColumnOfTurn(int turn)
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].Turn == turn) return i;
        }
        return -1;
    }

    /// <summary>The index of the first row with that area id (any repository), or -1.</summary>
    public int RowOfArea(string areaId)
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Key.AreaId == areaId) return i;
        }
        return -1;
    }

    /// <summary>The band of a task, or null if it has no visible column.</summary>
    public AreaBand? BandOfTask(int taskIndex) => Bands.FirstOrDefault(b => b.Task.Index == taskIndex);

    /// <summary>
    /// How a task spread: for each row where the agent changed something in the task, the cell of its first change, in time order (column,
    /// then position within the turn). Joined by a line it shows how a change propagates across the rows. Empty if the task has no visible column.
    /// </summary>
    public IReadOnlyList<(int Row, int Column)> Propagation(int taskIndex)
    {
        if (BandOfTask(taskIndex) is not { } band) return [];
        List<(int Row, int Column, int Order)> first = [];
        for (int r = 0; r < Rows.Count; r++)
        {
            for (int c = band.FirstColumn; c < band.FirstColumn + band.Count; c++)
            {
                int? order = Cell(r, c)?.Files.Where(f => f.WriteOrder is not null).Select(f => f.WriteOrder).Min();
                if (order is null) continue;
                first.Add((r, c, order.Value));
                break;
            }
        }
        return [.. first.OrderBy(p => p.Column).ThenBy(p => p.Order).ThenBy(p => p.Row).Select(p => (p.Row, p.Column))];
    }

    /// <summary>The hue of an activity: the heaviest active kind (at equal weight: error, write, read), or none.</summary>
    public static HeatHue Hue(FileActivity a, ActivityFilter filter)
    {
        int reads = filter.Reads ? a.Reads : 0;
        int writes = filter.Writes ? a.Writes : 0;
        int errors = filter.Errors ? a.Errors : 0;
        if (reads + writes + errors == 0) return HeatHue.None;
        if (errors >= writes && errors >= reads) return HeatHue.Error;
        return writes >= reads ? HeatHue.Modified : HeatHue.Read;
    }

    // ---- rows on the screen ----

    /// <summary>The lines to draw: each row followed, if expanded, by its files with activity (up to <see cref="FilesPerExpandedRow"/>) and a "+N more" line.</summary>
    public IReadOnlyList<AreaDisplayRow> DisplayRows(IReadOnlySet<AreaRowKey> expanded)
    {
        List<AreaDisplayRow> lines = [];
        for (int r = 0; r < Rows.Count; r++)
        {
            AreaRow row = Rows[r];
            lines.Add(new AreaDisplayRow(r, null, row.Name, row.Total, false));
            if (!expanded.Contains(row.Key)) continue;
            Dictionary<FilePath, FileActivity> perFile = [];
            for (int c = 0; c < Columns.Count; c++)
            {
                if (Cell(r, c) is not { } cell) continue;
                foreach (AreaFileCell file in cell.Files) perFile[file.Path] = perFile.GetValueOrDefault(file.Path).Plus(file.Activity);
            }
            List<KeyValuePair<FilePath, FileActivity>> ordered =
                [.. perFile.OrderByDescending(kv => kv.Value.Value(Filter)).ThenBy(kv => kv.Key.Value, StringComparer.Ordinal)];
            foreach (KeyValuePair<FilePath, FileActivity> file in ordered.Take(FilesPerExpandedRow))
            {
                lines.Add(new AreaDisplayRow(r, file.Key, RelativeToProject(file.Key), file.Value, false));
            }
            if (ordered.Count > FilesPerExpandedRow) lines.Add(new AreaDisplayRow(r, null, AreaText.MoreFiles(ordered.Count - FilesPerExpandedRow), default, true));
        }
        return lines;
    }

    /// <summary>What a line shows in a column: the cell of an area row, or the activity of that file in that turn.</summary>
    public FileActivity ActivityAt(AreaDisplayRow line, int column)
    {
        if (line.IsMore || Cell(line.Row, column) is not { } cell) return default;
        if (line.File is not { } file) return cell.Activity;
        foreach (AreaFileCell f in cell.Files)
        {
            if (f.Path == file) return f.Activity;
        }
        return default;
    }

    private string RelativeToProject(FilePath path) =>
        path.IsUnder(_snapshot.Project) ? path.RelativeTo(_snapshot.Project).Replace('\\', '/') : path.Value;

    // ---- construction ----

    private sealed class RowAcc(AreaRowKey key, string name, int order)
    {
        public readonly AreaRowKey Key = key;
        public string Name = name;
        public readonly int Order = order;
        public FileActivity Total;
        public readonly HashSet<FilePath> Files = [];
    }

    public static AreasMatrix Build(AreasSnapshot snapshot, AreaDimension dimension, ActivityFilter filter)
    {
        Dictionary<AreaRowKey, RowAcc> rows = [];
        Dictionary<(AreaRowKey Row, int Turn), List<AreaFileCell>> cells = [];
        foreach (AreaTurnActivity turn in snapshot.Activity.Turns)
        {
            foreach (AreaFileActivity file in turn.Files)
            {
                var activity = new FileActivity(file.Reads, 0, file.Writes, 0, file.External, file.Errors);
                if (activity.Value(filter) == 0) continue;
                (AreaRepo? repo, AreaAssignment assignment) = snapshot.Repos.Classify(file.Path);
                var key = new AreaRowKey(repo?.Root ?? snapshot.Project, repo is null ? null : assignment.Of(dimension));
                if (!rows.TryGetValue(key, out RowAcc? row)) rows[key] = row = new RowAcc(key, NameOf(repo, key), OrderOf(repo, key, dimension));
                row.Total = row.Total.Plus(activity);
                row.Files.Add(file.Path);
                if (!cells.TryGetValue((key, turn.Turn), out List<AreaFileCell>? list)) cells[(key, turn.Turn)] = list = [];
                list.Add(new AreaFileCell(file.Path, activity, file.Order, file.WriteOrder));
            }
        }

        // Rows: areas in the order of their rules (suggested modules by id), "Unclassified" last
        List<FilePath> repoOrder = [.. snapshot.Repos.Repos.Select(r => r.Root)];
        List<RowAcc> ordered = [.. rows.Values
            .OrderBy(r => r.Key.AreaId is null ? 1 : 0)
            .ThenBy(r => repoOrder.IndexOf(r.Key.Repo))
            .ThenBy(r => r.Order)
            .ThenBy(r => r.Key.AreaId, StringComparer.Ordinal)];
        if (ordered.Select(r => r.Key.Repo).Distinct().Count() > 1)
        {
            foreach (RowAcc r in ordered) r.Name = RepoName(snapshot, r.Key.Repo) + " / " + r.Name;
        }
        Dictionary<AreaRowKey, int> rowIndex = ordered.Select((r, i) => (r.Key, i)).ToDictionary(x => x.Key, x => x.i);

        // Columns: the turns that have some visible activity, grouped by task
        SortedSet<int> turns = [.. cells.Keys.Select(k => k.Turn)];
        List<AreaColumn> columns = [];
        List<AreaBand> bands = [];
        foreach (int turn in turns)
        {
            AreaTask? task = snapshot.Tasks.FirstOrDefault(t => turn >= t.FirstTurn && turn <= t.LastTurn);
            if (task is null) continue;
            if (bands.Count == 0 || bands[^1].Task.Index != task.Index) bands.Add(new AreaBand(task, columns.Count, 0));
            bands[^1] = bands[^1] with { Count = bands[^1].Count + 1 };
            columns.Add(new AreaColumn(turn, bands.Count - 1));
        }
        Dictionary<int, int> columnOfTurn = columns.Select((c, i) => (c.Turn, i)).ToDictionary(x => x.Turn, x => x.i);
        Dictionary<(int Row, int Column), AreaCell> grid = [];
        foreach (KeyValuePair<(AreaRowKey Row, int Turn), List<AreaFileCell>> entry in cells)
        {
            if (!columnOfTurn.TryGetValue(entry.Key.Turn, out int column)) continue;
            FileActivity sum = default;
            foreach (AreaFileCell f in entry.Value) sum = sum.Plus(f.Activity);
            grid[(rowIndex[entry.Key.Row], column)] =
                new AreaCell(sum, entry.Value.OrderBy(f => f.Order).ThenBy(f => f.Path.Value, StringComparer.Ordinal).ToValueList());
        }
        return new AreasMatrix(snapshot, dimension, filter,
            [.. ordered.Select(r => new AreaRow(r.Key, r.Name, r.Total, r.Files.Count))], columns, bands, grid);
    }

    private static string NameOf(AreaRepo? repo, AreaRowKey key) =>
        key.AreaId is null ? AreaStrings.Unclassified : repo?.Find(key.AreaId)?.Name ?? key.AreaId;

    private static int OrderOf(AreaRepo? repo, AreaRowKey key, AreaDimension dimension)
    {
        // The author's order for a rules file; suggested areas are ordered by id (see Build)
        if (key.AreaId is null || repo is null || repo.Source != AreaSource.File) return 0;
        int i = 0;
        foreach (AreaRule rule in repo.Rules.Areas)
        {
            if (rule.Dimension == dimension && rule.Id == key.AreaId) return i;
            i++;
        }
        return int.MaxValue;
    }

    private static string RepoName(AreasSnapshot snapshot, FilePath root) =>
        snapshot.Repos.Repos.FirstOrDefault(r => r.Root == root)?.Name ?? (root.FileName.Length > 0 ? root.FileName : root.Value);
}
