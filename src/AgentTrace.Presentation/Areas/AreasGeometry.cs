namespace AgentTrace.Presentation.Areas;

public enum AreasHitKind { None, Cell, RowHeader, TaskHeader, TurnHeader }

/// <summary>
/// What is under a point. <c>Row</c>: index of a screen line (<see cref="AreasMatrix.DisplayRows"/>); <c>Column</c>: index of a column, or, for a
/// task header, of its band. -1 where it does not apply.
/// </summary>
public readonly record struct AreasHit(AreasHitKind Kind, int Row, int Column)
{
    public static readonly AreasHit None = new(AreasHitKind.None, -1, -1);
}

/// <summary>
/// The layout of the Areas map, shared by the painting and the hit testing so they cannot drift: a header with the task bands and the turn
/// numbers on top, the row names on the left and the cells in between. <c>zoom</c> scales the width of a column; <c>scrollX</c> and
/// <c>scrollY</c> move the cells (the headers stay in place).
/// </summary>
public static class AreasGeometry
{
    public const double HeaderWidth = 210;
    public const double TaskBandHeight = 36;
    public const double TurnBandHeight = 18;
    public const double RowHeight = 24;
    public const double ColumnWidth = 64;
    public const double MinZoom = 0.4;
    public const double MaxZoom = 3;

    public static double TopHeight => TaskBandHeight + TurnBandHeight;

    public static double ColumnX(int column, double scrollX, double zoom) => HeaderWidth + column * ColumnWidth * zoom - scrollX;

    public static double RowY(int row, double scrollY) => TopHeight + row * RowHeight - scrollY;

    /// <summary>The content size (cells without the headers), to know how far it can scroll.</summary>
    public static (double Width, double Height) ContentSize(int rows, int columns, double zoom) => (columns * ColumnWidth * zoom, rows * RowHeight);

    public static AreasHit HitTest(double x, double y, double scrollX, double scrollY, double zoom, int rows, int columns, IReadOnlyList<AreaBand> bands)
    {
        if (x < 0 || y < 0) return AreasHit.None;
        if (x < HeaderWidth)
        {
            if (y < TopHeight) return AreasHit.None;
            int r = (int)((y - TopHeight + scrollY) / RowHeight);
            return r < rows ? new AreasHit(AreasHitKind.RowHeader, r, -1) : AreasHit.None;
        }
        int column = (int)Math.Floor((x - HeaderWidth + scrollX) / (ColumnWidth * zoom));
        if (column < 0 || column >= columns) return AreasHit.None;
        if (y < TaskBandHeight)
        {
            for (int b = 0; b < bands.Count; b++)
            {
                if (column >= bands[b].FirstColumn && column < bands[b].FirstColumn + bands[b].Count) return new AreasHit(AreasHitKind.TaskHeader, -1, b);
            }
            return AreasHit.None;
        }
        if (y < TopHeight) return new AreasHit(AreasHitKind.TurnHeader, -1, column);
        int row = (int)((y - TopHeight + scrollY) / RowHeight);
        return row < rows ? new AreasHit(AreasHitKind.Cell, row, column) : AreasHit.None;
    }
}
