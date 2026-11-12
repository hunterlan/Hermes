using Avalonia.Threading;

namespace Hermes.App.Services;

/// <summary>
/// The last line of defence for bugs: an exception nobody caught on the UI thread is shown in a "Something went
/// wrong" dialog instead of ending the app without a word. Exceptions left unobserved in a task are ignored for now.
/// It is not how Hermes handles expected failures; storage errors are caught where they happen and explained in their
/// own words.
/// </summary>
public sealed class UnexpectedErrorHandler(DialogService dialogs)
{
    private bool _showing;

    /// <summary>Hooks the handler up to the UI thread and to tasks nobody awaited (only to ignore them).</summary>
    public static void Install(DialogService dialogs)
    {
        var handler = new UnexpectedErrorHandler(dialogs);
        Dispatcher.UIThread.UnhandledException += (_, e) => e.Handled = handler.OnUiThreadException(e.Exception);
        // These arrive late, when the garbage collector runs, and have nothing to do with what the user is doing, so
        // they don't show the dialog. They are left for logging in #92.
        TaskScheduler.UnobservedTaskException += (_, e) => IgnoreUnobservedTaskException(e);
    }

    /// <summary>Marks an exception nobody observed in a task as observed, so it doesn't end the process.</summary>
    public static void IgnoreUnobservedTaskException(UnobservedTaskExceptionEventArgs e) => e.SetObserved();

    /// <summary>
    /// Shows the exception, and says whether the app may carry on. It does, unless the process is out of memory, where
    /// even the dialog may not open.
    /// </summary>
    public bool OnUiThreadException(Exception exception)
    {
        if (exception is OutOfMemoryException)
            return false;
        Show(exception);
        return true;
    }

    /// <summary>Shows the dialog on the UI thread. One at a time: an error that repeats must not pile dialogs up.</summary>
    public void Show(Exception exception)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (_showing)
                return;
            _showing = true;
            try
            {
                await dialogs.ShowUnexpectedErrorAsync(exception);
            }
            catch (InvalidOperationException)
            {
                // No window to show it in (the app is starting or closing).
            }
            finally
            {
                _showing = false;
            }
        });
    }
}
