namespace AgentTrace.Presentation.Graph;

/// <summary>
/// Where time advances in the graph: left to right or top to bottom. The axis the batches advance along is called
/// "long"; the other ("cross") spreads the parallel calls and the subagent lanes.
/// </summary>
public enum GraphOrientation { Horizontal, Vertical }

public static class GraphOrientations
{
    public static bool IsVertical(this GraphOrientation orientation) => orientation == GraphOrientation.Vertical;
}
