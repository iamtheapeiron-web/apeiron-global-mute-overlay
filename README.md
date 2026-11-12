# APEIRON Global Mute Overlay

Windows microphone mute hotkeys and a configurable desktop indicator. MIT licensed, C# WinForms, Windows 10/11 x64.

## Current development build

**0.1.0-beta.3 RU** prepares the agreed interface changes. Only the Russian executable was built and tested in this iteration. [Русское описание](README_RU.md).

- Shared key blocking starts off when there is no saved choice. Save settings now persists its current state; individual Toggle/Hold preferences are preserved.
- Compact blocking controls include the effective status, an assignable modifier combination and its independent enable checkbox.
- Display rules move to Appearance as two dropdowns, retaining all five normal rules and three Hold rules.
- Transparency uses a slider and numeric field, 0-95%, with the original stored opacity retained for visual compatibility.
- Color swatches, bordered white swatch, explicit previews and saved drag/corner positioning replace raw coordinates.
- A persistent unsaved-changes marker distinguishes applied settings from saved settings.
- New Hold recovery is memory-only. A legacy file is only read and retired after restoration; it cannot enable new recovery writes.
- The US-keyboard-layout explanation is absent from RU and retained in the English source.

The microphone tab's content and the core Toggle/Hold behavior are retained. Pause remains session-only. No extra tray status icons were added.

This branch contains source for review; beta.3 has not been published as a GitHub release. [Published releases](https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay/releases).

See [instructions](docs/README_RU.txt), [Windows checklist](docs/WINDOWS_CHECKLIST_RU.txt), [building](BUILDING.md) and [verification](TEST_REPORT.txt).

The executable is unsigned. Windows endpoint mute can be bypassed by exclusive audio access. Exclusive fullscreen may hide the indicator. Real Windows audio, hooks, UAC, startup and games need Windows validation.

[Website](https://apeiron.framer.media/) | [Support](https://www.paypal.com/paypalme/StanislavMed)
