# APEIRON Global Mute Overlay v0.1.0-beta.5

A free, open-source Windows microphone-mute utility with global hotkeys and a customizable desktop indicator.

## Download

Choose the language in the archive name:

- **English:** `APEIRON-Global-Mute-Overlay-v0.1.0-beta.5-EN-win-x64.zip`
- **Русский:** `APEIRON-Global-Mute-Overlay-v0.1.0-beta.5-RU-win-x64.zip`

Windows 10/11 x64. The two builds have the same features and share settings. There is no in-app language switch.

Each archive contains only the EXE, a short `README.txt` and `LICENSE.txt`. Full instructions and test materials are available in the repository.

## Changes since the first public beta

- Separate English and Russian builds.
- One shared key-blocking switch for all enabled microphone hotkeys. Blocking defaults off when no saved choice exists; disabled actions do not reserve their keys.
- A customizable hotkey to toggle key blocking: one ordinary key, Ctrl/Alt/Shift + a key, or Left + Right Shift, Alt or Ctrl together.
- Modifier pairs fire once when the second side is pressed. Release both sides before repeating; the first modifier keeps its normal action in the other app.
- Simpler settings: display-rule dropdowns, transparency from 0 to 95%, visible color swatches and previews, and placement without coordinate fields.
- Clearer pause text in the window and tray. Uncheck to resume assigned-key commands.
- Removed the unsaved-changes label. Use **Save settings** to keep changes after restart.
- Hold recovery now uses memory without a recovery-file write on each press. Normal release/exit restores the original state; after a crash, check the microphone manually.

Toggle and temporary Hold retain their previous behavior. Primary mouse clicks are never blocked.

## Setup and updating

1. Save settings and **Exit** the previous version from the tray.
2. Extract your chosen archive to a permanent folder and run `APEIRON_Global_Mute_Overlay.exe`.
3. Select your microphone, assign keys and click **Save settings**.

Your existing microphone, bindings and appearance are preserved. Older individual Toggle/Hold blocking preferences are replaced by the shared switch.

Default mic keys: **F9 Toggle / F10 Hold**. The blocking-command hotkey starts unassigned. Pause clears at restart.

## Beta and feedback

This is an unsigned beta. Automated checks passed against both production executables; wider microphone/game compatibility is being refined through feedback.

Report steps, expected/actual result, Windows version, microphone and game/app in Issues. Include **General - Copy diagnostics** if possible.

Exclusive audio access can bypass software mute. Exclusive fullscreen or protected games can affect indicator visibility or key delivery. Elevated games may require running the app with matching privileges.

<details>
<summary>File verification / Проверка файлов</summary>

SHA256 of the EXE inside each archive:

**RU**
```text
b9aa71bb13618b7006aa40734910c13f1f83cc522cd025ceb977cadba997a45f
```

**EN**
```text
e63c8f5e5fb8258d011f38793f0a252adf16a9614d03034f9160be9aa514c82e
```

Optional checks compare the EXE with the published build. [Test report](https://github.com/iamtheapeiron-web/apeiron-global-mute-overlay/blob/main/TEST_REPORT.txt) and Windows checklists are available in the repository.

</details>

---

**По-русски:** доступны отдельные архивы RU и EN. Добавлены горячая клавиша общей блокировки и пары левого/правого модификатора, упрощены настройки оформления и описание паузы. Изменения сохраняются кнопкой «Сохранить настройки». Перед обновлением завершите старую версию через «Выход» в трее.

License: MIT.  
Website: https://apeiron.framer.media/  
Support: https://www.paypal.com/paypalme/StanislavMed
