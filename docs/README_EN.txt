APEIRON Global Mute Overlay
v0.1.0-beta.5 | EN | Windows 10 / 11, 64-bit

English build. A separate Russian archive has RU in its filename.
The downloaded build determines the language; there is no language switch.
Both builds share settings and only one instance runs at a time.

UPDATING
Save settings in the old version, then use Exit in its tray menu.
Extract this archive to a permanent folder and run the EXE.
Your microphone, key bindings, colors, shape, transparency and position are preserved.
An existing Ctrl/Alt/Shift blocking combination is preserved if it has no conflict.
The saved master blocking state is preserved; without a saved choice it defaults off.
Individual Toggle/Hold blocking preferences have been removed.
Master blocking now applies to every enabled microphone hotkey, regardless of
old individual choices. Save settings again if you move the EXE and use startup.

FIRST RUN
On Microphone, choose a recording device and click Use selected microphone.
On Controls, assign your keys. Defaults: F9 Toggle, F10 Hold.
Set up the indicator and its display rules on Appearance, then Save settings.
Closing the window hides it in the tray. Exit closes the program.
Uses .NET Framework 4.x. The app does not record audio.

TOGGLE AND HOLD
Toggle changes the Windows mute state of the selected microphone.
Hold temporarily uses the opposite state; releasing the key restores the
original state. Toggle is ignored while Hold is active.

ONE KEY BLOCKING SWITCH
Block microphone keys in other apps:
- On: the keys of all enabled microphone actions control the microphone
  without triggering actions in other apps.
- Off: the same keys still control the microphone and also work in other apps.
Disabling an action also stops reserving its key. Primary mouse clicks are
never blocked. Blocking does not interfere with input in the app's own window.
The status below the switch reports the effective result.
A key already down keeps its original routing until release.

BLOCKING HOTKEY
In the same section, click Assign..., release all keys, then press:
- one ordinary keyboard key, such as F8;
- Ctrl, Alt or Shift with a key, such as Ctrl+Shift+B;
- both Left and Right Shift, both Left and Right Alt, or both Left and Right Ctrl.
Release all keys to confirm. The binding starts unassigned.
Assigning a binding enables Hotkey. This checkbox can disable the command
without removing its binding. Change... reassigns it; Clear removes it.
The command toggles master blocking without changing the microphone state.
Keeping a key down does not repeat the command.

A modifier pair fires when its second side is pressed. The first modifier
keeps its ordinary action in the foreground app or game. Release BOTH sides
before repeating the command. This means two physical sides held together,
not double-tapping one modifier. Arbitrary ordinary-key pairs are not supported.

A single command key must differ from both stored microphone bindings,
including disabled actions. A modifier combination may share its ordinary
completing key with a microphone action: the full combination changes only
blocking, while the plain key controls the mic. A command's modifiers or pair
members cannot also be enabled standalone microphone keys. Conflicts are rejected.
A single modifier, Esc, Windows combinations and Ctrl+Alt+Delete cannot be assigned.
Mouse wheel and direct gamepad input are not supported.

The blocking command remains reserved in other apps even with master blocking off.
Only the completing key of a combination or pair is consumed. The first modifier
is not delayed or reserved. Pick a command that suits your other software.

PAUSE AND SAVING
Pause operation in Controls and the tray pauses the assigned microphone keys
and the blocking command. The app will stop responding to assigned keys.
Clear this checkbox to resume.
An active Hold is restored. Monitoring, the indicator and the microphone button
remain available; these details are in the pause tooltip.
Pause clears at restart.
Changes apply immediately. Use Save settings to keep your blocking state,
command assignment and other settings after restart.
The unsaved-changes label has been removed. Pause and preview do not change
saved settings.

INDICATOR AND RECOVERY
Transparency runs from 0% (fully visible) to 95% (nearly transparent).
Preview does not change the microphone state. Place the indicator by dragging
or choosing a corner/monitor. All previous brief and persistent display rules remain.
Hold stores the original state in memory, without a per-press recovery-file write.
Release, pause and normal exit restore an available microphone. If disconnected,
the app waits for the same device. After a crash, check the microphone manually.
An old recovery file is handled during upgrade; new Holds do not create one.

PRIVILEGES AND LIMITS
General contains Administrator restart preferences, privilege status and startup.
For elevated games, run the app with suitable privileges. Startup changes apply
when saving settings. Reconnect a microphone with pending memory recovery before
restarting the app. Only one instance can run.
The indicator reports the device's Windows mute state, not audio activity or
separate in-app mute states. A hardware switch may not report its state to Windows.
Exclusive audio access can bypass software mute. Exclusive fullscreen may hide
the desktop indicator. This is an unsigned beta.

Automated tests use a fake audio backend; the layout was inspected under Mono/Linux.
Real Windows audio, global hooks, hotkey capture, UAC and games were not tested here.
Short Windows checklist:
https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay/blob/main/docs/WINDOWS_CHECKLIST_EN.txt
For feedback, include your actions, expected/actual result, Windows, device and game,
and, if possible, General - Copy diagnostics.
Settings: %LOCALAPPDATA%\APEIRON_Global_Mute_Overlay

Beta.5 source:
https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay/tree/codex/beta5-ru-en
MIT license: LICENSE.txt
Website: https://apeiron.framer.media/
Support: https://www.paypal.com/paypalme/StanislavMed
