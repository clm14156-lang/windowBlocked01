using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public sealed class MembershipCenterViewModel : INotifyPropertyChanged
{
    private readonly Func<DateTimeOffset> _utcNow;
    private MembershipStatus? _status;
    private bool _isOpen;
    private MembershipType _membershipType;

    public MembershipCenterViewModel(Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        CloseCommand = new RelayCommand<object>(_ => Close());
        RenewCommand = new RelayCommand<object>(_ =>
        {
            if (!CanRenew) return;
            Close();
            RenewalRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RenewalRequested;

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
                OnPropertyChanged(nameof(CanRenew));
            }
        }
    }

    public bool CanRenew => MembershipType == MembershipType.Annual;
    internal bool IsMonthly => CanRenew && _status?.Plan == "monthly";
    public string ExpiryText => MembershipType == MembershipType.Lifetime ? "永久有效" :
        _status?.GrantExpiresAtUtc is { } expiry ? $"有效期至 {expiry.LocalDateTime:yyyy年M月d日}" : "有效期尚未验证";
    public string DaysRemaining => _status?.GrantExpiresAtUtc is { } expiry
        ? Math.Max(0, (int)Math.Ceiling((expiry - _utcNow()).TotalDays)).ToString()
        : "—";

    public void ApplyStatus(MembershipStatus value)
    {
        _status = value;
        MembershipType = value.IsActive ? value.Kind == "lifetime" ? MembershipType.Lifetime : MembershipType.Annual : MembershipType.Normal;
        RefreshExpiry();
    }

    public void Open(MembershipType membershipType)
    {
        if (membershipType == MembershipType.Normal)
        {
            return;
        }

        MembershipType = membershipType;
        RefreshExpiry();
        IsOpen = true;
    }

    private void RefreshExpiry()
    {
        OnPropertyChanged(nameof(ExpiryText));
        OnPropertyChanged(nameof(DaysRemaining));
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
