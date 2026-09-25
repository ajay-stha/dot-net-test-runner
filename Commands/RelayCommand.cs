using System.Windows.Input;

namespace DotNetTestRunner.Commands;

public sealed class RelayCommand : ICommand
{
    private readonly Action _Execute;
    private readonly Func<bool>? _CanExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _Execute = execute;
        _CanExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _CanExecute?.Invoke() ?? true;
    }

    public void Execute(object? parameter)
    {
        _Execute();
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
