using System.Windows;
using System.Windows.Controls;

namespace FocusApp.Desktop.Views;

public partial class PeriodFocusDistributionToolTip : UserControl
{
    public static readonly DependencyProperty PointerLeftProperty = DependencyProperty.Register(
        nameof(PointerLeft), typeof(double), typeof(PeriodFocusDistributionToolTip), new PropertyMetadata(double.NaN));

    public double PointerLeft
    {
        get => (double)GetValue(PointerLeftProperty);
        set => SetValue(PointerLeftProperty, value);
    }

    public static readonly DependencyProperty PointerOnTopProperty = DependencyProperty.Register(
        nameof(PointerOnTop), typeof(bool), typeof(PeriodFocusDistributionToolTip), new PropertyMetadata(false));
    public bool PointerOnTop { get => (bool)GetValue(PointerOnTopProperty); set => SetValue(PointerOnTopProperty, value); }

    public PeriodFocusDistributionToolTip() => InitializeComponent();
}
