using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DotNetTestRunner.Infrastructure.Logging;
using DotNetTestRunner.Infrastructure.Services;
using DotNetTestRunner.Presentation.ViewModels;

namespace DotNetTestRunner;

public partial class MainWindow : Window
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    private readonly MainWindowViewModel _MainWindowViewModel;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    public MainWindow()
    {
        InitializeComponent();

        var testRunLogger = (System.Windows.Application.Current as App)?.TestRunLogger ?? new TestRunLogger();
        _MainWindowViewModel = new MainWindowViewModel(new SettingsService(), testRunLogger);
        DataContext = _MainWindowViewModel;

        StateChanged += MainWindow_StateChanged;
    }

    /// <summary>
    /// Requests native rounded window corners and hooks window messages once the handle exists.
    /// </summary>
    protected override void OnSourceInitialized(System.EventArgs e)
    {
        base.OnSourceInitialized(e);

        int preference = DWMWCP_ROUND;
        nint hwnd = new WindowInteropHelper(this).Handle;
        // Ignored on Windows versions that do not support the attribute.
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

        HwndSource.FromHwnd(hwnd)?.AddHook(WindowHook);
    }

    /// <summary>
    /// Constrains the maximized size to the monitor work area so the window never
    /// covers the taskbar.
    /// </summary>
    private static nint WindowHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            nint monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != nint.Zero)
            {
                var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    RECT work = monitorInfo.rcWork;
                    RECT area = monitorInfo.rcMonitor;
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    mmi.ptMaxPosition.X = work.Left - area.Left;
                    mmi.ptMaxPosition.Y = work.Top - area.Top;
                    mmi.ptMaxSize.X = work.Right - work.Left;
                    mmi.ptMaxSize.Y = work.Bottom - work.Top;
                    Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                }
            }
        }

        return nint.Zero;
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        _MainWindowViewModel.AdjustFontSize(e.Delta > 0 ? 0.5 : -0.5);
        e.Handled = true;
    }

    /// <summary>
    /// Minimizes the window.
    /// </summary>
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>
    /// Toggles the window between maximized and normal states.
    /// </summary>
    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>
    /// Closes the window.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Updates the maximize/restore glyph to match the current window state.
    /// </summary>
    private void MainWindow_StateChanged(object? sender, System.EventArgs e)
    {
        // Segoe MDL2 Assets: E922 = Maximize, E923 = Restore.
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeRestoreButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}