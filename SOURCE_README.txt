APEIRON Global Mute Overlay v0.1.0-beta.3 RU
C# WinForms / .NET Framework 4.x / Windows x64 / MIT.

Only RU was built and tested in this iteration. See BUILDING.md.
Settings schema 5 removes the file-recovery opt-in and persists shared blocking.
Stored opacity and coordinates remain compatible; the UI exposes inverse transparency.
SettingsPresentation.cs contains disk-free dirty tracking and stable rule choices.
Tests/PresentationCases.cs covers memory-only recovery, legacy files, migration,
shared blocking persistence, display-rule mapping and transparency.
Tests/UI is a layout-only development harness and is not included in the runtime archive.
