# APEIRON Global Mute Overlay

**0.1.0-beta.4 RU** is a Russian test build for Windows 10/11 x64. MIT.
The English executable for this iteration has not been built or validated.

This update unifies key blocking: the shared switch blocks all enabled microphone hotkeys, without individual Toggle/Hold blocking preferences. Disabled actions do not reserve their keys. Fresh settings default to blocking off; an existing saved master value is preserved.

The blocking command accepts one ordinary keyboard key, a Ctrl/Alt/Shift combination, or simultaneous left+right Shift, Alt or Ctrl. The second side completes a pair; both sides must be released before another pair command. The first modifier retains its ordinary application behavior. A single command key must differ from both mic bindings; modifier conflicts with enabled actions are rejected.

Commands change only key blocking, preserving active Hold and microphone state. Existing modifier combinations, microphone selection, visuals and placement survive migration. Save settings to persist the command and master state. Pause remains session-only. The compact UI and memory-only Hold recovery from beta.3 remain.

[Russian documentation](README_RU.md) · [Windows checklist](docs/WINDOWS_CHECKLIST_RU.txt) · [Test report](TEST_REPORT.txt) · [Building](BUILDING.md).

Automated checks use a fake audio backend. The RU layout was inspected under Mono/Linux. Real Windows audio, input hooks, shortcut capture, UAC and games remain unverified here. This development branch is not a published release.

[Releases](https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay/releases) · [Website](https://apeiron.framer.media/) · [Support](https://www.paypal.com/paypalme/StanislavMed)
