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
        private readonly Controller controller;
        private readonly InputRouter input;
        private readonly OptionalRestoreStore recovery;
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
        private ComboBox deviceList, shapes, monitors;
        private Label deviceStatus, deviceDetails, selectionInfo, footer, toggleBinding, holdBinding, placementHint;
        private Button toggleNow, placeButton, mutedColor, onColor, iconColor;
        private CheckBox toggleEnabled, holdEnabled, autoStart, startMinimized;
        private CheckBox fileRecovery, offerAdminRestart;
        private CheckBox blockToggle, blockHold;
        private CheckBox pauseHotkeys, blockKeys;
        private Label blockingStatus;
        private ToolStripMenuItem trayPause, trayBlocking, trayMute;
        private bool startupAdministratorPending = true;
        private TableLayoutPanel elevationBanner;
        private Label elevationWarning;
        private NumericUpDown duration, size, opacity, x, y;
        private TableLayoutPanel lookStack;

        public MainForm(Settings settings, bool trayLaunch, string notice) {
            this.settings = settings;
            if (settings.FileRecoveryEnabled) XmlFiles.WarmUpRecovery();
            recovery = new OptionalRestoreStore(new RestoreStore(Path.Combine(Program.DataDir, "hold-recovery.xml")), settings.FileRecoveryEnabled);
            controller = new Controller(settings, new CoreAudio(), new MonotonicClock(), recovery);
            elevation = new ElevationNotice(ApeironProcessSecurity.GetElevation(ownProcessId));
            input = new InputRouter(controller);
            initiallyHidden = (trayLaunch || settings.StartMinimized) && !String.IsNullOrEmpty(settings.DeviceId) && !controller.HasPending && String.IsNullOrEmpty(notice);
            Text = Program.Name + " - " + Program.Version + (elevation.OwnElevation == true ? " [Administrator]" : "");
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
            footer = TextLabel(String.IsNullOrEmpty(notice) ? "Choose a microphone, then Save settings." : notice);
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
                if (!hooksOkay) footer.Text = "Global key hooks could not start. Try Restart as Administrator in General.";
                controller.Poll(); Render(); UpdateStatus();
                RefreshDevices();
                if (initiallyHidden) Hide();
                timer.Start();
                BeginInvoke((Action)MaybeOfferAdministratorRestart);
            };
            timer.Interval = 20; timer.Tick += delegate { TickApplication(); };
            EnsureOverlayOnScreen();
            initializing = false;
        }
        private static Icon LoadIcon() {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("APEIRON.ico"))
            using (var icon = new Icon(stream)) return (Icon)icon.Clone();
        }
        private TableLayoutPanel Page(TabControl tabs, string title) {
            var page = new TabPage(title) { AutoScroll = true, Padding = new Padding(4) }; tabs.TabPages.Add(page);
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); page.Controls.Add(panel); return panel;
        }
        private static void Add(TableLayoutPanel panel, Control control) {
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            control.Margin = new Padding(0, 0, 0, 10);
            int row = panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(control, 0, row);
        }
        private Label TextLabel(string text) {
            var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(580, 0), Margin = new Padding(0, 4, 0, 6) };
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
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(100, 30), Padding = new Padding(7, 3, 7, 3), Margin = new Padding(0, 0, 8, 4) };
            b.Click += delegate { action(); }; return b;
        }
        private static CheckBox Check(string text, bool value) {
            return new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 4, 0, 6) };
        }
        private static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals, decimal step) {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Increment = step, Width = 95, ThousandsSeparator = false };
        }
        private void Row(TableLayoutPanel parent, string text, Control control) {
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var label = new Label { Text = text, AutoSize = true, Margin = new Padding(0, 5, 8, 0) };
            row.Controls.Add(label, 0, 0); row.Controls.Add(control, 1, 0); Add(parent, row);
        }
        private TableLayoutPanel Group(TableLayoutPanel parent, string title) {
            var group = new GroupBox { Text = title, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(12, 22, 12, 10) };
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
        private string SelectedDeviceText() { return "Current device: " + (String.IsNullOrEmpty(settings.DeviceId) ? "none" : settings.DeviceName); }
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
                footer.Text = "Could not list microphones: " + failure.Message; return;
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
                if (devices.Count == 0 && footer != null) footer.Text = "No active recording devices found. Check Windows sound settings.";
                else if (recovered) footer.Text = "Recording devices refreshed.";
            } finally { deviceList.EndUpdate(); }
        }
        private void SelectMicrophone() {
            var selected = deviceList.SelectedItem as DeviceInfo;
            if (selected == null) { footer.Text = "Select a microphone from the list first."; return; }
            if (controller.SelectDevice(selected)) footer.Text = "Microphone selected. Save settings to keep this choice.";
            else footer.Text = controller.Error;
            SyncInput(); selectionInfo.Text = SelectedDeviceText(); Render(); UpdateStatus();
        }
        private void BuildControls(TableLayoutPanel page) {
            var hotkeys = Group(page, "Hotkeys");
            pauseHotkeys = Check("Pause hotkeys (until restart)", false); Add(hotkeys, pauseHotkeys);
            Add(hotkeys, TextLabel("While paused, Toggle and Hold stop and their keys pass through to other apps. Monitoring, the overlay and manual controls stay active. Blocking choices are kept for resume."));
            pauseHotkeys.CheckedChanged += delegate {
                if (!initializing && pauseHotkeys.Checked != input.Paused) SetHotkeysPaused(pauseHotkeys.Checked);
            };
            blockKeys = Check("Block keys in other apps (until restart)", true); Add(hotkeys, blockKeys);
            blockingStatus = TextLabel(""); Add(hotkeys, blockingStatus);
            Add(hotkeys, TextLabel("This switch temporarily overrides both blocking choices below. Turning it off lets keys reach other apps; Toggle and Hold still work. Turning it on restores your individual choices."));
            blockKeys.CheckedChanged += delegate {
                if (!initializing && blockKeys.Checked != input.KeyBlockingEnabled) SetKeyBlockingEnabled(blockKeys.Checked);
            };
            var toggle = Group(page, "Mute / Unmute");
            toggleEnabled = Check("Enable Toggle hotkey", settings.ToggleEnabled); Add(toggle, toggleEnabled);
            toggleBinding = TextLabel(KeyName(settings.ToggleKey));
            var toggleRow = Flow(); toggleRow.Controls.Add(Button("Assign Toggle key", delegate { AssignKey(false); })); toggleRow.Controls.Add(toggleBinding); Add(toggle, toggleRow);
            blockToggle = Check("Block Toggle key in other apps", settings.BlockToggleKey); Add(toggle, blockToggle);
            Add(toggle, TextLabel("Disabling this hotkey leaves microphone monitoring and the overlay active."));
            var hold = Group(page, "Hold Mode");
            holdEnabled = Check("Enable Hold Mode", settings.HoldEnabled); Add(hold, holdEnabled);
            holdBinding = TextLabel(KeyName(settings.HoldKey));
            var holdRow = Flow(); holdRow.Controls.Add(Button("Assign Hold key", delegate { AssignKey(true); })); holdRow.Controls.Add(holdBinding); Add(hold, holdRow);
            blockHold = Check("Block Hold key in other apps", settings.BlockHoldKey); Add(hold, blockHold);
            Add(hold, TextLabel("Hold temporarily uses the opposite microphone state. Release restores the previous state. Toggle is ignored while Hold is active."));
            var normal = Group(page, "Normal microphone changes");
            string[] titles = { "Show on change", "Show only when muted", "Show only when unmuted", "Keep visible while muted", "Don't show for normal changes" };
            for (int i = 0; i < titles.Length; i++) {
                NormalRule value = (NormalRule)i;
                var radio = new RadioButton { AutoSize = true, Text = titles[i], Checked = settings.Normal == value, Margin = new Padding(0, 3, 0, 3) };
                radio.CheckedChanged += delegate { if (!initializing && radio.Checked) { settings.Normal = value; controller.RulesChanged(); Render(); } }; Add(normal, radio);
            }
            var during = Group(page, "During Hold Mode");
            string[] holdTitles = { "Follow normal overlay rules", "Always show while held", "Don't show during Hold" };
            for (int i = 0; i < holdTitles.Length; i++) {
                HoldRule value = (HoldRule)i;
                var radio = new RadioButton { AutoSize = true, Text = holdTitles[i], Checked = settings.DuringHold == value, Margin = new Padding(0, 3, 0, 3) };
                radio.CheckedChanged += delegate { if (!initializing && radio.Checked) { settings.DuringHold = value; controller.RulesChanged(); Render(); } }; Add(during, radio);
            }
            duration = Number(0.2M, 10M, (decimal)settings.PopupSeconds, 1, 0.1M);
            Row(page, "Popup duration (seconds)", duration);
            Add(page, TextLabel("Duration applies to brief popups and Test previews. Always show while held stays visible until release. Hold release creates no extra popup."));
            Add(page, TextLabel("Blocking reserves the assigned key for the overlay in other apps, including the desktop. Hold blocking is on by default. This settings window and key capture remain usable. Primary mouse clicks are never blocked."));
            Add(page, TextLabel("Punctuation key labels use US legends. Your assigned key does not change when updating the app."));
            blockToggle.CheckedChanged += delegate { if (!initializing) { settings.BlockToggleKey = blockToggle.Checked; BlockingChanged(); } };
            blockHold.CheckedChanged += delegate { if (!initializing) { settings.BlockHoldKey = blockHold.Checked; BlockingChanged(); } };
            toggleEnabled.CheckedChanged += delegate { if (initializing) return; settings.ToggleEnabled = toggleEnabled.Checked; CheckKeyConflict(false); SyncInput(); };
            holdEnabled.CheckedChanged += delegate { if (initializing) return; settings.HoldEnabled = holdEnabled.Checked; CheckKeyConflict(true); controller.RulesChanged(); SyncInput(); Render(); };
            duration.ValueChanged += delegate { if (!initializing) settings.PopupSeconds = (double)duration.Value; };
        }
        private void CheckKeyConflict(bool changedHold) {
            if (settings.ToggleEnabled && settings.HoldEnabled && settings.ToggleKey == settings.HoldKey) {
                if (changedHold) { settings.HoldEnabled = false; holdEnabled.Checked = false; }
                else { settings.ToggleEnabled = false; toggleEnabled.Checked = false; }
                footer.Text = "Toggle and Hold need different keys. Assign another key before enabling both.";
            }
        }
        private void AssignKey(bool hold) {
            controller.ReleaseHold(); capturing = true; ApeironGlobalInput.SetSuppressionBypass(true);
            try {
                int key = KeyCapture.Capture(this, hold ? "Assign Hold key" : "Assign Toggle key");
                if (key == 0) return;
                int other = hold ? settings.ToggleKey : settings.HoldKey;
                if (key == other) { footer.Text = "This key is already assigned to " + (hold ? "Toggle." : "Hold."); return; }
                if (hold) { settings.HoldKey = key; holdBinding.Text = KeyName(key); }
                else { settings.ToggleKey = key; toggleBinding.Text = KeyName(key); }
                footer.Text = "Key applied. Save settings to keep it.";
            } finally { capturing = false; ApeironGlobalInput.SetSuppressionBypass(false); SyncInput(); }
        }
        internal static string KeyName(int key) {
            return KeyNames.Format(key, ((Keys)key).ToString());
        }
        private void BuildAppearance(TableLayoutPanel page) {
            shapes = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            shapes.Items.AddRange(new object[] { "Circle", "Rounded square", "Square" }); shapes.SelectedItem = settings.Shape;
            Row(page, "Shape", shapes);
            size = Number(32, 220, settings.Size, 0, 2); Row(page, "Size (screen pixels)", size);
            opacity = Number(15, 100, settings.Opacity, 0, 1); Row(page, "Opacity (%)", opacity);
            mutedColor = ColorButton(settings.MutedColor); onColor = ColorButton(settings.OnColor); iconColor = ColorButton(settings.IconColor);
            Row(page, "MUTED background", mutedColor); Row(page, "Mic ON background", onColor); Row(page, "Microphone icon", iconColor);
            var tests = Flow(); tests.Controls.Add(Button("Test MUTED", delegate { controller.Preview(true); Render(); }));
            tests.Controls.Add(Button("Test Mic ON", delegate { controller.Preview(false); Render(); })); Add(page, tests);
            var position = Flow(); x = Number(-30000, 30000, settings.X, 0, 1); y = Number(-30000, 30000, settings.Y, 0, 1);
            position.Controls.Add(new Label { Text = "X", AutoSize = true, Margin = new Padding(0, 4, 5, 0) }); position.Controls.Add(x);
            position.Controls.Add(new Label { Text = "Y", AutoSize = true, Margin = new Padding(15, 4, 5, 0) }); position.Controls.Add(y);
            Row(page, "Position", position);
            monitors = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            foreach (var screen in Screen.AllScreens) monitors.Items.Add(screen);
            monitors.DisplayMember = "DeviceName"; monitors.SelectedItem = Screen.PrimaryScreen;
            Row(page, "Preset monitor", monitors);
            var presets = Flow();
            foreach (string corner in new[] { "Top left", "Top right", "Bottom left", "Bottom right" }) {
                string captured = corner; presets.Controls.Add(Button(corner, delegate { Preset(captured); }));
            } Add(page, presets);
            placeButton = Button("Place by dragging", delegate {
                if (controller.Placement) FinishPlacement();
                else {
                    controller.BeginPlacement(); overlay.SetPlacementMode(true);
                    foreach (Control c in page.Controls) c.Enabled = c == placeButton || c == placementHint;
                    placeButton.Text = "Finish placement"; placeButton.UseVisualStyleBackColor = false;
                    placeButton.BackColor = Color.FromArgb(84, 201, 126); placeButton.ForeColor = Color.Black;
                    placeButton.Font = new Font(Font, FontStyle.Bold);
                    placementHint.Text = "Drag the overlay, then click the green Finish placement button."; Render();
                }
            }); Add(page, placeButton);
            placementHint = TextLabel("Use a corner preset, enter coordinates, or place the overlay by dragging."); Add(page, placementHint);
            shapes.SelectedIndexChanged += delegate { AppearanceChanged(); };
            foreach (var n in new[] { size, opacity, x, y }) n.ValueChanged += delegate { AppearanceChanged(); };
        }
        private Button ColorButton(string color) {
            Button button = null;
            button = Button(color, delegate {
                using (var picker = new ColorDialog { Color = ColorTranslator.FromHtml(button.Text), FullOpen = true })
                    if (picker.ShowDialog(this) == DialogResult.OK) { button.Text = ColorTranslator.ToHtml(Color.FromArgb(picker.Color.R, picker.Color.G, picker.Color.B)); AppearanceChanged(); }
            }); return button;
        }
        private void AppearanceChanged() {
            if (initializing) return;
            settings.Shape = (string)shapes.SelectedItem; settings.Size = (int)size.Value;
            settings.Opacity = (int)opacity.Value; settings.X = (int)x.Value; settings.Y = (int)y.Value;
            settings.MutedColor = ColorHex(mutedColor.Text); settings.OnColor = ColorHex(onColor.Text); settings.IconColor = ColorHex(iconColor.Text);
            appearanceDirty = true; Render();
        }
        private static string ColorHex(string value) {
            var c = ColorTranslator.FromHtml(value); return String.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }
        private void Preset(string corner) {
            var screen = monitors.SelectedItem as Screen ?? Screen.PrimaryScreen;
            Rectangle r = screen.WorkingArea; int margin = 24;
            x.Value = corner.Contains("right") ? r.Right - settings.Size - margin : r.Left + margin;
            y.Value = corner.Contains("Bottom") ? r.Bottom - settings.Size - margin : r.Top + margin;
            controller.Preview(controller.Muted ?? true); Render();
        }
        private void FinishPlacement() {
            if (!controller.Placement) return;
            settings.X = overlay.Left; settings.Y = overlay.Top;
            controller.EndPlacement(); overlay.SetPlacementMode(false);
            x.Value = Math.Max(x.Minimum, Math.Min(x.Maximum, settings.X));
            y.Value = Math.Max(y.Minimum, Math.Min(y.Maximum, settings.Y));
            foreach (Control c in lookStack.Controls) c.Enabled = true;
            placeButton.Text = "Place by dragging"; placeButton.UseVisualStyleBackColor = true;
            placeButton.BackColor = SystemColors.Control; placeButton.ForeColor = SystemColors.ControlText; placeButton.Font = Font;
            placementHint.Text = "Position applied. Save settings to keep it."; appearanceDirty = true; Render();
        }
        private void EnsureOverlayOnScreen() {
            Rectangle bounds = new Rectangle(settings.X, settings.Y, settings.Size, settings.Size);
            foreach (var screen in Screen.AllScreens) if (screen.WorkingArea.IntersectsWith(bounds)) return;
            settings.X = Screen.PrimaryScreen.WorkingArea.Left + 50; settings.Y = Screen.PrimaryScreen.WorkingArea.Top + 50;
            if (x != null) { x.Value = settings.X; y.Value = settings.Y; }
        }
        private void BuildGeneral(TableLayoutPanel page) {
            var recoveryGroup = Group(page, "Hold recovery");
            fileRecovery = Check("Save Hold recovery to a file", settings.FileRecoveryEnabled); Add(recoveryGroup, fileRecovery);
            Add(recoveryGroup, TextLabel("Off by default: Hold uses memory and does not write its recovery file. Available microphones are restored on release or normal exit. An unfinished recovery cannot survive restart while this option is off."));
            Add(recoveryGroup, TextLabel("Enable file recovery to retain an unfinished Hold after a crash or restart. An existing recovery from an older version is completed even when this option is off."));
            fileRecovery.CheckedChanged += delegate {
                if (initializing) return;
                try {
                    if (fileRecovery.Checked) XmlFiles.WarmUpRecovery();
                    recovery.SetEnabled(fileRecovery.Checked);
                    settings.FileRecoveryEnabled = recovery.Enabled;
                    footer.Text = recovery.Enabled ? "Hold recovery file enabled. Save settings to keep this choice." : "Hold recovery uses memory. Save settings to keep this choice.";
                } catch (Exception ex) {
                    initializing = true; fileRecovery.Checked = recovery.Enabled; initializing = false;
                    footer.Text = "Recovery setting was not changed: " + ex.Message; Program.Log(ex);
                }
            };
            var adminGroup = Group(page, "Administrator rights");
            var privilegeStatus = TextLabel(elevation.OwnElevation == true ? "Running as Administrator." :
                elevation.OwnElevation == false ? "Running without Administrator rights." : "Could not determine current Administrator status.");
            privilegeStatus.Font = new Font(Font, FontStyle.Bold); Add(adminGroup, privilegeStatus);
            offerAdminRestart = Check("Ask about Administrator rights at startup", settings.OfferAdministratorRestart); Add(adminGroup, offerAdminRestart);
            offerAdminRestart.CheckedChanged += delegate { if (!initializing) settings.OfferAdministratorRestart = offerAdminRestart.Checked; };
            var restart = Button("Restart as Administrator", RestartElevated); restart.Enabled = elevation.OwnElevation != true; Add(adminGroup, restart);
            Add(adminGroup, TextLabel("When enabled, startup offers an Administrator restart unless the app is already elevated. The question appears once per session. Foreground privilege monitoring still shows mismatch warnings. Restart saves current settings first; Windows asks for elevation."));
            autoStart = Check("Start with Windows (in the tray)", settings.AutoStart); Add(page, autoStart);
            startMinimized = Check("Start minimized when opened manually", settings.StartMinimized); Add(page, startMinimized);
            Add(page, TextLabel("Startup changes take effect when you Save settings. Keep the EXE at its final location before enabling startup."));
            autoStart.CheckedChanged += delegate { if (!initializing) settings.AutoStart = autoStart.Checked; };
            startMinimized.CheckedChanged += delegate { if (!initializing) settings.StartMinimized = startMinimized.Checked; };
            Add(page, Button("Open settings folder", delegate { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Program.DataDir + "\"") { UseShellExecute = true }); }));
            Add(page, Button("Copy diagnostics", CopyDiagnostics));
            Add(page, Button("Reset settings", delegate {
                controller.ReleaseHold();
                if (controller.HasPending) { footer.Text = "Reconnect the microphone to restore Hold before resetting settings."; return; }
                if (MessageBox.Show(this, "Reset this application's settings and startup entry? The app will close.", Program.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
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
            menu.Items.Add("Settings", null, delegate { ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            trayPause = new ToolStripMenuItem("Pause hotkeys");
            trayPause.Click += delegate { SetHotkeysPaused(!input.Paused); }; menu.Items.Add(trayPause);
            trayBlocking = new ToolStripMenuItem("Block keys in other apps");
            trayBlocking.Click += delegate { SetKeyBlockingEnabled(!input.KeyBlockingEnabled); }; menu.Items.Add(trayBlocking);
            menu.Items.Add(new ToolStripSeparator());
            trayMute = new ToolStripMenuItem("Mute / Unmute");
            trayMute.Click += delegate { controller.Toggle(); Render(); UpdateStatus(); }; menu.Items.Add(trayMute);
            menu.Items.Add("Test MUTED", null, delegate { controller.Preview(true); Render(); });
            menu.Items.Add("Test Mic ON", null, delegate { controller.Preview(false); Render(); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, delegate { ExitApp(); });
            menu.Opening += delegate { UpdateHotkeyControls(); };
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowSettings(); };
            UpdateHotkeyControls();
        }
        private void SetHotkeysPaused(bool paused) {
            input.SetPaused(paused, ApeironGlobalInput.IsDown(settings.ToggleKey), ApeironGlobalInput.IsDown(settings.HoldKey));
            SyncInput(); Render(); UpdateStatus();
            footer.Text = paused ? "Hotkeys paused. Monitoring and manual controls remain active." : "Hotkeys resumed.";
            if (controller.HasPending && !controller.HoldActive) footer.Text += " Hold recovery is pending for the selected microphone.";
        }
        private void BlockingChanged() {
            SyncInput();
            footer.Text = input.Paused || !input.KeyBlockingEnabled ? "Blocking choice will apply when hotkeys and key blocking are enabled. Save settings to keep it after restart." :
                "Blocking choice applied. A key already held keeps its route until release. Save settings to keep this choice.";
        }
        private void SetKeyBlockingEnabled(bool enabled) {
            input.KeyBlockingEnabled = enabled; SyncInput();
            footer.Text = enabled ? "Key blocking enabled for your individual choices. An existing press keeps its route until release." :
                "Key blocking off until restart. Toggle and Hold actions still work unless paused.";
            if (input.Paused) footer.Text += " Hotkeys are currently paused.";
        }
        private void UpdateHotkeyControls() {
            pauseHotkeys.Checked = input.Paused;
            blockKeys.Checked = input.KeyBlockingEnabled;
            blockToggle.Enabled = settings.ToggleEnabled && KeySuppression.CanBlock(settings.ToggleKey);
            blockHold.Enabled = settings.HoldEnabled && KeySuppression.CanBlock(settings.HoldKey);
            trayPause.Checked = input.Paused;
            trayBlocking.Checked = input.KeyBlockingEnabled;
            string status = input.Paused ? "Blocking inactive while hotkeys are paused." :
                !input.KeyBlockingEnabled ? "Blocking off. Toggle and Hold keys pass through to other apps." :
                input.BlockToggleKey && input.BlockHoldKey ? "Blocking active for Toggle and Hold." :
                input.BlockToggleKey ? "Blocking active for Toggle." :
                input.BlockHoldKey ? "Blocking active for Hold." : "No enabled key is selected for blocking in Controls.";
            blockingStatus.Text = status; trayBlocking.ToolTipText = status;
        }
        private void SaveSettings() {
            TrySaveSettings();
        }
        private bool TrySaveSettings() {
            FinishPlacement();
            try { settings.Normalize(); Startup.Set(settings.AutoStart); XmlFiles.Write(Program.SettingsPath, settings); footer.Text = "Settings saved."; return true; }
            catch (Exception ex) { footer.Text = "Could not save settings: " + ex.Message; Program.Log(ex); return false; }
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
                footer.Text = "Reconnect the microphone to restore Hold, or enable recovery to a file before restarting.";
                return;
            }
            if (!TrySaveSettings()) return;
            restarting = true;
            ApeironGlobalInput.SetSuppressionBypass(true);
            try {
                var process = Process.Start(new ProcessStartInfo(Program.ExePath, "--wait-for " + ownProcessId) { Verb = "runas", UseShellExecute = true });
                if (process == null) { restarting = false; ApeironGlobalInput.SetSuppressionBypass(false); SyncInput(); footer.Text = "Administrator restart did not start."; return; }
                process.Dispose();
                ExitApp();
            } catch (System.ComponentModel.Win32Exception ex) {
                restarting = false; ApeironGlobalInput.SetSuppressionBypass(false); SyncInput();
                footer.Text = ex.NativeErrorCode == 1223 ? "Administrator restart was cancelled. This instance is still running." : ex.Message;
            } catch (Exception ex) { restarting = false; ApeironGlobalInput.SetSuppressionBypass(false); SyncInput(); footer.Text = ex.Message; Program.Log(ex); }
        }
        private void CopyDiagnostics() {
            string text = Program.Name + " " + Program.Version + Environment.NewLine +
                "Windows: " + Environment.OSVersion + Environment.NewLine + "64-bit process: " + Environment.Is64BitProcess + Environment.NewLine +
                "Microphone: " + settings.DeviceName + Environment.NewLine + "Connected: " + controller.Ready + Environment.NewLine +
                "Windows mute: " + (controller.Muted.HasValue ? controller.Muted.Value.ToString() : "Unknown") + Environment.NewLine +
                "Hold active: " + controller.HoldActive + Environment.NewLine + "Hold recovery pending: " + controller.HasPending + Environment.NewLine +
                "Recovery file enabled: " + recovery.Enabled + Environment.NewLine + "Recovery retained on disk: " + recovery.HasPersistedRecovery + Environment.NewLine +
                "Block Toggle / Hold keys: " + settings.BlockToggleKey + " / " + settings.BlockHoldKey + Environment.NewLine +
                "Key blocking enabled (session): " + input.KeyBlockingEnabled + Environment.NewLine +
                "Effective Toggle / Hold blocking: " + input.BlockToggleKey + " / " + input.BlockHoldKey + Environment.NewLine +
                "Toggle / Hold binding: " + KeyName(settings.ToggleKey) + " / " + KeyName(settings.HoldKey) + Environment.NewLine +
                "Hotkeys paused: " + input.Paused + Environment.NewLine +
                "Hardware mute support: " + controller.HardwareMute + Environment.NewLine + "Global hooks started: " + hooksOkay + Environment.NewLine +
                "Elevated: " + (elevation.OwnElevation.HasValue ? elevation.OwnElevation.Value.ToString() : "Unknown") + Environment.NewLine +
                "Observed foreground app: " + elevation.ProcessName + Environment.NewLine + "Privilege mismatch: " + elevation.Mismatch + Environment.NewLine +
                "Last error: " + controller.Error + Environment.NewLine +
                "Enumeration error: " + lastEnumerationError + Environment.NewLine +
                String.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Timing ms (last / max):\r\nInput queue: {0:F1} / {1:F1}\r\nControl + overlay update: {2:F1} / {3:F1}\r\nAudio status poll: {4:F1} / {5:F1}\r\nDevice enumeration (off UI thread): {6:F1}",
                    lastQueueMilliseconds, maxQueueMilliseconds, lastControlMilliseconds, maxControlMilliseconds,
                    lastPollMilliseconds, maxPollMilliseconds, lastEnumerationMilliseconds);
            var timing = controller.LastTiming;
            if (timing != null) text += Environment.NewLine + String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Last action: {0}; total {1:F1} ms; initial read {2:F1}; recovery state {3:F1}; SetMute {4:F1}; verification {5:F1}",
                timing.Action, timing.TotalMilliseconds, timing.ReadMilliseconds, timing.JournalMilliseconds, timing.WriteMilliseconds, timing.ConfirmMilliseconds);
            try { Clipboard.SetText(text); footer.Text = "Diagnostics copied."; } catch (Exception ex) { footer.Text = ex.Message; }
        }
        private void SyncInput() {
            ApeironGlobalInput.ClearPresses(); input.Sync(ApeironGlobalInput.IsDown(settings.ToggleKey), ApeironGlobalInput.IsDown(settings.HoldKey));
            ApeironGlobalInput.ConfigureWakeup(settings.ToggleKey, settings.HoldKey, input.ToggleEnabled, input.HoldEnabled, input.BlockToggleKey, input.BlockHoldKey, QueueControlInput);
            UpdateHotkeyControls();
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
                        catch { name = "Application " + processId; }
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
                    elevationWarning.Text = elevation.ProcessName + " is running as Administrator. Hotkeys may not work there until the overlay is also elevated.";
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
                    (elevation.OwnElevation == false ? "The overlay is running without Administrator rights." : "Administrator status could not be confirmed.") +
                    "\r\n\r\nSome elevated games and applications can block global hotkeys. Restart the overlay as Administrator?" :
                    elevation.ProcessName + " is running as Administrator, while the overlay is not.\r\n\r\nHotkeys may not work in that application. Restart the overlay as Administrator?";
                var result = MessageBox.Show(this, reason + "\r\n\r\nCurrent settings will be saved. Windows will ask for elevation.",
                    startup ? "Administrator rights" : "Administrator rights mismatch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
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
                deviceStatus.Text = (controller.Muted == true ? "MUTED" : "Mic ON") + (controller.HoldActive ? " - Hold active" : "");
                deviceStatus.ForeColor = controller.Muted == true ? Color.Firebrick : Color.DarkGreen;
                deviceDetails.Text = controller.HardwareMute ? "Windows reports hardware-backed mute." : "Windows software mute (shared audio mode).";
                if (!String.IsNullOrEmpty(controller.Error)) deviceDetails.Text += " " + controller.Error;
            } else {
                deviceStatus.Text = String.IsNullOrEmpty(settings.DeviceId) ? "Choose a microphone." : "Microphone unavailable - waiting for the selected device.";
                deviceStatus.ForeColor = Color.DarkOrange;
                deviceDetails.Text = controller.Error;
            }
            if (controller.HasPending && !controller.HoldActive)
                deviceDetails.Text = "Hold recovery pending for " + controller.Pending.DeviceName + ". Reconnect that microphone to restore its previous state. " + controller.Error;
            toggleNow.Enabled = controller.Ready && !controller.HoldActive && !controller.HasPending;
            trayMute.Enabled = toggleNow.Enabled;
            string tip = "APEIRON Global Mute - " + (controller.Ready ? (controller.Muted == true ? "MUTED" : "Mic ON") : "Device unavailable");
            if (controller.HoldActive) tip += " (Hold)";
            if (input.Paused) tip += " (Hotkeys paused)";
            tray.Text = tip;
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
                ApeironGlobalInput.Stop(); tray.Visible = false; tray.Dispose(); overlay.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class KeyCapture {
        internal static int Capture(Form owner, string title) {
            int result = 0, upTicks = 0; bool armed = false;
            using (var dialog = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(400, 150),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, KeyPreview = false })
            using (var timer = new Timer { Interval = 20 }) {
                var label = new Label { Text = "Release all keys, then press a keyboard or mouse button.", Dock = DockStyle.Top, Height = 85, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(12) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 28, Left = 155, Top = 100 };
                dialog.Controls.Add(label); dialog.Controls.Add(cancel); dialog.CancelButton = cancel;
                cancel.Click += delegate { result = 0; timer.Stop(); };
                timer.Tick += delegate {
                    if (!armed) {
                        bool any = false;
                        for (int k = 1; k <= 254; k++) if (ApeironGlobalInput.IsDown(k)) { any = true; break; }
                        upTicks = any ? 0 : upTicks + 1; armed = upTicks >= 3;
                        if (armed) label.Text = "Press the key or mouse button you want to use.";
                    } else if (result == 0) {
                        // Prefer the physical left/right modifier over its generic VK alias.
                        for (int k = 1; k <= 254; k++) {
                            if (k == 16 || k == 17 || k == 18) continue;
                            if (ApeironGlobalInput.IsDown(k)) { result = k; label.Text = MainForm.KeyName(k) + "\r\nRelease it to confirm."; break; }
                        }
                    } else if (!ApeironGlobalInput.IsDown(result)) { timer.Stop(); dialog.DialogResult = DialogResult.OK; dialog.Close(); }
                };
                timer.Start(); return dialog.ShowDialog(owner) == DialogResult.OK ? result : 0;
            }
        }
    }
}
