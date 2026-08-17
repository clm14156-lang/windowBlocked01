using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusApp.Desktop.ViewModels;

public sealed class HomeDurationOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private int _minutes;

    public HomeDurationOptionViewModel(string label, string icon, bool isSelected = false, int minutes = 25)
    {
        Label = label;
        Icon = icon;
        _isSelected = isSelected;
        _minutes = minutes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; private set; }

    public string Icon { get; }

    public int Minutes => _minutes;

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

    internal void UpdateDuration(string label, int minutes)
    {
        if (Label != label)
        {
            Label = label;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }

        if (_minutes != minutes)
        {
            _minutes = minutes;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Minutes)));
        }
    }
}
