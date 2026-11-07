# Building APEIRON Global Mute Overlay

A standalone C# WinForms application targeting .NET Framework 4.x, Windows x64. No NuGet packages are required.

## Windows

From the repository root:

```bat
build.cmd
build.cmd test
```

The output is `Build/APEIRON_Global_Mute_Overlay.exe`. Tests reference that compiled executable.

An optional Python runner is also available:

```text
python build.py --tests
```

## Portable verification

With a Mono prefix containing `usr/bin/mono-sgen`, framework assemblies and `etc/mono/config`:

```text
python3 build.py --mono-root /absolute/mono/prefix --tests
python3 Tests/verify_exe.py Build/APEIRON_Global_Mute_Overlay.exe
```

Portable tests use a fake audio backend. Real Windows audio, hooks, UAC and GUI require Windows validation. See TEST_REPORT.txt.

## Source layout

- Source: audio control, overlay, hotkeys, optional recovery and settings.
- Assets: APEIRON icon and About image.
- Tests: controller/input policy tests and executable resource inspection.

The public version is 0.1.0-beta.1; numeric Windows metadata uses build revision 0.1.0.8. Runtime behavior matches RC1.
