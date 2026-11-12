# Building

C# WinForms / .NET Framework 4.x / Windows x64. No NuGet packages.

For this RU iteration:

```sh
python3 build.py --mono-root /absolute/mono/prefix --language ru --tests
```

A supplied Mono prefix needs mono-sgen, mcs, framework/GAC assemblies, etc/mono/config and mingw windres. The RU output is `Build/RU/APEIRON_Global_Mute_Overlay.exe`. Tests reference that exact executable; PE resources and manifest are verified separately.

On Windows with Python installed:

```text
python build.py --language ru --tests
```

The existing `build.cmd` / `build.cmd test` runner compiles both language variants using Windows .NET Framework csc and explicit UTF-8 encoding. EN beta.3 has not been built or tested in this iteration. The RU define is `APEIRON_RU`; language is not stored as a setting.

Optional Linux layout preview: [Tests/UI/README.md](Tests/UI/README.md). It isolates Windows integration and uses a compatibility icon; it is not shipped in the archive.

Version: 0.1.0-beta.3. Windows/assembly/manifest revision: 0.1.0.10. Settings schema: 5.
