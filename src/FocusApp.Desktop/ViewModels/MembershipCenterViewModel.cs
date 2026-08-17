using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class MembershipCenterViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private bool _hasRenewFeedback;
    private MembershipType _membershipType = MembershipType.Annual;

    public MembershipCenterViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        RenewCommand = new RelayCommand<object>(_ => HasRenewFeedback = true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }

    public ICommand RenewCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public MembershipType MembershipType
    {
        get => _membershipType;
        private set
        {
            if (SetField(ref _membershipType, value))
            {
                OnPropertyChanged(nameof(IsAnnual));
                OnPropertyChanged(nameof(IsLifetime));
            }
        }
    }

    public bool IsAnnual => MembershipType == MembershipType.Annual;

    public bool IsLifetime => MembershipType == MembershipType.Lifetime;

    public bool HasRenewFeedback
    {
        get => _hasRenewFeedback;
        private set => SetField(ref _hasRenewFeedback, value);
    }

    public void Open(MembershipType membershipType)
    {
        if (membershipType == MembershipType.Normal)
        {
            return;
        }

        MembershipType = membershipType;
        HasRenewFeedback = false;
        IsOpen = true;
    }

    private void Close()
    {
        IsOpen = false;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
