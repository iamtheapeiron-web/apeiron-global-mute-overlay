# Layout-only preview

This optional harness displays the controls built from the same C# sources, in a separate host form. The production MainForm is never Shown: Windows hooks, device enumeration, UAC and periodic monitoring do not start. External actions such as Save, startup/Administrator/reset and website links are disabled in the preview. It is not shipped in the user archive.

Compile the layout build using the same Mono prefix as build.py:

```sh
python3 Tests/UI/preview.py --mono-root /absolute/mono/prefix
```

From a terminal with access to a desktop display:

```sh
python3 Tests/UI/preview.py --mono-root /absolute/mono/prefix --run
```

Mono cannot decode the PNG-compressed Windows ICO resource. The layout build substitutes a classic system icon while keeping the C# sources identical. The delivery EXE retains all seven original icon sizes and is independently checked by Tests/verify_exe.py.

This preview does not validate Windows audio, native input, UAC, startup, Windows-specific rendering/DPI or game behavior.

Pass `--binding-example pair` with `--run` to inspect a left/right Shift binding without starting hooks or capture.
