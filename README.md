# APEIRON Global Mute Overlay

**0.1.0-beta.5** - separate RU and EN builds for Windows 10/11 x64. MIT.
The archive filename identifies the language; no in-app language switch.

## Beta.5 changes

- Simplified pause wording in Controls and the tray. Clear the checkbox to resume assigned-key commands.
- Pause details and its automatic reset at restart are in the tooltip.
- Removed the unsaved-changes label and its tracking. Save settings still persists your choices.
- Both builds have matching features and share settings.

## Controls

Toggle changes the selected Windows device's mute state. Hold temporarily reverses it and restores the original state on release.
One master blocking switch applies to all enabled microphone hotkeys. Disabled actions do not reserve keys. Blocking defaults off without a saved choice.
The blocking command supports one ordinary key, Ctrl/Alt/Shift + key, or both sides of Shift, Alt or Ctrl together. A pair fires on its second modifier and requires both sides released before repeating. The first modifier retains its normal app behavior.

The compact appearance UI, display-rule dropdowns, 0-95% transparency, color swatches and memory-only Hold recovery remain. New Holds do not create a recovery file. No extra tray icons.

## Update and validation

Exit the old version from its tray, extract your chosen archive and run the EXE. Your device, bindings and appearance are preserved. Use Save settings to keep changes. Pause clears at restart.

Automated checks passed separately against both production EXEs. Layouts were inspected under Mono/Linux. Real Windows audio, global hooks, hotkey capture, UAC and games were not exercised here.

[English instructions](docs/README_EN.txt) · [RU](docs/README_RU.txt) · [Windows checklist EN](docs/WINDOWS_CHECKLIST_EN.txt) · [RU](docs/WINDOWS_CHECKLIST_RU.txt) · [Test report](TEST_REPORT.txt) · [Building](BUILDING.md).

This branch contains the prepared builds' source. [Published releases](https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay/releases).

[Website](https://apeiron.framer.media/) | [Support](https://www.paypal.com/paypalme/StanislavMed)
