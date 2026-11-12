using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

public static class ApeironNative {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("shell32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
}


public static class ApeironGlobalInput {
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError=true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet=CharSet.Auto, SetLastError=true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static HookProc keyboardProc = KeyboardHook;
    private static HookProc mouseProc = MouseHook;
    private static IntPtr keyboardHook = IntPtr.Zero;
    private static IntPtr mouseHook = IntPtr.Zero;
    private static readonly Apeiron.GlobalMute.InputMailbox input = new Apeiron.GlobalMute.InputMailbox(new Apeiron.GlobalMute.MonotonicClock());
    private static readonly Apeiron.GlobalMute.KeySuppression suppression = new Apeiron.GlobalMute.KeySuppression();
    private static readonly Apeiron.GlobalMute.BlockingShortcut shortcut = new Apeiron.GlobalMute.BlockingShortcut();
    private static readonly uint ownProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
    private static volatile bool bypassSuppression;
    private static int toggleKey, holdKey;
    private static System.Threading.Thread worker;
    private static uint workerId;
    private static readonly System.Threading.ManualResetEvent ready = new System.Threading.ManualResetEvent(false);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG {
        public IntPtr hwnd; public uint message; public UIntPtr wParam;
        public IntPtr lParam; public uint time; public POINT pt; public uint lPrivate;
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint id, uint msg, UIntPtr wp, IntPtr lp);

    public static bool Start() {
        if (worker != null) return keyboardHook != IntPtr.Zero && mouseHook != IntPtr.Zero;
        ready.Reset();
        worker = new System.Threading.Thread(Pump);
        worker.IsBackground = true;
        worker.Start();
        ready.WaitOne(1500);
        return keyboardHook != IntPtr.Zero && mouseHook != IntPtr.Zero;
    }

    private static void Pump() {
        workerId = GetCurrentThreadId();
        IntPtr module = IntPtr.Zero;
        try {
            using (System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess()) {
                if (p.MainModule != null) module = GetModuleHandle(p.MainModule.ModuleName);
            }
        } catch {}
        keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
        mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
        ready.Set();
        try {
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) {
                TranslateMessage(ref msg); DispatchMessage(ref msg);
            }
        } finally {
            if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            keyboardHook = mouseHook = IntPtr.Zero;
        }
    }

    public static void Stop() {
        if (worker != null) {
            PostThreadMessage(workerId, 0x0012, UIntPtr.Zero, IntPtr.Zero);
            worker.Join(1500); worker = null;
        }
        ClearPresses();
    }

    public static bool IsDown(int vk) {
        if (vk <= 0 || vk >= 256) return false;
        // Suppressed downs may never appear in GetAsyncKeyState. The hook owns their state.
        if (suppression.IsBlockedDown(vk) || shortcut.IsConsumedDown(vk)) return true;
        // Repair missed key-ups for pass-through input. Suppressed input uses hook-owned state above.
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public static void ConfigureWakeup(int toggle, int hold, bool toggleEnabled, bool holdEnabled, bool blockToggle, bool blockHold, Action wakeup) {
        ConfigureWakeup(toggle, hold, toggleEnabled, holdEnabled, blockToggle, blockHold,
            0, Apeiron.GlobalMute.ShortcutModifiers.None, false, wakeup);
    }
    public static bool IsActionKeyDown(int vk) { return shortcut.IsActionDown(vk, IsDown(vk)); }
    public static Apeiron.GlobalMute.ShortcutModifiers ReadModifiers() {
        var mods = Apeiron.GlobalMute.ShortcutModifiers.None;
        if (IsDown(162) || IsDown(163)) mods |= Apeiron.GlobalMute.ShortcutModifiers.Control;
        if (IsDown(164) || IsDown(165)) mods |= Apeiron.GlobalMute.ShortcutModifiers.Alt;
        if (IsDown(160) || IsDown(161)) mods |= Apeiron.GlobalMute.ShortcutModifiers.Shift;
        if (IsDown(91) || IsDown(92)) mods |= Apeiron.GlobalMute.ShortcutModifiers.Windows;
        return mods;
    }
    public static void ConfigureWakeup(int toggle, int hold, bool toggleEnabled, bool holdEnabled, bool blockToggle, bool blockHold,
        int shortcutKey, Apeiron.GlobalMute.ShortcutModifiers modifiers, bool shortcutEnabled, Action wakeup) {
        ConfigureWakeup(toggle, hold, toggleEnabled, holdEnabled, blockToggle, blockHold, shortcutKey, modifiers, 0, shortcutEnabled, wakeup);
    }
    public static void ConfigureWakeup(int toggle, int hold, bool toggleEnabled, bool holdEnabled, bool blockToggle, bool blockHold,
        int shortcutKey, Apeiron.GlobalMute.ShortcutModifiers modifiers, int shortcutSecondKey, bool shortcutEnabled, Action wakeup) {
        toggleKey = toggle; holdKey = hold;
        shortcut.Configure(shortcutKey, modifiers, shortcutSecondKey, shortcutEnabled, IsDown(shortcutKey), IsDown(shortcutSecondKey));
        bool td = IsActionKeyDown(toggle), hd = IsActionKeyDown(hold);
        suppression.Configure(toggle, hold, toggleEnabled, holdEnabled, blockToggle, blockHold, td, hd);
        input.Configure(toggle, hold, td, hd, wakeup);
    }
    public static void UpdateBlocking(bool blockToggle, bool blockHold) { suppression.UpdateBlocking(blockToggle, blockHold); }
    public static void SetSuppressionBypass(bool bypass) { bypassSuppression = bypass; }

    public static Apeiron.GlobalMute.InputSnapshot ConsumeSnapshot(bool reconcile) {
        return input.Take(reconcile, reconcile && IsActionKeyDown(toggleKey), reconcile && IsActionKeyDown(holdKey));
    }

    public static void ClearPresses() {
        input.ClearPresses();
    }

    private static bool SetState(int vk, bool down) {
        bool passThrough = bypassSuppression;
        if (!passThrough) {
            IntPtr window = ApeironNative.GetForegroundWindow(); uint pid;
            passThrough = window != IntPtr.Zero && ApeironNative.GetWindowThreadProcessId(window, out pid) != 0 && pid == ownProcessId;
        }
        var modifiers = shortcut.NeedsModifiers(vk, down) ? ReadModifiers() : Apeiron.GlobalMute.ShortcutModifiers.None;
        int partner = shortcut.PartnerToCheck(vk, down);
        var command = shortcut.Process(vk, down, modifiers, passThrough, !bypassSuppression,
            partner != 0 ? (bool?)IsDown(partner) : null);
        bool block = suppression.Process(vk, down, passThrough || command.SkipActions);
        if (!command.SkipActions) input.Record(vk, down);
        if (command.Triggered) input.RequestBlockingToggle();
        return block || command.Suppress;
    }

    private static IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            int msg = wParam.ToInt32();
            int vk = Marshal.ReadInt32(lParam);
            if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && SetState(vk, true)) return new IntPtr(1);
            else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && SetState(vk, false)) return new IntPtr(1);
        }
        return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
    }

    private static IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            int msg = wParam.ToInt32();
            int vk = 0; bool down = false;
            if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP) { vk = 0x01; down = msg == WM_LBUTTONDOWN; }
            else if (msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP) { vk = 0x02; down = msg == WM_RBUTTONDOWN; }
            else if (msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP) { vk = 0x04; down = msg == WM_MBUTTONDOWN; }
            else if (msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP) {
                MSLLHOOKSTRUCT info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                int xButton = (int)((info.mouseData >> 16) & 0xffff);
                if (xButton == 1 || xButton == 2) vk = xButton == 1 ? 0x05 : 0x06;
                down = msg == WM_XBUTTONDOWN;
            }
            if (vk != 0 && SetState(vk, down)) return new IntPtr(1);
        }
        return CallNextHookEx(mouseHook, nCode, wParam, lParam);
    }
}


public static class ApeironProcessSecurity {
    private const UInt32 PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const UInt32 TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION {
        public int TokenIsElevated;
    }

    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern IntPtr OpenProcess(UInt32 dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, UInt32 DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
        IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    public static bool? GetElevation(int pid) {
        IntPtr process = IntPtr.Zero;
        IntPtr token = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try {
            process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return null;
            if (!OpenProcessToken(process, TOKEN_QUERY, out token)) return null;
            int size = Marshal.SizeOf(typeof(TOKEN_ELEVATION));
            buffer = Marshal.AllocHGlobal(size);
            int ret;
            if (!GetTokenInformation(token, TokenElevation, buffer, size, out ret)) return null;
            TOKEN_ELEVATION elevation = (TOKEN_ELEVATION)Marshal.PtrToStructure(buffer, typeof(TOKEN_ELEVATION));
            return elevation.TokenIsElevated != 0;
        } catch {
            return null;
        } finally {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (token != IntPtr.Zero) CloseHandle(token);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }
    public static bool IsElevated(int pid) { return GetElevation(pid) == true; }
}

public class ApeironOverlayForm : Form {
    public Color FillColor = Color.Red;
    public Color IconColor = Color.White;
    public string ShapeName = "Circle";
    public string IconKind = "Muted";
    public bool PlacementMode = false;

    private Point dragStart;
    private bool dragging = false;

    public ApeironOverlayForm() {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        DoubleBuffered = true;
        Width = 72;
        Height = 72;
        Opacity = 0.88;
        UpdateRegion();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams {
        get {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= ApeironNative.WS_EX_TOOLWINDOW | ApeironNative.WS_EX_NOACTIVATE;
            if (!PlacementMode) cp.ExStyle |= ApeironNative.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    public void ForceTopMost() {
        if (IsHandleCreated) {
            ApeironNative.SetWindowPos(
                Handle, ApeironNative.HWND_TOPMOST, 0,0,0,0,
                ApeironNative.SWP_NOMOVE | ApeironNative.SWP_NOSIZE |
                ApeironNative.SWP_NOACTIVATE | ApeironNative.SWP_SHOWWINDOW
            );
        }
    }

    public void SetPlacementMode(bool enabled) {
        PlacementMode = enabled;
        if (!IsHandleCreated) return;
        int ex = ApeironNative.GetWindowLong(Handle, ApeironNative.GWL_EXSTYLE);
        if (enabled) ex &= ~ApeironNative.WS_EX_TRANSPARENT;
        else ex |= ApeironNative.WS_EX_TRANSPARENT;
        ex |= ApeironNative.WS_EX_TOOLWINDOW | ApeironNative.WS_EX_NOACTIVATE;
        ApeironNative.SetWindowLong(Handle, ApeironNative.GWL_EXSTYLE, ex);
        Cursor = enabled ? Cursors.SizeAll : Cursors.Default;
        Invalidate();
    }

    public void UpdateRegion() {
        if (Width <= 0 || Height <= 0) return;
        Rectangle r = new Rectangle(0, 0, Width, Height);
        GraphicsPath gp = new GraphicsPath();
        if (ShapeName == "Square") {
            gp.AddRectangle(r);
        } else if (ShapeName == "Rounded square") {
            int radius = Math.Max(8, Math.Min(Width, Height) / 5);
            int d = radius * 2;
            gp.AddArc(0, 0, d, d, 180, 90);
            gp.AddArc(Width-d-1, 0, d, d, 270, 90);
            gp.AddArc(Width-d-1, Height-d-1, d, d, 0, 90);
            gp.AddArc(0, Height-d-1, d, d, 90, 90);
            gp.CloseFigure();
        } else {
            gp.AddEllipse(r);
        }
        Region old = Region;
        Region = new Region(gp);
        if (old != null) old.Dispose();
        gp.Dispose();
        Invalidate();
    }

    protected override void OnResize(EventArgs e) {
        base.OnResize(e);
        UpdateRegion();
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (PlacementMode && e.Button == MouseButtons.Left) {
            dragging = true;
            dragStart = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (PlacementMode && dragging) {
            Point screen = PointToScreen(e.Location);
            Location = new Point(screen.X - dragStart.X, screen.Y - dragStart.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        dragging = false;
    }

    private void DrawMic(Graphics g, bool slash) {
        float w = Width;
        float h = Height;
        float s = Math.Min(w,h);
        float lw = Math.Max(2f, s * 0.055f);

        using (Pen p = new Pen(IconColor, lw)) {
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;

            RectangleF capsule = new RectangleF(w*0.39f, h*0.22f, w*0.22f, h*0.34f);
            using (GraphicsPath mic = new GraphicsPath()) {
                float rad = capsule.Width/2f;
                mic.AddArc(capsule.X, capsule.Y, capsule.Width, capsule.Width, 180, 180);
                mic.AddLine(capsule.Right, capsule.Y+rad, capsule.Right, capsule.Bottom-rad);
                mic.AddArc(capsule.X, capsule.Bottom-capsule.Width, capsule.Width, capsule.Width, 0, 180);
                mic.AddLine(capsule.X, capsule.Bottom-rad, capsule.X, capsule.Y+rad);
                g.DrawPath(p, mic);
            }

            g.DrawArc(p, w*0.30f, h*0.35f, w*0.40f, h*0.30f, 0, 180);
            g.DrawLine(p, w*0.50f, h*0.65f, w*0.50f, h*0.76f);
            g.DrawLine(p, w*0.39f, h*0.76f, w*0.61f, h*0.76f);

            if (slash) {
                using (Pen sp = new Pen(IconColor, lw*1.25f)) {
                    sp.StartCap = LineCap.Round; sp.EndCap = LineCap.Round;
                    g.DrawLine(sp, w*0.25f, h*0.23f, w*0.75f, h*0.78f);
                }
            }

        }
    }

    protected override void OnPaint(PaintEventArgs e) {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(FillColor);

        DrawMic(g, IconKind == "Muted");

        if (PlacementMode) {
            using (Pen p = new Pen(Color.White, Math.Max(1, Width/48f))) {
                p.DashStyle = DashStyle.Dash;
                g.DrawRectangle(p, 2, 2, Width-5, Height-5);
            }
        }
    }
}
