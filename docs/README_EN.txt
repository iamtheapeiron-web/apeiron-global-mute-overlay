APEIRON Global Mute Overlay
v0.1.0-beta.2 | EN | Windows 10 / 11, x64

This build has an English interface. There is no in-app language switch.
Download the archive marked RU for Russian. Both builds share settings
and cannot run simultaneously.

UPDATING
Save settings in the previous version, then Exit it from the tray.
Extract this archive to a permanent folder and run APEIRON_Global_Mute_Overlay.exe.
Your microphone, key bindings, appearance and individual blocking choices remain.
If you move the EXE, Save settings again to update its startup path.

QUICK START
1. In Microphone, select a device and click Use selected microphone.
2. In Controls, assign keys or use the defaults: F9 Toggle / F10 Hold.
3. In Appearance, choose your display and position.
4. Save settings. X hides the window to the tray; Exit closes the app.
Uses Windows .NET Framework 4.x. No Python, PowerShell or modules are needed.
The application does not record audio.

NEW: KEY BLOCKING COMBINATION
Controls - Key blocking shortcut - Assign combination.
Release all keys, then press Ctrl, Alt or Shift with a keyboard key,
for example Ctrl + Shift + B. Multiple modifiers are supported.
Release all keys to confirm, then Save settings.
The shortcut is enabled automatically after assignment. It starts unassigned
so the app does not introduce an unexpected global shortcut.

The combination toggles the shared Block keys in other apps option.
It preserves your individual Toggle/Hold blocking choices and does not change mute.
Toggle and Hold still control the microphone while shared blocking is off.
The combination itself remains reserved for this command in other apps even
with shared blocking off. Its modifier keys pass through normally.
Auto-repeat never toggles the option repeatedly.
Its main key may match Toggle or Hold: the full combination only changes blocking.
Its Ctrl/Alt/Shift modifiers cannot also be enabled standalone mic hotkeys.
Windows-key combinations, Esc and Ctrl+Alt+Delete cannot be assigned.
Choose a combination that does not conflict with system or game shortcuts.

HOTKEYS AND TRAY
Toggle changes the selected microphone's Windows mute state.
Hold temporarily reverses that state; release restores the original state.
Toggle is ignored during Hold. External changes during Hold are corrected
to the temporary state; release returns to the original state.
Pause hotkeys pauses Toggle, Hold and the new combination. Active Hold is restored.
Monitoring, overlay and manual controls remain available.
Block keys in other apps, in the tray or Controls:
- On: individual Block Toggle / Hold key choices apply.
- Off: both mic keys pass through to other apps.
After restarting, hotkeys resume and shared blocking is on.
These two temporary modes are not saved. The new shortcut's binding and
enabled setting, and individual mic-key blocking preferences, are saved.
An existing physical press retains its original route until release.
Resuming waits for already-held keys to be released first.
Primary mouse clicks are never blocked. Settings and key capture stay usable.
The new command is disabled during key capture.
Microphone actions accept individual keyboard keys or mouse buttons;
modifier combinations are currently supported only for the shared blocking command.
Mouse wheel and direct gamepad input are not supported.
Punctuation captions use US legends without changing your saved key binding.

DISPLAY
MUTED means Windows endpoint mute is on. Mic ON means it is off;
it does not measure the audio signal. Separate app mutes and physical switches
not exposed to Windows are not reported. Select a physical mic to control it
before virtual processing. Normal and Hold display rules are independent.
Release does not create an extra popup. Test previews do not change the microphone.
After dragging, click the green Finish placement button; Hide/X also finishes.

DEVICE AND RECOVERY
The app keeps one selected endpoint when the Windows default changes.
After disconnection, live indication hides and it waits for that same endpoint.
Test previews remain available while disconnected.
Hold recovery uses memory by default and does not write a recovery file.
Release, pause, disabling Hold and normal Exit restore an available device.
If unavailable, recovery waits in memory and is lost if the process exits.
General - Save Hold recovery to a file retains it after a crash or restart.
Old recovery files are honored even with the option off. Changing the option
does not change mute. Windows caching does not guarantee power-loss durability.
The new combination does not write a file on every press.

PRIVILEGES AND STARTUP
General shows current rights and offers Restart as Administrator.
The optional startup question appears once per session unless elevation is confirmed.
Foreground privilege mismatches can still show a warning.
Cancelling Windows UAC leaves the current instance running.
If pending recovery exists only in memory, reconnect the mic or enable file
recovery before restarting. Windows startup applies on Save and uses normal rights.

DIAGNOSTICS AND LIMITS
General - Copy diagnostics includes state and operation timing.
Settings: %LOCALAPPDATA%\APEIRON_Global_Mute_Overlay
Exclusive audio access can bypass software mute. Exclusive fullscreen can hide
the overlay. Global input depends on application privileges and game restrictions.
This is an unsigned beta. Automated checks use a fake audio backend.
This build still needs Windows validation of real audio, games, global hooks,
UAC and GUI text layout. Describe your actions, expected result and actual result
when reporting an issue, and include Windows version, microphone, game and diagnostics.

Beta.2 source:
https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay/tree/codex/beta2-ru-en-shortcut
Repository: https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay
License: MIT, see LICENSE.txt.
Website: https://apeiron.framer.media/
Support: https://www.paypal.com/paypalme/StanislavMed
