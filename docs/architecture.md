# Project architecture

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
  -> Addon host (API 1.2)
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

The addon host exposes API 1.2 through `DesktopFamiliar.AddonSdk`. An addon manifest declares a supported host API range and sensitive capabilities; `AddonCompatibilityPolicy` rejects unknown manifest formats, missing identities/entry points, assemblies, and incompatible hosts before activation. `AddonInstallationStore` owns the atomic local installation registry and safe ZIP extraction. `AddonSettingsStore` atomically persists addon-owned JSON under its data directory; startup transfers legacy Auto Clicker values before schema-8 core-data normalization removes them. `AddonRuntimeLoader` creates a collectible assembly-load context only for enabled packages that have passed those checks. `IAddonViewportActionRegistry` is a host service: every visible addon command is grouped below the overlay's single **Action** submenu and invoked through `DeferredMenuAction` after the menu closes. API 1.2 adds the settings-section registry, scoped settings store, localized-string provider, and transient companion-status presenter. The Auto Clicker uses these services for its runtime, settings UI, target picker, hotkeys, input engine, and short dialogue messages without a reference to the application assembly. `docs/addons/sdk-compatibility.md` is the normative versioning contract; the running app will only be wired to the pinned first-party catalogue, never arbitrary external packages.

`ApplicationRestartService` is the process boundary used after an addon uninstall. It starts the same executable command line before the current WPF application closes, allowing the unloaded addon to finish runtime cleanup in the replacement process.

`CompanionActivityState` is runtime-only and distinguishes Idle from Reminder. `FocusModeVisibilityPolicy` combines activity with the persisted focus toggle: normal mode displays the selected companion, while Focus displays it only for Reminder. Consumers use `CompanionManager.ShouldDisplayCompanion` instead of duplicating the rule.

## Animation flow

`WpfAnimationFrameLoader` decodes a GIF, image, or PNG sequence into frozen WPF frames and durations. `AnimationTimeline` is a UI-free index state machine for whole-animation loops and intro/selected-loop/outro transitions. `ScenePlaybackPolicy` owns role completion, reminder ordering, and the Focus rule that permits dialogue and voice only for the reminder role. `IdleSceneSelectionPolicy` selects ordinary random-idle, companion-birthday, or user-birthday scenes. `AnimationFrameSettingsPolicy` owns the PNG-only contract for loop ranges and dialogue start frames. `FrameAnimationPlayer` owns only the dispatcher timer, image target, frame notifications, and event translation.

`CompanionInteractionStatePolicy` maps drag direction and ordered percentage-based click areas to optional grouped assignments. It returns no scene for an unassigned state, so the overlay only starts interaction playback when that exact state has an animation; it only restores state-default playback after an assigned drag scene has actually started. The overlay owns pointer capture, cursor feedback, window placement, and the short crossfade between assigned interaction scenes; the policy remains WPF-free. The editor owns the direct preview drawing surface for click-area configuration. Keep playback decisions in animation services and WPF decoding in the loader; do not place reusable animation rules in a view.

## Calendar flow

`CalendarOccurrenceCalculator` owns date spans, recurrence, exclusions, and occurrence end times. `CalendarOccurrenceStatusTracker` owns completed and expired state for one-time events and weekly occurrences. `CalendarNavigationPolicy` owns the one-year history limit. `CalendarRetentionPolicy` removes year-old terminal one-time events and prunes year-old occurrence history while retaining active recurring series. `CalendarReminderService` performs retention, schedules reminders, and persists snooze, completion, and expiration through the manager.

`CalendarReminderPresentationCoordinator` maps reminder lifecycle events to runtime Reminder state and applies focus visibility before presenting. Snooze, acknowledgement, or expiration returns the companion to Idle. Calendar data is local; there is no Windows, Outlook, or Google synchronization adapter.

## Persistence and packages

`JsonAppDataStore` reads and atomically replaces `desktop-familiar.json`. `CompanionDataNormalizer` repairs invalid values in the current schema without performing I/O. `CompanionAssetStore` copies user-selected files and can remove only the exact asset directory for a confirmed companion deletion. `CompanionBundleService` exports and securely imports `.companionpack` archives through a staging directory, extracting only assets referenced by the manifest.

Fresh installs start with an empty companion library. Private character sources, pack-builder scripts, and generated companion bundles are outside the public source tree and ignored by Git.

The current clean persistence baseline is schema 8. When persisted fields change, update model defaults and `DesktopCompanionData.CurrentSchemaVersion`, implement an explicit migration before accepting older schemas, add migration and normalization tests, and update `docs/data-format.md`.

## Error boundaries

- `AppLogger.Initialize` and global exception handlers are registered before normal startup work.
- Recoverable save failures use `AppPersistenceException` and preserve the previous state.
- Fatal WPF, notification-area, background-domain, and unobserved-task exceptions are logged.
- Fatal UI errors open `CrashReportWindow`; if that fails, a minimal message is shown.
- Logging swallows its own failures so reporting cannot recursively crash the app.

Catch exceptions only when a layer can recover, add boundary context, or translate the error.

## UI and theming

Windows compose controls in XAML. `CompanionCard`, `FieldHelpLabel`, and shared themed input styles own reusable presentation. Code-behind coordinates view behavior; reusable calculations and persistence belong in services.

All visual colours use semantic `DynamicResource` keys defined by `ThemeTokens` and populated by `ThemeService`. Page, primary, secondary, and input surfaces use their matching background, text, accent, hover, and active roles. Do not add literal UI colours for themed elements.

`TrayService` keeps its native `ThemedTrayContextMenu` populated before Windows opens it, then refreshes it after closure when companion, focus, or theme state changes; the overlay uses WPF context menus. Both derive presentation from the same theme palette. `TrayToggleMenuItem` owns the notification-menu Focus toggle. Tray commands execute directly through the native menu's item callbacks; WPF overlay window-opening commands pass through `DeferredMenuAction` after its menu's closed boundary, preventing WPF menu input mode from leaking into the activated window.

## Localization

User-facing text is keyed in `Resources/Strings/en.json`. XAML uses `Loc`; C# uses `LocalizationService`. New visible text requires a mapping entry and must not be hard-coded in a view or service.
