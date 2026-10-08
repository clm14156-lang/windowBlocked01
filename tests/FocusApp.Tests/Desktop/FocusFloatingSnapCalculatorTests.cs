using System.Windows;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusFloatingSnapCalculatorTests
{
    [Fact]
    public void FindSnapState_UsesTheScreenBottomWhenTheTaskbarShortensWorkArea()
    {
        var monitor = new FocusMonitorArea(
            new Rect(0, 0, 1920, 1080),
            new Rect(0, 0, 1920, 1040));

        var state = FocusFloatingSnapCalculator.FindSnapState(
            new Rect(800, 840, 300, 220),
            monitor);

        Assert.Equal(FocusFloatingWindowState.FoldedBottom, state);
    }

    [Fact]
    public void FindSnapState_AlsoUsesTheVisibleWorkAreaEdgeAboveTheTaskbar()
    {
        var monitor = new FocusMonitorArea(
            new Rect(0, 0, 1920, 1080),
            new Rect(0, 0, 1920, 1040));

        var state = FocusFloatingSnapCalculator.FindSnapState(
            new Rect(800, 820, 300, 220),
            monitor);

        Assert.Equal(FocusFloatingWindowState.FoldedBottom, state);
    }

    [Fact]
    public void FindSnapState_ChoosesTheNearestAvailableEdgeAtACorner()
    {
        var monitor = new FocusMonitorArea(
            new Rect(1920, 0, 1920, 1080),
            new Rect(1920, 0, 1920, 1080));

        var state = FocusFloatingSnapCalculator.FindSnapState(
            new Rect(1924, 5, 300, 220),
            monitor);

        Assert.Equal(FocusFloatingWindowState.FoldedTop, state);
    }

    [Theory]
    [InlineData(-150)]
    [InlineData(0)]
    [InlineData(1620)]
    [InlineData(1800)]
    public void FindSnapState_DoesNotDockAtEitherSide(double left)
    {
        var monitor = new FocusMonitorArea(
            new Rect(0, 0, 1920, 1080),
            new Rect(0, 0, 1920, 1040));

        var state = FocusFloatingSnapCalculator.FindSnapState(
            new Rect(left, 400, 300, 220),
            monitor);

        Assert.Null(state);
    }

    [Fact]
    public void FindSnapState_SnapsWhenWindowHasCrossedTheBottomEdge()
    {
        var monitor = new FocusMonitorArea(
            new Rect(0, 0, 1920, 1080),
            new Rect(0, 0, 1920, 1040));

        var state = FocusFloatingSnapCalculator.FindSnapState(
            new Rect(800, 900, 300, 220),
            monitor);

        Assert.Equal(FocusFloatingWindowState.FoldedBottom, state);
    }

    [Fact]
    public void GetSnappedBounds_UsesHorizontalBarsAtBothEdgesAndKeepsWindowOnWorkArea()
    {
        var workArea = new Rect(0, 0, 1920, 1040);

        var horizontal = FocusFloatingSnapCalculator.GetSnappedBounds(
            FocusFloatingWindowState.FoldedTop,
            new Rect(900, 10, 300, 220),
            workArea);
        var bottom = FocusFloatingSnapCalculator.GetSnappedBounds(
            FocusFloatingWindowState.FoldedBottom,
            new Rect(1900, 900, 300, 220),
            workArea,
            320);

        Assert.Equal(new Size(160, 50), horizontal.Size);
        Assert.Equal(0, horizontal.Top);
        Assert.Equal(new Size(320, 50), bottom.Size);
        Assert.Equal(workArea.Bottom, bottom.Bottom);
        Assert.Equal(workArea.Right, bottom.Right);
    }

    [Theory]
    [InlineData(80, 160)]
    [InlineData(240, 240)]
    [InlineData(800, 320)]
    public void GetSnappedBounds_ClampsContentWidthOnASecondaryMonitor(double requested, double expected)
    {
        var workArea = new Rect(-1920, -1080, 1920, 1040);
        foreach (var state in new[] { FocusFloatingWindowState.FoldedTop, FocusFloatingWindowState.FoldedBottom })
        {
            var bounds = FocusFloatingSnapCalculator.GetSnappedBounds(state, new Rect(-1800, -900, 300, 220), workArea, requested);
            Assert.Equal(expected, bounds.Width);
            Assert.Equal(50, bounds.Height);
            Assert.True(workArea.Contains(bounds));
            Assert.Equal(-1650, bounds.Left + bounds.Width / 2);
        }
    }

    [Fact]
    public void GetExpandedBounds_ExpandsTowardTheScreenInterior()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var top = new Rect(850, 0, 160, 50);
        var bottom = new Rect(850, 990, 320, 50);

        var expandedTop = FocusFloatingSnapCalculator.GetExpandedBounds(
            FocusFloatingWindowState.FoldedTop,
            top,
            workArea);
        var expandedBottom = FocusFloatingSnapCalculator.GetExpandedBounds(
            FocusFloatingWindowState.FoldedBottom,
            bottom,
            workArea);

        Assert.Equal(0, expandedTop.Top);
        Assert.Equal(workArea.Bottom, expandedBottom.Bottom);
        Assert.Equal(300, expandedTop.Width);
        Assert.Equal(115, expandedBottom.Height);
        Assert.True(expandedTop.Bottom <= workArea.Bottom);
        Assert.True(workArea.Contains(expandedBottom));
    }

    [Theory]
    [InlineData(115)] [InlineData(189)] [InlineData(217)] [InlineData(245)]
    public void GetExpandedBounds_UsesTheTaskDependentHeightAndStaysAnchoredOnBothEdges(double height)
    {
        var workArea = new Rect(-1920, -1080, 1920, 1040);
        foreach (var state in new[] { FocusFloatingWindowState.FoldedTop, FocusFloatingWindowState.FoldedBottom })
        {
            var folded = new Rect(-1700, state == FocusFloatingWindowState.FoldedTop ? workArea.Top : workArea.Bottom - 50, 160, 50);
            var expanded = FocusFloatingSnapCalculator.GetExpandedBounds(state, folded, workArea, height);
            Assert.Equal(new Size(300, height), expanded.Size);
            Assert.True(workArea.Contains(expanded));
            if (state == FocusFloatingWindowState.FoldedTop) Assert.Equal(workArea.Top, expanded.Top);
            else Assert.Equal(workArea.Bottom, expanded.Bottom);
        }
    }

    [Fact]
    public void StateMachine_PreservesFoldedEdgeAcrossExpandAndCollapse()
    {
        var stateMachine = new FocusFloatingWindowStateMachine();

        stateMachine.Fold(FocusFloatingWindowState.FoldedBottom);
        Assert.True(stateMachine.BeginExpand());
        Assert.Equal(FocusFloatingWindowState.Expanding, stateMachine.State);

        stateMachine.CompleteExpand();
        Assert.Equal(FocusFloatingWindowState.ExpandedFromFold, stateMachine.State);
        Assert.Equal(FocusFloatingWindowState.FoldedBottom, stateMachine.FoldedState);

        Assert.True(stateMachine.BeginCollapse());
        Assert.Equal(FocusFloatingWindowState.Collapsing, stateMachine.State);

        stateMachine.CompleteCollapse();
        Assert.Equal(FocusFloatingWindowState.FoldedBottom, stateMachine.State);
    }
}
