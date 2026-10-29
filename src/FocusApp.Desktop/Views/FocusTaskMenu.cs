using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Services;
using Path = System.Windows.Shapes.Path;

namespace FocusApp.Desktop.Views;

// Native popups shared by the focus drawer and floating task list.
internal sealed class FocusTaskMenu : IDisposable
{
    private readonly FrameworkElement _owner;
    private readonly FocusSessionViewModel _session;
    private readonly Action<FocusTaskViewModel, FocusSubTaskViewModel?> _edit;
    private readonly Window? _window;
    private ContextMenu? _taskContextMenu;
    internal ContextMenu? CurrentMenu => _taskContextMenu;
    internal FocusSessionViewModel Session => _session;
    internal event EventHandler? OpenStateChanged;

    internal FocusTaskMenu(FrameworkElement owner, FocusSessionViewModel session, Action<FocusTaskViewModel, FocusSubTaskViewModel?> edit)
    {
        _owner = owner; _session = session; _edit = edit;
        _window = Window.GetWindow(owner);
        if (_window is not null) _window.Deactivated += OwnerDeactivated;
        _session.PropertyChanged += SessionChanged;
    }
    internal void Close() { if (_taskContextMenu is { } menu) menu.IsOpen = false; }
    private void OwnerDeactivated(object? sender, EventArgs e) => Close();
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusSessionViewModel.ActiveTarget) or nameof(FocusSessionViewModel.Stage)) Close();
    }
    private bool IsValid(FocusTaskViewModel parent, FocusSubTaskViewModel? child) =>
        _session.IsFocusing && _session.ActiveTarget?.Tasks.Contains(parent) == true && (child is null || parent.SubTasks.Contains(child));
    public void Dispose()
    {
        Close();
        if (_window is not null) _window.Deactivated -= OwnerDeactivated;
        _session.PropertyChanged -= SessionChanged;
    }
    internal void OpenAtPointer(FocusTaskViewModel parent, FocusSubTaskViewModel? child)
    {
        var pointer = GetPhysicalCursorPos(out var point)
            ? new Point(point.X, point.Y)
            : _owner.PointToScreen(Mouse.GetPosition(_owner));
        OpenAtScreenPosition(parent, child, pointer);
    }

    internal void Open(FocusTaskViewModel parent, FocusSubTaskViewModel? child, Point location) =>
        OpenAtScreenPosition(parent, child, _owner.PointToScreen(location));

    private void OpenAtScreenPosition(FocusTaskViewModel parent, FocusSubTaskViewModel? child, Point pointer)
    {
        if (!_session.IsFocusing || _session.ActiveTarget?.Tasks.Contains(parent) != true ||
            child is not null && !parent.SubTasks.Contains(child)) return;
        Close();
        // Keep the native top-level popup outside the owner; only the monitor's work area limits it.
        var menu = CreateTaskPopup(parent, child);
        menu.CustomPopupPlacementCallback = (size, _, _) =>
        {
            // WPF custom placement uses device pixels, including the supplied popup size.
            var origin = _owner.PointToScreen(new Point());
            var screen = MonitorWorkAreaProvider.GetWorkAreaAtScreenPoint(pointer);
            var position = PositionTaskMenuNearPointer(pointer, size, screen);
            return [new(new Point(position.X - origin.X, position.Y - origin.Y), PopupPrimaryAxis.None)];
        };
        var style = (Style)_owner.FindResource("DrawerContextMenuItemStyle");
        string? SelectedColor() => child?.FlagColor ?? (child is null ? parent.FlagColor : null);
        var mark = new MenuItem { Header = "标记", Style = style, Icon = FlagIcon(SelectedColor()) };
        var palette = new StackPanel();
        var heading = new TextBlock { Text = "选择标记", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(5, 2, 0, 5) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "DrawerTaskMenuEdit");
        palette.Children.Add(heading);
        var grid = new UniformGrid { Rows = 4, Columns = 5 };
        foreach (var marker in TaskMarkerCatalog.All)
        {
            grid.Children.Add(CreateMarkerChoice(marker, SelectedColor(), () =>
            {
                SetTaskMarker(parent, child, marker.Id);
                menu.IsOpen = false;
            }));
        }
        palette.Children.Add(grid);
        mark.Items.Add(new MenuItem { Header = palette, Style = (Style)_owner.FindResource("DrawerMarkerPaletteItemStyle") });
        mark.Loaded += (_, _) =>
        {
            mark.ApplyTemplate();
            if (mark.Template.FindName("PART_Popup", mark) is not Popup popup) return;
            popup.Placement = PlacementMode.Custom;
            popup.CustomPopupPlacementCallback = (size, target, _) =>
            {
                var origin = mark.PointToScreen(new Point());
                var center = mark.PointToScreen(new Point(mark.ActualWidth / 2, mark.ActualHeight / 2));
                var bounds = MonitorWorkAreaProvider.GetWorkAreaAtScreenPoint(center);
                var gap = 4 * VisualTreeHelper.GetDpi(mark).DpiScaleX;
                var x = origin.X + target.Width + gap;
                if (x + size.Width > bounds.Right - 4) x = origin.X - size.Width - gap;
                var position = ClampMenuToScreen(new Point(x, origin.Y), size, bounds);
                return [new(new Point(position.X - origin.X, position.Y - origin.Y), PopupPrimaryAxis.None)];
            };
        };
        menu.Items.Add(mark);
        var edit = new MenuItem { Header = "编辑", Style = style,
            Icon = LineIcon("M 1,10 L 1,14 L 5,14 L 13,6 L 9,2 Z M 9,2 L 11,0 L 15,4 L 13,6", menu) };
        edit.Click += (_, _) => { menu.IsOpen = false; if (IsValid(parent, child)) _edit(parent, child); };
        menu.Items.Add(edit);
        menu.Items.Add(new Separator { Style = (Style)_owner.FindResource("DrawerTaskMenuSeparatorStyle") });
        var delete = new MenuItem { Header = "删除", Style = style,
            Foreground = (Brush)menu.FindResource("DrawerTaskMenuDelete"),
            Icon = LineIcon("M 1,3 L 13,3 M 3,3 L 3,13 L 11,13 L 11,3 M 5,1 L 9,1 M 6,6 L 6,10 M 8,6 L 8,10", menu, "DrawerTaskMenuDelete") };
        delete.Click += (_, _) =>
        {
            if (_session.ActiveTarget?.Tasks.Contains(parent) != true || !_session.IsFocusing) return;
            if (child is null) _session.TaskDrawer.DeleteTaskCommand.Execute(parent);
            else parent.SubTasks.Remove(child);
        };
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    private ContextMenu CreateTaskPopup(FocusTaskViewModel parent, FocusSubTaskViewModel? child, bool quick = false)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = _owner, Placement = PlacementMode.Custom, StaysOpen = false,
            DataContext = (object?)child ?? parent
        };
        menu.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FocusApp.Desktop;component/Views/FocusTaskResources.xaml", UriKind.Relative) });
        menu.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FocusApp.Desktop;component/Resources/FocusTheme.Light.xaml", UriKind.Relative) });
        BindingOperations.SetBinding(menu, FocusThemeScope.IsDarkProperty, new Binding(nameof(FocusSessionViewModel.IsDarkFocusTheme)) { Source = _session });
        menu.Style = (Style)_owner.FindResource(quick ? "DrawerMarkerQuickMenuStyle" : "DrawerTaskMenuStyle");
        menu.Opened += (_, _) => OpenStateChanged?.Invoke(this, EventArgs.Empty);
        menu.Closed += (_, _) => { if (ReferenceEquals(_taskContextMenu, menu)) _taskContextMenu = null; OpenStateChanged?.Invoke(this, EventArgs.Empty); };
        _taskContextMenu = menu;
        return menu;
    }

    private Button CreateMarkerChoice(TaskMarker marker, string? selectedId, Action select)
    {
        var button = new Button
        {
            Tag = marker.Id, Content = new Image { Source = marker.Icon, Width = 24, Height = 24 },
            ToolTip = marker.Label, Style = (Style)_owner.FindResource("DrawerMarkerChoice")
        };
        System.Windows.Automation.AutomationProperties.SetName(button, marker.Label);
        if (TaskMarkerCatalog.Find(selectedId)?.Id == marker.Id)
        {
            button.SetResourceReference(Control.BackgroundProperty, "FocusTaskSubTaskSurface");
            button.SetResourceReference(Control.BorderBrushProperty, "FocusTaskSelectedAccent");
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, "已选中");
        }
        button.Click += (_, args) => { select(); args.Handled = true; };
        return button;
    }

    private void SetTaskMarker(FocusTaskViewModel parent, FocusSubTaskViewModel? child, string? marker)
    {
        if (!IsValid(parent, child)) return;
        if (child is null) parent.FlagColor = marker;
        else if (parent.SubTasks.Contains(child)) child.FlagColor = marker;
    }

    internal void OpenQuick(FocusTaskViewModel parent, FocusSubTaskViewModel? child, FrameworkElement anchor)
    {
        if (!_session.IsFocusing || _session.ActiveTarget?.Tasks.Contains(parent) != true ||
            child is not null && !parent.SubTasks.Contains(child)) return;
        var current = child is null ? parent.FlagColor : child.FlagColor;
        if (TaskMarkerCatalog.Find(current) is not { } selected) return;
        Close();
        var menu = CreateTaskPopup(parent, child, quick: true);
        menu.PlacementTarget = anchor;
        menu.CustomPopupPlacementCallback = (size, target, _) =>
        {
            var origin = anchor.PointToScreen(new Point());
            var bounds = MonitorWorkAreaProvider.GetWorkAreaAtScreenPoint(origin);
            var gap = 6 * VisualTreeHelper.GetDpi(anchor).DpiScaleY;
            var y = origin.Y + target.Height + gap;
            if (y + size.Height > bounds.Bottom - 4) y = origin.Y - size.Height - gap;
            var position = ClampMenuToScreen(new Point(origin.X, y), size, bounds);
            return [new(new Point(position.X - origin.X, position.Y - origin.Y), PopupPrimaryAxis.None)];
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var marker in TaskMarkerCatalog.All.Where(item => item.Row == selected.Row))
            row.Children.Add(CreateMarkerChoice(marker, current, () => { SetTaskMarker(parent, child, marker.Id); menu.IsOpen = false; }));
        var divider = new Border { Width = 1, Margin = new Thickness(3, 6, 3, 6) };
        divider.SetResourceReference(Border.BackgroundProperty, "DrawerTaskMenuDivider");
        row.Children.Add(divider);
        var clear = new Button
        {
            Style = (Style)_owner.FindResource("DrawerMarkerChoice"), Tag = "clear", ToolTip = "清除标记",
            Content = LineIcon("M 1,3 L 13,3 M 3,3 L 3,13 L 11,13 L 11,3 M 5,1 L 9,1 M 6,6 L 6,10 M 8,6 L 8,10", menu)
        };
        System.Windows.Automation.AutomationProperties.SetName(clear, "清除标记");
        clear.Click += (_, args) => { SetTaskMarker(parent, child, null); menu.IsOpen = false; args.Handled = true; };
        row.Children.Add(clear);
        menu.Items.Add(new MenuItem { Header = row, Style = (Style)_owner.FindResource("DrawerMarkerPaletteItemStyle") });
        menu.IsOpen = true;
    }

    internal static Point PositionTaskMenuNearPointer(Point pointer, Size size, Rect screen)
    {
        var x = pointer.X;
        var y = pointer.Y;
        if (x + size.Width > screen.Right) x -= size.Width;
        if (y + size.Height > screen.Bottom) y -= size.Height;
        return new Point(
            Math.Clamp(x, screen.Left, Math.Max(screen.Left, screen.Right - size.Width)),
            Math.Clamp(y, screen.Top, Math.Max(screen.Top, screen.Bottom - size.Height)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalCursorPos(out CursorPoint point);

    private static Point ClampMenuToScreen(Point point, Size size, Rect screen) => new(
        Math.Clamp(point.X, screen.Left + 4, Math.Max(screen.Left + 4, screen.Right - size.Width - 4)),
        Math.Clamp(point.Y, screen.Top + 4, Math.Max(screen.Top + 4, screen.Bottom - size.Height - 4)));


    private static Path FlagIcon(string? color) => new() { Width = 12, Height = 14, Stretch = Stretch.Uniform, Data = FocusTaskFlags.Geometry, Fill = FocusTaskFlags.BrushFor(color) };
    private static Path LineIcon(string geometry, FrameworkElement resources, string brushKey = "DrawerTaskMenuEdit") => new()
    {
        Width = 14, Height = 14, Stretch = Stretch.Uniform, Data = Geometry.Parse(geometry), Stroke = (Brush)resources.FindResource(brushKey),
        StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
    };
}
