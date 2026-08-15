using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusApp.Desktop.ViewModels;

public sealed class NavigationItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public NavigationItemViewModel(NavigationPage page, string title, string icon)
    {
        Page = page;
        Title = title;
        Icon = icon;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public NavigationPage Page { get; }

    public string Title { get; }

    public string Icon { get; }

    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
