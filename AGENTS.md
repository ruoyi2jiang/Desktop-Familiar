# Desktop Familiar contributor workflow

This file applies to the entire repository.

## Before changing code

1. Read `README.md` and the relevant files in `docs/`.
2. Check the working tree. Preserve unrelated local changes and user assets.
3. Identify the owning layer before editing:
   - serializable state/validation: `Models/`;
   - pure animation/calendar rules: `Services/Animation` or `Services/Calendar`;
   - JSON, migrations, packages, assets: `Services/Storage`;
   - orchestration/runtime state: focused service in `Services/`;
   - reusable visual: `Views/Controls`;
   - window-only coordination: the relevant `Views/*.xaml(.cs)`.

## Architecture rules

- Keep each service responsible for one concern. Prefer interfaces at I/O/time boundaries.
- Domain rules must not depend on WPF, timers, the registry, the filesystem, or message boxes.
- Models do not perform I/O and must deep-clone mutable child collections.
- UI code-behind coordinates controls; move reusable calculations and persistence out of it.
- Viewport context-menu commands that open WPF windows must use `DeferredMenuAction` and dispatch only after `ContextMenu.Closed` at `DispatcherPriority.ContextIdle`. Do not change this to `Normal`: the menu can retain keyboard input otherwise, leaving the opened window unable to accept typing.
- Use `DynamicResource` semantic theme tokens. Do not add unthemed literal control colours.
- Put every user-facing string in `Resources/Strings/en.json`.
- Keep the current `%LOCALAPPDATA%\DesktopFamiliar` data contract explicit. Do not add compatibility aliases or accept older schemas without a tested migration.
- Catch exceptions only where the code can recover or add boundary context. Log full exception objects.
- A failed persistent mutation must not leave unsaved in-memory state or emit success/change events.

## Required verification

For code changes run:

```powershell
dotnet build .\DesktopFamiliar.slnx -c Release
dotnet test .\tests\DesktopFamiliar.Tests\DesktopFamiliar.Tests.csproj -c Release --no-build
```

Add regression tests for pure rules, migrations, persistence, and every fixed bug. For XAML or tray changes, also perform the applicable checklist in `docs/development.md`.

## Documentation must stay current

Update documentation in the same change:

- user-visible behavior -> `docs/user-manual.md` and, if it changes the headline feature set, `README.md`;
- architecture or folder ownership -> `docs/architecture.md`;
- persisted schema/package fields -> `docs/data-format.md` plus a migration test;
- commands, artifacts, or release flow -> `docs/development.md`;
- contributor rules -> this `AGENTS.md`.

Do not mark work complete with stale documentation.

## Addons

- Read `docs/addons/overview.md` and the relevant addon document before changing addon-owned code.
- Each addon folder has a scoped `AGENTS.md`; follow it in addition to this file.
- Public addon contracts live in `DesktopFamiliar.AddonSdk/`. Any API or manifest compatibility change requires tests and an update to `docs/addons/sdk-compatibility.md`.
- Addon-only settings and migrations belong to that addon’s data contract, not `DesktopCompanionData`.
- Initial addon distribution is first-party only: use the pinned catalogue, validate the declared API range and SHA-256, and never add arbitrary repository or release-page discovery.

## Release and licensing

- A source refactor does not authorize an installer build, cache deletion, version bump, signing, publishing, or distribution.
- Keep project/installer version declarations synchronized only when the owner requests a release build.
- Public source is licensed under PolyForm Noncommercial 1.0.0. Do not change licence terms without explicit owner direction.
- Never stage, commit, publish, or redistribute `CompanionPacks/`, generated `.companionpack` files, private pack-builder scripts, imported user assets, or third-party character content without documented permission.
- Public application and installer builds must start with an empty companion library.

## Definition of done

- The responsible module owns the behavior and no duplicate rule remains in a view.
- Exceptions have an intentional recovery/fatal path.
- Release build has no warnings.
- Unit tests pass.
- Localization and documentation are updated.
- No generated `bin`, `obj`, temporary build, cache, installer, or user-data files are presented as source changes.
