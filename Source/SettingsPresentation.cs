using System;
using System.Reflection;

namespace Apeiron.GlobalMute {
    // Capture values without serialization or disk I/O. Only user settings count;
    // a device's refreshed display name and schema migration are not user edits.
    public sealed class SettingsChangeTracker {
        private static readonly FieldInfo[] Fields = typeof(Settings).GetFields(BindingFlags.Instance | BindingFlags.Public);
        private object[] saved;
        public SettingsChangeTracker(Settings settings) { Accept(settings); }
        public void Accept(Settings settings) {
            var values = new object[Fields.Length];
            for (int i = 0; i < Fields.Length; i++) values[i] = Fields[i].GetValue(settings);
            saved = values;
        }
        public bool HasChanges(Settings settings) {
            for (int i = 0; i < Fields.Length; i++) {
                if (Fields[i].Name == "SchemaVersion" || Fields[i].Name == "DeviceName") continue;
                if (!Object.Equals(saved[i], Fields[i].GetValue(settings))) return true;
            }
            return false;
        }
    }

    public sealed class OverlayRuleChoice {
        public int Value { get; private set; }
        private readonly string title;
        private OverlayRuleChoice(int value, string title) { Value = value; this.title = title; }
        public override string ToString() { return Ui.Text(title); }
        public static OverlayRuleChoice[] NormalChoices() {
            return new[] {
                new OverlayRuleChoice((int)NormalRule.OnChange, "Briefly on any change"),
                new OverlayRuleChoice((int)NormalRule.MutedOnly, "Briefly when muted"),
                new OverlayRuleChoice((int)NormalRule.UnmutedOnly, "Briefly when unmuted"),
                new OverlayRuleChoice((int)NormalRule.KeepMuted, "Keep visible while muted"),
                new OverlayRuleChoice((int)NormalRule.Hidden, "Do not show")
            };
        }
        public static OverlayRuleChoice[] HoldChoices() {
            return new[] {
                new OverlayRuleChoice((int)HoldRule.FollowNormal, "Same as normal mode"),
                new OverlayRuleChoice((int)HoldRule.Always, "Throughout the hold"),
                new OverlayRuleChoice((int)HoldRule.Hidden, "Do not show")
            };
        }
    }

    public static class InputStatusText {
        public static string Blocking(InputRouter input) {
            return Ui.Text(input.Paused ? "Key blocking paused" :
                !input.KeyBlockingEnabled ? "Key blocking off" :
                input.BlockToggleKey && input.BlockHoldKey ? "Both keys blocked" :
                input.BlockToggleKey ? "Blocked: Toggle" : input.BlockHoldKey ? "Blocked: Hold" : "No active keys to block");
        }
    }
}
