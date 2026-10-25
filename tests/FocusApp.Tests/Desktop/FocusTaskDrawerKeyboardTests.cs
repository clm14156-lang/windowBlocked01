using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusTaskDrawerKeyboardTests
{
    [Fact]
    public void EmptyDrawerTabFocusesExistingEditorAndEnterSavesExactlyOneTask()
    {
        Sta(() =>
        {
            using var host = new Host(0);
            var saves = 0; host.Session.TargetTasksChanged += (_, _) => saves++;
            var remaining = host.Session.RemainingFocusSeconds;
            Assert.Equal(Visibility.Visible, host.EmptyState.Visibility);
            Assert.True(host.Key(host.Drawer, Key.Tab)); Pump();
            Assert.True(host.Model.IsCreating);
            Assert.True(host.Input.IsKeyboardFocused);
            var draft = host.Model.Draft;
            Assert.False(host.Key(host.Input, Key.Tab));
            Assert.Same(draft, host.Model.Draft);
            Assert.False(host.Model.CreateTaskCommand.CanExecute(null));
            host.Input.Text = "  快捷创建  ";
            host.Key(host.Input, Key.Enter, preview: false); Pump();
            Assert.Equal("快捷创建", Assert.Single(host.Target.Tasks).Name);
            Assert.Equal(1, saves);
            Assert.False(host.Model.IsCreating);
            Assert.Equal(Visibility.Collapsed, host.EmptyState.Visibility);
            Assert.Equal(remaining, host.Session.RemainingFocusSeconds);
            Assert.Equal(270, host.Drawer.ActualWidth); Assert.Equal(710, host.Drawer.ActualHeight);
            Assert.True(host.Key(host.Drawer, Key.Tab));
            Assert.True(host.Model.IsCreating);
        });
    }

    [Fact]
    public void EscapeCancelsAndOnlyClosedHiddenOrInactiveDrawersKeepNormalTabNavigation()
    {
        Sta(() =>
        {
            using var host = new Host(0);
            Assert.True(host.Key(host.Drawer, Key.Tab)); Pump();
            host.Input.Text = "未保存";
            Assert.True(host.Key(host.Input, Key.Escape)); Pump();
            Assert.False(host.Model.IsCreating); Assert.Empty(host.Target.Tasks);
            host.Model.CloseCommand.Execute(null);
            Assert.False(host.Key(host.Drawer, Key.Tab));
            host.Model.ToggleCommand.Execute(null);
            host.Drawer.Visibility = Visibility.Collapsed;
            Assert.False(host.Key(host.Drawer, Key.Tab));
            host.Drawer.Visibility = Visibility.Visible;
            host.Session.RequestEndCommand.Execute(null); host.Session.DiscardEndCommand.Execute(null);
            Assert.False(host.Key(host.Drawer, Key.Tab));
        });
        Sta(() =>
        {
            using var host = new Host(1);
            Assert.Equal(Visibility.Collapsed, host.EmptyState.Visibility);
            Assert.True(host.Key(host.Drawer, Key.Tab));
            Assert.True(host.Model.IsCreating);
        });
    }

    [Fact]
    public void TextAndPasswordInputsAndEditableSelectorsAreNotIntercepted()
    {
        Sta(() =>
        {
            using var host = new Host(0);
            foreach (var source in new DependencyObject[] { new TextBox(), new PasswordBox(), new RichTextBox(), new ComboBox { IsEditable = true } })
            {
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(host.Drawer)!, 0, Key.Tab)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = source };
                var handled = host.Drawer.TryHandleCreationKey(args);
                Assert.False(handled); Assert.False(args.Handled); Assert.False(host.Model.IsCreating);
            }
        });
    }

    private sealed class Host : IDisposable
    {
        public FocusTargetViewModel Target { get; }
        public FocusSessionViewModel Session { get; } = new(runTimer: false);
        public FocusTaskDrawerViewModel Model => Session.TaskDrawer;
        public FocusTaskDrawer Drawer { get; }
        public TextBox Input => (TextBox)Drawer.FindName("DraftTaskTitleInput");
        public StackPanel EmptyState => (StackPanel)Drawer.FindName("DrawerEmptyState");
        private readonly Window _window;
        public Host(int count)
        {
            Target = new("目标", Enumerable.Range(0, count).Select(i => $"任务{i}"));
            Session.Start(30, Target); Session.AdvancePreparationBy(TimeSpan.FromSeconds(5)); Model.ToggleCommand.Execute(null);
            var layout = new Grid();
            foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
                layout.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
            Drawer = new FocusTaskDrawer { DataContext = Model, Focusable = true }; layout.Children.Add(Drawer);
            _window = new Window { Content = layout, Width = 270, Height = 710, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            _window.Show(); _window.Activate(); Drawer.Focus(); Pump();
        }
        public bool Key(UIElement source, Key key, bool preview = true)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(source)!, 0, key)
                { RoutedEvent = preview ? Keyboard.PreviewKeyDownEvent : Keyboard.KeyDownEvent };
            source.RaiseEvent(args); return args.Handled;
        }
        public void Dispose() { _window.Close(); }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => {}, DispatcherPriority.ApplicationIdle);
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new InvalidOperationException("Task drawer keyboard interaction failed.", failure);
    }
}
