using CommunityToolkit.Mvvm.Input;

namespace Hermes.App.Tests;

/// <summary>
/// The UI runs its async work through the toolkit's async commands instead of <c>async void</c> methods. With the
/// default options an unexpected exception is not swallowed: it is thrown on the synchronization context that
/// executed the command, which for the app is the UI thread and so reaches the app's unhandled-error handler.
/// </summary>
public class AsyncCommandExceptionTests
{
    [Fact]
    public void An_unexpected_exception_reaches_the_context_that_executed_the_command()
    {
        var context = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var command = new AsyncRelayCommand(FailAsync);

            command.Execute(null);

            var raised = Assert.Single(context.Raised);
            Assert.IsType<InvalidOperationException>(raised);
            Assert.Equal("unexpected", raised.Message);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static async Task FailAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("unexpected");
    }

    /// <summary>Runs what is posted to it at once and keeps the exceptions it throws, as the UI's handler would see them.</summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public List<Exception> Raised { get; } = [];

        public override void Post(SendOrPostCallback callback, object? state)
        {
            try
            {
                callback(state);
            }
            catch (Exception ex)
            {
                Raised.Add(ex);
            }
        }
    }
}
