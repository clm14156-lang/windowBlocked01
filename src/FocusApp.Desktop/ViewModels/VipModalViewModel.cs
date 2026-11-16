using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Desktop.Services.Cloud;

namespace FocusApp.Desktop.ViewModels;

public sealed class VipModalViewModel : INotifyPropertyChanged
{
    public const string MonthlyPlan = "Monthly";
    public const string YearlyPlan = "Yearly";
    public const string LifetimePlan = "Lifetime";

    private bool _isOpen;
    private string _upgradeFeedback = string.Empty;
    private string _selectedPlanKey = YearlyPlan;

    public VipModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        LaterCommand = new RelayCommand<object>(_ => Close());
        UpgradeCommand = new RelayCommand<object>(_ => OpenOfficialWebsite());
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

    public bool HasUpgradeFeedback => UpgradeFeedback.Length > 0;

    public string UpgradeFeedback
    {
        get => _upgradeFeedback;
        private set
        {
            if (SetField(ref _upgradeFeedback, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUpgradeFeedback)));
        }
    }

    public string SelectedPlanKey
    {
        get => _selectedPlanKey;
        private set => SetField(ref _selectedPlanKey, value);
    }

    public void Open()
    {
        UpgradeFeedback = string.Empty;
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
            UpgradeFeedback = string.Empty;
        }
    }

    private void OpenOfficialWebsite()
    {
        if (AccountWebsiteLinks.OfficialWebsite is not { } address)
        {
            UpgradeFeedback = "功能暂未开放";
            return;
        }

        try
        {
            AccountWebsiteLinks.Open(address);
            UpgradeFeedback = string.Empty;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            UpgradeFeedback = "暂时无法打开官网，请稍后重试。";
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
