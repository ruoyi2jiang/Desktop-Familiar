# Development and release workflow

## Build and test

From the repository root:

```powershell
dotnet restore .\DesktopFamiliar.slnx
dotnet build .\DesktopFamiliar.slnx -c Release --no-restore
dotnet test .\tests\DesktopFamiliar.Tests\DesktopFamiliar.Tests.csproj -c Release --no-build
```

Run without changing the version:

```powershell
dotnet run --project .\DesktopFamiliar.csproj
```

The xUnit test project targets `net8.0-windows`. Tests should avoid the user's real `%LOCALAPPDATA%`; persistence tests use disposable temporary directories.

## Change workflow

1. Read `AGENTS.md` and the relevant document under `docs/`.
2. Inspect the current working tree and preserve unrelated user changes.
3. Make the smallest cohesive change in the responsible service/control.
4. Add or update tests for behavior, migration, and regressions.
5. Add localization keys for visible text.
6. Update the user manual for user-visible behavior and architecture/data docs for structural changes.
7. Run Release build and tests.
8. Launch from source for manual WPF/tray verification when UI behavior changed.

## Build artifacts

Framework-dependent single-file publish:

```powershell
.\build-release.ps1
```

Output: `Artifacts\DesktopFamiliar-win-x64\DesktopFamiliar.exe`.

Self-contained publish:

```powershell
.\build-release.ps1 -SelfContained
```

Output: `Artifacts\DesktopFamiliar-standalone-win-x64\DesktopFamiliar.exe`.

Installer:

```powershell
.\build-installer.cmd
```

This requires Inno Setup 6 or 7. The version appears in `DesktopFamiliar.csproj`, `app.manifest`, `build-installer.ps1`, and `Installer/DesktopFamiliar.iss`; keep them aligned when intentionally preparing a release. Do not create an installer merely to verify a source refactor.

The canonical application-icon artwork is `Assets/AppIcon.png`. Regenerate the multi-resolution `Assets/AppIcon.ico` from that transparent source whenever the artwork changes; the WPF windows, executable, notification-area icon, and installer all reference the same `.ico` file.

The product name is **Desktop Familiar**, the executable is `DesktopFamiliar.exe`, and source namespaces use `DesktopFamiliar`.

## Public repository safety

The public repository must remain character-neutral. `.gitignore` excludes `CompanionPacks/`, private pack-builder scripts, generated `.companionpack` files, local application data, and `Artifacts/`. Before committing or publishing, verify ignored and staged files with:

```powershell
git status --short --ignored
git diff --cached --name-only
```

Do not force-add ignored character assets. The public application and installer must start with an empty companion library.

## Manual UI checklist

- App starts once, selects/shows the current companion, and exits from both menus.
- Notification-area and overlay context menus open at the same width without an unused right column and follow the active theme; their 4px row inset, text alignment, and font sizing match. Focus has no row-level hover fill. **Select companion** retains the ordinary hover colour while its child is open without switching to the darker pressed colour; the child stays open while the pointer crosses into it and closes after the pointer leaves it or enters another root row.
- Switching companions does not move card layout or animation registration.
- GIF, one PNG, and multi-PNG previews play with expected timing.
- **Set the frames to loop** and **Start dialog at** remain hidden for empty, GIF, and static-image registries and appear after PNG frames are imported. Switching between PNG and GIF/static sources resets all four persisted frame settings, and runtime ignores stale non-PNG frame settings.
- State-assignment helper text appears from the question-mark icons without a hover delay; the compact grid shows Idle above Start.
- Idle and reminder custom loops play their intro, selected range, and outro at the correct state boundary; birthday scenes return to idle, and an assigned exit scene completes before normal shutdown.
- Dialogue hide/show state and reminder indicator behave correctly, and leaving reminder state never exposes an empty bubble.
- Event, companion-editor, and settings text inputs use the same shared 38px field height and visually align their text with the shared dropdown and date-picker presenters; selected dropdown text and open dropdown items use the input-surface text token, and the calendar icon remains transparent when hovered.
- Outline buttons on primary and secondary surfaces use their containing surface's text-on-background token for the default text and border, then invert that surface's background/text roles on hover; the selected companion card uses the primary background.
- The viewport **Enter focus** command activates Focus; the viewport menu stays suppressed during Focus, the notification-area toggle exits it, and idle/reminder visibility follows the focus policy. Non-reminder scene dialogue and voice stay suppressed in Focus.
- A due reminder explicitly reveals a Focus-hidden companion, presents after assigned reminder-scene dialogue (or immediately over the idle fallback), and returns to hidden idle after snooze, acknowledgement, or expiration.
- Library, editor, calendar, and settings open without exceptions. Open Calendar and Configure separately from both the overlay and notification-area menus, then verify ordinary typing, Tab navigation, copy/paste, and Backspace work immediately in their text inputs.
- Theme edits apply immediately. Crimson Moon renders warm white/deep crimson surfaces with gold and charcoal accents; the Settings sidebar has visibly distinct default, hover, and selected states. Eternal Sea appears under its current name, and Stella Dark renders dark violet surfaces with readable lavender and warm-gold accents. On every dark surface, Settings page titles, calendar event guidance and units, section headings, field labels, selected dropdown values, dropdown items, data-grid text, helper text, and preview captions remain readable and use the text token belonging to that surface rather than the Windows default foreground.
- Calendar past dates are disabled; month/year selection cannot navigate earlier than the same month one year ago; one-time, multi-day, and weekly events render and remind.
- Year-old completed/expired one-time events are discarded; active weekly series remain while their year-old terminal occurrence history is pruned.
- The User settings page saves/clears a birthday, and matching companion/user birthday scenes participate in idle triggers without exceeding their configured probability.
- A completed occurrence has a checkmark, an expired occurrence is visibly muted, and opening either shows the matching status badge vertically aligned with the Event details title; weekly badges follow the clicked occurrence.
- Deleting one weekly occurrence removes only that date and reminder; deleting the series removes every occurrence.
- A forced recoverable save failure preserves the prior state; a fatal test failure produces a copyable latest-session log.

## Documentation ownership

- User-facing behavior: `docs/user-manual.md` and README capability summary.
- Service/folder/dependency changes: `docs/architecture.md`.
- Persisted fields or package changes: `docs/data-format.md`.
- Build, test, or release changes: this file.
- Agent workflow expectations: `AGENTS.md`.

Public source is provided under PolyForm Noncommercial 1.0.0. Companion packs and third-party character assets are outside that licence and must remain excluded unless the owner has documented redistribution rights.
