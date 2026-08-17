using System.Collections.ObjectModel;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class HomePageViewModel
{
    private readonly HomeDurationOptionViewModel? _customDurationOption;

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

        _customDurationOption = DurationOptions.FirstOrDefault(option => !string.IsNullOrEmpty(option.Icon));
        CustomTimeModal = new CustomTimeModalViewModel(ConfirmCustomTime);
        FocusTargetModal = new FocusTargetModalViewModel();
        FocusSession = new FocusSessionViewModel();
        SelectDurationCommand = new RelayCommand<HomeDurationOptionViewModel>(SelectDuration);
        StartFocusCommand = new RelayCommand<object>(_ => StartFocus());
        OpenFocusTargetCommand = new RelayCommand<object>(_ => FocusTargetModal.Open());
    }

    public ReadOnlyCollection<HomeDurationOptionViewModel> DurationOptions { get; }

    public ICommand SelectDurationCommand { get; }

    public ICommand StartFocusCommand { get; }

    public ICommand OpenFocusTargetCommand { get; }

    public CustomTimeModalViewModel CustomTimeModal { get; }

    public FocusTargetModalViewModel FocusTargetModal { get; }

    public FocusSessionViewModel FocusSession { get; }

    private void SelectDuration(HomeDurationOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        if (ReferenceEquals(option, _customDurationOption))
        {
            CustomTimeModal.Open();
            return;
        }

        SelectOnly(option);
    }

    private void ConfirmCustomTime(int minutes)
    {
        if (_customDurationOption is null)
        {
            return;
        }

        _customDurationOption.UpdateDuration($"{minutes} 分钟", minutes);
        SelectOnly(_customDurationOption);
    }

    private void StartFocus()
    {
        var selectedDuration = DurationOptions.First(option => option.IsSelected);
        FocusSession.Start(selectedDuration.Minutes);
    }

    private void SelectOnly(HomeDurationOptionViewModel option)
    {
        if (option.IsSelected)
        {
            return;
        }

        foreach (var durationOption in DurationOptions)
        {
            durationOption.IsSelected = ReferenceEquals(durationOption, option);
        }
    }
}
