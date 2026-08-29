using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FocusApp.Desktop.Views;

public partial class ExportRecordsModal : UserControl
{
    private const double WindowEdgeMargin = 12;
    private const double PopupGap = 6;

    public ExportRecordsModal()
    {
        InitializeComponent();
    }

    private CustomPopupPlacement[] TimeRangePopup_Placement(Size popupSize, Size targetSize, Point offset) =>
        PlacePopup(TimeRangeOptionButton, popupSize, targetSize);

    private CustomPopupPlacement[] IncludedContentPopup_Placement(Size popupSize, Size targetSize, Point offset) =>
        PlacePopup(IncludedContentOptionButton, popupSize, targetSize);

    private CustomPopupPlacement[] PlacePopup(FrameworkElement target, Size popupSize, Size targetSize)
    {
        var window = Window.GetWindow(this);
        if (window is null || window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            return [new CustomPopupPlacement(new Point(0, targetSize.Height + PopupGap), PopupPrimaryAxis.Horizontal)];
        }

        var targetOrigin = target.TranslatePoint(new Point(0, 0), window);
        var centeredX = targetOrigin.X + (targetSize.Width - popupSize.Width) / 2;
        var windowX = Math.Clamp(
            centeredX,
            WindowEdgeMargin,
            Math.Max(WindowEdgeMargin, window.ActualWidth - popupSize.Width - WindowEdgeMargin));

        var belowWindowY = targetOrigin.Y + targetSize.Height + PopupGap;
        var fitsBelow = belowWindowY + popupSize.Height <= window.ActualHeight - WindowEdgeMargin;
        var relativeY = fitsBelow ? targetSize.Height + PopupGap : -popupSize.Height - PopupGap;

        return [new CustomPopupPlacement(
            new Point(windowX - targetOrigin.X, relativeY),
            PopupPrimaryAxis.Horizontal)];
    }
}
