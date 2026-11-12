# Changelog

## 0.1.0-beta.2

- Separate fixed-language RU and EN downloads with no language selector.
- Configurable Ctrl/Alt/Shift combination toggles shared mic-key blocking.
- Combination starts unassigned and persists on Save settings.
- Exact modifiers and one command per press; shared Toggle/Hold base keys never perform a mic action for the full combination.
- Pause also pauses the command. Master changes preserve queued Hold release and existing down/up routes.
- Settings schema 4 preserves previous device, bindings, appearance and individual blocking choices.
- UTF-8 Windows builds and localized shape captions with stable settings IDs.


## v0.1.0-beta.1

First public beta prepared from the tested RC1 behavior.

- Control one selected Windows microphone without OBS.
- Toggle and temporary Hold with original-state restoration.
- Configurable overlay and separate normal/Hold display rules.
- Shared key-blocking switch plus individual Toggle/Hold choices.
- Pause hotkeys without stopping monitoring.
- Optional file recovery, Administrator prompt and Windows startup.
- Readable key captions and copyable diagnostics.

Owner feedback found no blockers in the checked RC1 scenarios. Other device/game compatibility remains open to feedback.
