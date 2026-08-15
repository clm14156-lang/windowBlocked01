using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusApp.Desktop.ViewModels;

public sealed class HomeDurationOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public HomeDurationOptionViewModel(string label, string icon, bool isSelected = false)
    {
        Label = label;
        Icon = icon;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }

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
