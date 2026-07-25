using System.Windows.Threading;
using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Services.Addons;

/// <summary>Adapts the application's localized strings to the stable addon SDK contract.</summary>
public sealed class DesktopFamiliarAddonStringProvider : IAddonStringProvider
{
    public string Get(string key) => LocalizationService.Get(key);

    public string Format(string key, params object[] arguments) => LocalizationService.Format(key, arguments);
}

/// <summary>Dispatches addon status messages to the companion overlay without exposing the overlay type.</summary>
public sealed class DispatcherAddonCompanionStatusPresenter : IAddonCompanionStatusPresenter
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _show;

    public DispatcherAddonCompanionStatusPresenter(Dispatcher dispatcher, Action<string> show)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _show = show ?? throw new ArgumentNullException(nameof(show));
    }

    public void Show(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            return;
        _dispatcher.BeginInvoke(() => _show(message));
    }
}
