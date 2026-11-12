using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Apeiron.GlobalMute {
    internal static class Program {
        internal const string Name = "APEIRON Global Mute Overlay";
        internal const string Version = "0.1.0-beta.3";
        internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "APEIRON_Global_Mute_Overlay");
        internal static readonly string SettingsPath = Path.Combine(DataDir, "settings.xml");
        internal static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }
        [STAThread]
        private static void Main(string[] args) {
            bool tray = false; int wait = 0;
            try {
                for (int i = 0; i < args.Length; i++) {
                    if (args[i] == "--tray") tray = true;
                    else if (args[i] == "--wait-for" && i + 1 < args.Length) wait = Int32.Parse(args[++i]);
                    else throw new ArgumentException(Ui.Text("Unknown option: ") + args[i]);
                }
                if (wait > 0) {
                    try { using (var process = Process.GetProcessById(wait)) {
                        if (!process.WaitForExit(20000)) throw new InvalidOperationException(Ui.Text("The previous instance is still running."));
                    } } catch (ArgumentException) { }
                }
                string sid = WindowsIdentity.GetCurrent().User.Value;
                using (var mutex = new Mutex(false, "Local\\APEIRON.GlobalMuteOverlay." + sid)) {
                    bool own;
                    try { own = mutex.WaitOne(0); } catch (AbandonedMutexException) { own = true; }
                    if (!own) { MessageBox.Show(Ui.Text("Already running. Open Settings from the APEIRON tray icon."), Name); return; }
                    try {
                        Directory.CreateDirectory(DataDir);
                        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                        try { ApeironNative.SetCurrentProcessExplicitAppUserModelID("APEIRON.GlobalMuteOverlay"); } catch { }
                        Settings settings = new Settings(); string notice = "";
                        if (File.Exists(SettingsPath)) {
                            try { settings = XmlFiles.Read<Settings>(SettingsPath); }
                            catch (Exception ex) { notice = Ui.Text("Saved settings could not be read. Defaults loaded; your previous file will be kept as a backup when you save."); Log(ex); }
                        }
                        settings.Normalize();
                        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) {
                            Log(e.Exception);
                            MessageBox.Show(Ui.Text("The application needs to close. ") + e.Exception.Message + Ui.Text("\r\n\r\nDetails: ") + Path.Combine(DataDir, "last-error.log"), Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            Application.Exit();
                        };
                        using (var form = new MainForm(settings, tray, notice)) Application.Run(form);
                    } finally { mutex.ReleaseMutex(); }
                }
            } catch (Exception ex) {
                Log(ex); MessageBox.Show(ex.Message, Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        internal static void Log(Exception ex) {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(Path.Combine(DataDir, "last-error.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine); } catch { }
        }
    }

    internal static class Startup {
        private const string Key = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        internal static void Set(bool enabled) {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key)) {
                if (enabled) key.SetValue(Program.Name, "\"" + Program.ExePath + "\" --tray", RegistryValueKind.String);
                else key.DeleteValue(Program.Name, false);
            }
        }
    }
}
