# Desktop Familiar user manual

## Starting and closing Desktop Familiar

Desktop Familiar starts in the Windows notification area and displays the selected companion unless Focus mode is enabled. A fresh installation contains no default companion, so the companion library opens first. Use **Exit** from the companion or notification-area menu to close the process completely.

The notification-area menu provides:

- **Focus** as a toggle at the top of the menu.
- **Select companion** to switch the active character.
- **Configure** to open the companion library.
- **Calendar** to open the local event calendar.
- **Settings** to change general, auto-clicker, user, viewport, dialogue, and theme options.
- **Exit** to close Desktop Familiar.

Clicking anywhere outside the notification-area menu closes it. Windows opened from either the notification-area or companion menu receive normal keyboard input immediately after the menu closes.

Right-clicking the companion provides the relevant quick actions. Choose **Click the screen for me**, immediately below **Enter focus**, to open the same target picker used by the auto-clicker hotkey. The lower section includes **Calendar**, **Configure**, and **Settings**. The overlay and notification-area menus share the same compact width, themed rounded treatment, and inner spacing. Submenu owners such as **Select companion** retain their ordinary hover colour while the child menu is open, without switching to a darker selected state. The child menu remains available while the pointer moves into it, then both the child and parent hover close after the pointer leaves it or enters another main-menu row. The viewport menu begins with **Enter focus**, directly above the first action without a divider. While Focus is active, the viewport menu is unavailable; use the notification-area Focus toggle to leave Focus mode. The overlay can be dragged to another screen position.

## Companion library

The library displays every companion using the first frame of its default idle scene. The selected card uses the theme's primary background. Select a card with **Select**, open it with **Configure**, save a portable copy with **Export**, or permanently remove it with **Delete**. Deletion requires confirmation. Use **Import companion** at the top of the library to open a `.companionpack` bundle.

An exported bundle contains that companion's complete configuration plus every referenced animation, PNG frame, dialogue voice, imported font, and bubble image. It can be shared as one file and imported without separately copying assets. If an imported companion has the same name as an existing one, choose **Overwrite existing** to replace that companion while keeping its selected identity, or **Import as new** to keep both with a numbered name. Imported files are copied beneath Desktop Familiar's local asset directory; the original bundle can be moved or deleted afterward.

Create or edit a companion to set:

- name, birthday, and the nickname used to address you;
- installed or imported `.ttf`/`.otf` font;
- dialogue text colour and optional bubble artwork;
- any number of animation registries;
- required idle default plus optional start, reminder, exit, self-birthday 1/2, user-birthday 1/2, and random-idle assignments.

A companion must have at least one animation source and an idle-default assignment before it can be saved. Every other state is optional. When no reminder scene is assigned, reminders use the idle-default animation without replaying its dialogue or voice.

## Animation registries

An animation can use an animated GIF, a single static image, or an ordered PNG sequence. PNG sequences give the best control over transparency and timing. One PNG is valid as a static scene.

For a PNG sequence you can reorder or remove frames, set each duration in seconds, apply one duration to all frames, and preview the result. Use consistently sized canvases and align the character to the same baseline to prevent visible shifts between scenes. The overlay scales the composed character to fit the configured viewport height while preserving aspect ratio.

Each registry can include dialogue lines, a characters-per-second speed, optional voice audio per line, a dialogue start frame, and a completion mode. A line with voice audio advances when the audio ends. Other lines use their length and configured text speed. For a PNG sequence, **Start dialog at** delays the first line until the selected animation frame is displayed. This control remains hidden until at least one PNG frame is imported.

For PNG sequences, **Set the frames to loop** appears after at least one frame is imported and applies whenever that scene loops, whether it is used for idle, reminders, or dialogue. Choose:

- **Whole animation** to repeat every frame;
- **Selected range** to play the preceding intro once, repeat the selected range, then play the remaining outro once when the dialogue or state ends;
- **One frame** to play the intro once, hold the selected pose, then play any remaining outro.

These individual-frame controls and their runtime effects apply only to PNG sequences. Switching a registry between PNG frames and a GIF or static image resets the loop selection to the whole animation and resets dialogue to start at the first frame. Desktop Familiar also ignores stale individual-frame values found on a non-PNG scene. The **Loop** playback option ends a transient scene when its dialogue ends. Idle-default and reminder-state scenes continue looping after their dialogue finishes and stop only when their state changes.

State-assignment guidance is available from the question-mark icon beside the relevant label. Its tooltip opens immediately on hover, keeping the assignment grid compact without removing the explanation.

## Runtime states and dialogue

When a companion appears, Desktop Familiar prepares the first frame before showing the overlay, so a frame retained from the previous state is never flashed. Its optional start scene then plays once before Desktop Familiar switches to idle. A reminder that reveals a Focus-hidden companion starts directly in the reminder state.

- Idle continuously plays the idle-default scene. When random-idle scenes are assigned, one may play after the configured interval before the idle default returns. With no random-idle scene assigned, the default loop continues without being interrupted by that timer.
- Reminder plays the assigned reminder scene while a calendar reminder is active. If none is assigned, it keeps the idle-default visual without replaying that scene's dialogue or voice.
- Exit plays the optional exit scene before Desktop Familiar closes when the overlay is visible.
- On the companion's birthday, assigned self-birthday 1/2 scenes have a combined 30% chance to replace an ordinary random-idle trigger. On the birthday saved under **Settings > User**, assigned user-birthday 1/2 scenes independently have a combined 30% chance. A scene is chosen randomly when both slots in the matching pool are assigned, and the idle default returns afterward.

Choose **Enter focus** from the viewport menu, or enable the **Focus** toggle in the notification-area menu, to keep the companion hidden while idle. It appears automatically for an event reminder, then hides again when the reminder ends. The viewport menu remains disabled during Focus, including while the reminder is visible; use the notification-area toggle to leave Focus. Focus mode is saved between app sessions. With Focus disabled, the selected companion remains visible. While Focus is active, dialogue text and voice attached to start, idle, random-idle, birthday, fallback-idle, and exit scenes are suppressed; only an explicitly assigned reminder scene may present its own dialogue and voice.

Use `{{user}}` in dialogue to insert the companion's configured nickname for you.

The circular down pointer hides a visible dialogue and is the only action that makes the ellipsis appear. While dialogue or a reminder is active, the ellipsis uses the theme colour; after that content ends, it uses the overlay background colour. Clicking the ellipsis always removes it, clears the hidden setting, and restores the bubble when active content is available. Normal dialogue and reminders follow the same rule. Ending reminder state clears stopped dialogue content before the idle scene begins, so an empty bubble is never displayed during the transition.

## Calendar and reminders

Open **Calendar** to see the current month. Use the month and year selectors or the arrows to navigate. The earliest view is the same calendar month one year ago; future years and years containing saved events remain selectable. Past days are disabled for new events.

Select a day or date range, then enter an event name and optional notes. New events default to a timed event rather than full day. If selecting a start time would leave the end time at or before it, the end time automatically moves to 30 minutes later. An event may be:

- all day or a time period;
- one-time, including a continuous multi-day span;
- weekly on selected weekdays;
- Anniversary, Business, Life, or Other;
- normal or urgent.

Opening a weekly event and choosing **Delete event** offers **Delete this occurrence** or **Delete whole series**. Deleting one occurrence removes only the selected date from the calendar and its reminders; the rest of the weekly series continues. One-time events retain the normal delete confirmation.

Completed reminder occurrences show a checkmark in the month view. Expired reminder occurrences use a muted themed appearance so they remain visible without looking active. Open an event to see its status badge: **Scheduled**, **Reminder off**, **Completed**, or **Expired**. For a weekly series, the badge describes the specific occurrence you opened rather than the whole series.

The event form uses the same themed field height and text alignment as the Configure screen. The shared date-picker icon remains transparent on hover so it does not look like a separate filled action. The form scrolls when it is taller than the window. Use **Cancel** or click the shaded backdrop outside the form to close it without saving.

Enable reminders to choose how many minutes early to notify and the **Remind me later** interval. When a reminder is due, the companion enters reminder state and plays the assigned reminder scene's dialogue first. If that optional scene is not assigned, the idle-default visual is used without its dialogue. In Focus mode this explicitly reveals the hidden companion before reminder presentation begins. The reminder then displays the event name and start time. Its two actions are stacked below the message, and the bubble grows upward to keep both actions visible without a scrollbar. Choose **Remind me later** to dismiss the bubble, return to idle, and repeat the reminder after the configured interval. Choose **I'm on it** to dismiss the bubble, return to idle, and mark that occurrence complete. The completed reminder-scene dialogue is not shown again after either action. If the reminder is not acknowledged before the event ends, Desktop Familiar marks the occurrence expired and stops reminding. Reminder bubbles use the same down-arrow and ellipsis behavior as other dialogue.

Desktop Familiar discards completed or expired one-time reminders once their end date is at least one year old. Active weekly series remain, while their completed and expired occurrence history older than one year is pruned. Weekly recurrence currently has no end-date setting, so every valid weekly series remains active.

The calendar is currently stored only in Desktop Familiar. It does not synchronize with Windows, Outlook, Google Calendar, or other calendar accounts.

## Settings

- **General**: run after Windows sign-in and set the random-idle interval.
- **Auto clicker**: choose a global toggle hotkey and the interval in milliseconds or seconds. Pressing the hotkey always opens a full-screen target picker; click the desired screen position to start. Press the hotkey again to stop, or press Esc to cancel target selection. The target is not saved, so every start requires an intentional new selection.
- **User**: save your birthday for user-birthday animation states, or clear it.
- **View**: set the character viewport width/height and optionally truncate long dialogue.
- **Theme**: select, create, edit, name, and delete user themes. New themes can start from an existing palette. Built-in themes cannot be deleted.

The built-in **Crimson Moon** theme uses warm white and deep crimson as its main colours, with restrained gold and charcoal accents. Its Settings sidebar uses a darker-crimson hover, while the selected row uses charcoal with a gold marker for clear state contrast. **Eternal Sea** is the cool blue alternative. **Stella Dark** uses deep moonlit violet surfaces, pale lavender text, and warm starlight-gold secondary accents. Existing profiles receive Stella Dark automatically without changing their selected theme.

Theme edits apply immediately to windows, inputs, buttons, the overlay menu, and the notification-area menu. Page titles, event details, guidance, units, selected dropdown values, and dropdown items use the text colour assigned to their containing page, card, or input surface. Standard text inputs share the same height and inner spacing across event details, companion configuration, and settings. Outline buttons use the text-on-background role from the surface that contains them for their text and border, then invert that surface's background and text roles on hover.

## Files and troubleshooting

Imported files and companion-bundle assets are copied beneath `%LOCALAPPDATA%\DesktopFamiliar\Assets`, so the original source or bundle can be moved after a successful save. Configuration is in `%LOCALAPPDATA%\DesktopFamiliar\desktop-familiar.json`.

Each launch replaces `%LOCALAPPDATA%\DesktopFamiliar\Logs\latest.log`. If a fatal error stops Desktop Familiar, a report window displays that log and offers **Copy report**. Review the text before posting it publicly because local paths can appear in the report. A failed configuration save is recoverable: Desktop Familiar restores the previous saved state and shows a warning.

Wording is loaded from `Resources\Strings\en.json`. An installed copy can override individual entries in `%LOCALAPPDATA%\DesktopFamiliar\Localization\custom.en.json`; restart Desktop Familiar after editing it and keep JSON keys and placeholders such as `{0}` intact.
