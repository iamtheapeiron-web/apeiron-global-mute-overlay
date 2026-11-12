using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Apeiron.GlobalMute;

// Layout harness only. The delivery MainForm never gets Shown: its hooks,
// device enumeration, UAC and timers do not start. Do not ship this executable.
internal static class Preview {
    private static void DisableExternalActions(Control control) {
        foreach (string text in new[] { "Save settings", "Hide to tray", "Exit", "Restart as Administrator", "Open settings folder", "Reset settings", "Website", "Support via PayPal" })
            if (control is Button && control.Text == Ui.Text(text)) control.Enabled = false;
        foreach (Control child in control.Controls) DisableExternalActions(child);
    }
    [STAThread]
    private static void Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var program = typeof(MainForm).Assembly.GetType("Apeiron.GlobalMute.Program");
        program.GetField("DataDir", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Path.GetFullPath(args[0]));
        var settings = new Settings();
        if (args.Length > 1 && args[1] == "pair") {
            settings.BlockingShortcutEnabled = true; settings.BlockingShortcutKey = 160; settings.BlockingShortcutSecondKey = 161;
        }
        using (var form = new MainForm(settings, false, ""))
        using (var shell = new Form { Text = form.Text + " (layout preview)", Icon = form.Icon, Font = form.Font,
            AutoScaleMode = AutoScaleMode.Dpi, ClientSize = form.ClientSize, MinimumSize = form.MinimumSize, StartPosition = FormStartPosition.CenterScreen }) {
            ((NotifyIcon)typeof(MainForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form)).Visible = false;
            program.GetField("SettingsPath", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Path.Combine(Path.GetFullPath(args[0]), "settings.xml"));
            foreach (Control root in form.Controls) DisableExternalActions(root);
            while (form.Controls.Count > 0) shell.Controls.Add(form.Controls[0]);
            shell.Resize += delegate { form.ClientSize = shell.ClientSize; };
            Application.Run(shell);
        }
    }
}
