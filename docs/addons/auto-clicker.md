# Auto Clicker addon

The Auto Clicker is the first-party addon with ID `desktopfamiliar.auto-clicker`. It declares settings-page, viewport-menu, global-hotkey, and input-injection capabilities. Input injection and background messages remain opt-in and retain their existing safety warning.

Its source boundary is `Addons/AutoClicker/`:

- `Models/` defines addon identity and persisted `AutoClickerSettings`.
- `Services/` owns target/interval rules, global hotkeys, input injection, scoped settings, and runtime coordination.
- `Views/` owns the target picker and settings section contributed through the SDK.
- `addon.json` is the package manifest source of truth for the published addon.

The addon requires host API 1.4 or later within major version 1. It registers an inline settings section, a viewport action, and uses scoped settings, localized strings, and transient companion-status presentation supplied by the host. The host places the Auto Clicker toggle in the title row and keeps its version details, configuration controls, update action, and uninstall action in the same installed-addon card. The main application references only the SDK, so it does not load the addon's WPF views, hotkeys, or input engine until the package is installed and enabled.

Settings are stored at `%LOCALAPPDATA%\DesktopFamiliar\Addons\data\desktopfamiliar.auto-clicker\settings.json`. On first startup after upgrading from the former core feature, the host copies legacy Auto Clicker preferences there before removing those properties from the core document. A later addon install then uses the migrated settings. The selected target area and active click session remain runtime-only.

If Windows rejects either configured global hotkey during startup, the addon stays loaded so its settings remain repairable. It presents a companion warning and a persistent warning inside the settings card; choosing available hotkeys and saving clears the warning. A hotkey conflict must not throw from addon initialization, because unloading the addon would also remove the controls needed to correct it.

Saving interval, hotkey, or input-mode changes must not stop an active click session or target selection. The click service updates the active timer range immediately and switches an active session between cursor and background input while retaining its selected area. If Windows cannot resolve that area's background window, the current session keeps its existing mode and the saved mode applies when the user next starts clicking.

## Manual verification

- Install and restart the addon, then verify its hotkey and the viewport **Action > Click the screen for me** command both open the target picker over every monitor.
- Verify that Escape cancels selection, selecting a location starts clicks at the saved interval, and pressing the start/stop hotkey again stops.
- Validate millisecond and second interval inputs. While clicking, save a different interval and confirm clicking continues with the same selected area and immediately uses the new range.
- Switch between cursor and background modes from both Settings and the input-mode hotkey. Confirm clicking continues with the same selected area and immediately uses the new mode.
- Reserve one configured hotkey in another process before startup. Confirm the addon remains loaded, the companion reports the conflict, and its settings card shows a persistent warning until available shortcuts are saved.
- Disable the addon from its settings card and confirm its hotkeys no longer work and its viewport action disappears; re-enable it and confirm both return.

Use the shared [local development](overview.md#local-development) and [package and release](overview.md#package-and-release) workflows, follow the [SDK compatibility contract](sdk-compatibility.md), and do not put Auto Clicker fields back into `desktop-familiar.json`.
