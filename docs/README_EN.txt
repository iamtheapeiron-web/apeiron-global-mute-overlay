APEIRON Global Mute Overlay
v0.1.0-beta.1 | Windows 10 / 11, x64

First beta. Core behavior was checked by the owner on their system.
Device/game compatibility is being refined through feedback.
Additional Windows checks are optional; see WINDOWS_CHECKLIST_RU.txt.

UPDATING
Save settings in the previous build and Exit it from the tray.
Extract into a permanent folder and run the EXE. Your microphone, bindings,
appearance and individual blocking choices remain.
If the EXE moves, Save settings again to update its startup path.

QUICK START
1. Run APEIRON_Global_Mute_Overlay.exe.
2. In Microphone, choose a device and click Use selected microphone.
3. In Controls, assign keys or use F9 Toggle / F10 Hold.
4. In Appearance, set your preferred display and position.
5. Save settings. X hides to the tray; Exit closes the application.
Uses Windows .NET Framework 4.x. No OBS, PowerShell, Python or modules
are required. The application does not record audio.

HOTKEYS AND BLOCKING
Toggle changes the selected endpoint's Windows mute.
Hold temporarily uses the opposite state, then restores the original state.
Toggle is ignored during Hold. External changes during Hold are corrected
back to the temporary state; release returns the original state.

Tray controls, also available in Controls:
- Pause hotkeys stops both actions and passes new presses through.
  Active Hold is restored. Monitoring, overlay, manual mute and previews remain.
- Block keys in other apps is a shared blocking switch.
  Off: both keys pass through; Toggle/Hold actions still work.
  On: the individual Block Toggle / Hold key choices in Controls apply.

The shared switch never overwrites individual choices or changes mute.
Individual choices can be edited while paused or while blocking is bypassed.
Pause and shared bypass last until exit. A new launch clears pause and
enables shared blocking using your saved individual preferences.
Save settings persists individual choices, not these two temporary modes.
A press already in progress keeps its original routing through release.
Resuming ignores keys already held until they are fully released.
Primary mouse clicks are never blocked; settings and key capture remain usable.
Punctuation captions use US legends, such as ` / ~ (US); saved bindings stay fixed.
Single keyboard keys and mouse buttons are supported; chords, wheel input
and direct gamepad input are not implemented.

DISPLAY
MUTED means Windows endpoint mute is on. Mic ON means it is off, not that
an audio signal is present. Separate app mutes and a physical switch not exposed
to Windows are not reported. Choose the physical mic to control it before
virtual audio processing.
Normal microphone changes and During Hold Mode have independent rules.
Keep visible while muted stays visible in MUTED; Always show while held
stays visible during Hold. Popup duration controls brief displays and previews.
Release creates no extra popup. Test previews a state without changing audio.
Finish placement completes dragging; Hide/X also completes it.

DEVICE AND RECOVERY
The endpoint stays fixed when the Windows default changes.
When disconnected, live indication hides and the app waits for that endpoint.
Test previews remain available while disconnected.
Hold recovery is kept in memory by default. Release, pause, Hold disable
and normal Exit restore an available endpoint. If unavailable, recovery remains
in memory and is lost if the process exits before restoration.
General - Save Hold recovery to a file retains unfinished recovery between
launches. The file is published before mute changes and removed after restore.
Existing recovery files are honored even if this option is off.
Changing storage mode during Hold does not change mute.
Normal Windows caching does not guarantee power-loss durability.

PRIVILEGES AND STARTUP
General shows current rights and offers Restart as Administrator.
Ask about Administrator rights at startup offers restart once per session
unless elevation is confirmed. Already-elevated launches show [Administrator].
The question can be disabled and saved. Foreground privilege mismatches can
show a banner. UAC cancellation leaves the current instance running.
Administrator restart is blocked while pending recovery exists only in memory;
reconnect the microphone or enable file recovery first.
Start with Windows applies on Save, launching normally in the tray.
An enabled startup question may open settings. A second instance is blocked.

DIAGNOSTICS AND LIMITS
General - Copy diagnostics includes state and operation timing.
Settings folder: %LOCALAPPDATA%\APEIRON_Global_Mute_Overlay
OBS Overlay settings remain separate.
Controls one recording endpoint. Software mute may be bypassed by exclusive
audio access. Exclusive fullscreen may hide the overlay; use windowed/borderless.
Hotkey delivery depends on privileges and game restrictions. The EXE is unsigned.
Source, checksums and verification results are included. Portable tests use
a fake audio backend; actual Windows audio, hooks/UAC and GUI were not executed here.
License: MIT, see LICENSE.

REPORTING A BUG
Describe what you pressed, what you expected and what happened.
Include Windows version, microphone model and game name when relevant.
If possible, attach General - Copy diagnostics.

Website: https://apeiron.framer.media/
PayPal: https://www.paypal.com/paypalme/StanislavMed
