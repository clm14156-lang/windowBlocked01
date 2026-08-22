using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusTargetModal : UserControl
{
    public static readonly DependencyProperty IsTaskScrollBarActiveProperty = DependencyProperty.Register(
        nameof(IsTaskScrollBarActive),
        typeof(bool),
        typeof(FocusTargetModal),
        new PropertyMetadata(false));

    private readonly DispatcherTimer _taskScrollBarIdleTimer;

    public FocusTargetModal()
    {
        InitializeComponent();

        _taskScrollBarIdleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        _taskScrollBarIdleTimer.Tick += TaskScrollBarIdleTimer_Tick;
    }

    public bool IsTaskScrollBarActive
    {
        get => (bool)GetValue(IsTaskScrollBarActiveProperty);
        private set => SetValue(IsTaskScrollBarActiveProperty, value);
    }

    private static void FocusEditor(TextBox textBox, bool placeCaretAtStart)
    {
        textBox.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (!textBox.IsVisible)
                {
                    return;
                }

                textBox.Focus();
                if (placeCaretAtStart)
                {
                    textBox.CaretIndex = 0;
                    textBox.SelectionStart = 0;
                    textBox.SelectionLength = 0;
                }
                else
                {
                    textBox.SelectAll();
                }
            }));
    }

    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } textBox)
        {
            var placeCaretAtStart = ReferenceEquals(textBox, NewTaskTextBox) ||
                                    ReferenceEquals(textBox, NewTargetTextBox);
            FocusEditor(textBox, placeCaretAtStart);
        }
    }

    private void TaskScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) <= 0.01)
        {
            return;
        }

        IsTaskScrollBarActive = true;
        _taskScrollBarIdleTimer.Stop();
        _taskScrollBarIdleTimer.Start();
    }

    private void TaskScrollBarIdleTimer_Tick(object? sender, EventArgs e)
    {
        _taskScrollBarIdleTimer.Stop();
        IsTaskScrollBarActive = false;
    }

    private void NewTaskTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FocusTargetModalViewModel viewModel)
        {
            viewModel.ConfirmAddTaskCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void NewTaskTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitPendingTaskInput();
    }

    private void FocusTargetModal_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!NewTaskTextBox.IsMouseOver)
        {
            CommitPendingTaskInput();
        }

        if (Keyboard.FocusedElement is TextBox focusedEditor && !focusedEditor.IsMouseOver)
        {
            CommitTaskEdit(focusedEditor);
        }
    }

    private void CommitPendingTaskInput()
    {
        if (DataContext is FocusTargetModalViewModel { IsAddingTask: true } viewModel)
        {
            viewModel.ConfirmAddTaskCommand.Execute(null);
        }
    }

    private void EditTaskTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            sender is TextBox { DataContext: FocusTaskViewModel task } &&
            DataContext is FocusTargetModalViewModel viewModel)
        {
            viewModel.ConfirmEditTaskCommand.Execute(task);
            e.Handled = true;
        }
    }

    private void EditTaskTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            CommitTaskEdit(textBox);
        }
    }

    private void CommitTaskEdit(TextBox textBox)
    {
        if (textBox.DataContext is FocusTaskViewModel { IsEditing: true } task &&
            DataContext is FocusTargetModalViewModel viewModel)
        {
            viewModel.ConfirmEditTaskCommand.Execute(task);
        }
    }

    private void EditTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FocusTaskViewModel task } &&
            DataContext is FocusTargetModalViewModel viewModel)
        {
            viewModel.BeginEditTaskCommand.Execute(task);
        }
    }

    private void DeleteTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FocusTaskViewModel task } &&
            DataContext is FocusTargetModalViewModel viewModel)
        {
            viewModel.DeleteTaskCommand.Execute(task);
        }
    }

    private void TaskRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border { DataContext: FocusTaskViewModel task })
        {
            task.IsHovered = true;
        }
    }

    private void TaskRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border { DataContext: FocusTaskViewModel task })
        {
            task.IsHovered = false;
        }
    }
}
