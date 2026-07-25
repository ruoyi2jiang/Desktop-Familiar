# Addon SDK compatibility

`DesktopFamiliar.AddonSdk` is the versioned contract between the main application and an addon. The running host currently exposes API **1.3**. The SDK version is for developers; end users install addon packages, not SDK assemblies.

Every addon manifest declares the API range it supports. The host may load it only when:

```text
minimumHostApi <= hostApi < maximumHostApiExclusive
```

For example, this addon works with all 1.x hosts and refuses a future 2.0 host until its author has reviewed that major API change:

```json
{
  "manifestVersion": 1,
  "id": "desktopfamiliar.example",
  "displayName": "Example addon",
  "description": "A short description shown in the Addons marketplace.",
  "version": "1.4.0",
  "minimumHostApi": { "major": 1, "minor": 3 },
  "maximumHostApiExclusive": { "major": 2, "minor": 0 },
  "assemblyFileName": "ExampleAddon.dll",
  "entryPoint": "ExampleAddon.EntryPoint",
  "capabilities": ["SettingsPage"]
}
```

An addon may release a new package without releasing Desktop Familiar when it remains inside the installed host's API range. An addon that needs API 1.3 cannot run in an app exposing API 1.2; the app must be upgraded first. New optional SDK members require a minor API version. Breaking or removed behavior requires a new major API version.

Host API 1.1 exposes `IAddonViewportActionRegistry` through `IAddonHost.Services`. An addon may register commands for the companion viewport. The host renders every visible addon command below a single **Action** submenu and defers execution until the context menu closes; it omits the submenu when no command is visible. Addons must keep and dispose their registration when they unload.

Host API 1.2 adds four services:

- `IAddonSettingsSectionRegistry` registers an addon-owned `IAddonSettingsSection` on the host's Addons page.
- `IAddonSettingsStore` atomically reads and writes JSON scoped to the installed addon ID.
- `IAddonStringProvider` resolves user-facing strings through the host's localization system.
- `IAddonCompanionStatusPresenter` presents a short, non-animation companion status message.

An API-1.2 addon obtains only the services it declares it needs from `IAddonHost.Services`; it must fail startup cleanly if a required service is unavailable. The Auto Clicker is the reference implementation. API 1.2 does not make host WPF windows, application services, or the core persistence model public.

Host API 1.3 adds the optional `IInlineAddonSettingsSection` presentation contract. An addon that implements it returns its header and settings content as `AddonInlineSettingsContent`; the host places both inside that addon's installed-card surface, along with host-owned version and update controls. The addon still owns its controls and behavior, while the host owns card placement and installation actions. Addons that only implement `IAddonSettingsSection` remain supported as separate settings sections.

The manifest format is independently versioned and currently uses `manifestVersion: 1`. A host must reject unknown manifest versions rather than guessing how to load them. `assemblyFileName` is a file name (not a path) at the package root; `entryPoint` names the type to instantiate from that assembly.

First-party update distribution uses the explicitly configured `Addons/catalogue.json` file in the main repository. It lists the display name and description shown in Settings → Addons, plus package URL, version, API range, SHA-256 hash, and release-notes link. The application must not scrape GitHub release pages or automatically load arbitrary repository code. The first-party catalogue only accepts `https://github.com/ruoyi2jiang/Desktop-Familiar/releases/...` package and release-notes URLs. Before third-party packages are supported, package/catalogue verification and publisher trust need a separate approved design.
