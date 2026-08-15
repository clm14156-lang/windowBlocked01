using System.Collections.ObjectModel;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class HomePageViewModel
{
    public HomePageViewModel(IEnumerable<HomeDurationOptionViewModel> durationOptions)
    {
        DurationOptions = new ReadOnlyCollection<HomeDurationOptionViewModel>(durationOptions.ToList());

        if (DurationOptions.Count == 0)
        {
            throw new ArgumentException("At least one duration option is required.", nameof(durationOptions));
        }

        if (!DurationOptions.Any(option => option.IsSelected))
        {
            DurationOptions[0].IsSelected = true;
        }

        SelectDurationCommand = new RelayCommand<HomeDurationOptionViewModel>(SelectDuration);
    }

    public ReadOnlyCollection<HomeDurationOptionViewModel> DurationOptions { get; }

    public ICommand SelectDurationCommand { get; }

    private void SelectDuration(HomeDurationOptionViewModel? option)
    {
        if (option is null || option.IsSelected)
        {
            return;
        }

        foreach (var durationOption in DurationOptions)
        {
            durationOption.IsSelected = ReferenceEquals(durationOption, option);
        }
    }
}
