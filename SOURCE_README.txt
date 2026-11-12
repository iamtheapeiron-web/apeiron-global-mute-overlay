APEIRON Global Mute Overlay v0.1.0-beta.2
C# WinForms / .NET Framework 4.x / Windows x64 / MIT.

build.cmd       builds EN and RU executables.
build.cmd test  builds both and runs the production-assembly tests for each.
See BUILDING.md for the Python/portable runner.

Source/Ui.cs fixes the language at compile time with APEIRON_RU.
Stored device IDs, key codes and shape IDs are shared between languages.
Source/BlockingShortcut.cs implements the pure shortcut/press routing policy.
Native.cs connects it to the existing low-level hooks without file/audio I/O there.
Tests/ShortcutCases.cs covers priority, pairing, modifiers, queues and migration.

https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay
