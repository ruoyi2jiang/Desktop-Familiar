# Data and package format

## User data

The root JSON document is `DesktopCompanionData`, stored at `%LOCALAPPDATA%\DesktopFamiliar\desktop-familiar.json`, and currently uses schema version 10. It contains:

- `Settings`: selected companion, viewport, overlay position, dialogue behavior, random-idle interval, startup mirror, focus mode, user birthday, and saved themes.
- `Companions`: locally configured `CompanionDefinition` objects.
- `CalendarEvents`: local event definitions and reminder state.

`CompanionDataNormalizer` repairs invalid current-format values after loading. Schema 3 adds empty grouped interaction-state assignments to every companion, preserving the existing idle-only behavior. Schema 8 retires the former core Auto Clicker fields. Before normalization writes a pre-schema-8 document, startup copies any legacy click preferences to the Auto Clicker addon's scoped settings file if it does not already exist. This preserves the settings even when the addon is installed later. Future schema changes must provide a tested migration before the application accepts the old format.

Theme selection stores both `SelectedThemeId` and its `Theme` kind; the saved-theme ID is authoritative, and normalization repairs the kind from a selected built-in or custom theme before writing the repair back.

## Local chat

Schema 10 adds `Settings.Chat` with `Enabled`, nullable `ModelId` (catalogue ID or linked file ID), and `Shortcut`. Schema 9 migrates with chat disabled, no selected model, and Ctrl+Shift+F9. The companion's optional `ThinkingPhrase` defaults to empty, which uses the localized thinking text. The phrase travels in companion bundles.

Downloaded model weights live in `Chat/Models/<catalogue-id>/model.gguf` under the application data root. New setups share the CPU engine in `Chat/Engines/b12496/`. Existing installations with `Chat/Models/<catalogue-id>/engine/` still work and can run linked files. The optional `Chat/local-models.json` document has `Version: 1` and a `Files` object mapping each `local:<lowercase SHA-256>` ID to its absolute GGUF path. Linking saves the reference atomically and leaves the model file at the chosen location. Re-selecting the same content at a new path updates that entry. An absent index means there are no linked files.

Private notes and conversations are stored separately in `Chat/history.json` (version 1). Its `Pets` dictionary is keyed by companion GUID. Each entry stores the last known name, ordered question/reply turns with timestamps, and explicit notes. Clearing multiple selected histories updates the file atomically. Keeping notes leaves a notes-only entry available for later removal. Every new import receives a fresh local GUID, including after deleting and reimporting the same bundle. Overwriting an existing companion preserves its local conversation.

## Addon data and manifests

Addon installation state and addon-owned settings are separate from `DesktopCompanionData`. The host registry is `%LOCALAPPDATA%\DesktopFamiliar\Addons\installed-addons.json` (`schemaVersion: 1`), packages are installed at `Addons\packages\<addon-id>\<version>`, and addon-owned settings are stored at `Addons\data\<addon-id>\settings.json`. A successful package update preserves the installation's enabled flag and removes the superseded version directory; locked package or saved-data deletion is moved to an internal pending-removal directory and retried before addons load on the next startup. The Auto Clicker settings document currently has `schemaVersion: 1` and stores its enable flag, interval range, distinct global hotkeys, and background-mode preference. The selected target area, target window handle, active state, and transient dialogue are runtime-only. The first-party catalogue lists an addon ID, display name, short description, version, compatibility range, package URL, SHA-256 checksum, and release-notes URL for the Available-tab detail pane. The core addon manifest uses `manifestVersion: 1` and is parsed with the canonical `AddonManifestJson` options, including string capability names. A package has `addon.json` plus its named root assembly; extraction rejects traversal paths, a bad checksum, or a manifest that differs from its trusted catalogue record. The full compatibility range and manifest example are documented in `docs/addons/sdk-compatibility.md`.

## Companion and scenes

Schema 9 adds two optional text fields, `Description` and `Personality`, directly to each companion. A schema-8 library migrates with both fields empty. Version-1 companion bundles carry the fields in their companion object. Earlier bundles that omit the fields import with blank text. Speaking examples come from existing scene dialogue and have no separate serialized field.

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
