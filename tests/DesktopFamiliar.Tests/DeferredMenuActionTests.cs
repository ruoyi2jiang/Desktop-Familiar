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

    [Fact]
    public void DispatchedAction_DoesNotRunAfterANewerMenuInteraction()
    {
        var deferred = new DeferredMenuAction();
        var executionCount = 0;
        Action? scheduled = null;
        deferred.Queue(() => executionCount++);

        deferred.DispatchPending(action => scheduled = action);
        deferred.InvalidateDispatchedAction();

        scheduled!();

        Assert.Equal(0, executionCount);
    }

    [Theory]
    [InlineData("Views/CompanionOverlayWindow.xaml.cs", "menu.Closed += OverlayContextMenu_Closed;", "_menuAction.Queue(App.CurrentApp.ShowCalendarEvents)")]
    [InlineData("Views/CompanionOverlayWindow.xaml.cs", "menu.Closed += OverlayContextMenu_Closed;", "_menuAction.Queue(App.CurrentApp.ShowSettings)")]
    [InlineData("Views/CompanionOverlayWindow.xaml.cs", "menu.Closed += OverlayContextMenu_Closed;", "_menuAction.Queue(App.CurrentApp.ToggleAutoClick)")]
    public void WindowOpeningMenuRoutes_WaitForTheirClosedBoundary(
        string relativePath,
        string closedSubscription,
        string queuedWindowAction)
    {
        var source = File.ReadAllText(FindRepositoryFile(relativePath));

        Assert.Contains(closedSubscription, source, StringComparison.Ordinal);
        Assert.Contains(queuedWindowAction, source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Normal", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Views/CompanionOverlayWindow.xaml.cs")]
    public void MenuOpening_InvalidatesOlderDispatchedActions(string relativePath)
    {
        var source = File.ReadAllText(FindRepositoryFile(relativePath));

        Assert.Contains("_menuAction.InvalidateDispatchedAction();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayMenu_IsBuiltBeforeTheNativeOpeningEvent()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "TrayService.cs")));
        var constructorBuild = source.IndexOf("BuildTrayMenu();", StringComparison.Ordinal);
        var stateChangeHandler = source.IndexOf("private void ManagerTrayContentChanged", StringComparison.Ordinal);

        Assert.True(constructorBuild >= 0);
        Assert.True(stateChangeHandler > constructorBuild);
        Assert.DoesNotContain("_trayMenu.Opening +=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayMenuCommands_ExecuteFromTheirNativeItemCallbacks()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "TrayService.cs")));

        Assert.Contains("settings.Click += (_, _) => _showSettings();", source, StringComparison.Ordinal);
        Assert.Contains("setEvent.Click += (_, _) => _showCalendarEvents();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_menuAction.Queue(_showSettings)", source, StringComparison.Ordinal);
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
