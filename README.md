# APEIRON Global Mute Overlay

A Windows microphone mute utility with global hotkeys and a configurable desktop indicator. Free and open source under MIT.

Version **0.1.0-beta.2**, Windows 10/11 x64. [Русское описание](README_RU.md).

## Language downloads

Choose the archive marked **EN** or **RU**. Each executable has a fixed interface language; there is no language selector. Both builds use the same settings and single-instance identity.

- `APEIRON-Global-Mute-Overlay-v0.1.0-beta.2-EN-win-x64.zip`
- `APEIRON-Global-Mute-Overlay-v0.1.0-beta.2-RU-win-x64.zip`

This branch prepares beta.2. Its assets have not been published as a GitHub release.
[Published releases](https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay/releases).

## Features

- Toggle the mute state of one selected Windows recording device.
- Hold a key to temporarily reverse its state; release restores the original state.
- Configure the indicator's position, shape, size, opacity, colors and display rules.
- Set individual Toggle/Hold key blocking and change the shared blocking option from settings, tray, or a configurable modifier combination.
- Pause hotkeys while keeping monitoring, the indicator and manual controls active.
- Optional recovery file, Administrator restart prompt and Windows startup.

## New blocking combination

Open **Controls - Key blocking shortcut - Assign combination**. Press Ctrl, Alt or Shift with another keyboard key, release all keys and Save settings. It starts unassigned.

The combination only toggles **Block keys in other apps**. Microphone actions keep working when blocking is off, and individual preferences remain saved. The combination itself remains reserved in other apps even with shared blocking off; modifiers pass through normally. Pause hotkeys also pauses the command. Auto-repeat produces no extra toggles.

The base key may match Toggle/Hold: the full combination has priority over the mic action. Its modifiers cannot also be enabled standalone mic keys. Windows-key combinations, Esc and Ctrl+Alt+Delete are not supported.

## Updating and setup

Save settings and Exit the old version from its tray icon. Extract the chosen language archive to a permanent folder and run the EXE. Existing settings are retained. If the EXE moved, Save settings to update its startup path.

Choose a microphone, assign keys and Save settings. Defaults are F9 Toggle / F10 Hold. The new blocking combination has no default. Restart clears hotkey pause and enables shared blocking with the saved individual choices.

See [English instructions](docs/README_EN.txt), [Russian instructions](docs/README_RU.txt), [Windows checks](docs/WINDOWS_CHECKLIST_RU.txt) and [building](BUILDING.md).

## Beta limits and feedback

The app controls Windows endpoint mute without recording audio. App-specific mutes and hardware switches not exposed to Windows are not reported. Exclusive audio access may bypass software mute; exclusive fullscreen may hide the indicator. Global input depends on privileges and game restrictions. These builds are unsigned.

Portable tests use a fake audio backend. Real Windows audio, hooks/UAC, games and GUI layout still require Windows validation for beta.2. See [TEST_REPORT.txt](TEST_REPORT.txt). When reporting an issue, include actions, expected/actual results, Windows version, microphone/game and General - Copy diagnostics when possible.

[Website](https://apeiron.framer.media/) | [Support](https://www.paypal.com/paypalme/StanislavMed)
