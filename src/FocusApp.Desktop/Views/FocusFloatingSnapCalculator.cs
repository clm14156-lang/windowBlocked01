using System.Windows;

namespace FocusApp.Desktop.Views;

public enum FocusFloatingWindowState
{
    Floating,
    FoldedTop,
    FoldedBottom,
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
    public const double HorizontalWidth = 160;
    public const double MaximumHorizontalWidth = 320;
    public const double HorizontalHeight = 50;
    public const double FloatingWidth = 300;
    public const double FloatingHeight = ViewModels.FocusFloatingWindowViewModel.EmptyWindowHeight;
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
                    monitor.Bounds.Bottom, monitor.WorkArea.Bottom))
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
        Rect workArea,
        double foldedWidth = HorizontalWidth)
    {
        foldedWidth = Math.Clamp(foldedWidth, HorizontalWidth, MaximumHorizontalWidth);
        var centerX = floatingBounds.Left + floatingBounds.Width / 2;

        return state switch
        {
            FocusFloatingWindowState.FoldedTop => new Rect(
                Clamp(centerX - foldedWidth / 2, workArea.Left, workArea.Right - foldedWidth),
                workArea.Top,
                foldedWidth,
                HorizontalHeight),
            FocusFloatingWindowState.FoldedBottom => new Rect(
                Clamp(centerX - foldedWidth / 2, workArea.Left, workArea.Right - foldedWidth),
                workArea.Bottom - HorizontalHeight,
                foldedWidth,
                HorizontalHeight),
            _ => floatingBounds
        };
    }

    public static Rect GetExpandedBounds(
        FocusFloatingWindowState foldedState,
        Rect foldedBounds,
        Rect workArea,
        double floatingHeight = FloatingHeight)
    {
        floatingHeight = Math.Clamp(floatingHeight, FloatingHeight, ViewModels.FocusFloatingWindowViewModel.MaximumWindowHeight);
        var centerX = foldedBounds.Left + foldedBounds.Width / 2;
        var centerY = foldedBounds.Top + foldedBounds.Height / 2;
        var left = Clamp(centerX - FloatingWidth / 2, workArea.Left, workArea.Right - FloatingWidth);
        var top = Clamp(centerY - floatingHeight / 2, workArea.Top, workArea.Bottom - floatingHeight);

        return foldedState switch
        {
            FocusFloatingWindowState.FoldedTop => new Rect(left, workArea.Top, FloatingWidth, floatingHeight),
            FocusFloatingWindowState.FoldedBottom => new Rect(left, workArea.Bottom - floatingHeight, FloatingWidth, floatingHeight),
            _ => new Rect(left, top, FloatingWidth, floatingHeight)
        };
    }

    public static bool IsFolded(FocusFloatingWindowState state)
        => state is FocusFloatingWindowState.FoldedTop
            or FocusFloatingWindowState.FoldedBottom;

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
