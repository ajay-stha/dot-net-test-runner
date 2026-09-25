using System.Windows.Input;

namespace DotNetTestRunner.Commands;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _Execute;
    private readonly Func<bool>? _CanExecute;
    private bool _IsExecuting;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _Execute = execute;
        _CanExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (_IsExecuting)
        {
            return false;
        }

        return _CanExecute?.Invoke() ?? true;
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _IsExecuting = true;
        NotifyCanExecuteChanged();

        try
        {
            await _Execute();
        }
        finally
        {
            _IsExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
