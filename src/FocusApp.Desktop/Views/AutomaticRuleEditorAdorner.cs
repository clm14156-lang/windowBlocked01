using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace FocusApp.Desktop.Views;

// Keeping all interactive surfaces in the owner HWND prevents popup activation
// and ensures a complete mouse gesture is delivered to the same application.
internal sealed class AutomaticRuleEditorAdorner : Adorner
{
    private readonly FrameworkElement _surface;
    private readonly VisualCollection _visuals;
    private Point _position;

    public AutomaticRuleEditorAdorner(UIElement adornedElement, FrameworkElement surface) : base(adornedElement)
    {
        _surface = surface;
        _visuals = new VisualCollection(this) { surface };
        AddLogicalChild(surface);
    }

    public Point Position
    {
        get => _position;
        set { _position = value; InvalidateArrange(); }
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visuals[index];
    protected override System.Collections.IEnumerator LogicalChildren => new[] { _surface }.GetEnumerator();
    protected override Size MeasureOverride(Size constraint)
    {
        _surface.Measure(new Size(280, 400));
        return AdornedElement.RenderSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        _surface.Arrange(new Rect(Position, new Size(280, 400)));
        return finalSize;
    }
}
