<p align="center">
  <img src="Assets/APEIRON_icon.png" width="128" alt="APEIRON">
</p>

# APEIRON Global Mute Overlay

A Windows microphone-mute utility with global hotkeys and a configurable desktop overlay. Controls one selected Windows recording device; OBS is not required.

**Version:** `v0.1.0-beta.1`  
**Platform:** Windows 10/11 x64  
**Status:** Beta, unsigned

## Download and setup

Download the Windows ZIP from [Releases](https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay/releases), extract it into a permanent folder and run `APEIRON_Global_Mute_Overlay.exe`.

1. In **Microphone**, choose your device and click **Use selected microphone**.
2. In **Controls**, assign keys or use **F9 Toggle / F10 Hold**.
3. Set the overlay's appearance and click **Save settings**.

When updating, exit the previous build from its tray first. Existing device selection, bindings and appearance are retained.

## Controls

- **Toggle** changes the selected microphone's Windows mute.
- **Hold** temporarily applies the opposite state and restores the original when released.
- **Pause hotkeys** stops both hotkey actions while monitoring and manual controls continue.
- **Block keys in other apps** temporarily controls key forwarding. Turning it off keeps Toggle/Hold working and lets their keys reach other apps.
- Individual Toggle/Hold blocking choices remain in **Controls**.

Pause and the shared blocking override last until exit. A new launch clears pause and re-enables shared blocking using saved individual choices.

## Features

- Configurable overlay position, shape, size, opacity and colors.
- Separate display rules for normal changes and Hold.
- One selected device stays fixed when Windows changes its default microphone.
- Tray operation, optional startup and an Administrator restart prompt.
- Hold recovery in memory by default, with optional file recovery.
- Diagnostics for reporting problems.

## Privacy and limitations

The app changes endpoint mute without recording audio or changing gain.
Mic ON describes Windows mute, not the presence of an audio signal.
Separate app mutes and physical switches not exposed to Windows are not reported.
Software mute may be bypassed by exclusive audio access.
Fullscreen games may hide the overlay. Privileges, protected games and other input software can affect hotkey delivery.

This is a beta. Core behavior was checked on the owner's system; compatibility with other devices and games is being refined through feedback. Automated tests use a fake audio backend; they do not replace native Windows testing.

## Report a bug

Use [Issues](https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay/issues). Describe **what you pressed, what you expected, and what happened**. Include Windows version, microphone model and game name when relevant. If possible, attach **General → Copy diagnostics**.

## Source and license

See [BUILDING.md](BUILDING.md) to build and run the portable logic tests.
[MIT License](LICENSE).

## Links

- [Website](https://apeiron.framer.media/)
- [Support via PayPal](https://www.paypal.com/paypalme/StanislavMed)
- [Русская версия](README_RU.md)
