# Blasphemous Skin Unlocker

A BepInEx plugin that adds a **toggle panel to the in-game Skins page**, letting you **unlock or
lock any skin** (color palette) — individually, or all at once. Works for every skin, e.g. the
ones the community calls "Alloy of Sin" or "Golden Burden".

## How it works (discovered)

Skins are **color palettes** managed by `Framework.Managers.ColorPaletteManager`. The in-game
Skins page (the Skin Selector tab of `ExtrasMenuWidget`) is a carousel that shows **only unlocked**
skins — locked ones are hidden, and the game has **no text names** for skins (names like "Alloy of
Sin" / "Golden Burden" are community/wiki names, not in the game).

So this mod overlays an IMGUI panel on the Skins page listing **every** skin id, each with its
**color-palette swatch** (so you can tell them apart without names) and an unlock toggle:

- **Unlock** → `ColorPaletteManager.UnlockColorPalette(id, showPopup: false)` (sets the state and
  writes the skin-settings file).
- **Lock** → `ColorPaletteManager.LockColorPalette(id)` followed by
  `SetCurrentSkinToSkinSettings(current)` to force the lock to be saved.

Newly unlocked skins appear in the game's carousel the next time you open the Skins page.

### The two DLC skins (Backer / Deluxe)

`PENITENT_BACKER` and `PENITENT_DELUXE` can't be saved that way at all — that's a quirk of the game,
not of the mod:

- The game keeps them in a separate `dlcPalettes` ownership map, not in `palettesStates`.
- `InitializeSkinFile` writes their `<id>_UNLOCKED` flag from `IsColorPaletteUnlocked`, which for
  these two returns DLC ownership — so an unlock is written to the save as `false`.
- `CleanOldSaveFileFormat` deletes those keys again while loading, so even a `true` wouldn't survive.
- On top of that, `Initialize` drops the *worn* skin back to the default when it isn't unlocked.

So for these two the mod flips `dlcPalettes` directly, records the ids in its own config
(`ForcedDlcUnlocks`), and re-applies them — plus the skin you were wearing — at every launch.

The mod drives the game's own color-palette manager and its skin-settings file only; it changes no
game files on disk beyond that save.

## Build & install

- Game: Steam Blasphemous, Unity 2017.4 (**Mono CLR 2.0**) → plugin targets **net35**.
- Requires BepInEx 5.x (x64, Mono) installed in the game folder.
- Copy `GameDir.local.props.example` to `GameDir.local.props` and set `<GameDir>` to your
  install (this file is gitignored), then `dotnet build -c Release`.
- Copy `bin/Release/BlasSkinUnlocker.dll` to `Blasphemous/BepInEx/plugins/`.

## Usage

1. Launch the game. Open **Extras → Skins**. The toggle panel appears automatically.
2. Each row shows the skin's **palette swatch** and id. Click **Unlock** / **Lock**, or
   **Unlock all** / **Lock all**. Use the **Filter** box to find a skin id.
3. Open/reopen the Skins page (or navigate the carousel) to see newly unlocked skins.

## Controls (rebind in `BepInEx/config/local.blasphemous.skinunlocker.cfg`)

- **F9** — hide / show the panel while the Skins page is open (it auto-shows there; can be disabled
  in config). It does nothing elsewhere, so it won't clash with other mods during gameplay.
- `ForcedDlcUnlocks` — the DLC skins this mod unlocked, maintained automatically by the Unlock /
  Lock buttons. Clear it to drop those unlocks.

## Notes

- The panel uses the mouse (it forces the cursor visible while open), so click the buttons directly.
- Skins are shown by their internal id (e.g. `PENITENT_OSSUARY`) plus a color swatch, because the
  game has no display names for them.
- The default skin (`PENITENT_DEFAULT`) is always unlocked and can't be locked — the game hides the
  whole Skins page (and with it this panel) while fewer than two skins are unlocked.
- The Backer/Deluxe unlocks live in this mod's config, so removing the mod re-locks those two.
  Every other skin stays unlocked, because the game saved those itself.
