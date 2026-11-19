# Hallow Overlay

A see-through stats panel over your games - part of the Hallow family with [Hallow Hub](https://github.com/Dom428/hallow-hub), [Hallow Vault](https://github.com/Dom428/hallow-vault), [Hallow Mixer](https://github.com/Dom428/hallow-mixer) and [Hallow Toolkit](https://github.com/Dom428/hallow-toolkit).

- **MIC MUTED** warning and now playing from Hallow Mixer (instant with Mixer 3.14+), FPS and 1% lows, CPU, GPU load and temperature, RAM, ping, session time, clock
- FPS comes from Windows' own frame events (ETW, the same ones PresentMon reads) - no admin prompt; Windows only allows it for admins and the Performance Log Users group, otherwise FPS stays hidden
- Shows only while a game runs (or always); any corner, one line or a column, three sizes; Ctrl+Shift+O to hide
- **Game server**: which server the game is talking to (from Windows' own Winsock events for the game's processes - same rules as FPS), its region from the host name (no online lookup), ping (or the nearest hop that answers, shown with ~), loss, "stalls" and the game's up / down traffic; the IP stays hidden by default
- **Match summary**: when a game closes, a one-line summary on the panel; Match history (every session of 2 minutes or more: FPS, ping, loss, stalls, servers, temperatures, load) with a Servers view - kept in `%APPDATA%\HallowOverlay\matches.json`, the last 5 in status.json
- Uses the Hallow accent colour picked in Hallow Toolkit (`%APPDATA%\Hallow\theme.json`, live); crash reports stay on this PC in `%APPDATA%\HallowOverlay\crashes` (newest 20, `lastCrash` in status) - nothing is sent anywhere
- Per game: its own corner, size and stats; "only when something's wrong" mode with your own limits; packet loss and jitter next to ping
- Its own click-through window - nothing is injected into games, so it's anti-cheat safe (borderless / windowed fullscreen needed; it tells you when a game is in exclusive fullscreen)
- Hallow family link: other Hallow apps can show / hide it and put short messages on it (named pipe `HallowFamily.Overlay.<user>` with `open` / `menu` / `invoke` / `quit` for Hallow Hub's one Hallow icon, `HallowOverlay.exe --cmd "ping"`, `%APPDATA%\HallowOverlay\status.json`)

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1`. No admin needed.

## Credits

Icons: [Font Awesome Free](https://fontawesome.com) 7.3.1 by Fonticons, Inc., licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (see FontAwesome-LICENSE.txt).