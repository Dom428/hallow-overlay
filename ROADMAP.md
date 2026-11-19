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

## Next

### Fixes from real use
- Check it over real games in borderless / windowed mode, and that the exclusive-fullscreen notice shows up for games that really use exclusive fullscreen (needs real games - not testable here)
- Compare the FPS and 1% low numbers with a game's own counter, including Vulkan / OpenGL games (only DirectX frames are counted now)
- Check "Clip saved" (Hallow Clips) and the break reminder / daily limit (Hallow Hub) arrive over a real game once those apps send them
- Check the game server finder in real games (UDP and TCP games, IPv6, relayed games like Valve SDR) - a game socket that connected before the overlay noticed the game can be missed
