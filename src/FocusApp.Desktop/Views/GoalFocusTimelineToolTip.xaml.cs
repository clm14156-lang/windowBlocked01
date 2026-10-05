using System.Windows;
using System.Windows.Controls;

namespace FocusApp.Desktop.Views;

public partial class GoalFocusTimelineToolTip : UserControl
{
    public static readonly DependencyProperty PointerLeftProperty = DependencyProperty.Register(
        nameof(PointerLeft), typeof(double), typeof(GoalFocusTimelineToolTip), new PropertyMetadata(105d));
    public double PointerLeft { get => (double)GetValue(PointerLeftProperty); set => SetValue(PointerLeftProperty, value); }
    public GoalFocusTimelineToolTip() => InitializeComponent();
}
