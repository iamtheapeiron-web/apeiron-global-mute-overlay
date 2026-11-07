APEIRON Global Mute Overlay - source package
v0.1.0-beta.1 | Windows file/build revision 0.1.0.8

Source/: standalone C# WinForms application targeting .NET Framework 4.x, x64.
Assets/: original APEIRON Windows icon and About image from the OBS project.
Tests/: compiled-production controller tests and a Python PE/resource inspector.

Windows build (no additional NuGet packages):
  build.cmd
Windows build and controller tests:
  build.cmd test

Optional Python build runner:
  python build.py
On Linux, provide a Mono installation prefix with usr/bin/mono-sgen,
usr/lib/mono/4.5/mcs.exe, framework GAC assemblies and etc/mono/config:
  python3 build.py --mono-root /absolute/prefix --tests
Portable builds still produce a Windows x64 .NET EXE.

Native.cs reuses the existing APEIRON OBS Overlay input hooks and overlay window.
The new app has no OBS/WebSocket/PowerShell runtime or launcher dependency.
CoreAudio.cs contains direct MMDevice / EndpointVolume COM interop.
It enumerates active capture devices, pins one opaque endpoint ID, checks
GetState during periodic monitoring and uses only endpoint SetMute / GetMute for control.
External state is polled every approximately 150 ms while the UI is running.
No audio samples are captured, and volume/gain levels are not changed.

Controller.cs owns the normal/Hold states, timed overlay layer, preview layer,
placement state and exact-device restoration. Its clock, audio adapter and
recovery storage are injected. Tests reference the compiled delivery EXE.
GetMute after SetMute must confirm a write before the indicator claims success.
With optional file recovery enabled, recovery is serialized, flushed to the OS,
closed and atomically published before Hold modifies the device. The default
memory mode writes no recovery file during ordinary Hold. Normal Windows file caching avoids a forced
hardware-cache flush per press; this covers an application crash, not guaranteed
power-loss durability. Settings Save still requests a durable disk flush.
The next startup
prioritizes an unfinished recovery over an older settings device selection.
Pending recovery prevents switching to a different device until restored.

Settings are separate from the OBS application's settings. Startup is a named
HKCU Run value pointing to this EXE with --tray. No other startup entries are
changed. A per-user/session mutex prevents a second Global Mute instance.

Microsoft primary references used during implementation:
https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume
https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmute
https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdeviceenumerator
https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdevice-getid
https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdevice-activate
https://learn.microsoft.com/en-us/windows/win32/coreaudio/pkey-device-friendlyname

See TEST_REPORT.txt for the boundary between automated checks and unexecuted
Windows integration checks. No real Windows microphone test was performed here.

Build 2 COM identity cross-check source:
https://github.com/microsoft/psi/blob/master/Sources/Audio/Microsoft.Psi.Audio.Windows/ComInterop/Guids.cs

Build 3 response path:
- Hook callbacks only record edges and post a coalesced BeginInvoke wake-up.
  No disk or audio work runs inside a low-level hook callback.
- InputMailbox preserves hook state while GetAsyncKeyState can still be stale.
  The 20 ms timer is a missed-event fallback and controls preview expiry.
- The overlay is painted after an action before periodic audio status queries.
- Device enumeration owns its own COM objects on a ThreadPool MTA thread.
- GetState validates the endpoint during periodic monitoring; hotkeys read mute,
  set mute and confirm mute without repeated GetState calls.
- General / Copy diagnostics reports queue age, action and per-operation times.
  SetMute and confirmation are still synchronous Windows calls; a slow driver
  can still delay those calls. No real Windows latency benchmark was run here.

References for the event-order and file-buffering decisions:
https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=netframework-4.8.1

Build 4 storage and privilege behavior:
Settings schema 2 adds FileRecoveryEnabled=false and OfferAdministratorRestart=true.
OptionalRestoreStore retains recovery in memory, loads an existing disk record once,
and calls disk Save/Clear only when file mode is enabled or a legacy record needs cleanup.
A failed mode switch preserves its previous setting. Backup cleanup precedes deleting
an active primary journal, so a backup-deletion error cannot silently remove its recovery.
Existing schema-1 bindings, including VK_OEM_3, remain unchanged.
The file serializer is warmed only when file recovery is enabled or a stored file is read.

ElevationNotice is a portable policy tested independently of Windows token calls.
A ThreadPool worker samples the foreground PID and TokenElevation about once a second.
Unknown tokens are nullable; only known non-elevated self + elevated foreground
produces a mismatch. Opening the overlay retains the last external-app warning.
A modal question is offered once per session when enabled and controls are active,
postponed during Hold/capture/placement/modal work. A nonmodal banner remains.
Administrator restart restores Hold, checks pending recovery is retained, saves settings,
then requests runas with --wait-for the old process. UAC cancellation retains the app.
The existing asInvoker manifest and normal HKCU startup are preserved.

Windows API references:
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid
https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-gettokeninformation

Build 5 input and startup behavior:
Settings schema 3 adds BlockHoldKey=true and BlockToggleKey=false. Existing
bindings are retained. KeySuppression latches a down's routing choice until
its matching up, including repeats, rebinds, option changes and focus changes.
The hook records an input edge and returns nonzero only for consumed events.
Foreground HWND/PID checks are local user32 calls; no COM or file I/O occurs in
a hook callback. Settings/capture contexts bypass new suppression decisions.
Primary mouse buttons are never swallowed; keyboard and XBUTTON1/2 can be.
Suppressed-down state is authoritative for IsDown because GetAsyncKeyState may
remain up for swallowed input; pass-through input retains physical fallback.
As with other hook-based capture, native key-up delivery still requires Windows
integration checking; dropped hooks, secure desktops and protected games can
limit it. A detected privilege mismatch restores an active Hold.

The startup prompt is explicitly queued from Form.Shown and has a separate
policy from mismatch detection. Non-elevated and unknown self state can offer
restart; confirmed elevated self skips it. A single shared once-per-session
latch prevents repeating questions. The saved OfferAdministratorRestart flag
controls the question; disabling all control modes also suppresses it.
The title displays [Administrator] when the process token confirms elevation.

Suppression API references:
https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate

Build 6 tray controls and pause:
InputRouter owns a session-only Paused flag, separate from serialized Settings.
Pausing releases Hold immediately and keeps its pending recovery policy on failure.
While paused, Tick records held-state boundaries without invoking audio actions.
Effective ToggleEnabled/HoldEnabled feed both routing and native suppression.
Resuming seeds currently held keys so they cannot create a stale press.
KeySuppression keeps an existing press's routing latched until its matching up;
new presses during pause pass through regardless of saved blocking choices.
No additional recovery-file writes or polling timers are introduced by pause.

Tray blocking items toggle the Controls checkboxes, using the same settings and
configuration path. They may be edited while paused and require Save settings
for persistence. Primary mouse bindings and disabled modes keep their existing
blocking restrictions. Tray checkmarks refresh on opening/configuration changes.
Controls exposes pause too; tray tooltip and Copy diagnostics report it.
Monitoring, preview and manual mute remain independent of the hotkey pause.
Automatic elevation questions are suppressed while paused; the banner remains.

RC1 master blocking and key captions:
InputRouter.KeyBlockingEnabled is session-only and defaults true. BlockToggleKey
and BlockHoldKey combine the master, pause, individual enable/blocking preferences
and CanBlock guard. MainForm.SyncInput passes these effective flags to native input.
Changing master does not write settings/recovery or directly modify microphone mute.
It reconfigures the existing hook policy and synchronizes keys currently held.
Existing KeySuppression latches a current press's route through its matching up.
The new Controls summary explains whether blocking is paused, bypassed, active
for one/both hotkeys or has no eligible configured key. The tray has one checked
master item, one Pause item, and the existing static brand icon.

KeyNames formats UI captions without Windows native calls. Common punctuation
uses explicitly marked US legends rather than claiming to translate a layout.
Existing numeric key identities are retained in settings and input routing.
Reference:
https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes

Version naming:
The public version and informational/ProductVersion text are 0.1.0-beta.1.
Assembly/FileVersion, the native fixed version and manifest use numeric 0.1.0.8
to keep build revision ordering after the six alpha builds and RC1. Resource and compiled
assembly checks verify both forms. The prerelease resource flag remains set.

The beta changes RC1 naming/documentation, with no changes to runtime behavior.
See LICENSE for MIT terms. The release kit separately records the owner's
publication/license review; this source package does not publish a repository.
