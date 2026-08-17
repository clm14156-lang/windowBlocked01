using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class VipModalViewModel : INotifyPropertyChanged
{
    public const string MonthlyPlan = "Monthly";
    public const string YearlyPlan = "Yearly";
    public const string LifetimePlan = "Lifetime";

    private bool _isOpen;
    private bool _hasUpgradeFeedback;
    private string _selectedPlanKey = YearlyPlan;

    public VipModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        LaterCommand = new RelayCommand<object>(_ => Close());
        UpgradeCommand = new RelayCommand<object>(_ => HasUpgradeFeedback = true);
        SelectPlanCommand = new RelayCommand<string>(SelectPlan);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }

    public ICommand LaterCommand { get; }

    public ICommand UpgradeCommand { get; }

    public ICommand SelectPlanCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public bool HasUpgradeFeedback
    {
        get => _hasUpgradeFeedback;
        private set => SetField(ref _hasUpgradeFeedback, value);
    }

    public string SelectedPlanKey
    {
        get => _selectedPlanKey;
        private set => SetField(ref _selectedPlanKey, value);
    }

    public void Open()
    {
        HasUpgradeFeedback = false;
        IsOpen = true;
    }

    private void Close()
    {
        IsOpen = false;
    }

    private void SelectPlan(string? planKey)
    {
        if (planKey is MonthlyPlan or YearlyPlan or LifetimePlan)
        {
            SelectedPlanKey = planKey;
            HasUpgradeFeedback = false;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
