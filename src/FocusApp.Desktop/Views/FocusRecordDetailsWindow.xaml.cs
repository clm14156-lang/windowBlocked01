using System.Windows;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusRecordDetailsWindow : Window
{
    private readonly StatisticsOverviewViewModel _ownerModel;
    private readonly FocusSessionRecordViewModel _record;
    public FocusRecordDetailsWindow(StatisticsOverviewViewModel ownerModel, FocusSessionRecordViewModel record)
    {
        InitializeComponent();
        _ownerModel = ownerModel;
        _record = record;
        DataContext = record;
        Deactivated += (_, _) => Close();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        DeleteButton.IsEnabled = false;
        if (await _ownerModel.DeleteFocusRecordAsync(_record))
        {
            if (IsVisible) Close();
        }
        else
        {
            DeleteError.Visibility = Visibility.Visible;
            DeleteButton.IsEnabled = true;
        }
    }
}
