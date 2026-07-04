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

- **F9** — show / hide the panel (it also auto-shows on the Skins page; can be disabled in config).

## Notes

- The panel uses the mouse (it forces the cursor visible while open), so click the buttons directly.
- Skins are shown by their internal id (e.g. `PENITENT_OSSUARY`) plus a color swatch, because the
  game has no display names for them.
- The default skin (`PENITENT_DEFAULT`) is always unlocked; DLC skins are gated by DLC ownership,
  so toggling those may not take effect.
