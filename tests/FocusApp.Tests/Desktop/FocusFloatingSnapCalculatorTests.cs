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

        Assert.Equal(FocusFloatingWindowState.FoldedLeft, state);
    }

    [Fact]
    public void FindSnapState_SnapsWhenWindowHasCrossedTheRightEdge()
    {
        var monitor = new FocusMonitorArea(
            new Rect(0, 0, 1920, 1080),
            new Rect(0, 0, 1920, 1040));

        var state = FocusFloatingSnapCalculator.FindSnapState(
            new Rect(1800, 400, 300, 220),
            monitor);

        Assert.Equal(FocusFloatingWindowState.FoldedRight, state);
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
    public void GetSnappedBounds_UsesRequestedOrientationAndKeepsWindowOnWorkArea()
    {
        var workArea = new Rect(0, 0, 1920, 1040);

        var horizontal = FocusFloatingSnapCalculator.GetSnappedBounds(
            FocusFloatingWindowState.FoldedTop,
            new Rect(900, 10, 300, 220),
            workArea);
        var vertical = FocusFloatingSnapCalculator.GetSnappedBounds(
            FocusFloatingWindowState.FoldedRight,
            new Rect(1700, 600, 300, 220),
            workArea);

        Assert.Equal(new Size(235, 40), horizontal.Size);
        Assert.Equal(0, horizontal.Top);
        Assert.Equal(new Size(50, 235), vertical.Size);
        Assert.Equal(workArea.Right, vertical.Right);
        Assert.InRange(vertical.Top, workArea.Top, workArea.Bottom - vertical.Height);
    }

    [Fact]
    public void GetExpandedBounds_ExpandsTowardTheScreenInterior()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var top = new Rect(850, 0, 235, 40);
        var left = new Rect(0, 400, 50, 235);

        var expandedTop = FocusFloatingSnapCalculator.GetExpandedBounds(
            FocusFloatingWindowState.FoldedTop,
            top,
            workArea);
        var expandedLeft = FocusFloatingSnapCalculator.GetExpandedBounds(
            FocusFloatingWindowState.FoldedLeft,
            left,
            workArea);

        Assert.Equal(0, expandedTop.Top);
        Assert.Equal(0, expandedLeft.Left);
        Assert.Equal(300, expandedTop.Width);
        Assert.Equal(220, expandedLeft.Height);
        Assert.True(expandedTop.Bottom <= workArea.Bottom);
        Assert.True(expandedLeft.Right <= workArea.Right);
    }

    [Fact]
    public void StateMachine_PreservesFoldedEdgeAcrossExpandAndCollapse()
    {
        var stateMachine = new FocusFloatingWindowStateMachine();

        stateMachine.Fold(FocusFloatingWindowState.FoldedRight);
        Assert.True(stateMachine.BeginExpand());
        Assert.Equal(FocusFloatingWindowState.Expanding, stateMachine.State);

        stateMachine.CompleteExpand();
        Assert.Equal(FocusFloatingWindowState.ExpandedFromFold, stateMachine.State);
        Assert.Equal(FocusFloatingWindowState.FoldedRight, stateMachine.FoldedState);

        Assert.True(stateMachine.BeginCollapse());
        Assert.Equal(FocusFloatingWindowState.Collapsing, stateMachine.State);

        stateMachine.CompleteCollapse();
        Assert.Equal(FocusFloatingWindowState.FoldedRight, stateMachine.State);
    }
}
