// Hallow Overlay 1.20 - the light look of the settings windows (Look.cs decides light or dark). The in-game panel, the move-mode
// card and saved benchmark pictures never use this: they stay dark (see-through over games).
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace HallowOverlay
{
    public static class Pal
    {
        // dark colour (Theme.xaml and code) -> light colour. Accent colours are not here: they're handled by Accent / Look.TextAccent.
        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "#16121C", Look.LBg },      // window background
            { "#211B2A", Look.LPanel },   // panels, search box
            { "#2E2639", Look.LPanel2 },  // raised panel
            { "#2A2333", Look.LPanel },   // buttons, combo box
            { "#241E2E", Look.LPanel },   // drop-downs, menus, tooltips
            { "#1A1522", Look.LPanel },   // text fields
            { "#1C1724", Look.LPanel },   // cards (layout rows, matches)
            { "#2A2236", Look.LPanel2 },  // update row
            { "#2E2440", Look.LPanel2 },  // selected match
            { "#3A3248", Look.LBorder },  // lines, borders, fader track, highlighted item
            { "#2C2536", Look.LBorder },  // card borders
            { "#55496A", "#B4A9C7" },     // hover border
            { "#58566A", "#B4ADC2" },     // switch track (off)
            { "#CDCAD6", "#FFFFFF" },     // switch knob (off)
            { "#6ED28C", Look.LGreen },   // switch track (on)
            { "#ECE8F4", Look.LText },    // text
            { "#8F88A0", Look.LMuted },   // muted text
            { "#A98BE0", Look.LLilac },   // lilac
            { "#C8414B", "#F8DADD" },     // muted (red) toggle: a pale red with dark text
            { "#E05A64", Look.LRed },     // ... and its red border
            { "#FF6B6B", Look.LRed },     // red lines
            { "#F0BE5A", AmberText },     // amber notes (small text)
        };
        // the spec's amber (#A66A00) is under 4.5:1 on the light background, so small amber TEXT uses it darkened the same way as the accent
        public static string AmberText { get { return Look.TextAccent(Look.LAmber); } }

        public static string Hex(string darkHex) { return Look.Pick(darkHex, Map); }
        public static SolidColorBrush B(string darkHex) { return new SolidColorBrush(Look.C(Hex(darkHex))); }

        // Theme.xaml for the current mode and accent
        public static string Theme(string xaml, string accent, bool light)
        {
            xaml = xaml.Replace("\r\n", "\n");
            if (light)
            {
                var ink = Look.TextAccent(accent);
                xaml = xaml.Replace("<Setter Property=\"CaretBrush\" Value=\"#F07820\"/>", "<Setter Property=\"CaretBrush\" Value=\"" + ink + "\"/>")
                           .Replace("Property=\"BorderBrush\" Value=\"#F07820\"/>", "Property=\"BorderBrush\" Value=\"" + ink + "\"/>")   // focus line
                           .Replace("<Border x:Name=\"hl\" Background=\"#FFFFFF\"", "<Border x:Name=\"hl\" Background=\"#1E1A26\"")          // hover: darken, not lighten
                           .Replace("Fill=\"#ECE8F4\" Stroke=\"#16121C\" StrokeThickness=\"3\"", "Fill=\"#FFFFFF\" Stroke=\"#8A80A0\" StrokeThickness=\"1.5\"")      // fader knob
                           .Replace("<Setter TargetName=\"k\" Property=\"Fill\" Value=\"#FFFFFF\"/>", "<Setter TargetName=\"k\" Property=\"Fill\" Value=\"#EEEAF5\"/>");
                xaml = Look.Swap(xaml, Map);
                // 1.21: filled accent buttons and the "on" chips get the family light fill + its text colour (after the swap, so an accent never gets swapped; violet had dark text at 4.47:1)
                var fill = Look.LightFill(accent); var fillText = Look.FillText(fill);
                xaml = xaml.Replace("<Setter Property=\"Background\" Value=\"#F07820\"/>", "<Setter Property=\"Background\" Value=\"" + fill + "\"/>")   // Accent button
                           .Replace("<SolidColorBrush x:Key=\"CheckedBg\" Color=\"#F07820\"/>", "<SolidColorBrush x:Key=\"CheckedBg\" Color=\"" + fill + "\"/>")   // chip on
                           .Replace("Value=\"#1A0E04\"", "Value=\"" + fillText + "\"");
            }
            return Accent.Themed(xaml, accent);
        }
    }
}
