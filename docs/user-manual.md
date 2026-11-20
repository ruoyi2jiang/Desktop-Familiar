# Desktop Familiar user manual

## Starting and closing Desktop Familiar

Desktop Familiar starts in the Windows notification area and displays the selected companion unless Focus mode is enabled. A fresh installation contains no default companion, so the companion library opens first. Use **Exit** from the companion or notification-area menu to close the process completely.

The notification-area menu provides:

- **Focus** as a toggle at the top of the menu.
- **Select companion** to switch the active character.
- **Configure** to open the companion library.
- **Calendar** to open the local event calendar.
- **Chat history** to read conversations by companion.
- **Settings** to change general, user, companion, chat, theme, and extra options.
- **Exit** to close Desktop Familiar.

Clicking anywhere outside the notification-area menu closes it. Windows opened from either the notification-area or companion menu receive normal keyboard input immediately after the menu closes.

Right-clicking the companion provides the relevant quick actions. **Chat** appears below **Enter focus** when a local model is configured and chat is enabled. **Action** contains **Play random idle** while idle and any visible addon commands. The lower section includes **Calendar**, **Configure**, **Chat history**, and **Settings**. The overlay and notification-area menus share the same compact width, themed rounded treatment, and inner spacing. Submenu owners such as **Select companion** retain their ordinary hover colour while the child menu is open, without switching to a darker selected state. The child menu remains available while the pointer moves into it, then both the child and parent hover close after the pointer leaves it or enters another main-menu row. The viewport menu begins with **Enter focus**, directly above the first action without a divider. While Focus is active, the viewport menu is unavailable; use the notification-area Focus toggle to leave Focus mode. The overlay can be dragged to another screen position; its cursor changes to a hand while the companion is being dragged.

## Local chat

1. Open **Settings → Chat**, turn on chat, and open the **Local model** dropdown. Windows x64 is required. Choose one of the following options.

   - **Download a supported model…** lists the bundled catalogue. Select **Gemma 3 1B** and click **Download**. The first installation takes about 832 MB of downloads including the CPU engine.
   - **Choose a GGUF file…** opens a file picker. Select your existing model and click **Use model**. Desktop Familiar uses the file in place. The first use downloads about 26 MB for the engine if the app has no engine yet.
   - **Scan a folder…** lists GGUF files inside the selected folder and its subfolders. Select the file you want and click **Use model**. Only the selected file is checked and linked.

   Gemma is recognized even if you renamed the file. For other GGUFs, the app tries a short local text-chat request before accepting them. Successful setup selects the model. If you later move the file, choose it again at its new location.

2. Open **Chat** on the pet's menu, or press **Ctrl+Shift+F9**. The same shortcut closes the prompt. To change it, focus **Open or close chat** in Settings and press the new combination. After leaving Focus mode from the notification area, reopen Chat and start typing.
3. The prompt follows the pet, centered above its head. At 100% display scaling, it stays at least 520 pixels wide and can grow to 640 for larger pets. It shrinks below 520 only when the monitor’s available width is smaller. It moves below when there is too little room above for both the prompt and a speech bubble. Type a message and press **Enter** or click **↵ Send** beside the text field. When voice input is enabled, the microphone sits between the field and Send. Listening status and errors appear beneath that row. Click **×** at the right end of the row or press **Escape** to close the prompt. A slow reply shows the companion's **Thinking phrase**, editable in **Configure → Profile**, after 0.2 seconds. Replies wrap and advance in short bubbles. Incoming reminders and scene dialogue take priority and pause the chat reading timer.
4. Give a pet a lasting note by starting a message with **Remember**, for example, “Remember my favourite tea is jasmine.” After a successful reply the app saves that note for the pet. Each request includes that companion's description, personality, dialogue examples from the selected 2D or 3D appearance, recent turns and recent notes. Each companion has one continuous conversation.
5. Choose **Chat history** in either menu and select a pet to read its conversation. The selected pet’s background uses your theme’s selection colour. Hovering over another pet previews its hover colour. Long pet lists and conversations show the same slim, theme-coloured scrollbar as Settings. In the reader, the pet list’s scrollbar runs along the divider. In Manage, it sits near the window’s right edge. Under **Settings → Chat**, the total beneath **Chat history** shows the size of the saved history file. **Manage** shows each pet’s name and the size of its saved history record, including notes and entries for pets removed from the library. The file total also includes shared JSON metadata. Kept conversations use the pet’s last picture or idle frame when the image is still available. In Manage, **Clear** is the only action on each row. Click it to clear that pet’s messages. If it has notes, you can keep them by unchecking **Also delete this pet’s saved notes**. For a pet with only notes left, **Clear** removes those notes after confirmation. If a reply arrives while the dialog is open, the confirmation still applies only to the kinds of data it offered to clear. **Clear all history** asks for confirmation and erases every pet’s messages and saved notes. Both controls are available while chat is off and without an installed model. Switching pets cancels an unfinished reply. Completed conversations stay on this computer and are excluded from companion exports.

**Configure → States → Expressions** lists the reactions for the selected appearance. The local model can choose Happy, Sad, Surprised, Confused, Angry, or Thoughtful when a scene is available. Automatic matches a unique whole scene name, ignoring case. Happy also recognizes Smile and Thoughtful recognizes Ponder. Pick another scene to override a match, or choose **Not assigned** to disable that reaction. Save to keep edits. For example, sharing good news can trigger Happy across the reply’s bubbles. Neutral replies use the optional **Chat reply animation**. Everyday playback continues if neither scene is available.

### Talking to a pet

Enable **Settings → Chat** and configure a local chat model. **Voice input** starts enabled. Choose your microphone and the language you speak. **Test microphone** records three seconds, plays the sample through your default sound output, then reports the peak level. The sample stays in memory. Leaving the Chat settings page or changing input devices cancels the test. Hover over the question marks beside **Local model** and **Prefer multilingual Whisper** for help choosing. **Windows language packs…** opens Windows Settings to add speech features. After installing a pack or connecting a microphone, choose **Refresh list**.

Desktop Familiar checks for a local Windows dictation recognizer for your language. If one is missing, it offers a verified download of multilingual Whisper Base and the Windows x64 CPU engine. **Prefer multilingual Whisper for recognition** lets you use it even when Windows provides your language. Download once and use it offline with any pet. Speech input is available whenever ordinary chat is available.

Hold the push-to-talk shortcut to open the prompt and talk, or hold the microphone button in an open prompt. Its default is **Ctrl+Shift+F10**. If your existing chat shortcut already uses that combination, push-to-talk starts on **Ctrl+Shift+F9**. You can change both in Chat settings. Keyboard users can also focus the microphone button and hold **Space**.

Release to transcribe up to 30 seconds of speech into the input. Correct the text and press **Enter**, or enable **Send automatically after releasing push-to-talk**. If the combined draft exceeds 1,200 characters, the full text stays in the prompt for editing before you send it. Closing the prompt during recognition, switching pets, entering Focus or disabling Chat discards unfinished input. Only submitted messages enter that pet’s history. Windows recognition processes the recording in memory. Whisper uses a temporary WAV that the app deletes after transcription.

In **Configure → Profile → Voice**, choose the pet’s reply language and speaking voice. The Windows list comes from your PC. **Windows default voice** follows the system voice and **Off** gives text-only replies. Windows voices support speed, pitch and volume. Changing a setting previews the pet’s first available dialogue line, or a sample phrase. **Preview voice** plays it again. Use **Save** to keep the changes for both appearances.

**Download Kokoro English** installs a shared offline engine and a bank with eleven speakers. **Import a Kokoro voice folder** accepts the English v0.19 sherpa-onnx format with `model.onnx` or `model.int8.onnx`, `voices.bin`, `tokens.txt` and `espeak-ng-data`. Imported files are copied into the local voice library. A first-time import can be prepared offline and offers a separate engine download for playback. Choose a speaker and tune speed and volume. Exported pets carry their voice selection and tuning. Install the matching voice or import the same bank on the other computer to hear it.

If an imported bank disappears from the voice list because one of its files is missing, import the original folder again. This restores the bank under the same ID, so pets that use it keep their speaker assignments.

## Companion library

Under each pet’s name, the Configure card shows its active appearance and animation count, for example **3D · 87 animations**. Each 2D scene counts once, regardless of its frame count. The 3D count matches the Animations list with **Show parts** off, so each combined activity counts once.

Each Configure card shows the full profile picture inside a tall, rounded rectangle. If the picture is unavailable, the card uses the first frame of the active appearance’s assigned idle. For a 3D pet, this is a snapshot rendered from its model. The selected card uses the theme's primary background. Select a card with **Select**, open it with **Configure**, save a portable copy with **Export**, or permanently remove it with **Delete**. The confirmation shows equal-sized **Cancel** and **Delete** buttons. Choose **Delete** to remove the pet’s configuration from the library. Check **Also delete this pet’s imported files, chat history, and saved notes** to clean up its local data too. The box starts unchecked. Other pets retain their conversations and any artwork they still use. A shared file queued for deletion is removed once its last reference is gone and cleanup next runs, at startup or when removing a pet. Use **Import companion** at the top of the library to open a `.companionpack` bundle.

An exported bundle contains that companion's complete configuration plus its profile picture and every referenced animation, PNG frame, dialogue voice, imported font, and bubble image. It can be shared as one file and imported without separately copying assets. If an imported companion has the same name as an existing one, choose **Overwrite existing** to replace that companion while keeping its selected identity, or **Import as new** to keep both with a numbered name. Imported files are copied beneath Desktop Familiar's local asset directory; the original bundle can be moved or deleted afterward. Each new import starts with its own empty chat history and notes, including when you reimport a previously deleted pet.

Create or edit a companion to set:

- name, birthday, and the nickname used to address you;
- an optional description (who the companion is) and personality (how they behave and speak);
- installed or imported `.ttf`/`.otf` font;
- dialogue text colour and optional bubble artwork;
- any number of animation registries;
- required idle default plus optional start, reminder, exit, self-birthday 1/2, user-birthday 1/2, and random-idle assignments.

**Profile → Idle preview** plays the assigned idle animation for the selected 2D or 3D appearance. The separate **Profile picture** below it appears on the Configure card and in both chat history lists. It defaults to a still of the idle animation and updates when you change the idle or switch appearances. Choose **Import profile picture** to use your own image across both appearances. **Use idle preview** restores the automatic picture.

The **Profile** tab shows up to six distinct speaking examples from the selected appearance’s animations. Choosing **3D model** shows its 3D scene dialogue. Switching back to **2D images** shows the sprite dialogue. If the selected appearance has no lines, the list is empty. Add lines under **Animations** and return to **Profile** to see them. Description and personality are saved and exported with the companion.

At the top of **Animations**, choose **2D images** or **3D model**. Both appearances share the pet’s profile and conversations. Each keeps its own scenes and **States** assignments.

3D entrances, dances, and jumps keep their original travel and the pet’s resting scale. On the desktop, the pet can move past all four sides of its configured character area, including up behind a dialogue bubble. The bubble stays in front. The physical screen edge is the limit, so placing the pet farther from an edge leaves more room on that side. Outside Focus mode, you can right-click the moving pet or the center of its usual resting area to open its menu.

For 3D, choose a self-contained `.glb` file (up to 64 MiB). Under **Animations**, choose a model animation for the scene. The picker offers a full activity when the GLB contains a matching start, middle, and end. **Show parts** reveals its individual clips and any grouped source scenes. Existing state assignments can still use a single part.

Drag the preview playhead to inspect a pose. Use **Set start here** and **Set end here**, or type the two times in seconds. **Play once** runs from beginning to end. **Loop for a duration** spends the specified number of seconds in the marked range, then plays the ending. In a one-off action, **Loop until dialogue ends** finishes the current loop cycle when speech ends and plays the ending. Default idle, reminder, and drag scenes keep looping after their own dialogue ends while that state is active.

In the dialogue grid, **Start at (s)** cues a line from the beginning of the scene. A timed line takes over the bubble and voice at that time. Blank times follow the preceding line or use the first-line delay. **Preview playback** shows silent captions using estimated reading times. Live voice lines follow the audio duration. Cues fire once per scene and are cancelled when the scene is interrupted.

To see how a 3D action ends, select it in **Animations** or choose **Preview playback**. With **States → Idle default** assigned, the preview plays the ending and eases into the moving idle. The playhead stays at the end of the selected scene. Drag it to inspect the original poses, or replay the scene. Without an assigned idle, the preview holds the last pose.

On the desktop, ordinary actions such as click reactions and random idles ease back into idle over 0.6 seconds, even when the model has no dedicated recovery clip. A one-shot 3D entrance can move into idle while its last greeting is still visible or playing. Other one-shots can hold their last pose while their remaining dialogue finishes.

Open **States** to assign the scenes for idle, startup, reminders, birthdays, exit, random idle, drag directions, and click areas. For example, make two scenes using the same bow clip. Assign **Welcome back** to **Start** and **Tap greeting** to a click area. Each can have its own words and timing. Existing 3D pets keep their idle and greeting assignments when upgraded.

**Random idle animations** starts collapsed and shows how many scenes are selected for the current appearance. Expand it to change the checkboxes. You can collapse it again and keep your selections with **Save**.

Under **Configure → States → Chat reply animation**, choose a fallback talking scene for the current appearance. The selected expression or talking scene loops across the pages of an AI reply and returns to idle when the final page closes. In 3D, the pet eases into the talking gesture and back to idle. The AI supplies the text. Thinking messages and errors use the normal pose. Reminders, scene dialogue and assigned drag or click interactions take priority. To try one of your assigned random idle scenes immediately, right-click the pet and choose **Action → Play random idle**.

A companion export includes the assets and settings for both appearances. Deleting a companion always removes its configuration. Selecting the wipe checkbox also deletes its locally imported model, sprite files, notes, and conversations. With the box clear, the files and archived history stay on disk. Importing a pack restores the settings saved in that export under a new pet ID. You can still read the old conversation in **Chat history**, or manually reuse retained assets. An export contains no private chat or notes.

The editor separates **Profile**, **Animations**, and **States** into three centered flat rhombus tabs that straddle the extended banner and page boundary while keeping the save controls fixed. The **States** tab puts the core **Idle default** and **Reminder** choices first, followed by grouped viewport interactions. You can optionally assign separate animations for dragging left, dragging right, and holding the companion still while dragging. Drag scenes loop until you release the companion and transition smoothly when the direction changes; the cursor changes while dragging. An unassigned drag state never starts interaction playback, so moving or releasing the companion leaves its current animation alone.

At the bottom of States, use the left-aligned **Advanced interactions** link to reveal its inline **Click interactions** section. The live idle preview is a direct selection surface: drag a rectangle over the part of the viewport that should react, then name the area and choose its animation. Areas are percentage based so they continue to fit after a viewport resize, and they are evaluated in displayed order when they overlap. An assigned click scene plays once before the companion returns to its normal state. For a 3D click scene, any configured speech finishes first. Click handling is inactive until at least one area has an assigned animation; clicks outside an assigned area, or on an area without an animation, leave the current idle animation alone.

A 2D companion must have at least one animation source and an idle-default assignment before it can be saved. Every other state is optional. When no reminder scene is assigned, reminders use the idle-default animation without replaying its dialogue or voice.

## Animation registries

An animation can use an animated GIF, a single static image, or an ordered PNG sequence. PNG sequences give the best control over transparency and timing. One PNG is valid as a static scene.

For a PNG sequence you can reorder or remove frames, set each duration in seconds, apply one duration to all frames, and preview the result. Use consistently sized canvases and align the character to the same baseline to prevent visible shifts between scenes. The overlay scales the composed character to fit the configured viewport height while preserving aspect ratio.

For both 2D and 3D scene dialogue, **leave the line’s voice file empty** to have the pet read its text using **Configure → Profile → Voice**. It speaks the authored text with the configured nickname substituted for `{{user}}`. This works with Chat disabled. Attach a recording to override the profile voice for that line. Choosing **Off** in Profile keeps unrecorded lines text-only and still plays attached recordings.

A spoken line advances when its audio ends. A timed 3D cue can interrupt it. If the assigned recording or profile voice is unavailable, the bubble uses the scene’s configured reading speed. During push-to-talk, unrecorded lines use reading time and recordings are muted. For a PNG sequence, **Start dialog at** delays the first line until the selected animation frame is displayed. This control remains hidden until at least one PNG frame is imported.

For PNG sequences, **Set the frames to loop** appears after at least one frame is imported and applies whenever that scene loops, whether it is used for idle, reminders, or dialogue. Choose:

- **Whole animation** to repeat every frame;
- **Selected range** to play the preceding intro once, repeat the selected range, then play the remaining outro once when the dialogue or state ends;
- **One frame** to play the intro once, hold the selected pose, then play any remaining outro.

These individual-frame controls and their runtime effects apply only to PNG sequences. Switching a registry between PNG frames and a GIF or static image resets the loop selection to the whole animation and resets dialogue to start at the first frame. Desktop Familiar also ignores stale individual-frame values found on a non-PNG scene. The **Loop** playback option ends a transient scene when its dialogue ends. Idle-default and reminder-state scenes continue looping after their dialogue finishes and stop only when their state changes.

State-assignment guidance is available from the question-mark icon beside the relevant label. Its tooltip opens immediately on hover, keeping the assignment grid compact without removing the explanation.

## Runtime states and dialogue

When a companion appears, Desktop Familiar prepares the first frame before showing the overlay, so a frame retained from the previous state is never flashed. Its optional start scene then plays once before Desktop Familiar switches to idle. A reminder that reveals a Focus-hidden companion starts directly in the reminder state.

When you open **Configure** for a pet with a model or an assigned 2D idle, the window opens first. A centered, rounded bar in your theme colour shows that its preview is loading. You can go back or close the window during loading. When the preview is ready, Profile appears. If the asset cannot be opened, Profile still opens so you can replace it.

For 3D pets, Desktop Familiar prepares each clip when it first plays. The separate animation preview loads when you first view it. Entering Focus stops playback and keeps the selected model in memory. Leaving Focus can replay the entrance immediately if the same model is still selected and its file is unchanged.

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

A reminder that expires while Desktop Familiar is running keeps its expired status when you reopen the same library, even weeks later. Its status is saved independently of pet configuration.

The calendar is currently stored only in Desktop Familiar. It does not synchronize with Windows, Outlook, Google Calendar, or other calendar accounts.

## Settings

- **General** controls startup at Windows sign-in. **App updates** shows your running version and checks the official stable releases when you open Settings. Use **Check for updates** to check again. Choose **Download update** and save open edits before applying it. An installed copy offers **Close app and run setup** to update the installation. A portable copy offers **Restart and update** to replace its executable in the current folder. If the release only has the other package type, **Save a copy…** downloads it separately. Development builds also offer **Save a copy…**. Closing Settings discards a staged update. Installed addons have their own update controls.
- **Addons**: the **Available** tab lists compatible first-party addons. When its catalogue is empty, opening Addons checks the pinned first-party catalogue automatically; use **Check for updates** to refresh it at any time. Select an addon to see its version, description, and install control; if none can be installed, the page says **No available addons**. Installed addons move to the **Installed** tab, which provides update and uninstall controls. An installation disabled by an earlier app version also provides **Enable** so it can be restored. A package is downloaded only after you choose an install or update, is verified before it is registered, then offers to restart Desktop Familiar now so the runtime can activate the change. Restart waits for the old process to exit before the replacement instance starts. Uninstalling removes its card and settings section immediately, asks whether to delete that addon's saved data as well, then offers the same restart. The initial channel accepts only Desktop Familiar GitHub Release packages; no third-party repository can be added.
- **Addons > Auto clicker**: first install and enable the Auto Clicker from the **Available** tab, then restart Desktop Familiar. Its settings are kept in the same installed-addon card, with the enable switch beside its title. When disabled, both hotkeys are unregistered and **Click the screen for me** is removed from the viewport menu. When enabled, choose distinct **Start/stop** and **Input mode** hotkeys, plus a minimum and maximum interval in milliseconds or seconds; each pause is chosen randomly from that range. Saving interval, hotkey, or input-mode changes keeps an active click session running; new intervals and input modes take effect immediately without selecting the area again. If Windows cannot resolve that area's background window, the current session keeps its existing mode and the saved mode applies on the next start. If Windows cannot register either configured hotkey at startup, the companion reports the conflict and the card keeps a warning visible so you can choose and save different shortcuts. Raising the minimum above the maximum automatically raises the maximum to match. Pressing the start/stop hotkey opens a full-screen target picker; drag to select the screen area where each click should land randomly, or click for a single fixed point. Press it again to stop, or press Esc to cancel target selection. The target area is not saved, so every start requires an intentional new selection. The companion briefly says it is taking over or has stopped clicking, temporarily pausing any current scene dialogue without changing the animation. **Send background clicks** is off by default; when enabled it attempts to send legacy click messages directly to the window beneath the selected area without moving your cursor. The input-mode hotkey switches a running session immediately while retaining the selected area; while idle, it selects the mode for the next start. Some applications ignore background messages. Do not use this mode with games unless their rules explicitly permit it, since it can violate game rules or trigger anti-cheat.
- **User**: save your birthday for user-birthday animation states, or clear it.
- **Companion** lets you set the character viewport width and height (120–1200px) and the interval between random idle animations. Dialogue wraps in a bubble 240–480px wide, centered above the character. The bubble grows to fit scene dialogue and reminders. Long AI replies can span several bubbles.
- **Theme**: select one of the three built-in themes, or create, edit, name, and delete user themes. New themes can start from any existing palette. Built-in themes cannot be edited or deleted.

The built-in **Crimson Moon** theme uses warm white and deep crimson as its main colours, with restrained gold and charcoal accents. Its Settings sidebar uses a darker-crimson hover, while the selected row uses charcoal with a gold marker for clear state contrast. **Eternal Sea** is the cool blue alternative. **Stella Dark** uses deep moonlit violet surfaces, pale lavender text, and warm starlight-gold secondary accents. Existing profiles receive Stella Dark automatically without changing their selected theme.

Theme edits apply immediately to windows, inputs, buttons, the overlay menu, and the notification-area menu. Page titles, event details, guidance, units, selected dropdown values, and dropdown items use the text colour assigned to their containing page, card, or input surface. Standard text inputs share the same height and inner spacing across event details, companion configuration, and settings. Outline buttons use the text-on-background role from the surface that contains them for their text and border, then invert that surface's background and text roles on hover.

## Files and troubleshooting

Imported files and companion-bundle assets are copied beneath `%LOCALAPPDATA%\DesktopFamiliar\Assets`. Schema 16 saves its configuration in `%LOCALAPPDATA%\DesktopFamiliar\Library\desktop-familiar.v16.json`.

Opening or closing the app leaves the saved library intact. On the first run with a different format, the app reads the settings it understands. Your next saved change creates a library for that format. An older installation can keep using its original `desktop-familiar.json`. Later changes in those separate copies stay with the version that saved them. Actions such as dragging the pet to save its position or acknowledging a reminder also count as saved changes.

Each launch replaces `%LOCALAPPDATA%\DesktopFamiliar\Logs\latest.log`. If a fatal error stops Desktop Familiar, a report window displays that log and offers **Copy report**. Review the text before posting it publicly because local paths can appear in the report. A failed configuration save is recoverable: Desktop Familiar restores the previous saved state and shows a warning.

Wording is loaded from `Resources\Strings\en.json`. An installed copy can override individual entries in `%LOCALAPPDATA%\DesktopFamiliar\Localization\custom.en.json`; restart Desktop Familiar after editing it and keep JSON keys and placeholders such as `{0}` intact.
