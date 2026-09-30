using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace DotNetTestRunner.Presentation.ViewModels;

/// <summary>
/// Accumulates the console output of a run and publishes it to the UI. Output arrives on
/// background threads and can be very chatty, so lines are queued and flushed in batches on
/// the UI thread rather than marshalled one at a time.
/// </summary>
public sealed class OutputLogBuffer : ObservableObject
{
    private readonly Dispatcher _UiDispatcher;
    private readonly StringBuilder _Builder = new();
    private readonly ConcurrentQueue<string> _PendingLines = new();
    private int _IsFlushScheduled;
    private string _Text = string.Empty;

    public OutputLogBuffer(Dispatcher uiDispatcher)
    {
        _UiDispatcher = uiDispatcher;
    }

    /// <summary>
    /// Gets the accumulated output. Updated on the UI thread as queued lines are flushed.
    /// </summary>
    public string Text
    {
        get => _Text;
        private set => SetProperty(ref _Text, value);
    }

    /// <summary>
    /// Queues a line to be shown. Safe to call from any thread.
    /// </summary>
    /// <param name="line">Line of output to append.</param>
    public void Append(string line)
    {
        _PendingLines.Enqueue(line);
        ScheduleFlush();
    }

    /// <summary>
    /// Discards all output, including lines that have not been shown yet.
    /// </summary>
    public void Clear()
    {
        while (_PendingLines.TryDequeue(out _))
        {
        }

        _Builder.Clear();
        Text = string.Empty;
    }

    private void ScheduleFlush()
    {
        // Only one flush may be in flight; the flush itself drains everything queued since.
        if (Interlocked.Exchange(ref _IsFlushScheduled, 1) == 1)
        {
            return;
        }

        _ = _UiDispatcher.BeginInvoke(FlushPendingLines);
    }

    private void FlushPendingLines()
    {
        try
        {
            var wroteLine = false;

            while (_PendingLines.TryDequeue(out var line))
            {
                if (_Builder.Length > 0)
                {
                    _Builder.AppendLine();
                }

                _Builder.Append(line);
                wroteLine = true;
            }

            if (wroteLine)
            {
                Text = _Builder.ToString();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _IsFlushScheduled, 0);

            // A line queued between the drain and the reset would otherwise wait for the
            // next Append, so schedule another pass for it here.
            if (!_PendingLines.IsEmpty)
            {
                ScheduleFlush();
            }
        }
    }
}
