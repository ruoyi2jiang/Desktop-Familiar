# Auto Clicker addon workflow

This folder owns the Auto Clicker addon. Root `AGENTS.md` rules still apply.

## Before changing this addon

1. Read `docs/addons/overview.md` and `docs/addons/auto-clicker.md`.
2. If the public contract or package manifest changes, read `docs/addons/sdk-compatibility.md` and update its examples.
3. Keep serializable addon models in `Models/`, pure rules in `Services/`, and WPF-only controls/windows in `Views/`.

## Compatibility and safety

- Do not raise `MinimumHostApi` or change the manifest format without a compatibility test and documentation update.
- Do not use an API absent from the declared host API range.
- The addon must declare sensitive capabilities such as global hotkeys and input injection in `addon.json`.
- Addon settings, migrations, and package installation are owned by the addon boundary; do not add addon-only fields to the core companion JSON contract.
- The Auto Clicker settings must implement `IInlineAddonSettingsSection` and render inside its installed-addon card. Its enable toggle belongs in the host's title-action slot. Do not restore a separate settings card or a duplicate enable/disable button; the settings toggle is the single feature control, while the host owns installed/latest-version, recovery-enable, update, and uninstall actions.
- A startup hotkey conflict must not throw from addon initialization. Keep the addon loaded, expose a persistent repair warning in its settings, and let an available saved binding clear that warning.
- The first-party Auto Clicker is trusted application code. Do not add external source, downloading, or automatic-update behavior here without an approved package-verification design.

## Required verification

Run the root Release build and test commands. Add tests for manifest compatibility, pure rules, and persistence changes.
For manual testing against the current source, exit the app and use the shared `Addons/deploy-local.ps1 -AddonDirectory Addons/AutoClicker` workflow; `dotnet run` alone may load an older package from the local addon registry.
