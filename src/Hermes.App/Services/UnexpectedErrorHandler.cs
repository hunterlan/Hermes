using Avalonia.Threading;

namespace Hermes.App.Services;

/// <summary>
/// The last line of defence for bugs: an exception nobody caught on the UI thread is shown in a "Something went
/// wrong" dialog, and Hermes quits when the dialog closes. Exceptions left unobserved in a task are ignored for now.
/// Code that runs after a bug can't be trusted, so quitting skips the usual close path: no prompt to save, no final
/// session write. The session the timer saved last stays as it is. This is not how Hermes handles expected failures;
/// storage errors are caught where they happen and explained in their own words.
/// </summary>
/// <param name="dialogs">Shows the dialog.</param>
/// <param name="quit">Ends the app without asking anything. A seam so tests don't end the test host.</param>
public sealed class UnexpectedErrorHandler(DialogService dialogs, Action quit)
{
    /// <summary>True once a bug was caught. The window then closes without prompting or saving the session.</summary>
    public bool IsQuitting { get; private set; }

    /// <summary>Hooks the handler up to the UI thread and to tasks nobody awaited (only to ignore them).</summary>
    public static void Install(UnexpectedErrorHandler handler)
    {
        Dispatcher.UIThread.UnhandledException += (_, e) => e.Handled = handler.OnUiThreadException(e.Exception);
        // These arrive late, when the garbage collector runs, and have nothing to do with what the user is doing, so
        // they neither show the dialog nor quit. They are left for logging in #92.
        TaskScheduler.UnobservedTaskException += (_, e) => IgnoreUnobservedTaskException(e);
    }

    /// <summary>Marks an exception nobody observed in a task as observed, so it doesn't end the process.</summary>
    public static void IgnoreUnobservedTaskException(UnobservedTaskExceptionEventArgs e) => e.SetObserved();

    /// <summary>
    /// Handles an exception from the UI thread. The result is for <c>Handled</c>: true only so the dialog can be shown
    /// before quitting. Out of memory is left to end the process, where even the dialog may not open.
    /// </summary>
    public bool OnUiThreadException(Exception exception)
    {
        if (exception is OutOfMemoryException)
            return false;
        Fail(exception);
        return true;
    }

    /// <summary>
    /// Shows the dialog on the UI thread, then quits, even when the dialog couldn't be shown. A second error while
    /// the first is being handled is ignored: the app is quitting anyway.
    /// </summary>
    public void Fail(Exception exception)
    {
        if (IsQuitting)
            return;
        IsQuitting = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await dialogs.ShowUnexpectedErrorAsync(exception);
            }
            catch (Exception)
            {
                // Nothing else can report it, and an exception leaving here would come back as another bug.
            }
            finally
            {
                quit();
            }
        });
    }
}
