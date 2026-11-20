# Desktop Familiar

Desktop Familiar is a Windows WPF desktop-companion application. It displays an animated 2D or 3D character in a transparent, always-on-top overlay and provides local animation, dialogue, theme, and calendar-reminder tools.

## Current capabilities

- Multiple locally configured companions with a shared profile across 2D and 3D, a live idle preview, and an optional portrait that defaults to a still of the active idle.
- Built-in offline text chat for Windows x64. Download Gemma 3 1B, choose an existing GGUF, or scan a folder for models. Toggle the prompt with a configurable shortcut and let the pet choose a configured expression as it replies. Each companion has private notes and history.
- Portable companion bundles that include configuration, animations, dialogue audio, fonts, and bubble artwork for one-step export and import.
- Optional offline push-to-talk using installed Windows recognition or downloadable multilingual Whisper. Each pet can speak with a tuned Windows voice or a shared Kokoro voice bank.
- GIF, static image, or ordered PNG-frame animations with per-frame timing. A companion can also carry a self-contained 3D GLB model. Play a full 3D activity with a marked loop range, timed repeats, or a loop that lasts through dialogue. Assign it to states or interactions. Switch appearances in Configure.
- Start, looping idle, optional reminder, birthday, exit, and random-idle scenes.
- Focus mode that suppresses the companion while idle, reveals it for reminders, and suppresses non-reminder scene dialogue and voice.
- Whole-animation, selected-range, or single-frame loops for any looping scene.
- Timed dialogue with a configurable PNG start frame or 3D start time, `{{user}}` nickname replacement, and a hideable bubble. Each line plays its attached recording or uses the pet’s profile voice to read its text aloud.
- Independently sized character viewport (120–1200px) with a centered, capped dialogue bubble, Windows startup option, and editable semantic colour themes.
- An optional installable Auto Clicker addon with separate global start/stop and input-mode hotkeys, fresh area selection, and randomized millisecond or second interval ranges.
- A local month calendar with month/year navigation, one-time, continuous multi-day, and weekly events; occurrence/series deletion, categories, urgency, reminders, snooze, and stop actions.
- A latest-session log and copyable crash report for fatal errors.
- Check for stable app updates in General settings, download a verified release, and restart to apply it. Installed addons have their own update controls.

See the [user manual](docs/user-manual.md) for usage and the [project guide](docs/architecture.md) for the codebase.
See [addons](ADDONS.md) for the versioned first-party addon boundary and SDK compatibility policy.

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

- Configuration: `%LOCALAPPDATA%\DesktopFamiliar\Library\desktop-familiar.v16.json`. Opening an existing library reads its settings into memory. A saved change writes the copy for that schema.
- Imported assets: `%LOCALAPPDATA%\DesktopFamiliar\Assets\<companion-id>`
- Latest session log: `%LOCALAPPDATA%\DesktopFamiliar\Logs\latest.log`
- Wording overrides: `%LOCALAPPDATA%\DesktopFamiliar\Localization\custom.en.json`

## Distribution status

This repository and its public builds contain no companion or character pack. Fresh installations start with an empty library so users can create or import content they have permission to use. See [the companion and asset policy](ASSETS.md).

## Licence

Desktop Familiar is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE.md). Personal, hobby, research, educational, charitable, and other qualifying non-commercial uses are permitted; commercial use requires separate permission from the licensor.

Because it restricts commercial use, this is not an OSI-approved open-source licence. Third-party content loaded through the companion importer is not licensed by this project.
