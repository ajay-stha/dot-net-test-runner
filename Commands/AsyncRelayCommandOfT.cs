using System.Windows.Input;

namespace DotNetTestRunner.Commands;

public sealed class AsyncRelayCommand<T> : ICommand
    where T : class
{
    private readonly Func<T, Task> _Execute;
    private readonly Func<T, bool>? _CanExecute;
    private bool _IsExecuting;

    public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool>? canExecute = null)
    {
        _Execute = execute;
        _CanExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (_IsExecuting || parameter is not T typedParameter)
        {
            return false;
        }

        return _CanExecute?.Invoke(typedParameter) ?? true;
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter) || parameter is not T typedParameter)
        {
            return;
        }

        _IsExecuting = true;
        NotifyCanExecuteChanged();

        try
        {
            await _Execute(typedParameter);
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