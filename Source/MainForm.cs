using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Apeiron.GlobalMute {
    public sealed class MainForm : Form {
        private readonly Settings settings;
        private readonly SettingsChangeTracker settingsChanges;
        private readonly ToolTip hints = new ToolTip { AutoPopDelay = 12000, InitialDelay = 450, ReshowDelay = 100, ShowAlways = true };
        private readonly Controller controller;
        private readonly InputRouter input;
        private readonly MemoryRestoreStore recovery;
        private readonly ElevationNotice elevation;
        private readonly int ownProcessId = Process.GetCurrentProcess().Id;
        private readonly ApeironOverlayForm overlay = new ApeironOverlayForm();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Timer timer = new Timer();
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private readonly bool initiallyHidden;
        private bool initializing = true, capturing, reallyExit, appearanceDirty = true, shuttingDown;
        private bool lastVisible, lastMuted, hooksOkay, lastPlacement, refreshingDevices;
        private bool checkingElevation, administratorPromptOpen, restarting;
        private int inputWakeupPending;
        private double nextPoll, nextDeviceRefresh, nextTopMost;
        private double nextElevationCheck;
        private double lastQueueMilliseconds, maxQueueMilliseconds, lastControlMilliseconds, maxControlMilliseconds;
        private double lastPollMilliseconds, maxPollMilliseconds, lastEnumerationMilliseconds;
        private string lastEnumerationError = "";
        private List<DeviceInfo> devices = new List<DeviceInfo>();
        private readonly List<Label> wrapping = new List<Label>();
        private ComboBox deviceList, shapes, monitors, normalRuleList, holdRuleList;
        private Label deviceStatus, deviceDetails, selectionInfo, footer, toggleBinding, holdBinding, placementHint;
        private Button toggleNow, placeButton, assignBlockingButton, clearBlockingButton;
        private ColorChoice mutedColor, onColor, iconColor;
        private Label saveState;
        private CheckBox toggleEnabled, holdEnabled, autoStart, startMinimized;
        private CheckBox offerAdminRestart;
        private CheckBox blockToggle, blockHold;
        private CheckBox pauseHotkeys, blockKeys;
        private Label blockingStatus;
        private CheckBox blockingShortcutEnabled;
        private Label blockingBinding;
        private ToolStripMenuItem trayPause, trayBlocking, trayMute;
        private bool startupAdministratorPending = true;
        private TableLayoutPanel elevationBanner;
        private Label elevationWarning;
        private NumericUpDown duration, size, transparency;
        private TrackBar transparencySlider;
        private TableLayoutPanel lookStack;

        public MainForm(Settings settings, bool trayLaunch, string notice) {
            this.settings = settings;
            settingsChanges = new SettingsChangeTracker(settings);
            recovery = new MemoryRestoreStore(new LegacyRestoreFile(Path.Combine(Program.DataDir, "hold-recovery.xml")));
            controller = new Controller(settings, new CoreAudio(), new MonotonicClock(), recovery);
            elevation = new ElevationNotice(ApeironProcessSecurity.GetElevation(ownProcessId));
            input = new InputRouter(controller);
            initiallyHidden = (trayLaunch || settings.StartMinimized) && !String.IsNullOrEmpty(settings.DeviceId) && !controller.HasPending && String.IsNullOrEmpty(notice);
            Text = Program.Name + " - " + Program.Version + " [" + Ui.LanguageCode + "]" + (elevation.OwnElevation == true ? Ui.Text(" [Administrator]") : "");
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(700, 680); MinimumSize = new Size(660, 570);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = LoadIcon(); overlay.Icon = Icon;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);
            elevationBanner = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(255, 236, 185), Padding = new Padding(10), Visible = false };
            elevationBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            elevationWarning = TextLabel(""); Add(elevationBanner, elevationWarning);
            Add(elevationBanner, Button("Restart as Administrator", RestartElevated));
            layout.Controls.Add(elevationBanner, 0, 0);
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) }; layout.Controls.Add(tabs, 0, 1);
            var mic = Page(tabs, "Microphone"); var controls = Page(tabs, "Controls");
            lookStack = Page(tabs, "Appearance"); var general = Page(tabs, "General"); var about = Page(tabs, "About");
            BuildMicrophone(mic); BuildControls(controls); BuildAppearance(lookStack); BuildGeneral(general); BuildAbout(about);
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
            saveState = TextLabel("There are unsaved changes"); saveState.ForeColor = Color.DarkOrange;
            saveState.Font = new Font(Font, FontStyle.Bold); saveState.Visible = false; bottom.Controls.Add(saveState);
            footer = TextLabel(String.IsNullOrEmpty(notice) ? Ui.Text("Choose a microphone, then Save settings.") : notice);
            bottom.Controls.Add(footer);
            var buttons = Flow();
            buttons.Controls.Add(Button("Save settings", delegate { SaveSettings(); }));
            buttons.Controls.Add(Button("Hide to tray", delegate { HideSettings(); }));
            buttons.Controls.Add(Button("Exit", delegate { ExitApp(); })); bottom.Controls.Add(buttons);
            layout.Controls.Add(bottom, 0, 2);
            BuildTray();
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                FinishPlacement();
                if (!reallyExit && (e.CloseReason == CloseReason.UserClosing)) { e.Cancel = true; HideSettings(); }
            };
            Resize += delegate { WrapLabels(); if (WindowState == FormWindowState.Minimized) FinishPlacement(); };
            SystemEvents.PowerModeChanged += OnPowerMode;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            Shown += delegate {
                WrapLabels();
                hooksOkay = ApeironGlobalInput.Start(); SyncInput();
                if (!hooksOkay) footer.Text = Ui.Text("Global key hooks could not start. Try Restart as Administrator in General.");
                controller.Poll(); Render(); UpdateStatus();
                RefreshDevices();
                if (initiallyHidden) Hide();
                timer.Start();
                BeginInvoke((Action)MaybeOfferAdministratorRestart);
            };
            timer.Interval = 20; timer.Tick += delegate { TickApplication(); };
            EnsureOverlayOnScreen();
            initializing = false;
            RefreshSaveState();
        }
        private static Icon LoadIcon() {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("APEIRON.ico"))
            using (var icon = new Icon(stream)) return (Icon)icon.Clone();
        }
        private TableLayoutPanel Page(TabControl tabs, string title) {
            var page = new TabPage(Ui.Text(title)) { AutoScroll = true, Padding = new Padding(4) }; tabs.TabPages.Add(page);
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); page.Controls.Add(panel); return panel;
        }
        private static void Add(TableLayoutPanel panel, Control control) {
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            control.Margin = new Padding(0, 0, 0, 6);
            int row = panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(control, 0, row);
        }
        private Label TextLabel(string text) {
            var label = new Label { Text = Ui.Text(text), AutoSize = true, MaximumSize = new Size(580, 0), Margin = new Padding(0, 4, 0, 6) };
            wrapping.Add(label); return label;
        }
        private void WrapLabels() {
            int max = Math.Max(260, ClientSize.Width - (int)(128 * DeviceScale));
            foreach (var label in wrapping) label.MaximumSize = new Size(max, 0);
        }
        private float DeviceScale { get { using (var graphics = CreateGraphics()) return graphics.DpiX / 96F; } }
        private static FlowLayoutPanel Flow() {
            return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0) };
        }
        private static Button Button(string text, Action action) {
            var b = new Button { Text = Ui.Text(text), AutoSize = true, MinimumSize = new Size(100, 30), Padding = new Padding(7, 3, 7, 3), Margin = new Padding(0, 0, 8, 4) };
            b.Click += delegate { action(); }; return b;
        }
        private static CheckBox Check(string text, bool value) {
            return new CheckBox { Text = Ui.Text(text), Checked = value, AutoSize = true, Margin = new Padding(0, 4, 0, 6) };
        }
        private static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals, decimal step) {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Increment = step, Width = 95, ThousandsSeparator = false };
        }
        private void Row(TableLayoutPanel parent, string text, Control control) {
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var label = new Label { Text = Ui.Text(text), AutoSize = true, MaximumSize = new Size(165, 0), Margin = new Padding(0, 5, 8, 0) };
            control.AccessibleName = Ui.Text(text);
            row.Controls.Add(label, 0, 0); row.Controls.Add(control, 1, 0); Add(parent, row);
        }
        private void Hint(Control control, string text) {
            if (!String.IsNullOrEmpty(text)) hints.SetToolTip(control, Ui.Text(text));
        }
        private void RefreshSaveState() {
            if (!initializing && saveState != null) saveState.Visible = settingsChanges.HasChanges(settings);
        }
        private TableLayoutPanel Group(TableLayoutPanel parent, string title) {
            var group = new GroupBox { Text = Ui.Text(title), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(12, 8, 12, 6) };
            var inner = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); group.Controls.Add(inner); Add(parent, group); return inner;
        }
        private void BuildMicrophone(TableLayoutPanel page) {
            Add(page, TextLabel("Control one Windows recording device. OBS is not required."));
            var group = Group(page, "Selected microphone");
            deviceList = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, DropDownWidth = 600 };
            Add(group, deviceList);
            var buttons = Flow();
            buttons.Controls.Add(Button("Refresh devices", RefreshDevices));
            buttons.Controls.Add(Button("Use selected microphone", SelectMicrophone)); Add(group, buttons);
            selectionInfo = TextLabel(SelectedDeviceText()); Add(group, selectionInfo);
            deviceStatus = TextLabel("Choose a microphone."); deviceStatus.Font = new Font(Font, FontStyle.Bold); Add(group, deviceStatus);
            deviceDetails = TextLabel(""); Add(group, deviceDetails);
            toggleNow = Button("Mute / Unmute now", delegate { controller.Toggle(); Render(); UpdateStatus(); }); Add(group, toggleNow);
            Add(page, TextLabel("Mic ON means Windows mute is off for this device. Each app can also have its own mute. Choose the physical microphone if you want to mute it before virtual audio processing."));
            Add(page, TextLabel("The selected device stays fixed when Windows changes its default microphone. If it disconnects, the app waits for the same device to return."));
        }
        private string SelectedDeviceText() { return Ui.Text("Current device: ") + (String.IsNullOrEmpty(settings.DeviceId) ? Ui.Text("none") : settings.DeviceName); }
        private void RefreshDevices() {
            if (refreshingDevices || shuttingDown || !IsHandleCreated) return;
            refreshingDevices = true;
            // Discovery owns its COM objects on a separate MTA thread. It cannot block hotkeys/paint.
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                var duration = Stopwatch.StartNew();
                List<DeviceInfo> found = null; Exception failure = null;
                try { using (var scanner = new CoreAudio()) found = scanner.Enumerate(); }
                catch (Exception ex) { failure = ex; Program.Log(ex); }
                double elapsed = duration.Elapsed.TotalMilliseconds;
                try { BeginInvoke((Action)delegate { ApplyDeviceList(found, failure, elapsed); }); }
                catch (InvalidOperationException) { }
            });
        }
        private void ApplyDeviceList(List<DeviceInfo> found, Exception failure, double elapsed) {
            if (shuttingDown) return;
            refreshingDevices = false; lastEnumerationMilliseconds = elapsed;
            if (failure != null) {
                lastEnumerationError = failure.Message;
                footer.Text = Ui.Text("Could not list microphones: ") + failure.Message; return;
            }
            string highlighted = (deviceList.SelectedItem as DeviceInfo) == null ? settings.DeviceId : ((DeviceInfo)deviceList.SelectedItem).Id;
            bool recovered = !String.IsNullOrEmpty(lastEnumerationError);
            lastEnumerationError = ""; devices = found;
            try {
                deviceList.BeginUpdate(); deviceList.Items.Clear();
                foreach (var device in devices) {
                    deviceList.Items.Add(device);
                    if (device.Id == settings.DeviceId) settings.DeviceName = device.Name;
                }
                deviceList.SelectedIndex = -1;
                for (int i = 0; i < devices.Count; i++) if (devices[i].Id == highlighted) deviceList.SelectedIndex = i;
                if (devices.Count == 0 && footer != null) footer.Text = Ui.Text("No active recording devices found. Check Windows sound settings.");
                else if (recovered) footer.Text = Ui.Text("Recording devices refreshed.");
            } finally { deviceList.EndUpdate(); }
        }
        private void SelectMicrophone() {
            var selected = deviceList.SelectedItem as DeviceInfo;
            if (selected == null) { footer.Text = Ui.Text("Select a microphone from the list first."); return; }
            if (controller.SelectDevice(selected)) footer.Text = Ui.Text("Microphone selected. Save settings to keep this choice.");
            else footer.Text = controller.Error;
            SyncInput(); selectionInfo.Text = SelectedDeviceText(); Render(); UpdateStatus();
        }
        private void BuildControls(TableLayoutPanel page) {
            var hotkeys = Group(page, "Hotkeys");
            pauseHotkeys = Check("Pause hotkeys (until restart)", false); Add(hotkeys, pauseHotkeys);
            Add(hotkeys, TextLabel("The indicator and manual controls remain active."));
            Hint(pauseHotkeys, "Pauses Toggle, Hold and the blocking shortcut. An active Hold is restored. Pause is cleared at restart.");
            pauseHotkeys.CheckedChanged += delegate {
                if (!initializing && pauseHotkeys.Checked != input.Paused) SetHotkeysPaused(pauseHotkeys.Checked);
            };
            var blocking = Group(page, "Key blocking");
            blockKeys = Check("Block keys in other apps", input.KeyBlockingEnabled); Add(blocking, blockKeys);
            blockingStatus = TextLabel(""); blockingStatus.Font = new Font(Font, FontStyle.Bold); Add(blocking, blockingStatus);
            Add(blocking, TextLabel("With blocking enabled, the selected keys control the microphone but do not trigger actions in other apps."));
            Hint(blockKeys, "Uses the individual Toggle and Hold choices below. Primary mouse clicks are never blocked. Save settings to keep the shared switch after restarting.");
            blockKeys.CheckedChanged += delegate {
                if (!initializing && blockKeys.Checked != input.KeyBlockingEnabled) SetKeyBlockingEnabled(blockKeys.Checked);
            };
            var shortcutRow = Flow();
            blockingShortcutEnabled = Check("Use shortcut", settings.BlockingShortcutEnabled); shortcutRow.Controls.Add(blockingShortcutEnabled);
            blockingBinding = TextLabel(BlockingShortcut.Format(settings.BlockingShortcutKey, settings.BlockingShortcutModifiers));
            shortcutRow.Controls.Add(blockingBinding);
            assignBlockingButton = Button("Assign...", AssignBlockingShortcut); shortcutRow.Controls.Add(assignBlockingButton);
            clearBlockingButton = Button("Clear", ClearBlockingShortcut); shortcutRow.Controls.Add(clearBlockingButton); Add(blocking, shortcutRow);
            Hint(blockingShortcutEnabled, "The shortcut toggles shared key blocking. It stays reserved for this command even with blocking off. Pause hotkeys also pauses it.");
            Hint(blockingBinding, "The shortcut toggles shared key blocking. It stays reserved for this command even with blocking off. Pause hotkeys also pauses it.");
            blockingShortcutEnabled.CheckedChanged += delegate {
                if (initializing) return;
                if (blockingShortcutEnabled.Checked && (!BlockingShortcut.IsValid(settings.BlockingShortcutKey, settings.BlockingShortcutModifiers) ||
                    BlockingShortcut.Conflicts(settings, settings.BlockingShortcutModifiers))) {
                    blockingShortcutEnabled.Checked = false;
                    footer.Text = Ui.Text("Assign a valid combination first. Its modifiers cannot be enabled Toggle or Hold keys."); return;
                }
                settings.BlockingShortcutEnabled = blockingShortcutEnabled.Checked; SyncInput();
            };
            var toggle = Group(page, "Mute / Unmute");
            toggleEnabled = Check("Enable Toggle hotkey", settings.ToggleEnabled); Add(toggle, toggleEnabled);
            toggleBinding = TextLabel(KeyName(settings.ToggleKey));
            var toggleRow = Flow(); var assignToggle = Button("Assign Toggle key", delegate { AssignKey(false); });
            toggleRow.Controls.Add(assignToggle); toggleRow.Controls.Add(toggleBinding); Add(toggle, toggleRow);
            blockToggle = Check("Block Toggle key in other apps", settings.BlockToggleKey); Add(toggle, blockToggle);
            Hint(toggleEnabled, "Disabling this hotkey leaves microphone monitoring and the overlay active.");
            var hold = Group(page, "Hold Mode");
            holdEnabled = Check("Enable Hold Mode", settings.HoldEnabled); Add(hold, holdEnabled);
            holdBinding = TextLabel(KeyName(settings.HoldKey));
            var holdRow = Flow(); var assignHold = Button("Assign Hold key", delegate { AssignKey(true); });
            holdRow.Controls.Add(assignHold); holdRow.Controls.Add(holdBinding); Add(hold, holdRow);
            blockHold = Check("Block Hold key in other apps", settings.BlockHoldKey); Add(hold, blockHold);
            Add(hold, TextLabel("Hold reverses the microphone state until release. Toggle is ignored during Hold."));
            Hint(holdEnabled, "Hold uses memory. Release or normal exit restores an available microphone. After an app crash, check the microphone state manually.");
            Hint(assignToggle, Ui.PunctuationHint); Hint(assignHold, Ui.PunctuationHint);
            Hint(toggleBinding, Ui.PunctuationHint); Hint(holdBinding, Ui.PunctuationHint);
            blockToggle.CheckedChanged += delegate { if (!initializing) { settings.BlockToggleKey = blockToggle.Checked; BlockingChanged(); } };
            blockHold.CheckedChanged += delegate { if (!initializing) { settings.BlockHoldKey = blockHold.Checked; BlockingChanged(); } };
            toggleEnabled.CheckedChanged += delegate { if (initializing) return; settings.ToggleEnabled = toggleEnabled.Checked; CheckKeyConflict(false); SyncInput(); };
            holdEnabled.CheckedChanged += delegate { if (initializing) return; settings.HoldEnabled = holdEnabled.Checked; CheckKeyConflict(true); controller.RulesChanged(); SyncInput(); Render(); };
        }
        private void ClearBlockingShortcut() {
            controller.ReleaseHold();
            settings.BlockingShortcutEnabled = false; settings.BlockingShortcutKey = 0;
            settings.BlockingShortcutModifiers = ShortcutModifiers.None;
            SyncInput(); Render(); UpdateStatus();
            footer.Text = Ui.Text("Shortcut cleared. Save settings to keep this change.");
        }
        private void CheckKeyConflict(bool changedHold) {
            if (settings.ToggleEnabled && settings.HoldEnabled && settings.ToggleKey == settings.HoldKey) {
                if (changedHold) { settings.HoldEnabled = false; holdEnabled.Checked = false; }
                else { settings.ToggleEnabled = false; toggleEnabled.Checked = false; }
                footer.Text = Ui.Text("Toggle and Hold need different keys. Assign another key before enabling both.");
            }
            if (settings.BlockingShortcutEnabled && BlockingShortcut.Conflicts(settings, settings.BlockingShortcutModifiers)) {
                if (changedHold) { settings.HoldEnabled = false; holdEnabled.Checked = false; }
                else { settings.ToggleEnabled = false; toggleEnabled.Checked = false; }
                footer.Text = Ui.Text("This key is a modifier of the key blocking shortcut. Change or disable that shortcut first.");
            }
        }
        private void AssignBlockingShortcut() {
            controller.ReleaseHold(); capturing = true; ApeironGlobalInput.SetSuppressionBypass(true);
            try {
                int key; ShortcutModifiers modifiers;
                if (!KeyCapture.CaptureShortcut(this, out key, out modifiers)) return;
                if (BlockingShortcut.Conflicts(settings, modifiers)) {
                    footer.Text = Ui.Text("A modifier of this combination is already an enabled Toggle or Hold key. Choose another combination."); return;
                }
                settings.BlockingShortcutKey = key; settings.BlockingShortcutModifiers = modifiers;
                settings.BlockingShortcutEnabled = true;
                blockingBinding.Text = BlockingShortcut.Format(key, modifiers);
                blockingShortcutEnabled.Checked = true;
                footer.Text = Ui.Text("Combination applied. Save settings to keep it.");
            } finally { capturing = false; ApeironGlobalInput.SetSuppressionBypass(false); SyncInput(); }
        }
        private void AssignKey(bool hold) {
            controller.ReleaseHold(); capturing = true; ApeironGlobalInput.SetSuppressionBypass(true);
            try {
                int key = KeyCapture.Capture(this, hold ? Ui.Text("Assign Hold key") : Ui.Text("Assign Toggle key"));
                if (key == 0) return;
                int other = hold ? settings.ToggleKey : settings.HoldKey;
                if (key == other) { footer.Text = Ui.Text("This key is already assigned to ") + (hold ? Ui.Text("Toggle.") : Ui.Text("Hold.")); return; }
                if (settings.BlockingShortcutEnabled && BlockingShortcut.UsesModifierKey(key, settings.BlockingShortcutModifiers)) {
                    footer.Text = Ui.Text("This key is a modifier of the key blocking shortcut. Change or disable that shortcut first."); return;
                }
                if (hold) { settings.HoldKey = key; holdBinding.Text = KeyName(key); }
                else { settings.ToggleKey = key; toggleBinding.Text = KeyName(key); }
                footer.Text = Ui.Text("Key applied. Save settings to keep it.");
            } finally { capturing = false; ApeironGlobalInput.SetSuppressionBypass(false); SyncInput(); }
        }
        internal static string KeyName(int key) {
            return KeyNames.Format(key, ((Keys)key).ToString());
        }
        private void BuildAppearance(TableLayoutPanel page) {
            var style = Group(page, "Indicator appearance");
            shapes = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            foreach (string shape in new[] { "Circle", "Rounded square", "Square" }) {
                var choice = new LocalizedChoice(shape); shapes.Items.Add(choice);
                if (shape == settings.Shape) shapes.SelectedItem = choice;
            }
            Row(style, "Shape", shapes);
            size = Number(32, 220, settings.Size, 0, 2); Row(style, "Size (screen pixels)", size);
            var alphaRow = Flow();
            transparencySlider = new TrackBar { Minimum = 0, Maximum = 95, Value = settings.Transparency, TickStyle = TickStyle.None,
                AutoSize = false, Width = 180, Height = 30, SmallChange = 1, LargeChange = 5, AccessibleName = Ui.Text("Transparency (%)") };
            transparency = Number(0, 95, settings.Transparency, 0, 1); transparency.Width = 65;
            alphaRow.Controls.Add(transparencySlider); alphaRow.Controls.Add(transparency); Row(style, "Transparency (%)", alphaRow);
            Hint(transparencySlider, "0% is fully visible. 95% is almost transparent."); Hint(transparency, "0% is fully visible. 95% is almost transparent.");
            mutedColor = ColorButton(settings.MutedColor, "MUTED background"); onColor = ColorButton(settings.OnColor, "Mic ON background"); iconColor = ColorButton(settings.IconColor, "Microphone icon");
            Row(style, "MUTED background", mutedColor); Row(style, "Mic ON background", onColor); Row(style, "Microphone icon", iconColor);
            var preview = Group(page, "Preview");
            var tests = Flow(); tests.Controls.Add(Button("Microphone muted", delegate { controller.Preview(true); Render(); }));
            tests.Controls.Add(Button("Microphone on", delegate { controller.Preview(false); Render(); })); Add(preview, tests);
            Add(preview, TextLabel("Does not change the microphone state."));
            BuildDisplayRules(page);
            var placement = Group(page, "Overlay placement");
            monitors = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            foreach (var screen in Screen.AllScreens) monitors.Items.Add(screen);
            monitors.DisplayMember = "DeviceName"; monitors.SelectedItem = Screen.PrimaryScreen;
            Row(placement, "Preset monitor", monitors);
            var presets = Flow();
            foreach (string corner in new[] { "Top left", "Top right", "Bottom left", "Bottom right" }) {
                string captured = corner; presets.Controls.Add(Button(corner, delegate { Preset(captured); }));
            } Add(placement, presets);
            // Keep Finish placement outside the disabled groups, so it remains clickable.
            placeButton = Button("Place by dragging", delegate {
                if (controller.Placement) FinishPlacement();
                else {
                    controller.BeginPlacement(); overlay.SetPlacementMode(true);
                    foreach (Control c in page.Controls) c.Enabled = c == placeButton || c == placementHint;
                    placeButton.Text = Ui.Text("Finish placement"); placeButton.UseVisualStyleBackColor = false;
                    placeButton.BackColor = Color.FromArgb(84, 201, 126); placeButton.ForeColor = Color.Black;
                    placeButton.Font = new Font(Font, FontStyle.Bold);
                    placementHint.Text = Ui.Text("Drag the overlay, then click the green Finish placement button."); Render();
                }
            }); Add(page, placeButton);
            placementHint = TextLabel("Choose a corner or drag the indicator into place."); Add(page, placementHint);
            shapes.SelectedIndexChanged += delegate { AppearanceChanged(); };
            size.ValueChanged += delegate { AppearanceChanged(); };
            transparencySlider.ValueChanged += delegate { transparency.Value = transparencySlider.Value; };
            transparency.ValueChanged += delegate {
                if (initializing) return;
                transparencySlider.Value = (int)transparency.Value; AppearanceChanged();
            };
        }
        private void BuildDisplayRules(TableLayoutPanel page) {
            var display = Group(page, "Indicator display");
            normalRuleList = RuleList(OverlayRuleChoice.NormalChoices(), (int)settings.Normal); Row(display, "In normal mode", normalRuleList);
            holdRuleList = RuleList(OverlayRuleChoice.HoldChoices(), (int)settings.DuringHold); Row(display, "During Hold", holdRuleList);
            Hint(normalRuleList, "Brief modes use the duration below. Keep visible while muted lasts until the microphone is on.");
            Hint(holdRuleList, "Same as normal mode uses the rule above. Throughout the hold stays visible until release. Releasing Hold creates no extra popup.");
            duration = Number(0.2M, 10M, (decimal)settings.PopupSeconds, 1, 0.1M); Row(display, "Brief display duration (sec.)", duration);
            Add(display, TextLabel("Applies to brief displays and indicator previews."));
            normalRuleList.SelectedIndexChanged += delegate {
                if (initializing || normalRuleList.SelectedItem == null) return;
                settings.Normal = (NormalRule)((OverlayRuleChoice)normalRuleList.SelectedItem).Value;
                controller.RulesChanged(); Render(); RefreshSaveState();
            };
            holdRuleList.SelectedIndexChanged += delegate {
                if (initializing || holdRuleList.SelectedItem == null) return;
                settings.DuringHold = (HoldRule)((OverlayRuleChoice)holdRuleList.SelectedItem).Value;
                controller.RulesChanged(); Render(); RefreshSaveState();
            };
            duration.ValueChanged += delegate { if (!initializing) { settings.PopupSeconds = (double)duration.Value; RefreshSaveState(); } };
        }
        private static ComboBox RuleList(OverlayRuleChoice[] choices, int selected) {
            var list = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DropDownWidth = 480 };
            foreach (var choice in choices) { list.Items.Add(choice); if (choice.Value == selected) list.SelectedItem = choice; }
            return list;
        }
        private ColorChoice ColorButton(string color, string name) {
            var choice = new ColorChoice { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Dock = DockStyle.Top, Margin = new Padding(0), HexValue = ColorHex(color) };
            var sample = new ColorSwatchButton { Width = 44, Height = 30, SelectedColor = ColorTranslator.FromHtml(color), Margin = new Padding(0, 0, 8, 0), AccessibleName = Ui.Text(name) };
            var choose = Button("Choose...", delegate {
                using (var picker = new ColorDialog { Color = ColorTranslator.FromHtml(choice.HexValue), FullOpen = true }) {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    choice.HexValue = ColorHex(ColorTranslator.ToHtml(Color.FromArgb(picker.Color.R, picker.Color.G, picker.Color.B)));
                    sample.SelectedColor = picker.Color; sample.Invalidate(); Hint(sample, choice.HexValue); Hint(choice, choice.HexValue);
                    AppearanceChanged();
                }
            });
            choose.AccessibleName = Ui.Text("Choose...") + " " + Ui.Text(name);
            sample.Click += delegate { choose.PerformClick(); };
            Hint(sample, choice.HexValue); Hint(choice, choice.HexValue);
            choice.Controls.Add(sample); choice.Controls.Add(choose); return choice;
        }
        private sealed class ColorChoice : FlowLayoutPanel { public string HexValue; }
        private sealed class ColorSwatchButton : Button {
            public Color SelectedColor;
            protected override void OnPaint(PaintEventArgs e) {
                base.OnPaint(e);
                var area = Rectangle.Inflate(ClientRectangle, -6, -6);
                using (var fill = new SolidBrush(SelectedColor)) e.Graphics.FillRectangle(fill, area);
                e.Graphics.DrawRectangle(SystemPens.ControlDarkDark, area);
            }
        }
        private void AppearanceChanged() {
            if (initializing) return;
            settings.Shape = ((LocalizedChoice)shapes.SelectedItem).Id; settings.Size = (int)size.Value;
            settings.Transparency = (int)transparency.Value;
            settings.MutedColor = mutedColor.HexValue; settings.OnColor = onColor.HexValue; settings.IconColor = iconColor.HexValue;
            appearanceDirty = true; Render(); RefreshSaveState();
        }
        private static string ColorHex(string value) {
            var c = ColorTranslator.FromHtml(value); return String.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }
        private void Preset(string corner) {
            var screen = monitors.SelectedItem as Screen ?? Screen.PrimaryScreen;
            Rectangle r = screen.WorkingArea; int margin = 24;
            settings.X = corner.Contains("right") ? r.Right - settings.Size - margin : r.Left + margin;
            settings.Y = corner.Contains("Bottom") ? r.Bottom - settings.Size - margin : r.Top + margin;
            appearanceDirty = true; controller.Preview(controller.Muted ?? true); Render(); RefreshSaveState();
        }
        private void FinishPlacement() {
            if (!controller.Placement) return;
            settings.X = overlay.Left; settings.Y = overlay.Top;
            controller.EndPlacement(); overlay.SetPlacementMode(false);
            settings.X = Math.Max(-30000, Math.Min(30000, settings.X));
            settings.Y = Math.Max(-30000, Math.Min(30000, settings.Y));
            foreach (Control c in lookStack.Controls) c.Enabled = true;
            placeButton.Text = Ui.Text("Place by dragging"); placeButton.UseVisualStyleBackColor = true;
            placeButton.BackColor = SystemColors.Control; placeButton.ForeColor = SystemColors.ControlText; placeButton.Font = Font;
            placementHint.Text = Ui.Text("Position applied. Save settings to keep it."); appearanceDirty = true; Render(); RefreshSaveState();
        }
        private void EnsureOverlayOnScreen() {
            Rectangle bounds = new Rectangle(settings.X, settings.Y, settings.Size, settings.Size);
            foreach (var screen in Screen.AllScreens) if (screen.WorkingArea.IntersectsWith(bounds)) return;
            settings.X = Screen.PrimaryScreen.WorkingArea.Left + 50; settings.Y = Screen.PrimaryScreen.WorkingArea.Top + 50;
        }
        private void BuildGeneral(TableLayoutPanel page) {
            var adminGroup = Group(page, "Administrator rights");
            var privilegeStatus = TextLabel(elevation.OwnElevation == true ? Ui.Text("Running as Administrator.") :
                elevation.OwnElevation == false ? Ui.Text("Running without Administrator rights.") : Ui.Text("Could not determine current Administrator status."));
            privilegeStatus.Font = new Font(Font, FontStyle.Bold); Add(adminGroup, privilegeStatus);
            offerAdminRestart = Check("Ask about Administrator rights at startup", settings.OfferAdministratorRestart); Add(adminGroup, offerAdminRestart);
            offerAdminRestart.CheckedChanged += delegate { if (!initializing) { settings.OfferAdministratorRestart = offerAdminRestart.Checked; RefreshSaveState(); } };
            var restart = Button("Restart as Administrator", RestartElevated); restart.Enabled = elevation.OwnElevation != true; Add(adminGroup, restart);
            Add(adminGroup, TextLabel("When enabled, startup offers an Administrator restart unless the app is already elevated. The question appears once per session. Foreground privilege monitoring still shows mismatch warnings. Restart saves current settings first; Windows asks for elevation."));
            autoStart = Check("Start with Windows (in the tray)", settings.AutoStart); Add(page, autoStart);
            startMinimized = Check("Start minimized when opened manually", settings.StartMinimized); Add(page, startMinimized);
            Add(page, TextLabel("Startup changes take effect when you Save settings. Keep the EXE at its final location before enabling startup."));
            autoStart.CheckedChanged += delegate { if (!initializing) { settings.AutoStart = autoStart.Checked; RefreshSaveState(); } };
            startMinimized.CheckedChanged += delegate { if (!initializing) { settings.StartMinimized = startMinimized.Checked; RefreshSaveState(); } };
            Add(page, Button("Open settings folder", delegate { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Program.DataDir + "\"") { UseShellExecute = true }); }));
            Add(page, Button("Copy diagnostics", CopyDiagnostics));
            Add(page, Button("Reset settings", delegate {
                controller.ReleaseHold();
                if (controller.HasPending) { footer.Text = Ui.Text("Reconnect the microphone to restore Hold before resetting settings."); return; }
                if (MessageBox.Show(this, Ui.Text("Reset this application's settings and startup entry? The app will close."), Program.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                try {
                    Startup.Set(false);
                    foreach (string suffix in new[] { "", ".bak", ".tmp" })
                        if (File.Exists(Program.SettingsPath + suffix)) File.Delete(Program.SettingsPath + suffix);
                    ExitApp();
                }
                catch (Exception ex) { footer.Text = ex.Message; }
            }));
            Add(page, TextLabel("Controls and appearance apply immediately. Save settings to keep them after restarting. This application's settings are separate from APEIRON OBS Overlay."));
        }
        private void BuildAbout(TableLayoutPanel page) {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("APEIRON_icon.png"))
            using (var original = new Bitmap(stream)) {
                Add(page, new PictureBox { Image = new Bitmap(original), Size = new Size(96, 96), SizeMode = PictureBoxSizeMode.Zoom });
            }
            var title = TextLabel(Program.Name); title.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold); Add(page, title);
            Add(page, TextLabel(Program.Version + " | Windows 10 / 11, x64"));
            Add(page, TextLabel("Microphone mute hotkeys with a visual reminder. Uses the selected Windows recording device without recording audio."));
            var links = Flow(); links.Controls.Add(Button("Website", delegate { OpenUrl("https://apeiron.framer.media/"); }));
            links.Controls.Add(Button("Support via PayPal", delegate { OpenUrl("https://www.paypal.com/paypalme/StanislavMed"); })); Add(page, links);
            Add(page, TextLabel("Controls one selected recording device. Windows endpoint mute does not report separate app mutes or a physical switch that the device does not expose to Windows."));
            Add(page, TextLabel("Software mute can be bypassed by exclusive audio access. A desktop overlay may be hidden by exclusive fullscreen games; use windowed or borderless mode."));
        }
        private void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { footer.Text = ex.Message; } }
        private void BuildTray() {
            tray.Icon = Icon; tray.Text = Program.Name; tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add(Ui.Text("Settings"), null, delegate { ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            trayPause = new ToolStripMenuItem(Ui.Text("Pause hotkeys"));
            trayPause.Click += delegate { SetHotkeysPaused(!input.Paused); }; menu.Items.Add(trayPause);
            trayBlocking = new ToolStripMenuItem(Ui.Text("Block keys in other apps"));
            trayBlocking.Click += delegate { SetKeyBlockingEnabled(!input.KeyBlockingEnabled); }; menu.Items.Add(trayBlocking);
            menu.Items.Add(new ToolStripSeparator());
            trayMute = new ToolStripMenuItem(Ui.Text("Mute / Unmute"));
            trayMute.Click += delegate { controller.Toggle(); Render(); UpdateStatus(); }; menu.Items.Add(trayMute);
            menu.Items.Add(Ui.Text("Test MUTED"), null, delegate { controller.Preview(true); Render(); });
            menu.Items.Add(Ui.Text("Test Mic ON"), null, delegate { controller.Preview(false); Render(); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(Ui.Text("Exit"), null, delegate { ExitApp(); });
            menu.Opening += delegate { UpdateHotkeyControls(); };
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowSettings(); };
            UpdateHotkeyControls();
        }
        private void SetHotkeysPaused(bool paused) {
            input.SetPaused(paused, ApeironGlobalInput.IsActionKeyDown(settings.ToggleKey), ApeironGlobalInput.IsActionKeyDown(settings.HoldKey));
            SyncInput(); Render(); UpdateStatus();
            footer.Text = paused ? Ui.Text("Hotkeys paused.") : Ui.Text("Hotkeys resumed.");
            if (controller.HasPending && !controller.HoldActive) footer.Text += Ui.Text(" Hold recovery is pending for the selected microphone.");
        }
        private void BlockingChanged() {
            SyncInput();
            footer.Text = Ui.Text("Blocking preference updated.");
        }
        private void SetKeyBlockingEnabled(bool enabled) {
            input.KeyBlockingEnabled = enabled;
            // Do not clear the mailbox or re-seed input here: a Hold release or
            // another quick shortcut press may already be queued on the hook thread.
            ApeironGlobalInput.UpdateBlocking(input.BlockToggleKey, input.BlockHoldKey); UpdateHotkeyControls();
            footer.Text = Ui.Text(enabled ? "Key blocking enabled." : "Key blocking off.");
            if (input.Paused) footer.Text += Ui.Text(" Hotkeys are currently paused.");
            RefreshSaveState();
        }
        private void UpdateHotkeyControls() {
            pauseHotkeys.Checked = input.Paused;
            blockKeys.Checked = input.KeyBlockingEnabled;
            blockToggle.Enabled = settings.ToggleEnabled && KeySuppression.CanBlock(settings.ToggleKey);
            blockHold.Enabled = settings.HoldEnabled && KeySuppression.CanBlock(settings.HoldKey);
            trayPause.Checked = input.Paused;
            trayBlocking.Checked = input.KeyBlockingEnabled;
            bool assigned = BlockingShortcut.IsValid(settings.BlockingShortcutKey, settings.BlockingShortcutModifiers);
            blockingShortcutEnabled.Enabled = assigned;
            blockingShortcutEnabled.Checked = settings.BlockingShortcutEnabled;
            blockingBinding.Text = BlockingShortcut.Format(settings.BlockingShortcutKey, settings.BlockingShortcutModifiers);
            assignBlockingButton.Text = Ui.Text(assigned ? "Change..." : "Assign..."); clearBlockingButton.Enabled = assigned;
            string status = InputStatusText.Blocking(input);
            blockingStatus.Text = status; trayBlocking.ToolTipText = status;
        }
        private void SaveSettings() {
            TrySaveSettings();
        }
        private bool TrySaveSettings() {
            FinishPlacement();
            try { settings.Normalize(); Startup.Set(settings.AutoStart); XmlFiles.Write(Program.SettingsPath, settings); settingsChanges.Accept(settings); RefreshSaveState(); footer.Text = Ui.Text("Settings saved."); return true; }
            catch (Exception ex) { footer.Text = Ui.Text("Could not save settings: ") + ex.Message; Program.Log(ex); return false; }
        }
        private void HideSettings() { FinishPlacement(); Hide(); }
        private void ShowSettings() { Show(); WindowState = FormWindowState.Normal; Activate(); RefreshDevices(); }
        private void ExitApp() { FinishPlacement(); controller.ReleaseHold(); reallyExit = true; Close(); }
        private void RestartElevated() {
            if (restarting || shuttingDown || elevation.OwnElevation == true) return;
            FinishPlacement();
            controller.ReleaseHold();
            SyncInput(); Render(); UpdateStatus();
            if (!recovery.CanRestartSafely) {
                footer.Text = Ui.Text("Reconnect the microphone to restore Hold before restarting.");
                return;
            }
            if (!TrySaveSettings()) return;
            restarting = true;
            ApeironGlobalInput.SetSuppressionBypass(true);
            try {
                var process = Process.Start(new ProcessStartInfo(Program.ExePath, "--wait-for " + ownProcessId) { Verb = "runas", UseShellExecute = true });
                if (process == null) { restarting = false; ApeironGlobalInput.SetSuppressionBypass(false); SyncInput(); footer.Text = Ui.Text("Administrator restart did not start."); return; }
                process.Dispose();
                ExitApp();
            } catch (System.ComponentModel.Win32Exception ex) {
                restarting = false; ApeironGlobalInput.SetSuppressionBypass(false); SyncInput();
                footer.Text = ex.NativeErrorCode == 1223 ? Ui.Text("Administrator restart was cancelled. This instance is still running.") : ex.Message;
            } catch (Exception ex) { restarting = false; ApeironGlobalInput.SetSuppressionBypass(false); SyncInput(); footer.Text = ex.Message; Program.Log(ex); }
        }
        private void CopyDiagnostics() {
            string text = Program.Name + " " + Program.Version + Environment.NewLine +
                "Windows: " + Environment.OSVersion + Environment.NewLine + Ui.Text("64-bit process: ") + Environment.Is64BitProcess + Environment.NewLine +
                Ui.Text("Microphone: ") + settings.DeviceName + Environment.NewLine + Ui.Text("Connected: ") + controller.Ready + Environment.NewLine +
                Ui.Text("Windows mute: ") + (controller.Muted.HasValue ? controller.Muted.Value.ToString() : Ui.Text("Unknown")) + Environment.NewLine +
                Ui.Text("Hold active: ") + controller.HoldActive + Environment.NewLine + Ui.Text("Hold recovery pending: ") + controller.HasPending + Environment.NewLine +
                Ui.Text("Legacy recovery pending: ") + recovery.HasLegacyRecovery + Environment.NewLine +
                Ui.Text("Block Toggle / Hold keys: ") + settings.BlockToggleKey + " / " + settings.BlockHoldKey + Environment.NewLine +
                Ui.Text("Key blocking enabled: ") + input.KeyBlockingEnabled + Environment.NewLine +
                Ui.Text("Effective Toggle / Hold blocking: ") + input.BlockToggleKey + " / " + input.BlockHoldKey + Environment.NewLine +
                Ui.Text("Toggle / Hold binding: ") + KeyName(settings.ToggleKey) + " / " + KeyName(settings.HoldKey) + Environment.NewLine +
                Ui.Text("Hotkeys paused: ") + input.Paused + Environment.NewLine +
                Ui.Text("Key blocking shortcut: ") + BlockingShortcut.Format(settings.BlockingShortcutKey, settings.BlockingShortcutModifiers) +
                    " / " + settings.BlockingShortcutEnabled + Environment.NewLine + Ui.Text("Language: ") + Ui.LanguageCode + Environment.NewLine +
                Ui.Text("Hardware mute support: ") + controller.HardwareMute + Environment.NewLine + Ui.Text("Global hooks started: ") + hooksOkay + Environment.NewLine +
                Ui.Text("Elevated: ") + (elevation.OwnElevation.HasValue ? elevation.OwnElevation.Value.ToString() : Ui.Text("Unknown")) + Environment.NewLine +
                Ui.Text("Observed foreground app: ") + elevation.ProcessName + Environment.NewLine + Ui.Text("Privilege mismatch: ") + elevation.Mismatch + Environment.NewLine +
                Ui.Text("Last error: ") + controller.Error + Environment.NewLine +
                Ui.Text("Enumeration error: ") + lastEnumerationError + Environment.NewLine +
                String.Format(System.Globalization.CultureInfo.InvariantCulture,
                    Ui.Text("Timing ms (last / max):\r\nInput queue: {0:F1} / {1:F1}\r\nControl + overlay update: {2:F1} / {3:F1}\r\nAudio status poll: {4:F1} / {5:F1}\r\nDevice enumeration (off UI thread): {6:F1}"),
                    lastQueueMilliseconds, maxQueueMilliseconds, lastControlMilliseconds, maxControlMilliseconds,
                    lastPollMilliseconds, maxPollMilliseconds, lastEnumerationMilliseconds);
            var timing = controller.LastTiming;
            if (timing != null) text += Environment.NewLine + String.Format(System.Globalization.CultureInfo.InvariantCulture,
                Ui.Text("Last action: {0}; total {1:F1} ms; initial read {2:F1}; recovery state {3:F1}; SetMute {4:F1}; verification {5:F1}"),
                timing.Action, timing.TotalMilliseconds, timing.ReadMilliseconds, timing.JournalMilliseconds, timing.WriteMilliseconds, timing.ConfirmMilliseconds);
            try { Clipboard.SetText(text); footer.Text = Ui.Text("Diagnostics copied."); } catch (Exception ex) { footer.Text = ex.Message; }
        }
        private void SyncInput() {
            ApeironGlobalInput.ClearPresses(); input.Sync(ApeironGlobalInput.IsActionKeyDown(settings.ToggleKey), ApeironGlobalInput.IsActionKeyDown(settings.HoldKey));
            ApeironGlobalInput.ConfigureWakeup(settings.ToggleKey, settings.HoldKey, input.ToggleEnabled, input.HoldEnabled, input.BlockToggleKey, input.BlockHoldKey,
                settings.BlockingShortcutKey, settings.BlockingShortcutModifiers, settings.BlockingShortcutEnabled && !input.Paused, QueueControlInput);
            UpdateHotkeyControls(); RefreshSaveState();
        }
        private void QueueControlInput() {
            // Runs on the hook thread: queue only; never do audio, disk I/O or UI work here.
            if (shuttingDown || !IsHandleCreated || System.Threading.Interlocked.Exchange(ref inputWakeupPending, 1) != 0) return;
            try { BeginInvoke((Action)delegate {
                System.Threading.Interlocked.Exchange(ref inputWakeupPending, 0);
                if (!shuttingDown) ProcessControlInput(true);
            }); } catch (InvalidOperationException) { System.Threading.Interlocked.Exchange(ref inputWakeupPending, 0); }
        }
        private void ProcessControlInput(bool fromHook) {
            if (capturing || shuttingDown || administratorPromptOpen || restarting) return;
            var snapshot = ApeironGlobalInput.ConsumeSnapshot(!fromHook);
            var duration = Stopwatch.StartNew();
            input.Tick(snapshot.ToggleDown, snapshot.HoldDown, snapshot.TogglePressed, snapshot.HoldPressed);
            if (!input.Paused && (snapshot.BlockingToggleCount & 1) != 0) SetKeyBlockingEnabled(!input.KeyBlockingEnabled);
            if (snapshot.HasEvent) {
                // Render/paint before any periodic Windows audio queries can block the UI.
                Render(); UpdateStatus();
                lastQueueMilliseconds = snapshot.QueueMilliseconds;
                maxQueueMilliseconds = Math.Max(maxQueueMilliseconds, lastQueueMilliseconds);
                lastControlMilliseconds = duration.Elapsed.TotalMilliseconds;
                maxControlMilliseconds = Math.Max(maxControlMilliseconds, lastControlMilliseconds);
            }
        }
        private void TickApplication() {
            if (shuttingDown || restarting) return;
            ProcessControlInput(false); Render();
            double now = watch.Elapsed.TotalSeconds;
            if (now >= nextPoll) {
                nextPoll = now + 0.15;
                var duration = Stopwatch.StartNew(); controller.Poll();
                lastPollMilliseconds = duration.Elapsed.TotalMilliseconds;
                maxPollMilliseconds = Math.Max(maxPollMilliseconds, lastPollMilliseconds);
                Render(); UpdateStatus();
            }
            if (Visible && !deviceList.DroppedDown && now >= nextDeviceRefresh) { nextDeviceRefresh = now + 5; RefreshDevices(); }
            if (now >= nextElevationCheck && elevation.OwnElevation != true) { nextElevationCheck = now + 1; CheckForegroundElevation(); }
            MaybeOfferAdministratorRestart();
            Render();
            if (overlay.Visible && now >= nextTopMost) { nextTopMost = now + 1; overlay.ForceTopMost(); }
        }
        private void CheckForegroundElevation() {
            if (checkingElevation || shuttingDown) return;
            checkingElevation = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                int processId = 0; string name = ""; bool? elevated = null;
                try {
                    var window = ApeironNative.GetForegroundWindow(); uint id;
                    if (window != IntPtr.Zero && ApeironNative.GetWindowThreadProcessId(window, out id) != 0 && id != 0 && id != ownProcessId) {
                        processId = (int)id; elevated = ApeironProcessSecurity.GetElevation(processId);
                        try { using (var process = Process.GetProcessById(processId)) name = process.ProcessName; }
                        catch { name = Ui.Text("Application ") + processId; }
                    }
                } catch { }
                try { BeginInvoke((Action)delegate {
                    checkingElevation = false;
                    if (shuttingDown) return;
                    bool previousMismatch = elevation.Mismatch;
                    elevation.Observe(processId, name, elevated);
                    if (elevation.Mismatch && !previousMismatch && controller.HoldActive) {
                        // A higher-privilege window can hide key-up from a normal process.
                        controller.ReleaseHold(); SyncInput(); Render(); UpdateStatus();
                    }
                    elevationWarning.Text = elevation.ProcessName + Ui.Text(" is running as Administrator. Hotkeys may not work there until the overlay is also elevated.");
                    elevationBanner.Visible = elevation.Mismatch;
                    MaybeOfferAdministratorRestart();
                }); } catch (InvalidOperationException) { }
            });
        }
        private void MaybeOfferAdministratorRestart() {
            bool busy = initializing || shuttingDown || restarting || administratorPromptOpen || capturing || controller.HoldActive || controller.Placement || !recovery.CanRestartSafely || !Enabled;
            bool enabled = settings.OfferAdministratorRestart && (input.ToggleEnabled || input.HoldEnabled);
            if (startupAdministratorPending && (!enabled || elevation.OwnElevation == true)) startupAdministratorPending = false;
            bool startup = startupAdministratorPending;
            if (!(startup ? elevation.ShouldAskAtStartup(enabled, busy) : elevation.ShouldAsk(enabled, busy))) return;
            startupAdministratorPending = false;
            elevation.MarkAsked(); administratorPromptOpen = true; ApeironGlobalInput.SetSuppressionBypass(true);
            bool previousTopMost = TopMost;
            try {
                ShowSettings(); TopMost = true;
                string reason = startup ?
                    (elevation.OwnElevation == false ? Ui.Text("The overlay is running without Administrator rights.") : Ui.Text("Administrator status could not be confirmed.")) +
                    Ui.Text("\r\n\r\nSome elevated games and applications can block global hotkeys. Restart the overlay as Administrator?") :
                    elevation.ProcessName + Ui.Text(" is running as Administrator, while the overlay is not.\r\n\r\nHotkeys may not work in that application. Restart the overlay as Administrator?");
                var result = MessageBox.Show(this, reason + Ui.Text("\r\n\r\nCurrent settings will be saved. Windows will ask for elevation."),
                    startup ? Ui.Text("Administrator rights") : Ui.Text("Administrator rights mismatch"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (result == DialogResult.Yes) RestartElevated();
            } finally {
                administratorPromptOpen = false;
                ApeironGlobalInput.SetSuppressionBypass(false);
                if (!shuttingDown) { TopMost = previousTopMost; SyncInput(); }
            }
        }
        private void UpdateStatus() {
            selectionInfo.Text = SelectedDeviceText();
            if (controller.Ready) {
                deviceStatus.Text = (controller.Muted == true ? Ui.Text("MUTED") : Ui.Text("Mic ON")) + (controller.HoldActive ? Ui.Text(" - Hold active") : "");
                deviceStatus.ForeColor = controller.Muted == true ? Color.Firebrick : Color.DarkGreen;
                deviceDetails.Text = controller.HardwareMute ? Ui.Text("Windows reports hardware-backed mute.") : Ui.Text("Windows software mute (shared audio mode).");
                if (!String.IsNullOrEmpty(controller.Error)) deviceDetails.Text += " " + controller.Error;
            } else {
                deviceStatus.Text = String.IsNullOrEmpty(settings.DeviceId) ? Ui.Text("Choose a microphone.") : Ui.Text("Microphone unavailable - waiting for the selected device.");
                deviceStatus.ForeColor = Color.DarkOrange;
                deviceDetails.Text = controller.Error;
            }
            if (controller.HasPending && !controller.HoldActive)
                deviceDetails.Text = Ui.Text("Hold recovery pending for ") + controller.Pending.DeviceName + Ui.Text(". Reconnect that microphone to restore its previous state. ") + controller.Error;
            toggleNow.Enabled = controller.Ready && !controller.HoldActive && !controller.HasPending;
            trayMute.Enabled = toggleNow.Enabled;
            string tip = "APEIRON Global Mute - " + (controller.Ready ? (controller.Muted == true ? Ui.Text("MUTED") : Ui.Text("Mic ON")) : Ui.Text("Device unavailable"));
            if (controller.HoldActive) tip += Ui.Text(" (Hold)");
            if (input.Paused) tip += Ui.Text(" (Hotkeys paused)");
            tray.Text = tip.Length <= 63 ? tip : tip.Substring(0, 63);
        }
        private void Render() {
            OverlayView view = controller.View;
            if (!view.Visible) { if (overlay.Visible) overlay.Hide(); lastVisible = false; return; }
            bool visualChange = appearanceDirty || !lastVisible || view.Muted != lastMuted || view.Placement != lastPlacement;
            if (appearanceDirty) {
                overlay.ShapeName = settings.Shape; overlay.Size = new Size(settings.Size, settings.Size);
                overlay.Opacity = settings.Opacity / 100.0; overlay.IconColor = ColorTranslator.FromHtml(settings.IconColor);
                if (!controller.Placement) overlay.Location = new Point(settings.X, settings.Y);
                overlay.UpdateRegion(); appearanceDirty = false;
            }
            if (!lastVisible || view.Muted != lastMuted || view.Placement != lastPlacement) {
                overlay.FillColor = ColorTranslator.FromHtml(view.Muted ? settings.MutedColor : settings.OnColor);
                overlay.IconKind = view.Muted ? "Muted" : "Unmuted"; overlay.Invalidate();
            }
            if (!overlay.Visible) overlay.Show();
            if (visualChange) overlay.Update();
            lastVisible = true; lastMuted = view.Muted; lastPlacement = view.Placement;
        }
        private void OnPowerMode(object sender, PowerModeChangedEventArgs e) {
            if (e.Mode == PowerModes.Suspend) PostSuspend();
        }
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff) PostSuspend();
        }
        private void PostSuspend() {
            if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)delegate { controller.Suspend(); SyncInput(); Render(); UpdateStatus(); });
        }
        protected override void Dispose(bool disposing) {
            if (disposing && !shuttingDown) {
                shuttingDown = true; timer.Stop(); timer.Dispose();
                SystemEvents.PowerModeChanged -= OnPowerMode; SystemEvents.SessionSwitch -= OnSessionSwitch;
                try { controller.Dispose(); } catch (Exception ex) { Program.Log(ex); }
                ApeironGlobalInput.ConfigureWakeup(0, 0, false, false, false, false, null);
                ApeironGlobalInput.Stop(); tray.Visible = false; tray.Dispose(); overlay.Dispose(); hints.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class KeyCapture {
        internal static bool CaptureShortcut(Form owner, out int key, out ShortcutModifiers modifiers) {
            int result = 0, upTicks = 0; bool armed = false;
            var selectedModifiers = ShortcutModifiers.None;
            using (var dialog = new ShortcutCaptureDialog { Text = Ui.Text("Assign combination"), StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(460, 190), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            using (var timer = new Timer { Interval = 20 }) {
                var label = new Label { Text = Ui.Text("Release all keys, then press Ctrl, Alt or Shift with a keyboard key. Esc cancels."),
                    Dock = DockStyle.Top, Height = 125, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(16) };
                var cancel = new Button { Text = Ui.Text("Cancel"), DialogResult = DialogResult.Cancel, Width = 130, Height = 32, Left = 165, Top = 140 };
                dialog.Controls.Add(label); dialog.Controls.Add(cancel); dialog.CancelButton = cancel;
                timer.Tick += delegate {
                    bool any = false;
                    for (int k = 1; k <= 254; k++) if (ApeironGlobalInput.IsDown(k)) { any = true; break; }
                    if (!armed) { upTicks = any ? 0 : upTicks + 1; armed = upTicks >= 3; return; }
                    if (result != 0) {
                        if (!any) { timer.Stop(); dialog.DialogResult = DialogResult.OK; dialog.Close(); }
                        return;
                    }
                    var mods = ApeironGlobalInput.ReadModifiers();
                    for (int k = 8; k <= 254; k++) {
                        if (!BlockingShortcut.IsValid(k, mods) || !ApeironGlobalInput.IsDown(k)) continue;
                        result = k; selectedModifiers = mods;
                        label.Text = BlockingShortcut.Format(k, mods) + "\r\n" + Ui.Text("Release all keys to confirm."); break;
                    }
                };
                timer.Start(); bool accepted = dialog.ShowDialog(owner) == DialogResult.OK;
                key = accepted ? result : 0; modifiers = accepted ? selectedModifiers : ShortcutModifiers.None; return accepted;
            }
        }
        private sealed class ShortcutCaptureDialog : Form {
            protected override bool ProcessCmdKey(ref Message message, Keys keyData) {
                // During capture, Ctrl/Alt combinations must not activate dialog
                // shortcuts (such as Alt+F4) before the polling capture sees them.
                if ((keyData & Keys.KeyCode) == Keys.Escape) return base.ProcessCmdKey(ref message, keyData);
                return true;
            }
        }
        internal static int Capture(Form owner, string title) {
            int result = 0, upTicks = 0; bool armed = false;
            using (var dialog = new Form { Text = Ui.Text(title), StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(400, 150),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, KeyPreview = false })
            using (var timer = new Timer { Interval = 20 }) {
                var label = new Label { Text = Ui.Text("Release all keys, then press a keyboard or mouse button."), Dock = DockStyle.Top, Height = 85, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(12) };
                var cancel = new Button { Text = Ui.Text("Cancel"), DialogResult = DialogResult.Cancel, Width = 90, Height = 28, Left = 155, Top = 100 };
                dialog.Controls.Add(label); dialog.Controls.Add(cancel); dialog.CancelButton = cancel;
                cancel.Click += delegate { result = 0; timer.Stop(); };
                timer.Tick += delegate {
                    if (!armed) {
                        bool any = false;
                        for (int k = 1; k <= 254; k++) if (ApeironGlobalInput.IsDown(k)) { any = true; break; }
                        upTicks = any ? 0 : upTicks + 1; armed = upTicks >= 3;
                        if (armed) label.Text = Ui.Text("Press the key or mouse button you want to use.");
                    } else if (result == 0) {
                        // Prefer the physical left/right modifier over its generic VK alias.
                        for (int k = 1; k <= 254; k++) {
                            if (k == 16 || k == 17 || k == 18) continue;
                            if (ApeironGlobalInput.IsDown(k)) { result = k; label.Text = MainForm.KeyName(k) + Ui.Text("\r\nRelease it to confirm."); break; }
                        }
                    } else if (!ApeironGlobalInput.IsDown(result)) { timer.Stop(); dialog.DialogResult = DialogResult.OK; dialog.Close(); }
                };
                timer.Start(); return dialog.ShowDialog(owner) == DialogResult.OK ? result : 0;
            }
        }
    }
}
