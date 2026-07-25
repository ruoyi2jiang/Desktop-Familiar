# Auto Clicker addon

The Auto Clicker is the first-party addon with ID `desktopfamiliar.auto-clicker`. It declares settings-page, viewport-menu, global-hotkey, and input-injection capabilities. Input injection and background messages remain opt-in and retain their existing safety warning.

Its source boundary is `Addons/AutoClicker/`:

- `Models/` defines addon identity and persisted `AutoClickerSettings`.
- `Services/` owns target/interval rules, global hotkeys, input injection, scoped settings, and runtime coordination.
- `Views/` owns the target picker and settings section contributed through the SDK.
- `addon.json` is the package manifest source of truth for the published addon.
- `build-package.ps1` produces the release ZIP and prints its SHA-256 without publishing it.

The addon requires host API 1.2 or later within major version 1. It registers a settings section, a viewport action, and uses scoped settings, localized strings, and transient companion-status presentation supplied by the host. The main application references only the SDK, so it does not load the addon's WPF views, hotkeys, or input engine until the package is installed and enabled.

Settings are stored at `%LOCALAPPDATA%\DesktopFamiliar\Addons\data\desktopfamiliar.auto-clicker\settings.json`. On first startup after upgrading from the former core feature, the host copies legacy Auto Clicker preferences there before removing those properties from the core document. A later addon install then uses the migrated settings. The selected target area and active click session remain runtime-only.

Build, publish, and catalogue the package according to [the addon overview](overview.md) and [SDK compatibility contract](sdk-compatibility.md). Do not put new Auto Clicker fields back into `desktop-familiar.json`.
