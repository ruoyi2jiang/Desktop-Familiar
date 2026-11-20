# Project architecture

## Companion appearance and playback

`CompanionDefinition` owns the appearance selector, the sprite registries and assignments, and a separately cloned `ModelAppearance`. All referenced assets participate in packaging and deletion even when their appearance is inactive. The persisted companion ID identifies its chat and notes across switches.

`GlbModelFile` loads self-contained binary glTF with SharpGLTF. The overlay clones the complete companion, including the shared profile. Its state rules read `CompanionDefinition.ActiveScenes` to select the sprite or model registry. `CompanionScenes` owns scene lookup, cloned dialogue lines, and state assignments for each appearance. The overlay schedules model dialogue from the start of the clip, cancels the cue on interruption, and waits for both speech and one-shot playback before returning to idle. `Views/Controls/CompanionModelView` draws the posed skeleton and visible meshes with WPF. Embedded base-colour textures use the material’s selected texture coordinates and linear RGB factor. The editor and overlay use the same control. Its animation clock pauses while hidden, and replacing a model releases the previous scene. The image path continues to use `FrameAnimationPlayer`.

## Overview

Desktop Familiar is a .NET 8 Windows application using WPF for application windows and Windows Forms for the notification-area icon. `App.xaml.cs` is the composition root: it creates storage, application state, reminder, overlay, and tray services, then connects their events.

```text
App (composition root)
  -> CompanionManager (in-memory state and domain events)
      -> ICompanionRepository
          -> CompanionStorage (facade)
              -> JsonAppDataStore
              -> CompanionDataNormalizer
              -> CompanionAssetStore
              -> CompanionBundleService
  -> CalendarReminderService
      -> CalendarOccurrenceCalculator
      -> IClock
  -> Addon host (API 1.4)
      -> Auto Clicker addon (installed package)
          -> GlobalHotkeyService
          -> AutoClickService
          -> AutoClickTargetWindow
  -> CompanionOverlayWindow
      -> FrameAnimationPlayer
          -> IAnimationFrameLoader
          -> AnimationTimeline
  -> ApplicationRestartService
  -> TrayService and WPF windows/controls
```

## Folder responsibilities

- `DesktopFamiliar.AddonSdk/`: versioned addon manifest, compatibility, capability, and lifecycle contracts.
- `Addons/deploy-local.ps1` and `Addons/build-package.ps1`: shared first-party local deployment, package construction, and artifact validation.
- `Addons/<addon-id>/`: addon-owned models, pure services, WPF views, manifest, and scoped contributor workflow.
- `Models/`: serializable core data and deep cloning. Models do not perform I/O or show UI.
- `Services/Animation/`: pure playback state plus frame-decoding contracts and implementation.
- `Services/Calendar/`: pure recurrence, status, navigation, retention, and occurrence calculations.
- `Services/Storage/`: JSON persistence, current-format normalization, asset copies, and portable companion bundles.
- `Services/Time/`: testable time provider.
- `Services/Validation/`: asset-aware companion validation.
- `Views/`: WPF windows. Reusable visuals belong in `Views/Controls/`.
- `Localization/` and `Resources/Strings/`: translation lookup, XAML integration, and English mappings.
- `tests/DesktopFamiliar.Tests/`: unit tests for domain, persistence, and UI resource contracts.

## Runtime state

`CompanionManager` owns the loaded `DesktopCompanionData`, exposes the selected companion, and raises narrow events after a successful mutation. It depends only on `ICompanionRepository`. `ThemeSelectionPolicy` resolves a saved-theme card to its built-in or custom kind, so selecting a built-in palette cannot be persisted as a custom theme. Before a save the manager keeps a deep snapshot; if persistence fails, it restores that snapshot and throws `AppPersistenceException` so the application can report the failure without leaving false in-memory state.

The addon host exposes API 1.4 through `DesktopFamiliar.AddonSdk`. An addon manifest declares a supported host API range and sensitive capabilities; `AddonCompatibilityPolicy` rejects unknown manifest formats, missing identities/entry points, assemblies, and incompatible hosts before activation. `AddonInstallationStore` owns the atomic local installation registry and safe ZIP extraction, preserves the installation's enabled state across an update, and retires superseded package directories. `AddonSettingsStore` atomically persists addon-owned JSON under its data directory; startup imports legacy Auto Clicker values from the original shared file. `AddonRuntimeLoader` creates a collectible assembly-load context only for enabled packages that have passed those checks. `IAddonViewportActionRegistry` is a host service: every visible addon command is grouped below the overlay's single **Action** submenu and invoked through `DeferredMenuAction` after the menu closes. API 1.2 adds the settings-section registry, scoped settings store, localized-string provider, and transient companion-status presenter; API 1.3 adds inline installed-card content; API 1.4 adds an optional addon-owned title-row action. The Auto Clicker uses these services for its runtime, settings UI, target picker, hotkeys, input engine, and short dialogue messages without a reference to the application assembly. `docs/addons/sdk-compatibility.md` is the normative versioning contract; the running app will only be wired to the pinned first-party catalogue, never arbitrary external packages.

`ApplicationRestartService` is the process boundary used after an addon lifecycle change. It starts the same executable command line with the old process ID; the replacement waits for that process to exit before acquiring the single-instance mutex, preventing the restart from being consumed by the still-running instance.

`CompanionActivityState` is runtime-only and distinguishes Idle from Reminder. `FocusModeVisibilityPolicy` combines activity with the persisted focus toggle: normal mode displays the selected companion, while Focus displays it only for Reminder. Consumers use `CompanionManager.ShouldDisplayCompanion` instead of duplicating the rule.

## Animation flow

`ModelAnimationCatalog` recognizes unambiguous start/middle/end families by clip name. `ModelClipTimeline` maps their shared source time to an embedded clip, handles arbitrary loop markers and plays the outro on release. Preview scrubbing and overlay playback use the same sampling code. `ModelDialogueTiming` schedules cues on scene time. An entrance preloads the selected idle clip after its first frame. When the visual entrance ends before its greeting, idle motion can run under the remaining speech without restarting the idle clock at the handoff.

`GlbModelFile.ReadForPlayback` validates the self-contained model and builds a SharpGLTF scene without eagerly binding its animation catalogue. `ModelPlaybackRig` binds the selected clip’s joint, morph, and visibility curves on first use, matching nodes by their glTF indices. The overlay stops the renderer while hidden and reuses the rig and geometry on reveal. Changing companions or appearances, replacing the model file, and closing the overlay release the old resources.

`CompanionModelView` projects each pose at the scale and origin set by the rest mesh and configured character rectangle. Depth planes encompass the current mesh. On the desktop, `OverlayLayoutPolicy` expands the transparent window in blocks to cover visible animation travel and the dialogue area. The model draws behind the logical overlay content. Window expansion preserves the screen coordinates of the character, click regions, dialogue, chat prompt, and central menu target. Each new clip can release the previous clip’s extra space.

`ScenePlaybackPolicy.ModelBlendSeconds` selects the same-model pose blend at a role change. Ordinary action returns use 0.6 seconds to ease from the displayed pose into the advancing idle. Entrance and chat handoffs retain their 0.3-second blend. The animation editor also plays the assigned idle after a scene preview. Its playhead continues to describe the selected source, and scrubbing restores that source immediately.

`WpfAnimationFrameLoader` decodes a GIF, image, or PNG sequence into frozen WPF frames and durations. `AnimationTimeline` is a UI-free index state machine for whole-animation loops and intro/selected-loop/outro transitions. `ScenePlaybackPolicy` owns role completion, reminder ordering, and the Focus rule that permits dialogue and voice only for the reminder role. `IdleSceneSelectionPolicy` selects ordinary random-idle, companion-birthday, or user-birthday scenes. `AnimationFrameSettingsPolicy` owns the PNG-only contract for loop ranges and dialogue start frames. `FrameAnimationPlayer` owns only the dispatcher timer, image target, frame notifications, and event translation.

`CompanionInteractionStatePolicy` maps drag direction and ordered percentage-based click areas to optional grouped assignments. It returns no scene for an unassigned state, so the overlay only starts interaction playback when that exact state has an animation; it only restores state-default playback after an assigned drag scene has actually started. The overlay owns pointer capture, cursor feedback, window placement, and the short crossfade between assigned interaction scenes; the policy remains WPF-free. The editor owns the direct preview drawing surface for click-area configuration. Keep playback decisions in animation services and WPF decoding in the loader; do not place reusable animation rules in a view.

## Calendar flow

`CalendarOccurrenceCalculator` owns date spans, recurrence, exclusions, and occurrence end times. `CalendarOccurrenceStatusTracker` owns completed and expired state for one-time events and weekly occurrences. `CalendarNavigationPolicy` owns the one-year history limit. `CalendarRetentionPolicy` removes year-old terminal one-time events and prunes year-old occurrence history while retaining active recurring series. `CalendarReminderService` performs retention and schedules reminders. Snooze and acknowledgement save immediately. `CalendarExpirationStore` records observed expirations against the loaded library’s fingerprint. The next user save includes those observations and any retention changes.

`CalendarReminderPresentationCoordinator` maps reminder lifecycle events to runtime Reminder state and applies focus visibility before presenting. Snooze, acknowledgement, or expiration returns the companion to Idle. Calendar data is local; there is no Windows, Outlook, or Google synchronization adapter.

## Persistence and packages

`JsonAppDataStore` resolves library files by schema and atomically saves to `Library/desktop-familiar.v<schema>.json`. The original root file remains available to legacy builds. `CompatibleLibraryReader` projects recognizable fields from a future schema. `CompanionDataNormalizer` migrates and repairs supported values in memory. Opening a library does not commit these changes. `CompanionAssetStore` copies user-selected files and can remove only the exact asset directory for a confirmed companion deletion. `CompanionBundleService` imports referenced assets through a staging directory.

Fresh installs start with an empty companion library. Private character sources, pack-builder scripts, and generated companion bundles are outside the public source tree and ignored by Git.

CompanionDefinition owns the profile text alongside the companion name. Its `GetDialogueExamples` method reads authored lines from `ActiveScenes` in scene order and ignores blank and repeated text. The profile editor and the local chat prompt both use this method.

## Built-in local chat

`ChatExpressionPolicy` resolves automatic scene names and overrides for the active appearance. At the start of a turn, `ChatSession` captures the available expression keys and scene IDs. When any are available, llama.cpp generates spoken text and one expression key, including neutral. If the engine rejects the format or returns malformed metadata without usable speech, it retries ordinary text within the same process. Only spoken text enters history. All pages of one reply share a `ChatReplyPlayback`, which tries the selected scene, then the ordinary talking scene if the first fails.

The overlay tracks whether the visible chat page contains a model reply. It plays the current appearance's optional chat scene across consecutive reply pages and restores normal playback when the last page closes. `ScenePlaybackPolicy` starts a reaction from silent idle or switches between replies with different scenes. It suppresses the reaction scene’s scripted lines. A same-model switch into or out of a reply blends the displayed joint pose and morph weights into the advancing target clip over 0.3 seconds, capped by the length of an incoming one-shot. Consecutive reply pages share the same animation clock.

`Services/Chat/ChatController` coordinates the global shortcut, prompt, presentation and active companion. `ChatSession` builds the request from a detached profile and the conversation for that GUID. Each turn has its own cancellation token. A switch, disabling chat, clearing the active history or shutting down invalidates pending work before it can commit. A successful turn appends the question, reply and any explicit note in one atomic write.

`IChatEngine` separates inference from conversation rules. `LlamaChatEngine` starts a CPU-only llama.cpp helper per turn, uses the Windows TCP owner table to verify that each loopback connection belongs to that process before sending HTTP data, and authenticates chat requests with a per-launch key. The helper exits after the turn. `Resources/chat-models.json` is the embedded download catalogue. Each entry defines a stable ID, localization key, HTTPS file and terms URLs, SHA-256 and estimated first-install download size. Add a compatible text model there to reuse the existing picker and llama.cpp integration. `ChatModelStore` resolves managed and linked files, verifies downloads, and reuses the Windows CPU engine. `LocalChatModelIndex` stores external file references and scans user-selected folders. Linking hashes the file, recognizes catalogue releases by digest, and checks other candidates with a one-token request through the normal chat runtime before saving. Profiles and private conversation data have distinct lifetimes.

`Views/CompanionOverlayWindow.Chat.cs` presents the reply queue with the reading durations from `ChatBubbleTiming`. The prompt is a separate owned window. The overlay uses `OverlayLayoutPolicy` to position it within the pet’s monitor work area and reserve space between the pet and spoken dialogue when it sits above the pet. Scene and reminder content can temporarily take the reply's place. `ChatHistoryWindow` displays a conversation reader or a compact per-pet management list. `ChatHistoryPetRow` lays out both lists. The reader uses a selectable list, and Manage hosts plain rows with a per-pet Clear button. `ChatHistoryStore` measures each saved UTF-8 record for Manage and reads the file length for the total in Settings. Both lists and the Profile tab use the circular `CompanionAvatar`. Configure cards fit the full image inside a rounded rectangular frame. Both presentations use `CompanionPictureLoader` to load the imported portrait or the active idle frame. For a 3D idle, `CompanionModelView` renders a frozen bitmap at clip time zero. The loader caches recent snapshots by model path, file version and clip. History controls stay available independently of inference. `CompanionDeletionService` completes and retries selected resource and history cleanup after the library deletion has been saved. It keeps the owner queued while another companion or an archived portrait uses an owned file.

The current clean persistence baseline is schema 15. When persisted fields change, update model defaults and `DesktopCompanionData.CurrentSchemaVersion`, implement an explicit migration before accepting older schemas, add migration and normalization tests, and update `docs/data-format.md`.

## Error boundaries

- `AppLogger.Initialize` and global exception handlers are registered before normal startup work.
- Recoverable save failures use `AppPersistenceException` and preserve the previous state.
- Fatal WPF, notification-area, background-domain, and unobserved-task exceptions are logged.
- Fatal UI errors open `CrashReportWindow`; if that fails, a minimal message is shown.
- Logging swallows its own failures so reporting cannot recursively crash the app.

Catch exceptions only when a layer can recover, add boundary context, or translate the error.

## UI and theming

Windows compose controls in XAML. In deletion confirmations, the shared `DialogActionRow` in `App.xaml` gives both buttons the width of the longer label and aligns their edges. The buttons inherit their spacing from the common button style. `CompanionCard`, `FieldHelpLabel`, and shared themed input styles own reusable presentation. Code-behind coordinates view behavior; reusable calculations and persistence belong in services.

All visual colours use semantic `DynamicResource` keys defined by `ThemeTokens` and populated by `ThemeService`. Page, primary, secondary, and input surfaces use their matching background, text, accent, hover, and active roles. Do not add literal UI colours for themed elements.

`TrayService` keeps its native `ThemedTrayContextMenu` populated before Windows opens it, then refreshes it after closure when companion, focus, or theme state changes; the overlay uses WPF context menus. Both derive presentation from the same theme palette. `TrayToggleMenuItem` owns the notification-menu Focus toggle. The tray Focus switch closes its native dropdown before the state change shows or hides the pet, releasing menu keyboard handling for the next window. Tray commands execute directly through the native menu's item callbacks; WPF overlay window-opening commands pass through `DeferredMenuAction` after its menu's closed boundary, preventing WPF menu input mode from leaking into the activated window.

## Localization

User-facing text is keyed in `Resources/Strings/en.json`. XAML uses `Loc`; C# uses `LocalizationService`. New visible text requires a mapping entry and must not be hard-coded in a view or service.

## Application updates

`Services/Updates/AppUpdateService` reads the official GitHub release pages, filters stable core version tags, and verifies each download’s size and SHA-256 digest as it streams to a staging file. `AppUpdateHandoff` checks the executable path against the per-user Inno Setup registration. An installed copy applies setup, a portable copy applies the standalone executable, and other package types can be saved separately. The General page owns the check and download lifetime. When the user applies a staged update, the embedded Windows PowerShell handoff waits for the old process, rechecks the digest, then launches setup or atomically replaces and relaunches the portable executable. If the replacement cannot launch, the helper restores the previous executable. Runtime builds with a separate app DLL can download a release to a user-selected path. The addon catalogue and installation store handle addon updates independently.
