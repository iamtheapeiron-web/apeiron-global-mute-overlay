APEIRON Global Mute Overlay v0.1.0-beta.4 RU
C# WinForms / .NET Framework 4.x / Windows x64 / MIT.

Only RU was built and tested in this iteration. See BUILDING.md.
Settings schema 6 removes per-action blocking preferences and adds an optional
second physical key for L/R modifier-pair commands. Existing modifier commands
and the saved master blocking value are preserved.
BlockingShortcut.cs owns single/chord/pair recognition and latched physical routes.
Tests/UnifiedHotkeyCases.cs covers unified blocking migration, single/pair commands,
conflicts, modifier ordering, repeat prevention, queued commands and active Hold.
The memory-only recovery and compact settings UI from beta.3 remain.
Tests/UI is a layout-only development harness, not part of the runtime archive.
