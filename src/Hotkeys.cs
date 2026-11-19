// Hallow Overlay - your own hotkeys: every action's key combo can be changed, turned off or put back to the default.
// Registered with Windows (RegisterHotKey) on the main window's handle; a combo another app already owns is refused and the old one is kept.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace HallowOverlay
{
    public class HotkeyAction
    {
        public string Id, Name, Default;   // Default "" = off until you set one
        public HotkeyAction(string id, string name, string def) { Id = id; Name = name; Default = def; }
    }

    // One key combo: modifiers (MOD_ALT 1, MOD_CONTROL 2, MOD_SHIFT 4, MOD_WIN 8) and a virtual key.
    public struct Combo
    {
        public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;
        public uint Mods, Vk;
        public bool IsOff { get { return Vk == 0; } }

        public override string ToString()
        {
            if (Vk == 0) return "";
            var p = new List<string>();
            if ((Mods & Ctrl) != 0) p.Add("Ctrl"); if ((Mods & Alt) != 0) p.Add("Alt"); if ((Mods & Shift) != 0) p.Add("Shift"); if ((Mods & Win) != 0) p.Add("Win");
            p.Add(KeyName(Vk)); return string.Join("+", p);
        }

        // "Ctrl+Shift+O", "Alt+F10", "" / "off" = off. Returns false for anything it can't read.
        public static bool TryParse(string s, out Combo c)
        {
            c = new Combo(); if (string.IsNullOrWhiteSpace(s) || s.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)) return true;
            var parts = s.Split('+').Select(x => x.Trim()).ToList();
            if (parts.Count == 0 || parts.Any(x => x.Length == 0)) return false;
            for (int i = 0; i < parts.Count - 1; i++)
            {
                var m = parts[i].ToLowerInvariant();
                if (m == "ctrl" || m == "control") c.Mods |= Ctrl; else if (m == "alt") c.Mods |= Alt; else if (m == "shift") c.Mods |= Shift; else if (m == "win") c.Mods |= Win; else return false;
            }
            var vk = VkFromName(parts[parts.Count - 1]); if (vk == 0) return false;
            c.Vk = vk; return true;
        }
        public static Combo Parse(string s) { Combo c; return TryParse(s, out c) ? c : new Combo(); }

        // What the capture box says is wrong with a combo, or null if it can be used.
        public string Problem()
        {
            if (Vk == 0) return null;
            bool fkey = Vk >= 0x70 && Vk <= 0x87, special = Vk == 0x13 || Vk == 0x91 || Vk == 0x2C;   // F1-F24, Pause, Scroll Lock, Print Screen
            if (Mods == 0 && !fkey && !special) return KeyName(Vk) + " on its own would stop that key from typing - add Ctrl, Alt or Shift";
            if (Mods == Shift && !fkey && !special) return "Shift+" + KeyName(Vk) + " is a normal typed character - add Ctrl or Alt";
            if (Mods == Alt && Vk == 0x73) return "Alt+F4 closes windows - pick another combo";
            if (Mods == Ctrl && (Vk == 0x43 || Vk == 0x56 || Vk == 0x58 || Vk == 0x5A || Vk == 0x41 || Vk == 0x53)) return "Ctrl+" + KeyName(Vk) + " is used by almost every app - add Shift or Alt";
            return null;
        }

        static readonly Dictionary<uint, string> names = new Dictionary<uint, string> {
            { 0x08, "Backspace" }, { 0x09, "Tab" }, { 0x0D, "Enter" }, { 0x13, "Pause" }, { 0x14, "CapsLock" }, { 0x1B, "Esc" }, { 0x20, "Space" },
            { 0x21, "PageUp" }, { 0x22, "PageDown" }, { 0x23, "End" }, { 0x24, "Home" }, { 0x25, "Left" }, { 0x26, "Up" }, { 0x27, "Right" }, { 0x28, "Down" },
            { 0x2C, "PrintScreen" }, { 0x2D, "Insert" }, { 0x2E, "Delete" }, { 0x91, "ScrollLock" }, { 0x90, "NumLock" },
            { 0x6A, "Num*" }, { 0x6B, "Num+" }, { 0x6D, "Num-" }, { 0x6E, "Num." }, { 0x6F, "Num/" },
            { 0xBA, ";" }, { 0xBB, "=" }, { 0xBC, "," }, { 0xBD, "-" }, { 0xBE, "." }, { 0xBF, "/" }, { 0xC0, "`" }, { 0xDB, "[" }, { 0xDC, "\\" }, { 0xDD, "]" }, { 0xDE, "'" } };
        public static string KeyName(uint vk)
        {
            if (vk >= 0x30 && vk <= 0x39 || vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);
            string n; return names.TryGetValue(vk, out n) ? n : "Key" + vk.ToString("X2");
        }
        static uint VkFromName(string n)
        {
            if (n.Length == 1) { var ch = char.ToUpperInvariant(n[0]); if (ch >= '0' && ch <= '9' || ch >= 'A' && ch <= 'Z') return ch; }
            int f; if (n.Length > 1 && (n[0] == 'F' || n[0] == 'f') && int.TryParse(n.Substring(1), out f) && f >= 1 && f <= 24) return (uint)(0x6F + f);
            if (n.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && n.Length == 4 && char.IsDigit(n[3])) return (uint)(0x60 + (n[3] - '0'));
            foreach (var kv in names) if (string.Equals(kv.Value, n, StringComparison.OrdinalIgnoreCase)) return kv.Key;
            if (n.StartsWith("Key", StringComparison.OrdinalIgnoreCase) && n.Length == 5) { uint v; if (uint.TryParse(n.Substring(3), System.Globalization.NumberStyles.HexNumber, null, out v) && v > 0 && v < 0xFF) return v; }
            return 0;
        }

        // From a WPF key press: null while only modifiers are down (the box shows them as you go).
        public static Combo? FromKeys(Key key, ModifierKeys mods)
        {
            var c = new Combo();
            if ((mods & ModifierKeys.Control) != 0) c.Mods |= Ctrl; if ((mods & ModifierKeys.Alt) != 0) c.Mods |= Alt;
            if ((mods & ModifierKeys.Shift) != 0) c.Mods |= Shift; if ((mods & ModifierKeys.Windows) != 0) c.Mods |= Win;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin || key == Key.None) return null;
            c.Vk = (uint)KeyInterop.VirtualKeyFromKey(key); if (c.Vk == 0) return null;
            return c;
        }
        public static string ModsText(ModifierKeys mods)
        {
            var p = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) p.Add("Ctrl"); if ((mods & ModifierKeys.Alt) != 0) p.Add("Alt"); if ((mods & ModifierKeys.Shift) != 0) p.Add("Shift"); if ((mods & ModifierKeys.Windows) != 0) p.Add("Win");
            return p.Count == 0 ? "" : string.Join("+", p) + "+";
        }
        public bool Same(Combo o) { return Vk != 0 && Vk == o.Vk && Mods == o.Mods; }
    }

    public class HotkeySet
    {
        public static readonly HotkeyAction[] Actions = {
            new HotkeyAction("toggle", "Show / hide the overlay", "Ctrl+Shift+O"),
            new HotkeyAction("bench", "Start / stop a benchmark", "Ctrl+Shift+F9"),
            new HotkeyAction("only-bad", "Only when something's wrong (on / off)", ""),
            new HotkeyAction("settings", "Open Overlay's settings", ""),
        };

        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr h, int id);
        const uint NoRepeat = 0x4000;
        const int IdBase = 0x4800;

        readonly IntPtr hwnd;
        readonly Dictionary<string, Combo> live = new Dictionary<string, Combo>();   // what is registered with Windows now
        public Dictionary<string, string> Wanted;                                     // the settings' combos (by action id)
        public HotkeySet(IntPtr hwnd, Dictionary<string, string> wanted) { this.hwnd = hwnd; Wanted = wanted ?? new Dictionary<string, string>(); }

        public static HotkeyAction Find(string id) { return Actions.FirstOrDefault(a => a.Id == id); }
        public static int IdOf(string action) { return IdBase + Array.FindIndex(Actions, a => a.Id == action); }
        public static string ActionOf(int id) { int i = id - IdBase; return i >= 0 && i < Actions.Length ? Actions[i].Id : null; }

        // The combo the settings ask for (the default when the settings don't mention the action).
        public Combo ComboFor(string id) { string s; return Combo.Parse(Wanted.TryGetValue(id, out s) ? s : Find(id).Default); }
        // "Ctrl+Shift+O", or "" when off - for menus and messages.
        public string Text(string id) { return ComboFor(id).ToString(); }
        public bool Registered(string id) { Combo c; return live.TryGetValue(id, out c) && !c.IsOff; }
        public bool Live { get { return hwnd != IntPtr.Zero; } }   // false: names only (--snap), nothing registered

        // Register every action's combo. Returns what couldn't be registered (action id -> reason).
        public Dictionary<string, string> RegisterAll()
        {
            var failed = new Dictionary<string, string>();
            if (!Live) return failed;
            foreach (var a in Actions)
            {
                var c = ComboFor(a.Id); if (c.IsOff) continue;
                var other = Actions.FirstOrDefault(o => o.Id != a.Id && Registered(o.Id) && live[o.Id].Same(c));
                if (other != null) { failed[a.Id] = c + " is already used for \"" + other.Name + "\""; continue; }
                if (RegisterHotKey(hwnd, IdOf(a.Id), c.Mods | NoRepeat, c.Vk)) live[a.Id] = c;
                else failed[a.Id] = TakenMessage(c, Marshal.GetLastWin32Error());
            }
            return failed;
        }
        public void UnregisterAll() { foreach (var id in live.Keys.ToList()) { UnregisterHotKey(hwnd, IdOf(id)); } live.Clear(); }

        static string TakenMessage(Combo c, int err)
        {
            return err == 1409 ? c + " is already taken by another app (Windows gives each combo to one app) - pick another one or close that app"
                : "Windows didn't accept " + c + " (error " + err + ")";
        }

        // Change one action's combo (empty Combo = off). The old one stays when the new one can't be used.
        // Returns null when it worked, or the message to show.
        public string Change(string id, Combo c)
        {
            var a = Find(id); if (a == null) return "Unknown hotkey";
            var prob = c.Problem(); if (prob != null) return prob;
            if (!c.IsOff)
            {
                var other = Actions.FirstOrDefault(o => o.Id != id && ComboFor(o.Id).Same(c));
                if (other != null) return c + " is already the hotkey for \"" + other.Name + "\" - change or turn that one off first";
            }
            if (!Live) { Wanted[id] = c.ToString(); return null; }
            Combo old; bool had = live.TryGetValue(id, out old) && !old.IsOff;
            if (had && old.Same(c)) { Wanted[id] = c.ToString(); return null; }
            if (c.IsOff) { if (had) UnregisterHotKey(hwnd, IdOf(id)); live.Remove(id); Wanted[id] = ""; return null; }
            if (had) UnregisterHotKey(hwnd, IdOf(id));
            if (RegisterHotKey(hwnd, IdOf(id), c.Mods | NoRepeat, c.Vk)) { live[id] = c; Wanted[id] = c.ToString(); return null; }
            int err = Marshal.GetLastWin32Error();
            bool back = had && RegisterHotKey(hwnd, IdOf(id), old.Mods | NoRepeat, old.Vk);   // put the old one back
            if (!back) live.Remove(id);
            return TakenMessage(c, err) + (back ? ". " + old + " still works." : ".");
        }

        // Every action back to its default; returns the messages for any default another app owns.
        public Dictionary<string, string> ResetDefaults()
        {
            var before = new Dictionary<string, Combo>(live);
            UnregisterAll(); Wanted.Clear();
            var failed = RegisterAll();
            foreach (var id in failed.Keys.ToList())
            {
                Combo old; if (!before.TryGetValue(id, out old) || old.IsOff || Actions.Any(o => o.Id != id && ComboFor(o.Id).Same(old))) continue;
                if (RegisterHotKey(hwnd, IdOf(id), old.Mods | NoRepeat, old.Vk)) { live[id] = old; Wanted[id] = old.ToString(); failed[id] += ". " + old + " still works."; }
            }
            return failed;
        }
    }
}
