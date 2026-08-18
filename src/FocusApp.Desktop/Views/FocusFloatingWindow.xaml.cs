using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace FocusApp.Desktop.Views;

public partial class FocusFloatingWindow : Window
{
    private readonly DispatcherTimer _collapseTimer;
    private readonly FocusFloatingWindowStateMachine _stateMachine = new();
    private Rect _foldedBounds;
    private bool _foldHoverArmed = true;
    private bool _isDragging;

    public FocusFloatingWindow()
    {
        InitializeComponent();
        FloatingContentView.ExpandRequested += FloatingContentView_ExpandRequested;
        _collapseTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        _collapseTimer.Tick += CollapseTimer_Tick;
    }

    public event EventHandler? ExpandRequested;

    public FocusFloatingWindowState State => _stateMachine.State;

    private void FloatingContentView_ExpandRequested(object? sender, EventArgs e)
    {
        ExpandRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            FloatingContentView.IsInteractiveRegion(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _collapseTimer.Stop();
        PrepareFloatingStateForDrag();

        e.Handled = true;
        _isDragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can be interrupted while the window is closing.
        }
        finally
        {
            _isDragging = false;
        }

        if (IsVisible && WindowState == WindowState.Normal)
        {
            EvaluateSnapAfterDrag();
        }
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        if (_foldHoverArmed && _stateMachine.IsFolded)
        {
            ExpandFromFold();
        }
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_stateMachine.IsFolded)
        {
            _foldHoverArmed = true;
            return;
        }

        if (_stateMachine.IsExpandedFromFold && !_isDragging)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void CollapseTimer_Tick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (!_isDragging && _stateMachine.IsExpandedFromFold && !IsMouseOver)
        {
            CollapseToFold();
        }
    }

    private void EvaluateSnapAfterDrag()
    {
        var monitor = MonitorWorkAreaProvider.GetForWindow(this);
        var currentBounds = GetCurrentBounds();
        var foldedState = FocusFloatingSnapCalculator.FindSnapState(currentBounds, monitor);
        if (foldedState is null)
        {
            SetFloatingState();
            return;
        }

        _foldedBounds = FocusFloatingSnapCalculator.GetSnappedBounds(
            foldedState.Value,
            currentBounds,
            monitor.WorkArea);
        ApplyFoldedState(foldedState.Value, armHover: false);
    }

    private void ExpandFromFold()
    {
        if (!_stateMachine.BeginExpand())
        {
            return;
        }

        var monitor = MonitorWorkAreaProvider.GetForWindow(this);
        var expandedBounds = FocusFloatingSnapCalculator.GetExpandedBounds(
            _stateMachine.FoldedState,
            _foldedBounds,
            monitor.WorkArea);

        ShowFloatingContent();
        ApplyBounds(expandedBounds);
        _stateMachine.CompleteExpand();
    }

    private void CollapseToFold()
    {
        if (!_stateMachine.BeginCollapse())
        {
            return;
        }

        var foldedState = _stateMachine.FoldedState;
        ShowFoldedContent(foldedState);
        ApplyBounds(_foldedBounds);
        _stateMachine.CompleteCollapse();
        _foldHoverArmed = true;
    }

    private void PrepareFloatingStateForDrag()
    {
        if (_stateMachine.IsFolded)
        {
            var monitor = MonitorWorkAreaProvider.GetForWindow(this);
            var floatingBounds = GetFloatingBoundsAround(GetCurrentBounds(), monitor.WorkArea);
            _stateMachine.Detach();
            ShowFloatingContent();
            ApplyBounds(floatingBounds);
            return;
        }

        if (_stateMachine.IsExpandedFromFold)
        {
            _stateMachine.Detach();
        }
    }

    private void ApplyFoldedState(FocusFloatingWindowState foldedState, bool armHover)
    {
        _stateMachine.Fold(foldedState);
        ShowFoldedContent(foldedState);
        ApplyBounds(_foldedBounds);
        _foldHoverArmed = armHover;
    }

    private void SetFloatingState()
    {
        _stateMachine.Detach();
        ShowFloatingContent();
        Width = FocusFloatingSnapCalculator.FloatingWidth;
        Height = FocusFloatingSnapCalculator.FloatingHeight;
        _foldHoverArmed = true;
    }

    private void ShowFloatingContent()
    {
        FloatingContentView.Visibility = Visibility.Visible;
        FoldedHorizontalView.Visibility = Visibility.Collapsed;
        FoldedVerticalView.Visibility = Visibility.Collapsed;
        ResetContentTransform();
    }

    private void ShowFoldedContent(FocusFloatingWindowState foldedState)
    {
        var isHorizontal = foldedState is FocusFloatingWindowState.FoldedTop
            or FocusFloatingWindowState.FoldedBottom;
        FloatingContentView.Visibility = Visibility.Collapsed;
        FoldedHorizontalView.Visibility = isHorizontal ? Visibility.Visible : Visibility.Collapsed;
        FoldedVerticalView.Visibility = isHorizontal ? Visibility.Collapsed : Visibility.Visible;
        ResetContentTransform();
    }

    private void ResetContentTransform()
    {
        ContentTranslateTransform.X = 0;
        ContentTranslateTransform.Y = 0;
    }

    private Rect GetCurrentBounds() => new(
        Left,
        Top,
        ActualWidth > 0 ? ActualWidth : Width,
        ActualHeight > 0 ? ActualHeight : Height);

    private void ApplyBounds(Rect bounds)
    {
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private static Rect GetFloatingBoundsAround(Rect sourceBounds, Rect workArea)
    {
        var centerX = sourceBounds.Left + sourceBounds.Width / 2;
        var centerY = sourceBounds.Top + sourceBounds.Height / 2;
        var maximumLeft = workArea.Right - FocusFloatingSnapCalculator.FloatingWidth;
        var maximumTop = workArea.Bottom - FocusFloatingSnapCalculator.FloatingHeight;
        var left = maximumLeft <= workArea.Left
            ? workArea.Left
            : Math.Clamp(centerX - FocusFloatingSnapCalculator.FloatingWidth / 2, workArea.Left, maximumLeft);
        var top = maximumTop <= workArea.Top
            ? workArea.Top
            : Math.Clamp(centerY - FocusFloatingSnapCalculator.FloatingHeight / 2, workArea.Top, maximumTop);
        return new Rect(
            left,
            top,
            FocusFloatingSnapCalculator.FloatingWidth,
            FocusFloatingSnapCalculator.FloatingHeight);
    }

    protected override void OnClosed(EventArgs e)
    {
        _collapseTimer.Stop();
        _collapseTimer.Tick -= CollapseTimer_Tick;
        FloatingContentView.ExpandRequested -= FloatingContentView_ExpandRequested;
        base.OnClosed(e);
    }
}
