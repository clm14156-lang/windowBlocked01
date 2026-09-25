using System.Windows;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class AutomaticRuleEditorWindow : Window
{
    public const double SurfaceInset = 10;
    private readonly AutomaticRuleModalViewModel _model;

    public AutomaticRuleEditorWindow(AutomaticRuleModalViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        Loaded += (_, _) => KeepWithinWorkArea();
        SizeChanged += (_, _) => KeepWithinWorkArea();
        Deactivated += (_, _) => Cancel();
        Closed += (_, _) =>
        {
            if (_model.IsEditorOpen) _model.CancelEditor();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Cancel();

    private void Delete_Click(object sender, RoutedEventArgs e) => _model.DeleteEditor();

    private void Save_Click(object sender, RoutedEventArgs e) => _model.SaveEditor();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Cancel();
        e.Handled = true;
    }

    private void Cancel()
    {
        if (_model.IsEditorOpen) _model.CancelEditor();
    }

    private void KeepWithinWorkArea()
    {
        if (!IsLoaded) return;

        var workArea = SystemParameters.WorkArea;
        if (Left < workArea.Left || Left >= workArea.Right ||
            Top < workArea.Top || Top >= workArea.Bottom) return;

        const double edgeMargin = 6;
        if (Top + Height > workArea.Bottom - edgeMargin)
            Top = Math.Max(workArea.Top + edgeMargin, workArea.Bottom - Height - edgeMargin);
    }
}
