# Data and package format

## User data

The root JSON document is `DesktopCompanionData`. Schema 16 saves to `%LOCALAPPDATA%\DesktopFamiliar\Library\desktop-familiar.v16.json`. It contains:

- `Settings`: selected companion, viewport, overlay position, dialogue bubble visibility, random-idle interval, startup mirror, focus mode, user birthday, and saved themes.
- `Companions`: locally configured `CompanionDefinition` objects.
- `CalendarEvents`: local event definitions and reminder state.

The `RunAtStartup` field mirrors the per-user Windows sign-in registration when the library opens. Setup and the portable app enable it by default only if both the `Library` directory and the legacy `desktop-familiar.json` are absent. The first use creates the directory, so an empty library also preserves an opt-out. Opening the app leaves the configuration JSON untouched.

Opening the library reads this build’s own schema file first. If it is absent, the app reads the highest numbered library in the same folder, then falls back to the original `%LOCALAPPDATA%\DesktopFamiliar\desktop-familiar.json` if there are no versioned libraries. Migration and normalization run in memory. A user save writes only the current schema’s file. A newer source can supply recognized fields and compatible list items. Unknown values stay in the source. Separate schema copies evolve independently. A newer document placed at the current write path is protected against replacement.

Schema 3 adds empty grouped interaction-state assignments to every companion. Schema 8 retires the former core Auto Clicker fields. Startup can copy legacy click preferences from the original shared file to the addon's scoped settings file. Future schema changes require a tested migration.

Theme selection stores both `SelectedThemeId` and its `Theme` kind. The saved-theme ID is authoritative. A correction to the theme kind is included in the next user save.

Existing libraries can contain the retired `Settings.TruncateLongDialogLines` flag. The reader ignores it and a subsequent save omits it. Opening the library alone preserves the source file.

## Local chat

The embedded `Resources/chat-models.json` catalogue holds each supported GGUF’s identity, digest, and `requirements`. The latter gives `suggestedFreeRamMb`, `suggestedThreads`, and `setupSpaceMb`. Sizes use decimal megabytes. A matching linked model uses the same entry by digest. Hardware readings and test results are transient.

Schema 16 adds voice preferences to `Settings.Chat`: `VoiceEnabled` defaults to true, `InputLanguage` to the system language, `MicrophoneId` to the default input, `TalkShortcut` to Ctrl+Shift+F10, and `SendOnRelease` and `PreferWhisper` to false. When the two shortcuts coincide, normalization preserves the text-chat shortcut and assigns a distinct default to push-to-talk. Disabling Chat takes precedence over the voice preference.

Each companion has one `Voice` object containing `Id`, `ReplyLanguage`, `Speaker`, `Rate`, `Pitch`, and `Volume`. An empty ID selects the Windows default. The ID `off` silences synthesized chat replies and unrecorded scene dialogue. A scene line’s own `VoicePath` takes precedence over the pet voice setting. A Windows token, catalogue ID or content-derived imported bank ID selects a specific voice. Gain defaults to 90 percent and pitch and speed default to neutral. Schema 15 loads these defaults in memory. Its matching calendar observations and completed history-wipe receipts carry forward on migration. Opening a pet leaves the source configuration untouched.

The embedded `Resources/voice-banks.json` lists Windows voice sources, input language choices, and optional speech downloads with their sizes and SHA-256 digests. Verified packages and imported banks live under `%LOCALAPPDATA%\DesktopFamiliar\Voice`. Format 6 companion bundles carry custom per-pet voice choices and tuning. Exports using defaults keep the applicable earlier bundle version. Shared engines, voice banks, microphone IDs and private recordings are excluded from exports.

Schema 15 adds a `ChatExpressions` object independently to the companion’s sprite settings and its `Model3D` settings. The supported keys are `happy`, `sad`, `surprised`, `confused`, `angry`, and `thoughtful`. A scene GUID assigns a reaction within that appearance, a JSON null disables it, and an omitted key enables automatic name matching. Schema 14 and older packs default to automatic matching in memory. A user save writes the new library file. The per-pet history format stays at version 2.

Schema 10 adds `Settings.Chat` with `Enabled`, nullable `ModelId` (catalogue ID or linked file ID), and `Shortcut`. Schema 9 migrates with chat disabled, no selected model, and Ctrl+Shift+F9. The companion's optional `ThinkingPhrase` defaults to empty, which uses the localized thinking text. The phrase travels in companion bundles.

Downloaded model weights live in `Chat/Models/<catalogue-id>/model.gguf` under the application data root. New setups share the CPU engine in `Chat/Engines/b12496/`. Existing installations with `Chat/Models/<catalogue-id>/engine/` still work and can run linked files. The optional `Chat/local-models.json` document has `Version: 1` and a `Files` object mapping each `local:<lowercase SHA-256>` ID to its absolute GGUF path. Linking saves the reference atomically and leaves the model file at the chosen location. Re-selecting the same content at a new path updates that entry. An absent index means there are no linked files.

Private notes and conversations are stored separately in `Chat/history.json` (version 2). Its `Pets` dictionary is keyed by companion GUID. Each entry stores the last known name, ordered question/reply turns with timestamps, and explicit notes. Removing a pet while keeping its chat saves the imported picture path and the active appearance’s fallback in `ArchivedPortrait`. Image pets use `IdleFramePath`. Model pets use optional `ModelPath` and `ModelClip` fields, with a null clip selecting the rest pose. Existing version-2 image portraits keep their original fields. Version 1 records migrate with no archived portrait and retain their notes and turns. The next write saves version 2. Per-pet clearing atomically updates this file. Clearing all history removes the file, including notes-only entries and records for deleted pets. Keeping notes leaves a notes-only entry available for later removal. Every new import receives a fresh local GUID, including after deleting and reimporting the same bundle. Overwriting an existing companion preserves its local conversation.

## Addon data and manifests

Addon installation state and addon-owned settings are separate from `DesktopCompanionData`. The host registry is `%LOCALAPPDATA%\DesktopFamiliar\Addons\installed-addons.json` (`schemaVersion: 1`), packages are installed at `Addons\packages\<addon-id>\<version>`, and addon-owned settings are stored at `Addons\data\<addon-id>\settings.json`. A successful package update preserves the installation's enabled flag and removes the superseded version directory; locked package or saved-data deletion is moved to an internal pending-removal directory and retried before addons load on the next startup. The Auto Clicker settings document currently has `schemaVersion: 1` and stores its enable flag, interval range, distinct global hotkeys, and background-mode preference. The selected target area, target window handle, active state, and transient dialogue are runtime-only. The first-party catalogue lists an addon ID, display name, short description, version, compatibility range, package URL, SHA-256 checksum, and release-notes URL for the Available-tab detail pane. The core addon manifest uses `manifestVersion: 1` and is parsed with the canonical `AddonManifestJson` options, including string capability names. A package has `addon.json` plus its named root assembly; extraction rejects traversal paths, a bad checksum, or a manifest that differs from its trusted catalogue record. The full compatibility range and manifest example are documented in `docs/addons/sdk-compatibility.md`.

## Companion and scenes

Schema 11 adds nullable `CompanionDefinition.ProfilePicturePath` and a root `PendingCompanionCleanup` list of GUIDs. Schema 10 migrates with the idle-preview fallback and an empty cleanup list. A confirmed full deletion saves its GUID in the same write that removes the pet. Successful cleanup removes the GUID. A retry during startup completes the previously requested wipe in the current library and records the cleared queue on the next user save. A build reading another format skips its pending wipes. Startup and subsequent pet removals retry unfinished deletions, excluding live pets. A GUID stays queued while a live pet or an archived chat portrait still references a file in its directory.

Automatic idle portraits are rendered from the existing assets. The profile editor copies imported portraits to `Assets/<companion-guid>/Portraits/` on save. Version-1 bundles can include the optional `profilePicturePath` field and its referenced image under `assets/`. Older bundles use the idle preview.

An empty `Library/history-wipe.v16.<pet-guid>` file records when the requested history wipe has completed. Asset retries preserve conversations started afterward in another version. The receipt is removed once the cleared queue is saved.

Schema 9 adds two optional text fields, `Description` and `Personality`, directly to each companion. A schema-8 library migrates with empty defaults for missing fields and preserves any existing text. Version-1 companion bundles carry the fields in their companion object. Earlier bundles that omit the fields import with blank text. Speaking examples are derived from the selected appearance’s scene dialogue at read time.

Schema 13 introduced the independent 3D scene registry and state assignments beneath `Model3D`. `Model3D.FilePath` is the shared GLB. Each scene has its own GUID, nullable `ModelClip` (null selects the rest pose), `DialogStartSeconds` (default 0), dialogue lines, optional voice files, speed and playback mode. Several scenes may use one clip. Role IDs and click areas resolve only within their appearance's registry. The sprite registry keeps its original field names on the companion. Both appearances share the pet GUID.

Schema 14 adds optional ordered `ModelParts` (embedded clip names for one full activity), `PartOfSceneId` (source scene grouped beneath an activity), and `ModelLoop` with `Mode` (`Once`, `FixedDuration`, or `UntilDialogueEnds`), `StartSeconds`, optional `EndSeconds` (defaults to the end of the source), and `DurationSeconds` for a fixed loop. A source with several parts shares the existing GLB curves. A dialogue line can carry an optional scene-relative `StartSeconds`. Schema 13 migrates with the existing single clip and playback policy. Its scene IDs and assignments retain their meanings. The automatic portrait uses the first source clip.

Schema 11 pets migrate as image pets. The schema 12 migration converts the previous `IdleClip` into an idle scene and any `GreetingClip` into a startup scene assigned to a full-viewport click area. The migration runs for a configured model even when the pet is displaying sprites. The new scene IDs persist on save.

`ChatReplyAnimationId` is an optional scene GUID on the companion for 2D and on `Model3D` for 3D. Each ID resolves in its own registry and travels with that appearance in the bundle. A missing or null field leaves chat animation unassigned in existing schema-13 libraries and format-3 bundles.

Each companion has a stable GUID, appearance settings, animation registries, and scene assignments for required idle plus optional start, reminder, exit, companion-birthday 1/2, user-birthday 1/2, and random-idle scenes. `InteractionStates` groups optional drag-left, drag-right, and drag-hold scene IDs plus ordered `ClickAreas`. Each click area stores a name, an optional scene ID, and percentage-based left, top, width, and height bounds. Scene sources are either `AnimationPath` for a GIF/static image or ordered `PngFrames` with per-frame `DurationSeconds`.

PNG sequences use `FrameLoopMode` (`EntireAnimation`, `SelectedRange`, or `SingleFrame`), `LoopStartFrameIndex`, `LoopEndFrameIndex`, and `DialogStartFrameIndex`. Indices are zero-based in JSON. For GIF, static-image, and missing sources, normalization resets the individual-frame settings and runtime ignores stale values defensively.

Dialogue lines store text and an optional copied voice path. `{{user}}` is replaced at presentation time with the companion's configured user nickname.

## Calendar reminder state

One-time events use `IsCompleted` and `IsExpired`. Weekly events store terminal occurrence timestamps in `CompletedOccurrences` and `ExpiredOccurrences`; `ExcludedOccurrenceDates` suppresses selected dates without deleting the series. `SnoozedUntil` stores the next attempt after a user snoozes a reminder.

Terminal one-time events are removed when their end date is at least one year old. Active weekly series remain, while completed and expired occurrence history at least one year old is pruned.

Observed expirations are written atomically to `Library/calendar-expirations.v16.json` between configuration saves. The document stores the source library’s SHA-256 and a list of event GUIDs and occurrence timestamps. On restart the app applies records that match the exact library it loaded. A user save commits those occurrences to the library and removes the temporary record. Opening an older or newer library leaves its configuration intact.

## Imported assets

`CompanionAssetStore` copies imports to:

```text
%LOCALAPPDATA%\DesktopFamiliar\Assets\<companion-guid>\<category>\<file>
```

Name collisions receive a GUID suffix. Stored configuration paths are absolute local paths. Export converts them to portable bundle paths, and import converts them back to the receiving computer's local asset paths.

When wiping a pet, cleanup also checks the legacy library and other versioned libraries for absolute paths used by different pets. These shared files remain until those references are released. The wiped pet’s own references in other versions do not protect its files.

## Portable companion bundles

Portable companions use the `.companionpack` extension. The file is a ZIP archive containing `manifest.json` and referenced assets beneath `assets/`:

```json
{
  "format": "DesktopCompanion.Bundle",
  "formatVersion": 4,
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

Format 5 carries explicit chat expression overrides for either appearance. Exports using only automatic expression matches retain the existing format. Format 4 includes combined activities, loop ranges and dialogue cues. Format 3 remains readable and includes the model, 3D scenes, assignments and their voice files. Import migrates format 2 model bundles using the same clip migration as schema 12. Image-only exports using automatic expressions keep format 1 for existing readers. Every non-empty model, portrait, font, bubble, animation, PNG-frame, and voice path must be relative and begin with `assets/`. Import rejects rooted paths, traversal, duplicate case-insensitive entries, more than 5,000 entries, individual assets over 512 MiB, and referenced assets totaling over 1 GiB. Only referenced entries are extracted. Extraction uses a unique staging directory before moving the completed set beneath `%LOCALAPPDATA%\DesktopFamiliar\Assets\<companion-id>\Bundles`.

The manifest must declare its format and version. Each appearance must have unique scene GUIDs so state assignments and combined activities can identify their scenes. The import preview counts the animations listed for the active appearance, counting each combined activity once.

On import, **Overwrite existing** preserves the existing companion GUID, while **Import as new** assigns a new GUID and unique display name. Scene GUIDs remain scoped to the imported companion.
