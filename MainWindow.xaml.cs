using System.Windows;
using System.Windows.Input;
using DotNetTestRunner.Infrastructure.Services;
using DotNetTestRunner.Presentation.ViewModels;

namespace DotNetTestRunner;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _MainWindowViewModel;

    public MainWindow()
    {
        InitializeComponent();
        _MainWindowViewModel = new MainWindowViewModel(new SettingsService());
        DataContext = _MainWindowViewModel;
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
}