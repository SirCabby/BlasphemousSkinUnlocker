[size=6][b]Blasphemous Skin Unlocker[/b][/size]

A lightweight mod that adds a [b]toggle panel to the in-game Skins page[/b], letting you [b]unlock or lock any skin[/b] — individually, or all at once. Works for every skin, including the ones the community calls [i]Alloy of Sin[/i] or [i]Golden Burden[/i].

This mod is open source!  Check it out at [url=https://github.com/SirCabby/BlasphemousSkinUnlocker]https://github.com/SirCabby/BlasphemousSkinUnlocker[/url]

[size=5][b]What it does[/b][/size]
[list]
[*]Open [b]Extras → Skins[/b] and a panel appears listing [b]every skin[/b], each with its [b]color swatch[/b] and an unlock toggle.
[*][b]Unlock[/b] or [b]Lock[/b] any skin, or use [b]Unlock all[/b] / [b]Lock all[/b].
[*][b]Filter[/b] box to find a skin quickly.
[*]Newly unlocked skins show up in the game's skin carousel.
[*]Unlocks [b]stick across restarts[/b] — including the [b]Backer[/b] and [b]Deluxe[/b] DLC skins, which the game itself refuses to save.
[/list]

[size=5][b]Requirements[/b][/size]
[list]
[*]Blasphemous
[*]BepInEx 5.4.x (x64 / Mono) — installed in Step 1 below.
[/list]

[size=5][b]Step 1 — Install BepInEx (the mod loader)[/b][/size]
[list=1]
[*]Open the [url=https://github.com/BepInEx/BepInEx/releases]BepInEx releases page[/url] and download the latest [b]BepInEx 5.4.x[/b] for [b]x64 Windows[/b] — the file is named like [i]BepInEx_win_x64_5.4.x.zip[/i]. [b]Do not[/b] use the 6.x (BepInEx 6 / IL2CPP) builds.
[*]Find your game folder: in Steam, right-click [b]Blasphemous[/b] → [b]Manage[/b] → [b]Browse local files[/b]. It looks like:
[code]C:\Program Files (x86)\Steam\steamapps\common\Blasphemous[/code]
[*]Extract the contents of the zip [b]directly into that folder[/b] (next to [i]Blasphemous.exe[/i]). You should now see a [i]BepInEx[/i] folder, plus [i]winhttp.dll[/i] and [i]doorstop_config.ini[/i].
[*]Launch the game once, then quit. This first run creates the [i]BepInEx\plugins[/i] and [i]BepInEx\config[/i] folders.
[/list]

[size=5][b]Step 2 — Install this mod[/b][/size]
[list=1]
[*]Drop [b]BlasSkinUnlocker.dll[/b] into the plugins folder:
[code]...\Blasphemous\BepInEx\plugins\[/code]
[*]Launch the game.
[/list]

[size=5][b]How to use it[/b][/size]
[list=1]
[*]Open [b]Extras → Skins[/b]. The toggle panel appears.
[*]Click [b]Unlock[/b] / [b]Lock[/b] on any skin (use the mouse — the cursor is shown while the panel is open).
[*]Reopen the Skins page to see newly unlocked skins in the carousel.
[*]Press [b]F9[/b] while the Skins page is open to hide or show the panel.
[/list]

[size=5][b]Uninstall[/b][/size]
Delete [b]BlasSkinUnlocker.dll[/b] from [i]BepInEx\plugins[/i]. Any skins you already unlocked stay unlocked; the Skins page returns to normal. The two DLC skins ([b]Backer[/b] / [b]Deluxe[/b]) are the exception — the game only ever grants those with the DLC installed, so they go back to locked.

[size=5][b]Troubleshooting[/b][/size]
[list]
[*][b]Panel doesn't appear?[/b] It only shows on the [b]Extras → Skins[/b] page; press [b]F9[/b] there if you hid it. Then open [i]BepInEx\LogOutput.log[/i] and look for [b][SkinUnlocker] v1.3 ready=True[/b]; a warning there means the game's skin code didn't match (e.g. after a game update).
[*][b]Nothing loads at all?[/b] Double-check you used BepInEx [b]5.4.x x64 (Mono)[/b], extracted it into the game folder (not a subfolder), and ran the game once before adding the DLL.
[/list]
