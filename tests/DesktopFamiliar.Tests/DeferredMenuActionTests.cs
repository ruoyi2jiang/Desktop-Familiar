using DesktopFamiliar.Views.Controls;

namespace DesktopFamiliar.Tests;

public sealed class DeferredMenuActionTests
{
    [Fact]
    public void Queue_DoesNotRunActionBeforeMenuCloses()
    {
        var deferred = new DeferredMenuAction();
        var executionCount = 0;

        deferred.Queue(() => executionCount++);

        Assert.True(deferred.HasPendingAction);
        Assert.Equal(0, executionCount);
    }

    [Fact]
    public void DispatchPending_SchedulesActionOnceAndClearsPendingState()
    {
        var deferred = new DeferredMenuAction();
        var executionCount = 0;
        Action? scheduled = null;
        deferred.Queue(() => executionCount++);

        deferred.DispatchPending(action => scheduled = action);

        Assert.False(deferred.HasPendingAction);
        Assert.NotNull(scheduled);
        Assert.Equal(0, executionCount);

        scheduled();
        deferred.DispatchPending(_ => throw new InvalidOperationException("The action was dispatched twice."));

        Assert.Equal(1, executionCount);
    }

    [Fact]
    public void DispatchPending_ClearsStateBeforeInvokingDispatcher()
    {
        var deferred = new DeferredMenuAction();
        deferred.Queue(() => { });

        deferred.DispatchPending(_ => Assert.False(deferred.HasPendingAction));
    }

    [Theory]
    [InlineData("Views/CompanionOverlayWindow.xaml.cs", "menu.Closed += OverlayContextMenu_Closed;", "_menuAction.Queue(App.CurrentApp.ShowCalendarEvents)")]
    [InlineData("Services/TrayService.cs", "_trayMenu.Closed += TrayMenu_Closed;", "_menuAction.Queue(_showCalendarEvents)")]
    public void WindowOpeningMenuRoutes_WaitForTheirClosedBoundary(
        string relativePath,
        string closedSubscription,
        string queuedWindowAction)
    {
        var source = File.ReadAllText(FindRepositoryFile(relativePath));

        Assert.Contains(closedSubscription, source, StringComparison.Ordinal);
        Assert.Contains(queuedWindowAction, source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ContextIdle", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
