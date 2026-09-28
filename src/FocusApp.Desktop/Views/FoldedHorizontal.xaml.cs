using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FoldedHorizontal : UserControl
{
    public FoldedHorizontal()
    {
        InitializeComponent();
    }

    public double GetPreferredWidth() => GetPreferredWidth(DataContext as FocusFloatingWindowViewModel);

    public double GetPreferredWidth(FocusFloatingWindowViewModel? model)
    {
        if (model is not { HasTarget: true })
            return FocusFloatingSnapCalculator.HorizontalWidth;

        var text = new FormattedText(model.TargetName, CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(FoldedTargetName.FontFamily, FoldedTargetName.FontStyle, FoldedTargetName.FontWeight, FoldedTargetName.FontStretch),
            FoldedTargetName.FontSize, FoldedTargetName.Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        // Leave a small allowance for borders and glyphs rounded at fractional display scaling.
        return Math.Clamp(Math.Ceiling(FocusFloatingSnapCalculator.HorizontalWidth + 25 + text.WidthIncludingTrailingWhitespace + 2),
            FocusFloatingSnapCalculator.HorizontalWidth, FocusFloatingSnapCalculator.MaximumHorizontalWidth);
    }
}
