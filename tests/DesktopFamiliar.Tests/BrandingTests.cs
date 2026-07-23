using System.Reflection;

namespace DesktopFamiliar.Tests;

public sealed class BrandingTests
{
    [Fact]
    public void ApplicationAssembly_UsesDesktopFamiliarBranding()
    {
        var assembly = typeof(DesktopFamiliar.App).Assembly;

        Assert.Equal("DesktopFamiliar", assembly.GetName().Name);
        Assert.Equal(
            "Desktop Familiar",
            assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
        Assert.Equal(
            "Desktop Familiar",
            assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
    }
}
