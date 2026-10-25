using System.Windows;

namespace FocusApp.Desktop.Views;

internal readonly record struct CalendarRecordHoverPlacement(Point Offset, bool PointerOnTop, double PointerLeft)
{
    private const double Inset = 8;
    private const double Gap = 2;

    public static CalendarRecordHoverPlacement Calculate(Rect row, Size popup, Size viewport)
    {
        var center = row.Left + row.Width / 2;
        var maxX = Math.Max(Inset, viewport.Width - Inset - popup.Width);
        var x = Math.Clamp(center - popup.Width / 2, Inset, maxX);
        var below = row.Bottom + Gap;
        var above = row.Top - Gap - popup.Height;
        var spaceAbove = Math.Max(0, row.Top - Gap - Inset);
        var spaceBelow = Math.Max(0, viewport.Height - Inset - below);
        // Prefer above the record; use the roomier side when it cannot fit there.
        var onTop = spaceAbove < popup.Height && spaceBelow > spaceAbove;
        var maxY = Math.Max(Inset, viewport.Height - Inset - popup.Height);
        var y = Math.Clamp(onTop ? below : above, Inset, maxY);
        return new(new Point(x - row.Left, y - row.Top), onTop,
            PoptipChrome.PointerLeftForAnchor(center - x, popup.Width));
    }
}
