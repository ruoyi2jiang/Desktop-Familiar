using DesktopFamiliar.AddonSdk;

namespace DesktopFamiliar.Tests;

public sealed class AddonViewportActionRegistryTests
{
    [Fact]
    public void GetVisibleActions_OmitsHiddenActionsAndOrdersTheRest()
    {
        var registry = new AddonViewportActionRegistry();
        var executions = new List<string>();
        using var hidden = registry.Register(new AddonViewportAction(
            "example.hidden", "Hidden", () => executions.Add("hidden"), isVisible: () => false));
        using var second = registry.Register(new AddonViewportAction(
            "example.second", "Second", () => executions.Add("second"), order: 2));
        using var first = registry.Register(new AddonViewportAction(
            "example.first", "First", () => executions.Add("first"), order: 1));

        var visible = registry.GetVisibleActions();

        Assert.Equal(["example.first", "example.second"], visible.Select(action => action.Id));
        Assert.All(visible, action => action.Execute());
        Assert.Equal(["first", "second"], executions);
    }

    [Fact]
    public void RegistrationDisposal_RemovesTheAction()
    {
        var registry = new AddonViewportActionRegistry();
        var registration = registry.Register(new AddonViewportAction("example.action", "Action", () => { }));

        registration.Dispose();

        Assert.Empty(registry.GetVisibleActions());
    }

    [Fact]
    public void Register_RejectsADuplicateActionId()
    {
        var registry = new AddonViewportActionRegistry();
        using var first = registry.Register(new AddonViewportAction("example.action", "First", () => { }));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register(new AddonViewportAction("example.action", "Second", () => { })));

        Assert.Contains("already registered", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
