using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace FocusApp.Desktop.ViewModels;

public sealed class BlockingContentItemViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly BlockingWebsiteItemViewModel? _website;
    private readonly BlockingApplicationItemViewModel? _application;

    public BlockingContentItemViewModel(BlockingWebsiteItemViewModel website)
    {
        _website = website;
        _website.PropertyChanged += Source_PropertyChanged;
        Name = website.Name;
        TypeText = "网站";
        IconGlyph = "\uE774";
        DisableCommand = new RelayCommand<object>(_ => Disable());
    }

    public BlockingContentItemViewModel(BlockingApplicationItemViewModel application)
    {
        _application = application;
        _application.PropertyChanged += Source_PropertyChanged;
        Name = application.Name;
        TypeText = "软件";
        IconGlyph = "\uE71D";
        DisableCommand = new RelayCommand<object>(_ => Disable());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string TypeText { get; }

    public string IconGlyph { get; }

    public ICommand DisableCommand { get; }

    public bool IsWebsite => _website is not null;

    public bool IsEnabled => _website?.IsEnabled ?? _application?.IsEnabled == true;

    public ImageSource? Favicon => _website?.Favicon;

    public void Disable()
    {
        if (_website is not null)
        {
            _website.IsEnabled = false;
        }
        else if (_application is not null)
        {
            _application.IsEnabled = false;
        }
    }

    public void Dispose()
    {
        if (_website is not null) _website.PropertyChanged -= Source_PropertyChanged;
        if (_application is not null) _application.PropertyChanged -= Source_PropertyChanged;
    }

    private void Source_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BlockingWebsiteItemViewModel.IsEnabled) or nameof(BlockingApplicationItemViewModel.IsEnabled))
        {
            OnPropertyChanged(nameof(IsEnabled));
        }

        if (e.PropertyName == nameof(BlockingWebsiteItemViewModel.Favicon))
        {
            OnPropertyChanged(nameof(Favicon));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
