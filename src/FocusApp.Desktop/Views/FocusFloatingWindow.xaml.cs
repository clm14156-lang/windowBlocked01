using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
using System.Windows.Interop;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusFloatingWindow : Window
{
    private readonly DispatcherTimer _collapseTimer;
    private readonly FocusFloatingWindowStateMachine _stateMachine = new();
    private Rect _foldedBounds;
    private FocusFloatingDockPosition? _pendingDockedPosition;
    private bool _foldHoverArmed = true;
    private bool _isDragging;
    private DispatcherOperation? _sizeRefreshOperation;
    private DispatcherOperation? _positionRefreshOperation;
    private HwndSource? _windowSource;

    private double PreferredFloatingHeight => (DataContext as FocusFloatingWindowViewModel)?.WindowHeight
        ?? FocusFloatingWindowViewModel.EmptyWindowHeight;

    public FocusFloatingWindow()
    {
        InitializeComponent();
        FloatingContentView.ExpandRequested += FloatingContentView_ExpandRequested;
        _collapseTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        _collapseTimer.Tick += CollapseTimer_Tick;
        DataContextChanged += Window_DataContextChanged;
        Loaded += Window_Loaded;
    }

    public event EventHandler? ExpandRequested;
    public event EventHandler? UserPositionChanged;

    public FocusFloatingPosition GetPositionForMemory()
    {
        if (!FocusFloatingSnapCalculator.IsFolded(_stateMachine.FoldedState))
            return MonitorWorkAreaProvider.CapturePosition(this);
        var monitor = MonitorWorkAreaProvider.GetForWindow(this);
        var fullBounds = FocusFloatingSnapCalculator.GetExpandedBounds(_stateMachine.FoldedState,
            _foldedBounds, monitor.WorkArea, PreferredFloatingHeight);
        var anchor = MonitorWorkAreaProvider.CapturePosition(this,
            new Point(_foldedBounds.Left + _foldedBounds.Width / 2, _foldedBounds.Top));
        return MonitorWorkAreaProvider.CapturePosition(this, fullBounds.TopLeft) with
        {
            DockedPosition = new FocusFloatingDockPosition(
                _stateMachine.FoldedState == FocusFloatingWindowState.FoldedTop
                    ? FocusFloatingDockEdge.Top : FocusFloatingDockEdge.Bottom, anchor.OffsetX)
        };
    }

    public void RestoreDockedState(FocusFloatingDockPosition? position)
    {
        if (position is null || !double.IsFinite(position.CenterOffsetX)) return;
        var foldedState = position.Edge switch
        {
            FocusFloatingDockEdge.Top => FocusFloatingWindowState.FoldedTop,
            FocusFloatingDockEdge.Bottom => FocusFloatingWindowState.FoldedBottom,
            _ => FocusFloatingWindowState.Floating
        };
        if (!FocusFloatingSnapCalculator.IsFolded(foldedState)) return;
        _collapseTimer.Stop();
        _pendingDockedPosition = position;
        _stateMachine.Fold(foldedState);
        ShowFoldedContent();
        Width = FoldedHorizontalView.GetPreferredWidth(DataContext as FocusFloatingWindowViewModel);
        Height = FocusFloatingSnapCalculator.HorizontalHeight;
        _foldHoverArmed = false;
        if (IsLoaded) RestoreDockedBounds();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => RestoreDockedBounds();

    private void RestoreDockedBounds()
    {
        if (_pendingDockedPosition is not { } position) return;
        // The native restore happens while hidden. Wait for Loaded, before the first
        // rendered frame, so WPF coordinates reflect the restored monitor's DPI.
        var monitor = MonitorWorkAreaProvider.GetForWindow(this);
        var anchor = new Rect(monitor.WorkArea.Left + position.CenterOffsetX, monitor.WorkArea.Top, 0, 0);
        _foldedBounds = FocusFloatingSnapCalculator.GetSnappedBounds(_stateMachine.FoldedState, anchor,
            monitor.WorkArea, FoldedHorizontalView.GetPreferredWidth(DataContext as FocusFloatingWindowViewModel));
        _pendingDockedPosition = null;
        ApplyFoldedState(_stateMachine.FoldedState, armHover: false);
    }

    public FocusFloatingWindowState State => _stateMachine.State;

    private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FocusFloatingWindowViewModel oldModel) oldModel.PropertyChanged -= Model_PropertyChanged;
        if (e.NewValue is FocusFloatingWindowViewModel newModel) newModel.PropertyChanged += Model_PropertyChanged;
        RefreshFoldedBounds();
        RefreshFloatingSize();
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusFloatingWindowViewModel.HasTarget) or nameof(FocusFloatingWindowViewModel.TargetName))
            RefreshFoldedBounds();
        if (e.PropertyName == nameof(FocusFloatingWindowViewModel.WindowHeight)
            && _sizeRefreshOperation?.Status != DispatcherOperationStatus.Pending)
        {
            // The session rebuilds its pending collection in one synchronous batch.
            // Resize once after that batch, rather than flashing through intermediate task counts.
            _sizeRefreshOperation = Dispatcher.InvokeAsync(RefreshFloatingSize, DispatcherPriority.DataBind);
        }
    }

    private void RefreshFloatingSize()
    {
        if (_isDragging || _stateMachine.IsFolded) return;
        if (!IsVisible)
        {
            Width = FocusFloatingSnapCalculator.FloatingWidth;
            Height = PreferredFloatingHeight;
            return;
        }
        var monitor = MonitorWorkAreaProvider.GetForWindow(this);
        if (_stateMachine.IsExpandedFromFold)
        {
            ApplyBounds(FocusFloatingSnapCalculator.GetExpandedBounds(_stateMachine.FoldedState,
                _foldedBounds, monitor.WorkArea, PreferredFloatingHeight));
            return;
        }
        var bounds = GetCurrentBounds();
        var left = Math.Clamp(bounds.Left, monitor.WorkArea.Left,
            Math.Max(monitor.WorkArea.Left, monitor.WorkArea.Right - FocusFloatingSnapCalculator.FloatingWidth));
        var top = Math.Clamp(bounds.Top, monitor.WorkArea.Top,
            Math.Max(monitor.WorkArea.Top, monitor.WorkArea.Bottom - PreferredFloatingHeight));
        ApplyBounds(new Rect(left, top, FocusFloatingSnapCalculator.FloatingWidth, PreferredFloatingHeight));
    }

    private void RefreshFoldedBounds()
    {
        if (_isDragging || _pendingDockedPosition is not null
            || (!_stateMachine.IsFolded && !_stateMachine.IsExpandedFromFold)) return;
        var monitor = MonitorWorkAreaProvider.GetForWindow(this);
        _foldedBounds = FocusFloatingSnapCalculator.GetSnappedBounds(_stateMachine.FoldedState, _foldedBounds,
            monitor.WorkArea, FoldedHorizontalView.GetPreferredWidth(DataContext as FocusFloatingWindowViewModel));
        if (_stateMachine.IsFolded) ApplyBounds(_foldedBounds);
    }

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
        var dragStart = GetCurrentBounds().TopLeft;
        var completedDrag = false;

        e.Handled = true;
        _isDragging = true;
        try
        {
            DragMove();
            completedDrag = true;
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
            var moved = completedDrag && GetCurrentBounds().TopLeft != dragStart;
            if (moved)
            {
                EvaluateSnapAfterDrag();
                UserPositionChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (_stateMachine.IsExpandedFromFold)
            {
                CollapseToFold();
            }
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
            monitor.WorkArea,
            FoldedHorizontalView.GetPreferredWidth(DataContext as FocusFloatingWindowViewModel));
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
            monitor.WorkArea,
            PreferredFloatingHeight);

        ShowFloatingContent();
        ApplyBounds(expandedBounds);
        _stateMachine.CompleteExpand();
    }

    private void CollapseToFold()
    {
        RefreshFoldedBounds();
        if (!_stateMachine.BeginCollapse())
        {
            return;
        }

        ShowFoldedContent();
        ApplyBounds(_foldedBounds);
        _stateMachine.CompleteCollapse();
        _foldHoverArmed = true;
    }

    private void PrepareFloatingStateForDrag()
    {
        if (_stateMachine.IsFolded)
        {
            ExpandFromFold();
        }
        // Starting a gesture does not detach. Its completed position decides whether
        // the user actually dragged away from an edge in EvaluateSnapAfterDrag.
    }

    private void ApplyFoldedState(FocusFloatingWindowState foldedState, bool armHover)
    {
        _stateMachine.Fold(foldedState);
        ShowFoldedContent();
        ApplyBounds(_foldedBounds);
        _foldHoverArmed = armHover;
    }

    private void SetFloatingState()
    {
        _stateMachine.Detach();
        ShowFloatingContent();
        Width = FocusFloatingSnapCalculator.FloatingWidth;
        Height = PreferredFloatingHeight;
        _foldHoverArmed = true;
    }

    private void ShowFloatingContent()
    {
        FloatingContentView.Visibility = Visibility.Visible;
        FoldedHorizontalView.Visibility = Visibility.Collapsed;
        ResetContentTransform();
    }

    private void ShowFoldedContent()
    {
        FloatingContentView.Visibility = Visibility.Collapsed;
        FoldedHorizontalView.Visibility = Visibility.Visible;
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

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // Returning starts folded even if the pointer already occupies that edge.
        // Subsequent genuine hover entry keeps the existing expansion behavior.
        if (_stateMachine.IsFolded) _foldHoverArmed = !IsMouseOver;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(WindowMessages);
    }

    private IntPtr WindowMessages(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Recheck after Windows has applied monitor, taskbar, or DPI changes.
        if (message is 0x007E or 0x001A or 0x02E0 &&
            _positionRefreshOperation?.Status != DispatcherOperationStatus.Pending)
        {
            _positionRefreshOperation = Dispatcher.InvokeAsync(() =>
            {
                if (!IsVisible || _isDragging) return;
                MonitorWorkAreaProvider.EnsureVisible(this);
                RefreshFoldedBounds();
                RefreshFloatingSize();
            }, DispatcherPriority.Loaded);
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _collapseTimer.Stop();
        _sizeRefreshOperation?.Abort();
        _positionRefreshOperation?.Abort();
        _windowSource?.RemoveHook(WindowMessages);
        _collapseTimer.Tick -= CollapseTimer_Tick;
        FloatingContentView.ExpandRequested -= FloatingContentView_ExpandRequested;
        if (DataContext is FocusFloatingWindowViewModel model) model.PropertyChanged -= Model_PropertyChanged;
        base.OnClosed(e);
    }
}
