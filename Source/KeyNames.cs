using System;
using System.Text;

namespace Apeiron.GlobalMute {
    public static class KeyNames {
        // OEM punctuation varies by layout. These labels explicitly use US legends,
        // while the stored virtual key and all input routing remain unchanged.
        public static string Format(int key, string systemName) {
            if (key >= 48 && key <= 57 || key >= 65 && key <= 90) return ((char)key).ToString();
            if (key >= 96 && key <= 105) return Ui.Text("Numpad ") + (key - 96);
            if (key >= 112 && key <= 135) return "F" + (key - 111);
            switch (key) {
                case 1: return Ui.Text("Mouse1 / Left"); case 2: return Ui.Text("Mouse2 / Right");
                case 3: return "Ctrl + Break"; case 4: return Ui.Text("Mouse3 / Middle");
                case 5: return Ui.Text("Mouse4 / X1"); case 6: return Ui.Text("Mouse5 / X2");
                case 8: return "Backspace"; case 9: return "Tab"; case 12: return "Clear";
                case 13: return "Enter"; case 16: return "Shift"; case 17: return "Ctrl";
                case 18: return "Alt"; case 19: return "Pause / Break"; case 20: return "Caps Lock";
                case 27: return "Esc"; case 32: return Ui.Text("Space");
                case 33: return "Page Up"; case 34: return "Page Down";
                case 35: return "End"; case 36: return "Home";
                case 37: return Ui.Text("Left Arrow"); case 38: return Ui.Text("Up Arrow");
                case 39: return Ui.Text("Right Arrow"); case 40: return Ui.Text("Down Arrow");
                case 44: return "Print Screen"; case 45: return "Insert"; case 46: return "Delete";
                case 91: return Ui.Text("Left Windows"); case 92: return Ui.Text("Right Windows");
                case 93: return Ui.Text("Menu"); case 95: return Ui.Text("Sleep");
                case 106: return Ui.Text("Numpad *"); case 107: return Ui.Text("Numpad +");
                case 108: return Ui.Text("Numpad Separator"); case 109: return Ui.Text("Numpad -");
                case 110: return Ui.Text("Numpad Decimal"); case 111: return Ui.Text("Numpad /");
                case 144: return "Num Lock"; case 145: return "Scroll Lock";
                case 160: return Ui.Text("Left Shift"); case 161: return Ui.Text("Right Shift");
                case 162: return Ui.Text("Left Ctrl"); case 163: return Ui.Text("Right Ctrl");
                case 164: return Ui.Text("Left Alt"); case 165: return Ui.Text("Right Alt");
                case 186: return "; / : (US)"; case 187: return "= / + (US)";
                case 188: return ", / < (US)"; case 189: return "- / _ (US)";
                case 190: return ". / > (US)"; case 191: return "/ / ? (US)";
                case 192: return "` / ~ (US)";
                case 219: return "[ / { (US)"; case 220: return "\\ / | (US)";
                case 221: return "] / } (US)"; case 222: return "' / \" (US)";
            }
            if (String.IsNullOrEmpty(systemName) || systemName == key.ToString())
                return Ui.Text("Key 0x") + key.ToString("X2");
            var label = new StringBuilder();
            for (int i = 0; i < systemName.Length; i++) {
                if (i > 0 && Char.IsUpper(systemName[i]) && Char.IsLower(systemName[i - 1])) label.Append(' ');
                label.Append(systemName[i]);
            }
            return label.ToString();
        }
    }
}
