using System.Windows.Controls;

namespace FocusApp.Desktop.Views;

/// <summary>Read-only summary of the saved session hovered in the calendar record list.</summary>
public partial class CalendarRecordHoverToolTip : UserControl
{
    public CalendarRecordHoverToolTip() => InitializeComponent();

    public void SetPlacement(bool pointerOnTop, double pointerLeft)
    {
        RecordPoptipSurface.PointerOnTop = pointerOnTop;
        RecordPoptipSurface.PointerLeft = pointerLeft;
    }
}
