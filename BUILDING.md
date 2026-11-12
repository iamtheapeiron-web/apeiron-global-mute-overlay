# Building

C# WinForms / .NET Framework 4.x / Windows x64. No NuGet packages.

Both languages, with tests:

```sh
python3 build.py --mono-root /absolute/mono/prefix --language both --tests
```

The portable prefix needs mono-sgen, mcs, framework/GAC assemblies, etc/mono/config and mingw windres. Output: `Build/RU/APEIRON_Global_Mute_Overlay.exe` and `Build/EN/APEIRON_Global_Mute_Overlay.exe`. Tests reference each exact EXE; PE resources and manifests are verified separately.

On Windows with Python:

```text
python build.py --language both --tests
```

`build.cmd` and `build.cmd test` also compile both variants with Windows .NET Framework csc and explicit UTF-8 encoding. The RU define is `APEIRON_RU`; the language is not a saved setting.

[Optional Linux layout preview](Tests/UI/README.md) isolates Windows integration and uses a compatibility icon. The preview executable is never shipped.

Public version: 0.1.0-beta.5. Windows/assembly/manifest revision: 0.1.0.12. Settings schema: 6.
