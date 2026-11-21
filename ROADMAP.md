# Hallow Overlay roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | See-through panel you can click through: big MIC MUTED warning (from Hallow Mixer), CPU, GPU load and temperature, RAM, ping, session time; corner and size choices; Ctrl+Shift+O shows and hides it |
| **1.1** | Tells you when a game is in exclusive fullscreen (use borderless); Hallow family link: named pipe with show / hide / toggle / flash / remind / game-on / game-off, `--cmd`, status.json; What's new window |
| **1.2** | FPS and 1% lows from Windows' own frame events (the ETW events PresentMon reads: DXGI / D3D9 presents) - no admin prompt; works for admins and the Performance Log Users group, hidden otherwise |
| **1.3** | Own corner, size and stats per game; only show a stat while it's in trouble (your own limits for GPU temperature, ping, RAM); packet loss and jitter next to ping |
| **1.4** | "Clip saved" flash from Hallow Clips and Hallow Hub's break reminder / daily limit on the panel (through the 1.1 link); now playing from Hallow Mixer; MIC MUTED at once with Mixer's long poll (3.14+) |
| **1.5** | The game's server from its own network traffic (Windows' Winsock events, no admin prompt; 1.1.1.1 ping when Windows doesn't allow it): region from its name, ping (or the nearest hop, ~), loss, jitter and stalls; "Server changed"; the game's up / down traffic; IP hidden by default |
| **1.6** | One Hallow icon: Overlay's menu in Hallow Hub's tray icon (open / menu / invoke / quit over the link), own icon hidden while Hub hosts it and back when Hub is gone; messages through Hub; "Use the Hallow icon" setting |
| **1.7** | Match summary for every game session (FPS, ping, loss, stalls, servers / regions, temperatures, load, RAM) in matches.json; one-line summary on the panel when the game closes; Match history window with a per-game filter and a Servers view; last 5 in status.json |
| **1.8** | Hallow accent colour from Hallow Toolkit (live, "Use the Hallow accent"; warnings stay red / amber); crash reports kept on this PC with a notice next time and `lastCrash` in status - nothing sent anywhere |
| **1.9** | GPU core / memory clocks, power draw (W and % of the limit) and VRAM; frame-time graph of the last 5 s with spikes in red (Small / Medium / Large, one geometry per refresh); average GPU power and peak VRAM in match summaries; nvidia-smi always ends with Overlay |
| **1.10** | Live FPS, 1% low, ping and server region (never the IP) in status for Hallow Remote's Live stats tab - every 2 s while a game runs, cleared when it ends |
| **1.11** | Updates itself from GitHub (asks first, SHA-256 checked); FPS for Vulkan / OpenGL games from the graphics kernel's present events (no admin); 0.1% lows |
| **1.12** | Benchmark mode (Ctrl+Shift+F9): average FPS, 1% / 0.1% lows, frame-time percentiles, stutters and a chart, saved as PNG + CSV; optional CSV log of every stat |
| **1.13** | Layout editor: order of the stats, own labels, a colour per stat, text or text + a small graph of the last minute, text size (per game too) |
| **1.14** | Your own hotkeys: every hotkey (show / hide, benchmark, plus new optional only-when-wrong and open settings) rebindable with a press-the-keys box, Off per hotkey, Reset to defaults; refuses its own duplicates and combos another app owns (keeps the old one) |
| **1.15** | Put it anywhere: 9 spots, any screen (by monitor id, main screen while it's missing), edge offset slider; Move it mode (settings, tray / Hallow menu `invoke move`, optional hotkey) with snapping to spots / edges / centre lines, arrow-key nudges, click-through restored on Enter / Esc / Done / focus loss / 2 min; saved as spot + offset share of the screen (survives resolution / DPI); own spot per game or "use my usual spot"; Reset position; `position` in status.json |
| **1.16** | Dragging fixed: move mode handles the panel's own mouse messages (press / move with capture / release) instead of WPF's DragMove; the "Drag me" card drags the panel too; "Saved here" flash after moving; status position predicted while hidden |
| **1.17** | Move it fixed for real: move mode started before the panel was ever shown (no game since start) left the new window click-through; the window is created first and the styles re-checked every second |
| **1.18** | Now playing without Hallow Mixer: when Mixer isn't running, the song or video comes straight from Windows' media sessions (what's playing, every 2 s, read only); with Mixer running it still comes from Mixer |
| **1.19** | Remembers where you put it: a free spot keeps the edges nearest the screen's edges (a centre / right spot measured at the move-mode size came back elsewhere at the normal width); every move saved at once and atomically (drag, nudges, Done / Enter, and a drag still held when move mode ends); "Saved for <game>" / "Saved as your usual spot"; test copies with their own data folder and made-up screens |
| **1.20** | Light and dark mode for the settings windows: follows `mode` in theme.json (Light / Dark / Same as Windows, live) or Settings > Look (Follow Hallow / Light / Dark); light palette from the family spec, darkened accent for text, title bars match; the in-game panel stays dark and see-through |
| **1.21** | Light mode readability fixes: filled accent buttons / chips use the family light fill (violet had dark text at 4.47:1 -> #8452F5 with white text), slider tracks use the darkened accent; light contrast test for every pair the settings windows use |

## Next

### Fixes from real use
- Check it over real games in borderless / windowed mode, and that the exclusive-fullscreen notice shows up for games that really use exclusive fullscreen (needs real games - not testable here)
- Compare the FPS and 1% low numbers with a game's own counter, including Vulkan / OpenGL games (only DirectX frames are counted now)
- Check "Clip saved" (Hallow Clips) and the break reminder / daily limit (Hallow Hub) arrive over a real game once those apps send them
- Check the game server finder in real games (UDP and TCP games, IPv6, relayed games like Valve SDR) - a game socket that connected before the overlay noticed the game can be missed
- Check the One Hallow icon with the real Hallow Hub 2.4 (menu, Exit, messages from Hub's icon) - tested with a fake Hub only
- Check match summaries after real games (the summary when a game closes, history entries, servers per region) - tested with made-up samples only
- Check the frame-time graph against a real game's stutters and the GPU power / clock numbers under load
- Check move mode and placement on real screens (two screens with different DPI, a screen left of the main one, unplugging one) - tested with made-up screens and the test's own window only; with WPF's system-DPI mode Windows scales the panel on screens with another DPI

## Later
- CPU temperature - Windows has no reliable source for it without an admin-level driver (the WMI thermal zone is usually the motherboard or a fixed value), so it waits until there's a safe way

### Gaps vs Afterburner / RTSS
What MSI Afterburner + RivaTuner Statistics Server, the Xbox Game Bar performance widget and the NVIDIA app overlay do that Hallow Overlay doesn't (yet):
- **FPS limiter / frame cap** (RTSS, NVIDIA app) - not doing: it needs code injected into the game (or a driver setting), which is exactly what keeps Overlay anti-cheat safe
- **OSD inside exclusive fullscreen** (RTSS) - not doing: needs injection; Overlay tells you to use borderless instead
- **Overclocking, voltage and fan curves** (Afterburner) - not doing: driver-level control that needs admin and can damage hardware
- **AMD / Intel GPU sensors** (Afterburner, Game Bar) - not now: needs the vendor's library and a card to test with (NVIDIA only here)
- **CPU temperature, per-core clocks and load** (Afterburner) - not now: no reliable source without an admin driver (see Later)
- **PC latency / Reflex stats** (NVIDIA app) - not doing: needs Reflex built into the game
- **Video and screenshot capture** (Afterburner, Game Bar, NVIDIA app) - covered by Hallow Clips
- **Vulkan / OpenGL FPS** (RTSS, PresentMon) - buildable: the graphics kernel's present events work without admin -> 1.11
- **Updates itself** - buildable -> 1.11
- **0.1% lows, benchmark mode with a report** (Afterburner, NVIDIA app) - buildable -> 1.11 / 1.12
- **Logging to a file** (Afterburner's hardware monitor log) - buildable as CSV -> 1.12
- **OSD layout editor: order, labels, colours, font size, text or graph per item** (RTSS / Afterburner) - buildable -> 1.13
- **Your own hotkeys** (Afterburner) - buildable -> 1.14
- **Put the OSD anywhere: free position, drag to place, any screen** (RTSS / Afterburner, Game Bar widgets) - buildable -> 1.15 (remembered exactly after restarts and in games: 1.19)
