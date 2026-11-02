using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;

namespace AgentTrace.Desktop.Tests;

/// <summary>Headless Avalonia application for the UI tests: real Skia so that frames can be captured.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Shared headless session (one per process) that runs each test on Avalonia's UI thread. Alternative to
/// <c>[AvaloniaFact]</c>: that one is incompatible with xunit.v3 4.0.2 (it uses internals of 3.2.2). The session is deliberately
/// not disposed: <c>Dispose()</c> leaves the test process hanging.
/// </summary>
public static class HeadlessHost
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder)));

    public static Task Run(Action action) =>
        Session.Value.Dispatch(action, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
}
