# Addons

Desktop Familiar addons are optional feature modules. The core application owns first-party discovery, verified installation state, capability consent, host-owned placement for settings and menus, and compatibility checks. An addon owns its own models, migrations, scoped persistence, runtime services, views, and user documentation. Addons use the SDK's localized-string and companion-status services rather than referencing application implementation types.

The first-party Auto Clicker is fully extracted behind this boundary. It is not built into the main application and has no direct project reference from it. Its installed card combines host-owned version/update actions with addon-owned configuration content through the versioned inline-settings contract. External repositories and third-party package installation are deliberately not supported yet.

The application installer is intentionally **core-only**: it does not include an addon package. Settings → Addons lists compatible first-party packages in an **Available** tab and installed packages in an **Installed** tab. When the catalogue is empty, opening this page checks the pinned first-party catalogue once; the **Check for addons** button always performs an explicit refresh. A package update takes effect after a normal app restart, so the running process never replaces a loaded addon assembly.

An addon has three lifecycle states:

- **Not installed**: no active addon package exists in the addon directory.
- **Installed and disabled**: its files and saved data remain, but it contributes no hotkeys, services, or menus.
- **Enabled**: the host has loaded a compatible addon and activated its declared contributions.

The host keeps its installation registry at `%LOCALAPPDATA%\DesktopFamiliar\Addons\installed-addons.json`, packages under `Addons\packages\<addon-id>\<version>`, and addon-specific saved data under `Addons\data\<addon-id>`. An addon must not extend `desktop-familiar.json` solely for addon behavior.

For the initial channel, the only catalogue URL is `https://raw.githubusercontent.com/ruoyi2jiang/Desktop-Familiar/main/Addons/catalogue.json`. Every entry must point to a GitHub Release URL in this same repository and include a SHA-256 checksum. The host verifies the manifest, declared API range, trusted URL, and checksum before making a package visible in its installation registry. It does not scrape GitHub releases or accept arbitrary repositories.

Build a first-party package explicitly with:

```powershell
.\Addons\AutoClicker\build-package.ps1 -OutputDirectory .\Artifacts\Addons
```

The script prints the package SHA-256. A separately approved release commit adds that package's version, release URL, release-notes URL, compatibility range, and checksum to `Addons/catalogue.json`; the core application does not need a new release when the addon's declared API range is compatible.

See [SDK compatibility](sdk-compatibility.md) for the host API contract and [Auto Clicker](auto-clicker.md) for the first-party addon boundary.
