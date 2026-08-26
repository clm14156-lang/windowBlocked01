using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FocusApp.Desktop.Views;

public partial class AutomaticBlockingToastWindow : Window
{
    public AutomaticBlockingToastWindow() => InitializeComponent();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left, workArea.Right - Width - 10);
        Top = Math.Max(workArea.Top, workArea.Bottom - Height - 10);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, -20);
        SetWindowLong(handle, -20, style | 0x08000000);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
