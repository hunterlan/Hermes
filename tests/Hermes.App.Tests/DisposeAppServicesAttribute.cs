using System.Reflection;
using Avalonia.Threading;
using Xunit.v3;

[assembly: Hermes.App.Tests.DisposeAppServices]

namespace Hermes.App.Tests;

/// <summary>
/// Disposes the app's DI container after every test.
/// Avalonia.Headless.XUnit creates a new <see cref="App"/>, and so a new container, for each test,
/// but only the desktop Exit handler disposes it. Without this, the day-change timer that
/// <c>WorkspaceService</c> starts stays in the runtime's timer queue, and through the service's
/// event subscriptions it keeps each test's whole window graph alive. See issue #124.
/// A singleton whose <c>Dispose</c> throws fails the test instead of being hidden.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class DisposeAppServicesAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        // No Avalonia test has started an app yet.
        if (App.Services is not IDisposable services)
            return;

        // Avalonia tests run After on the headless UI thread, the only thread with a dispatcher.
        // Plain [Fact] tests run in parallel on other threads and own no app, but App.Services
        // still points at whichever Avalonia test is running at that moment, so they must not
        // dispose it.
        // Read Dispatcher.UIThread only from a thread that has a dispatcher: while the headless
        // session sets up the next test's app there is none, and reading it from a plain [Fact]
        // then makes that thread the UI thread, so the next Avalonia test fails to start
        // ("a different thread owns it"). FromThread only looks; it never creates a dispatcher.
        if (Dispatcher.FromThread(Thread.CurrentThread) is not { } uiThread || uiThread != Dispatcher.UIThread)
            return;

        // A layout pass still queued by the test attaches views that look the container up,
        // so run it before disposing.
        uiThread.RunJobs();

        // Disposing a provider twice is a no-op.
        services.Dispose();
    }
}
