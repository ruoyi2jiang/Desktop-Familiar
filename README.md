# Desktop Familiar

Desktop Familiar is a Windows WPF desktop-companion application. It displays an animated 2D character in a transparent, always-on-top overlay and provides local animation, dialogue, theme, and calendar-reminder tools.

## Current capabilities

- Multiple locally configured companions with preview cards and quick switching.
- Portable companion bundles that include configuration, animations, dialogue audio, fonts, and bubble artwork for one-step export and import.
- GIF, static image, or ordered PNG-frame animations with per-frame timing.
- Start, looping idle, optional reminder, birthday, exit, and random-idle scenes.
- Focus mode that suppresses the companion while idle, reveals it for reminders, and suppresses non-reminder scene dialogue and voice.
- Whole-animation, selected-range, or single-frame loops for any looping scene.
- Timed dialogue with a configurable start frame, `{{user}}` nickname replacement, optional voice lines, and a hideable dialogue bubble.
- Configurable viewport, Windows startup option, and editable semantic colour themes.
- A configurable global auto-click hotkey with fresh target selection and millisecond or second intervals.
- A local month calendar with month/year navigation, one-time, continuous multi-day, and weekly events; occurrence/series deletion, categories, urgency, reminders, snooze, and stop actions.
- A latest-session log and copyable crash report for fatal errors.

See the [user manual](docs/user-manual.md) for usage and the [project guide](docs/architecture.md) for the codebase.

## Develop

Requirements:

- Windows 10 or 11
- .NET 8 SDK with the Windows Desktop workload/runtime

```powershell
dotnet run --project .\DesktopFamiliar.csproj
dotnet build .\DesktopFamiliar.slnx -c Release
dotnet test .\tests\DesktopFamiliar.Tests\DesktopFamiliar.Tests.csproj -c Release
```

Build a framework-dependent single executable:

```powershell
.\build-release.ps1
```

Add `-SelfContained` for a larger build that includes the runtime. See [development and release](docs/development.md) for installer commands, generated paths, and the verification checklist.

## Local data

Desktop Familiar keeps program files separate from user data:

- Configuration: `%LOCALAPPDATA%\DesktopFamiliar\desktop-familiar.json`
- Imported assets: `%LOCALAPPDATA%\DesktopFamiliar\Assets\<companion-id>`
- Latest session log: `%LOCALAPPDATA%\DesktopFamiliar\Logs\latest.log`
- Wording overrides: `%LOCALAPPDATA%\DesktopFamiliar\Localization\custom.en.json`

## Distribution status

This repository and its public builds contain no companion or character pack. Fresh installations start with an empty library so users can create or import content they have permission to use. See [the companion and asset policy](ASSETS.md).

## Licence

Desktop Familiar is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE.md). Personal, hobby, research, educational, charitable, and other qualifying non-commercial uses are permitted; commercial use requires separate permission from the licensor.

Because it restricts commercial use, this is not an OSI-approved open-source licence. Third-party content loaded through the companion importer is not licensed by this project.
