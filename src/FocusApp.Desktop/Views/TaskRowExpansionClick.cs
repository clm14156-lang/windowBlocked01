using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

/// <summary>Recognizes a parent-row click without taking over buttons or drag capture.</summary>
internal sealed class TaskRowExpansionClick
{
    private FrameworkElement? _header;
    private FocusTaskViewModel? _task;
    private Point _origin;

    internal void Begin(FrameworkElement header, DependencyObject? source, Point pointer, UIElement relativeTo)
    {
        Cancel();
        if (header.DataContext is not FocusTaskViewModel { HasSubTasks: true } task ||
            !IsRowSource(source, header)) return;
        _header = header;
        _task = task;
        _origin = relativeTo.TranslatePoint(pointer, header);
    }

    internal void Move(Point pointer, UIElement relativeTo)
    {
        if (_header is not null && ExceedsClickDistance(relativeTo.TranslatePoint(pointer, _header))) Cancel();
    }

    internal bool Complete(Point pointer, UIElement relativeTo)
    {
        if (_header is not { } header || _task is not { } task) return false;
        var local = relativeTo.TranslatePoint(pointer, header);
        var valid = header.IsVisible && ReferenceEquals(header.DataContext, task) && task.HasSubTasks &&
            !ExceedsClickDistance(local) && new Rect(header.RenderSize).Contains(local) &&
            IsRowSource(header.InputHitTest(local) as DependencyObject, header);
        Cancel();
        if (!valid) return false;
        task.IsSubTasksExpanded = !task.IsSubTasksExpanded;
        return true;
    }

    internal void Cancel() { _header = null; _task = null; }

    private bool ExceedsClickDistance(Point point) =>
        Math.Abs(point.X - _origin.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(point.Y - _origin.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private static bool IsRowSource(DependencyObject? source, FrameworkElement header)
    {
        while (source is not null && source != header)
        {
            if (source is ButtonBase or TextBoxBase or Thumb or ScrollBar) return false;
            source = source switch
            {
                ContentElement content => ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent,
                Visual => VisualTreeHelper.GetParent(source),
                _ => LogicalTreeHelper.GetParent(source)
            };
        }
        return source == header;
    }
}
