# Building

C# WinForms, .NET Framework 4.x, Windows x64, no NuGet dependencies.

## Windows

Run `build.cmd` to compile both languages, or `build.cmd test` to build and test both.
The batch script explicitly uses UTF-8 source encoding and .NET Framework 4.x csc.

Outputs:

- `Build/EN/APEIRON_Global_Mute_Overlay.exe`
- `Build/RU/APEIRON_Global_Mute_Overlay.exe`

The RU compiler define is `APEIRON_RU`. EN omits it. Both executables use the same settings path and instance mutex.

With Python installed, `python build.py --language both --tests` is equivalent. Use `--language en` or `--language ru` for one build.

## Portable verification

Supply a Mono prefix with `usr/bin/mono-sgen`, mcs, framework/GAC assemblies and `etc/mono/config`, plus mingw windres:

```sh
python3 build.py --mono-root /absolute/mono/prefix --language both --tests
```

Each test executable references its corresponding delivery EXE. `Tests/verify_exe.py` checks PE/resources/manifest metadata.
Portable tests do not execute Windows audio, global hooks, UAC, games or GUI layout.

Public version: 0.1.0-beta.2. Numeric Windows/assembly/manifest revision: 0.1.0.9.
