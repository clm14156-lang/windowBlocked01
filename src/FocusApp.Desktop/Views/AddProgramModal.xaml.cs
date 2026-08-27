using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class AddProgramModal : UserControl
{
    private readonly ProgramIconService _programIconService = new();
    private readonly IProgramFilePicker _programFilePicker = new ProgramFilePicker();

    public AddProgramModal()
    {
        InitializeComponent();
        DataContextChanged += AddProgramModal_DataContextChanged;
    }

    private void AddProgramModal_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AddProgramModalViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= ProgramModal_PropertyChanged;
            oldViewModel.ChooseProgramRequested -= ProgramModal_ChooseProgramRequested;
        }

        if (e.NewValue is AddProgramModalViewModel newViewModel)
        {
            newViewModel.PropertyChanged += ProgramModal_PropertyChanged;
            newViewModel.ChooseProgramRequested += ProgramModal_ChooseProgramRequested;
        }
    }

    private void ProgramModal_ChooseProgramRequested(object? sender, EventArgs e)
    {
        var selection = _programFilePicker.PickProgram();
        if (selection is not null && sender is AddProgramModalViewModel viewModel)
        {
            viewModel.ApplyProgramSelection(selection);
        }
    }

    private void ProgramModal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AddProgramModalViewModel viewModel &&
            (e.PropertyName is nameof(AddProgramModalViewModel.IsOpen) or nameof(AddProgramModalViewModel.RecentPrograms)) &&
            viewModel.IsOpen)
        {
            LoadProgramIcons(viewModel);
        }
    }

    private void LoadProgramIcons(AddProgramModalViewModel viewModel)
    {
        foreach (var program in viewModel.RecentPrograms.Where(program => program.Icon is null).ToArray())
        {
            _ = LoadProgramIconAsync(viewModel, program);
        }
    }

    private async Task LoadProgramIconAsync(AddProgramModalViewModel viewModel, RecentProgramRecordViewModel program)
    {
        var icon = await _programIconService.GetIconAsync(program.ExePath).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() =>
        {
            if (viewModel.IsOpen && viewModel.RecentPrograms.Contains(program))
            {
                program.Icon = icon;
            }
        });
    }

    private void ModalCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
