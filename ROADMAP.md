# Hallow Overlay roadmap

## Done

| Version | What it added |
|---|---|
| **1.0** | See-through panel you can click through: big MIC MUTED warning (from Hallow Mixer), CPU, GPU load and temperature, RAM, ping, session time; corner and size choices; Ctrl+Shift+O shows and hides it |
| **1.1** | Tells you when a game is in exclusive fullscreen (use borderless); Hallow family link: named pipe with show / hide / toggle / flash / remind / game-on / game-off, `--cmd`, status.json; What's new window |
| **1.2** | FPS and 1% lows from Windows' own frame events (the ETW events PresentMon reads: DXGI / D3D9 presents) - no admin prompt; works for admins and the Performance Log Users group, hidden otherwise |

## Next

### 1.3 - Smarter panel
- Position, size and which stats per game
- Only show a stat when it's bad (GPU hot, ping high, RAM almost full)
- Packet loss and jitter next to ping

### 1.4 - Hallow family
- "Clip saved" flash from Hallow Clips
- Break reminder and daily limit from Hallow Hub shown here instead of a pop-up
- Now playing from Hallow Mixer

### Fixes from real use
- Check it over real games in borderless / windowed mode, and that the exclusive-fullscreen notice shows up for games that really use exclusive fullscreen (needs real games - not testable here)
- Compare the FPS and 1% low numbers with a game's own counter, including Vulkan / OpenGL games (only DirectX frames are counted now)
