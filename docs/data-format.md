# Data and package format

## User data

The root JSON document is `DesktopCompanionData`, stored at `%LOCALAPPDATA%\DesktopFamiliar\desktop-familiar.json`, and currently uses schema version 4. It contains:

- `Settings`: selected companion, viewport, overlay position, dialogue behavior, random-idle interval, startup mirror, focus mode, user birthday, saved themes, and auto-click preferences.
- `Companions`: locally configured `CompanionDefinition` objects.
- `CalendarEvents`: local event definitions and reminder state.

`CompanionDataNormalizer` repairs invalid current-format values after loading. Schema 2 migrates schema-1 documents by adding the default auto-click interval (`1000` milliseconds) and hotkey (`Ctrl+Alt+C`). Schema 3 adds empty grouped interaction-state assignments to every companion, preserving the existing idle-only behavior. Schema 4 adds `AutoClickEnabled`, defaulting existing profiles to enabled. Schema 5 adds `AutoClickBackgroundModeEnabled`, defaulting existing profiles to cursor-based clicks. Schema 6 adds `AutoClickIntervalMaximumMilliseconds`, initialized from the existing interval so previous profiles retain a fixed delay. Schema 7 adds `AutoClickBackgroundModeHotkey` (`Ctrl+Alt+B`), separate from the start/stop hotkey. Future schema changes must provide a tested migration before the application accepts the old format.

The auto-click preferences are `AutoClickIntervalMilliseconds` and `AutoClickIntervalMaximumMilliseconds` (inclusive lower and upper bounds, each from 10 to 60,000), `AutoClickHotkey` (the normalized start/stop combination), `AutoClickBackgroundModeHotkey` (the distinct normalized input-mode combination), `AutoClickEnabled`, and the default-off `AutoClickBackgroundModeEnabled`. The selected target rectangle, target window handle, active state, and transient status dialogue are intentionally runtime-only. Theme selection stores both `SelectedThemeId` and its `Theme` kind; the saved-theme ID is authoritative, and normalization repairs the kind from a selected built-in or custom theme before writing the repair back.

## Companion and scenes

Each companion has a stable GUID, appearance settings, animation registries, and scene assignments for required idle plus optional start, reminder, exit, companion-birthday 1/2, user-birthday 1/2, and random-idle scenes. `InteractionStates` groups optional drag-left, drag-right, and drag-hold scene IDs plus ordered `ClickAreas`. Each click area stores a name, an optional scene ID, and percentage-based left, top, width, and height bounds. Scene sources are either `AnimationPath` for a GIF/static image or ordered `PngFrames` with per-frame `DurationSeconds`.

PNG sequences use `FrameLoopMode` (`EntireAnimation`, `SelectedRange`, or `SingleFrame`), `LoopStartFrameIndex`, `LoopEndFrameIndex`, and `DialogStartFrameIndex`. Indices are zero-based in JSON. For GIF, static-image, and missing sources, normalization resets the individual-frame settings and runtime ignores stale values defensively.

Dialogue lines store text and an optional copied voice path. `{{user}}` is replaced at presentation time with the companion's configured user nickname.

## Calendar reminder state

One-time events use `IsCompleted` and `IsExpired`. Weekly events store terminal occurrence timestamps in `CompletedOccurrences` and `ExpiredOccurrences`; `ExcludedOccurrenceDates` suppresses selected dates without deleting the series. `SnoozedUntil` stores the next attempt after a user snoozes a reminder.

Terminal one-time events are removed when their end date is at least one year old. Active weekly series remain, while completed and expired occurrence history at least one year old is pruned.

## Imported assets

`CompanionAssetStore` copies imports to:

```text
%LOCALAPPDATA%\DesktopFamiliar\Assets\<companion-guid>\<category>\<file>
```

Name collisions receive a GUID suffix. Stored configuration paths are absolute local paths. Export converts them to portable bundle paths, and import converts them back to the receiving computer's local asset paths.

## Portable companion bundles

Portable companions use the `.companionpack` extension. The file is a ZIP archive containing `manifest.json` and referenced assets beneath `assets/`:

```json
{
  "format": "DesktopCompanion.Bundle",
  "formatVersion": 1,
  "bundleId": "companion.stable-id",
  "bundleVersion": 1,
  "companion": {
    "Id": "stable-companion-guid",
    "Name": "Example",
    "Animations": [
      {
        "PngFrames": [
          { "FilePath": "assets/frames/0001-idle.png", "DurationSeconds": 0.1 }
        ]
      }
    ]
  }
}
```

Every non-empty font, bubble, animation, PNG-frame, and voice path must be relative and begin with `assets/`. Import rejects rooted paths, traversal, duplicate case-insensitive entries, more than 5,000 entries, individual assets over 512 MiB, and referenced assets totaling over 1 GiB. Only referenced entries are extracted. Extraction uses a unique staging directory before moving the completed set beneath `%LOCALAPPDATA%\DesktopFamiliar\Assets\<companion-id>\Bundles`.

On import, **Overwrite existing** preserves the existing companion GUID, while **Import as new** assigns a new GUID and unique display name. Scene GUIDs remain scoped to the imported companion.
