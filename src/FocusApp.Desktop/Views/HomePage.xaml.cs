using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FocusApp.Desktop.Views;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        AutomaticBlockingToolTip.CustomPopupPlacementCallback = PlaceAutomaticBlockingTooltip;
    }

    private static CustomPopupPlacement[] PlaceAutomaticBlockingTooltip(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        const double targetGap = 8;
        return
        [
            new CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - targetGap),
                PopupPrimaryAxis.Horizontal)
        ];
    }
}
