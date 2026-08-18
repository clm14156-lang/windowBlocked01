using System.Windows;

namespace FocusApp.Desktop.Views;

public enum FocusFloatingWindowState
{
    Floating,
    FoldedTop,
    FoldedBottom,
    FoldedLeft,
    FoldedRight,
    Expanding,
    ExpandedFromFold,
    Collapsing
}

public sealed class FocusFloatingWindowStateMachine
{
    public FocusFloatingWindowState State { get; private set; } = FocusFloatingWindowState.Floating;

    public FocusFloatingWindowState FoldedState { get; private set; } = FocusFloatingWindowState.Floating;

    public bool IsFolded => FocusFloatingSnapCalculator.IsFolded(State);

    public bool IsExpandedFromFold => State == FocusFloatingWindowState.ExpandedFromFold;

    public void Fold(FocusFloatingWindowState foldedState)
    {
        if (!FocusFloatingSnapCalculator.IsFolded(foldedState))
        {
            throw new ArgumentOutOfRangeException(nameof(foldedState));
        }

        FoldedState = foldedState;
        State = foldedState;
    }

    public bool BeginExpand()
    {
        if (!IsFolded)
        {
            return false;
        }

        State = FocusFloatingWindowState.Expanding;
        return true;
    }

    public void CompleteExpand()
    {
        EnsureState(FocusFloatingWindowState.Expanding);
        State = FocusFloatingWindowState.ExpandedFromFold;
    }

    public bool BeginCollapse()
    {
        if (!IsExpandedFromFold)
        {
            return false;
        }

        State = FocusFloatingWindowState.Collapsing;
        return true;
    }

    public void CompleteCollapse()
    {
        EnsureState(FocusFloatingWindowState.Collapsing);
        State = FoldedState;
    }

    public void Detach()
    {
        State = FocusFloatingWindowState.Floating;
        FoldedState = FocusFloatingWindowState.Floating;
    }

    private void EnsureState(FocusFloatingWindowState expected)
    {
        if (State != expected)
        {
            throw new InvalidOperationException($"Expected floating window state {expected}, but found {State}.");
        }
    }
}

public readonly record struct FocusMonitorArea(Rect Bounds, Rect WorkArea)
{
    public bool IsAvailable(FocusFloatingWindowState state) => FocusFloatingSnapCalculator.IsFolded(state);
}

public static class FocusFloatingSnapCalculator
{
    public const double HorizontalWidth = 235;
    public const double HorizontalHeight = 40;
    public const double VerticalWidth = 50;
    public const double VerticalHeight = 235;
    public const double FloatingWidth = 300;
    public const double FloatingHeight = 220;
    public const double DefaultSnapThreshold = 24;

    public static FocusFloatingWindowState? FindSnapState(
        Rect windowBounds,
        FocusMonitorArea monitor,
        double threshold = DefaultSnapThreshold)
    {
        var candidates = new List<(FocusFloatingWindowState State, double Distance)>
        {
            (FocusFloatingWindowState.FoldedTop,
                DistanceToClosestEdge(windowBounds.Top, windowBounds.Bottom,
                    monitor.Bounds.Top, monitor.WorkArea.Top)),
            (FocusFloatingWindowState.FoldedBottom,
                DistanceToClosestEdge(windowBounds.Top, windowBounds.Bottom,
                    monitor.Bounds.Bottom, monitor.WorkArea.Bottom)),
            (FocusFloatingWindowState.FoldedLeft,
                DistanceToClosestEdge(windowBounds.Left, windowBounds.Right,
                    monitor.Bounds.Left, monitor.WorkArea.Left)),
            (FocusFloatingWindowState.FoldedRight,
                DistanceToClosestEdge(windowBounds.Left, windowBounds.Right,
                    monitor.Bounds.Right, monitor.WorkArea.Right))
        };

        return candidates
            .Where(candidate => candidate.Distance <= threshold && monitor.IsAvailable(candidate.State))
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => (FocusFloatingWindowState?)candidate.State)
            .FirstOrDefault();
    }

    public static Rect GetSnappedBounds(
        FocusFloatingWindowState state,
        Rect floatingBounds,
        Rect workArea)
    {
        var centerX = floatingBounds.Left + floatingBounds.Width / 2;
        var centerY = floatingBounds.Top + floatingBounds.Height / 2;

        return state switch
        {
            FocusFloatingWindowState.FoldedTop => new Rect(
                Clamp(centerX - HorizontalWidth / 2, workArea.Left, workArea.Right - HorizontalWidth),
                workArea.Top,
                HorizontalWidth,
                HorizontalHeight),
            FocusFloatingWindowState.FoldedBottom => new Rect(
                Clamp(centerX - HorizontalWidth / 2, workArea.Left, workArea.Right - HorizontalWidth),
                workArea.Bottom - HorizontalHeight,
                HorizontalWidth,
                HorizontalHeight),
            FocusFloatingWindowState.FoldedLeft => new Rect(
                workArea.Left,
                Clamp(centerY - VerticalHeight / 2, workArea.Top, workArea.Bottom - VerticalHeight),
                VerticalWidth,
                VerticalHeight),
            FocusFloatingWindowState.FoldedRight => new Rect(
                workArea.Right - VerticalWidth,
                Clamp(centerY - VerticalHeight / 2, workArea.Top, workArea.Bottom - VerticalHeight),
                VerticalWidth,
                VerticalHeight),
            _ => floatingBounds
        };
    }

    public static Rect GetExpandedBounds(
        FocusFloatingWindowState foldedState,
        Rect foldedBounds,
        Rect workArea)
    {
        var centerX = foldedBounds.Left + foldedBounds.Width / 2;
        var centerY = foldedBounds.Top + foldedBounds.Height / 2;
        var left = Clamp(centerX - FloatingWidth / 2, workArea.Left, workArea.Right - FloatingWidth);
        var top = Clamp(centerY - FloatingHeight / 2, workArea.Top, workArea.Bottom - FloatingHeight);

        return foldedState switch
        {
            FocusFloatingWindowState.FoldedTop => new Rect(left, workArea.Top, FloatingWidth, FloatingHeight),
            FocusFloatingWindowState.FoldedBottom => new Rect(left, workArea.Bottom - FloatingHeight, FloatingWidth, FloatingHeight),
            FocusFloatingWindowState.FoldedLeft => new Rect(workArea.Left, top, FloatingWidth, FloatingHeight),
            FocusFloatingWindowState.FoldedRight => new Rect(workArea.Right - FloatingWidth, top, FloatingWidth, FloatingHeight),
            _ => new Rect(left, top, FloatingWidth, FloatingHeight)
        };
    }

    public static bool IsFolded(FocusFloatingWindowState state)
        => state is FocusFloatingWindowState.FoldedTop
            or FocusFloatingWindowState.FoldedBottom
            or FocusFloatingWindowState.FoldedLeft
            or FocusFloatingWindowState.FoldedRight;

    private static double Clamp(double value, double minimum, double maximum)
        => maximum <= minimum ? minimum : Math.Clamp(value, minimum, maximum);

    private static double DistanceToClosestEdge(
        double windowStart,
        double windowEnd,
        double monitorEdge,
        double workAreaEdge)
        => Math.Min(
            DistanceToEdge(windowStart, windowEnd, monitorEdge),
            DistanceToEdge(windowStart, windowEnd, workAreaEdge));

    private static double DistanceToEdge(double windowStart, double windowEnd, double edge)
    {
        // A window that already crosses an edge is touching it, even if its
        // far edge has moved well beyond the monitor boundary.
        if (windowStart <= edge && windowEnd >= edge)
        {
            return 0;
        }

        return windowStart > edge
            ? windowStart - edge
            : edge - windowEnd;
    }
}
