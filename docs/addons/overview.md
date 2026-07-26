# Addons

Desktop Familiar addons are optional feature modules. The core application owns first-party discovery, verified installation state, capability consent, host-owned placement for settings and menus, and compatibility checks. An addon owns its own models, migrations, scoped persistence, runtime services, views, and user documentation. Addons use the SDK's localized-string and companion-status services rather than referencing application implementation types.

The first-party Auto Clicker is fully extracted behind this boundary. It is not built into the main application and has no direct project reference from it. Its installed card combines host-owned version/update actions with addon-owned configuration content through the versioned inline-settings contract. An inline addon may also supply a title-row action, such as its single enable toggle. External repositories and third-party package installation are deliberately not supported yet.

The application installer is intentionally **core-only**: it does not include an addon package. Settings → Addons lists compatible first-party packages in an **Available** tab and installed packages in an **Installed** tab. When the catalogue is empty, opening this page checks the pinned first-party catalogue once; the **Check for addons** button always performs an explicit refresh. Only a catalogue version newer than the installed version is offered as an update. A package update preserves the installation's enabled state, retires the previous package directory, and takes effect after a normal app restart. The replacement process waits for the old process to exit so the single-instance guard cannot consume the restart.

An addon has three lifecycle states:

- **Not installed**: no active addon package exists in the addon directory.
- **Installed and disabled**: its files and saved data remain, but it contributes no hotkeys, services, or menus. The installed card provides a one-way **Enable** recovery action because an unloaded addon cannot contribute its own settings toggle.
- **Enabled**: the host has loaded a compatible addon and activated its declared contributions.

The host keeps its installation registry at `%LOCALAPPDATA%\DesktopFamiliar\Addons\installed-addons.json`, packages under `Addons\packages\<addon-id>\<version>`, and addon-specific saved data under `Addons\data\<addon-id>`. An addon must not extend `desktop-familiar.json` solely for addon behavior.

For the initial channel, the only catalogue URL is `https://raw.githubusercontent.com/ruoyi2jiang/Desktop-Familiar/main/Addons/catalogue.json`. Every entry must point to a GitHub Release URL in this same repository and include a SHA-256 checksum. The host verifies the manifest, declared API range, trusted URL, and checksum before making a package visible in its installation registry. It does not scrape GitHub releases or accept arbitrary repositories.

Each first-party addon owns and documents its feature-specific manual verification. Local source deployment, package construction, and publication use the shared processes below.

## Local development

`dotnet run` loads the addon versions recorded in `%LOCALAPPDATA%\DesktopFamiliar\Addons\installed-addons.json`; it does not load addon project outputs directly. Consequently, building the solution alone can still run an older installed addon. The Settings addon manager also cannot install an unpublished local ZIP because it intentionally accepts only checksum-verified packages from the pinned first-party catalogue.

From the repository root, first exit every running Desktop Familiar instance, then deploy any first-party addon source directory into the local package store:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Addons\deploy-local.ps1 -AddonDirectory .\Addons\<AddonDirectory>
dotnet run --project .\DesktopFamiliar.csproj
```

The shared script builds the Debug solution by default, reads the addon's project and manifest, copies its assembly, dependency metadata, runtime dependencies, and output assets, then atomically updates the local installation registry. It preserves that addon's enabled state and saved data and removes its superseded package directory. It refuses to deploy into the real application-data directory while Desktop Familiar is open. Use `-Configuration Release` to exercise Release binaries:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Addons\deploy-local.ps1 -AddonDirectory .\Addons\<AddonDirectory> -Configuration Release
dotnet run --project .\DesktopFamiliar.csproj -c Release
```

`-ExecutionPolicy Bypass` applies only to this PowerShell process and does not change the user's system policy. `-ApplicationDataRoot <path>` targets an isolated application-data directory, and `-SkipBuild` reuses an existing output for script testing. This is a local developer deployment only: it creates no release ZIP, changes no catalogue entry, and uploads nothing.

## Package and release

Creating or publishing an addon package requires separate owner approval; a source refactor alone does not authorize it. Every first-party addon directory must contain one project file and an `addon.json` manifest. Complete the repository's normal restore, Release build, and tests first, then build a reviewed package from the repository root with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Addons\build-package.ps1 -AddonDirectory .\Addons\<AddonDirectory> -OutputDirectory .\Artifacts\Addons
```

The shared script builds the addon in Release configuration without restoring packages, verifies that the project output matches the assembly declared by its manifest, and creates `<addon-id>-<version>.zip`. The archive contains `addon.json`, the addon assembly, its dependency manifest, copied runtime dependencies and output assets; shared SDK binaries and debug symbols are excluded. The script refuses to overwrite an existing artifact, prints the package SHA-256, and publishes nothing.

After review, upload the ZIP to an addon-specific GitHub Release. In a separate release commit, add its version, package URL, release-notes URL, declared API range, and SHA-256 to `Addons/catalogue.json`. The package can be released without a core application release when its declared range remains compatible with the published host API. Update the core application only when the addon needs a newer host API or another core change.

## Manual host verification

- In **Addons**, verify that the **Available** and **Installed** tabs show the correct lists, an available selection shows its details and install control, and **No available addons** appears when applicable.
- Opening Addons refreshes an empty catalogue once; the button always refreshes on explicit user action.
- Verify that the **Action** viewport submenu is absent when no addon action is visible and that every visible addon command is grouped beneath it.
- A disabled installation must show **Enable** even though its addon code is not loaded.
- An older catalogue release must never be offered over a newer installed version. Updating must remove the superseded package directory while preserving the enabled state.
- Installation, update, recovery enable, and uninstall status must state that a restart is required. Choosing restart must produce one replacement instance after the old process exits.
- Test only against the pinned first-party catalogue; do not substitute a third-party URL.

See [SDK compatibility](sdk-compatibility.md) for the host API contract and the individual addon document for feature-specific verification.
