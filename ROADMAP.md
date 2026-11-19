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

## Next

### 1.9 - GPU details and frame-time graph
- GPU core and memory clocks, power draw (W and % of the limit), VRAM used / total (NVIDIA)
- A small frame-time graph of the last 5 seconds with spikes highlighted (size is a setting), drawn without costing FPS
- Match summary: average GPU power and peak VRAM

### Fixes from real use
- Check it over real games in borderless / windowed mode, and that the exclusive-fullscreen notice shows up for games that really use exclusive fullscreen (needs real games - not testable here)
- Compare the FPS and 1% low numbers with a game's own counter, including Vulkan / OpenGL games (only DirectX frames are counted now)
- Check "Clip saved" (Hallow Clips) and the break reminder / daily limit (Hallow Hub) arrive over a real game once those apps send them
- Check the game server finder in real games (UDP and TCP games, IPv6, relayed games like Valve SDR) - a game socket that connected before the overlay noticed the game can be missed
- Check the One Hallow icon with the real Hallow Hub 2.4 (menu, Exit, messages from Hub's icon) - tested with a fake Hub only
- Check match summaries after real games (the summary when a game closes, history entries, servers per region) - tested with made-up samples only

## Later
- CPU temperature - Windows has no reliable source for it without an admin-level driver (the WMI thermal zone is usually the motherboard or a fixed value), so it waits until there's a safe way
