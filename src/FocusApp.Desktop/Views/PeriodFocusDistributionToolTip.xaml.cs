using System.Windows;
using System.Windows.Controls;

namespace FocusApp.Desktop.Views;

public partial class PeriodFocusDistributionToolTip : UserControl
{
    public static readonly DependencyProperty PointerLeftProperty = DependencyProperty.Register(
        nameof(PointerLeft), typeof(double), typeof(PeriodFocusDistributionToolTip), new PropertyMetadata(84d));

    public double PointerLeft
    {
        get => (double)GetValue(PointerLeftProperty);
        set => SetValue(PointerLeftProperty, value);
    }

    public PeriodFocusDistributionToolTip() => InitializeComponent();
}
