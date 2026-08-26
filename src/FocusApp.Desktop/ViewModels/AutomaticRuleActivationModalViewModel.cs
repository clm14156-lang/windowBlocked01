using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AutomaticRuleActivationModalViewModel : INotifyPropertyChanged
{
    private AutomaticRuleItemViewModel? _pendingRule;
    private bool _isOpen;

    public AutomaticRuleActivationModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        ConfirmCommand = new RelayCommand<object>(_ => Confirm());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<AutomaticRuleItemViewModel>? ActivationConfirmed;

    public ICommand CloseCommand { get; }

    public ICommand ConfirmCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public void Open(AutomaticRuleItemViewModel rule)
    {
        _pendingRule = rule;
        IsOpen = true;
    }

    private void Confirm()
    {
        var rule = _pendingRule;
        Close();
        if (rule is not null)
        {
            ActivationConfirmed?.Invoke(this, rule);
        }
    }

    private void Close()
    {
        _pendingRule = null;
        IsOpen = false;
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
