# Layout preview (development only)

The harness displays the production MainForm controls inside a separate shell.
The production MainForm never receives Shown, so hooks, audio enumeration,
UAC and production timers do not start. External actions are disabled.
It uses the same C# source and a compatibility icon for Mono's ICO decoder.

Compile one language:

```sh
python3 Tests/UI/preview.py --mono-root /absolute/mono/prefix --language ru
```

Run it from a desktop terminal:

```sh
python3 Tests/UI/preview.py --mono-root /absolute/mono/prefix --language ru --run --binding-example pair
```

Use `--language en` for English. The optional pair example displays Left+Right Shift.
Each language has separate Build/PreviewRuntime and Build/PreviewData directories.
The harness does not test real Windows integration or saving through startup APIs.
Do not ship the preview EXE in a runtime archive.
