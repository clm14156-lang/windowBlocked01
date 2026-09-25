using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace FocusApp.Desktop.Views;

/// <summary>Renders the paths from a bundled SVG resource without duplicating its artwork.</summary>
public sealed class SvgIcon : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(SvgIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillOverrideProperty = DependencyProperty.Register(
        nameof(FillOverride), typeof(Brush), typeof(SvgIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Brush? FillOverride
    {
        get => (Brush?)GetValue(FillOverrideProperty);
        set => SetValue(FillOverrideProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (string.IsNullOrWhiteSpace(Source) || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var resource = Application.GetResourceStream(new Uri(Source, UriKind.RelativeOrAbsolute));
        if (resource is null)
        {
            return;
        }

        using var stream = resource.Stream;
        var svg = XDocument.Load(stream).Root;
        if (svg is null)
        {
            return;
        }

        var viewBox = ((string?)svg.Attribute("viewBox") ?? "0 0 1024 1024")
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        if (viewBox.Length != 4 || viewBox[2] <= 0 || viewBox[3] <= 0)
        {
            return;
        }

        var scale = Math.Min(ActualWidth / viewBox[2], ActualHeight / viewBox[3]);
        context.PushTransform(new TranslateTransform(
            (ActualWidth - viewBox[2] * scale) / 2 - viewBox[0] * scale,
            (ActualHeight - viewBox[3] * scale) / 2 - viewBox[1] * scale));
        context.PushTransform(new ScaleTransform(scale, scale));
        foreach (var path in svg.Descendants().Where(element => element.Name.LocalName == "path"))
        {
            var data = (string?)path.Attribute("d");
            if (string.IsNullOrWhiteSpace(data))
            {
                continue;
            }

            var color = (string?)path.Attribute("fill") ?? "#000000";
            var brush = FillOverride ?? (Brush)new BrushConverter().ConvertFromInvariantString(color)!;
            context.DrawGeometry(brush, null, Geometry.Parse(data));
        }
        context.Pop();
        context.Pop();
    }
}
