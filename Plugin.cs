using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BlasSkinUnlocker
{
    // Blasphemous skin (color palette) unlocker.
    //
    // Skins are color palettes managed by Framework.Managers.ColorPaletteManager. The in-game
    // Skins page (the Skin Selector tab of ExtrasMenuWidget) is a carousel that shows only the
    // *unlocked* skins - locked ones are hidden and there are no text names (the wiki names like
    // "Alloy of Sin" / "Golden Burden" aren't in the game). So this mod overlays an IMGUI panel on
    // the Skins page listing EVERY skin id with its color-palette swatch and an unlock toggle.
    //
    // Normal skins go through the manager's own methods, which persist to the skin-settings file:
    //   ON  -> ColorPaletteManager.UnlockColorPalette(id, showPopup:false)   (persists internally)
    //   OFF -> ColorPaletteManager.LockColorPalette(id) + SetCurrentSkinToSkinSettings(current)
    //          to force the lock to be written.
    //
    // The two DLC skins (PENITENT_BACKER, PENITENT_DELUXE) can't persist that way at all. The game
    // keeps them in a separate `dlcPalettes` ownership map, writes their "<id>_UNLOCKED" flag from
    // that map (InitializeSkinFile -> IsColorPaletteUnlocked, i.e. always false without the DLC),
    // and strips those keys from the file again while loading (CleanOldSaveFileFormat). So for
    // those ids the mod flips `dlcPalettes` directly, remembers them in its own config, and
    // re-applies them at every launch - along with the saved skin selection, which Initialize()
    // resets to the default while the DLC skin still looks locked.
    [BepInPlugin("local.blasphemous.skinunlocker", "Blasphemous Skin Unlocker", "1.3.0")]
    public class SkinUnlocker : BaseUnityPlugin
    {
        const string CoreName    = "Framework.Managers.Core";
        const string ExtrasName  = "Gameplay.UI.Others.MenuLogic.ExtrasMenuWidget";
        const string DefaultSkin = "PENITENT_DEFAULT";

        static ManualLogSource L;

        ConfigEntry<KeyCode> cfgToggleKey;
        ConfigEntry<bool>    cfgAutoShow;
        ConfigEntry<string>  cfgForcedDlc;

        PropertyInfo pCorePalettes;   // static Core.ColorPaletteManager
        MethodInfo mGetAllIds, mGetUnlockedIds, mUnlock, mLock, mGetCurrent, mSetSkinSettings, mGetSprite;
        MethodInfo mSetCurrent, mGetSettingsPath, mParseSettings;   // DLC persistence
        FieldInfo fDlcPalettes, fDlcPaletteIds;                     // DLC persistence

        Type extrasType;
        PropertyInfo pExtrasActive;
        FieldInfo fCurrentMenu;
        object menuSkinSelector;

        bool ready;
        float gateAccum;
        bool onSkinsPage;
        bool userHidden;   // hid the panel with the hotkey while on the Skins page

        readonly HashSet<string> forcedDlc = new HashSet<string>();   // DLC skins this mod unlocked
        HashSet<string> dlcSkinIds;                                   // the game's DLC skin ids
        object repairedMgr;                                           // manager we already repaired

        List<string> skins;
        Vector2 scroll;
        string filter = "";
        GUIStyle titleStyle, rowStyle, hintStyle, btnStyle;
        Texture2D panelBg, rowBg, rowBgAlt;

        void Awake()
        {
            L = Logger;
            cfgToggleKey = Config.Bind("Keys", "TogglePanel", KeyCode.F9, "Hide / show the panel WHILE the Skins page is open (does nothing elsewhere, so it won't clash with other mods during gameplay).");
            cfgAutoShow  = Config.Bind("Panel", "ShowOnSkinsPage", true, "Show the panel while the Skins page is open.");
            cfgForcedDlc = Config.Bind("Persistence", "ForcedDlcUnlocks", "",
                "Comma-separated DLC skin ids unlocked by this mod (PENITENT_BACKER, PENITENT_DELUXE). The game derives those two from DLC ownership and refuses to keep them in its own save file, so the mod re-applies them at every launch. Maintained automatically by the Unlock / Lock buttons.");
            foreach (var s in (cfgForcedDlc.Value ?? "").Split(','))
            {
                var t = s.Trim();
                if (t.Length > 0) forcedDlc.Add(t);
            }

            var coreType = FindType(CoreName);
            extrasType   = FindType(ExtrasName);

            var pub = BindingFlags.Public | BindingFlags.Static;
            var ip  = BindingFlags.Public | BindingFlags.Instance;
            var np  = BindingFlags.NonPublic | BindingFlags.Instance;

            pCorePalettes = coreType?.GetProperty("ColorPaletteManager", pub);
            var mgrType = pCorePalettes?.PropertyType;
            if (mgrType != null)
            {
                mGetAllIds       = mgrType.GetMethod("GetAllColorPalettesId", ip, null, Type.EmptyTypes, null);
                // Use the unlocked-id list (unions palettesStates + dlcPalettes) rather than
                // IsColorPaletteUnlocked, which reads only DLC ownership for the DLC skins.
                mGetUnlockedIds  = mgrType.GetMethod("GetAllUnlockedColorPalettesId", ip, null, Type.EmptyTypes, null);
                mUnlock          = mgrType.GetMethod("UnlockColorPalette", ip, null, new[] { typeof(string), typeof(bool) }, null);
                mLock            = mgrType.GetMethod("LockColorPalette", ip, null, new[] { typeof(string) }, null);
                mGetCurrent      = mgrType.GetMethod("GetCurrentColorPaletteId", ip, null, Type.EmptyTypes, null);
                mSetCurrent      = mgrType.GetMethod("SetCurrentColorPaletteId", ip, null, new[] { typeof(string) }, null);
                mSetSkinSettings = mgrType.GetMethod("SetCurrentSkinToSkinSettings", ip, null, new[] { typeof(string) }, null);
                mGetSprite       = mgrType.GetMethod("GetColorPaletteById", ip, null, new[] { typeof(string) }, null);
                // DLC ownership map + the skin-settings reader, used to make DLC unlocks survive a restart.
                fDlcPalettes     = mgrType.GetField("dlcPalettes", np);
                fDlcPaletteIds   = mgrType.GetField("dlcPalettesIds", np);
                mGetSettingsPath = mgrType.GetMethod("GetPathSkinSettings", np, null, Type.EmptyTypes, null);
                mParseSettings   = mgrType.GetMethod("ParseCurrentSkinSettings", np, null, new[] { typeof(string) }, null);
            }

            if (extrasType != null)
            {
                pExtrasActive = extrasType.GetProperty("currentlyActive", ip);
                fCurrentMenu  = extrasType.GetField("currentMenu", np);
                var menuType = extrasType.GetNestedType("MENU", BindingFlags.Public | BindingFlags.NonPublic);
                if (menuType != null) menuSkinSelector = SafeGet(() => Enum.Parse(menuType, "SKINSELECTOR"));
            }

            ready = pCorePalettes != null && mGetAllIds != null && mGetUnlockedIds != null && mUnlock != null && mLock != null;
            L.LogInfo($"[SkinUnlocker] v1.3 ready={ready}. skinsPage={extrasType != null && fCurrentMenu != null}. dlcFix={fDlcPalettes != null}, remembered={forcedDlc.Count}. {cfgToggleKey.Value}=toggle panel.");
            if (!ready) L.LogWarning("[SkinUnlocker] color palette API not fully resolved - toggling may be unavailable.");
            if (fDlcPalettes == null) L.LogWarning("[SkinUnlocker] DLC palette map not resolved - Backer/Deluxe unlocks won't survive a restart.");
        }

        void Update()
        {
            gateAccum += Time.unscaledDeltaTime;
            if (gateAccum >= 0.3f)
            {
                gateAccum = 0f;
                onSkinsPage = IsOnSkinsPage();
                EnsureDlcUnlocks();
            }

            // The hotkey only hides/shows the panel WHILE the Skins page is open, so it can't clash
            // with other mods' hotkeys during normal gameplay. Reset when we leave the page.
            if (onSkinsPage) { if (Input.GetKeyDown(cfgToggleKey.Value)) userHidden = !userHidden; }
            else userHidden = false;

            if (PanelVisible() && skins == null) BuildList();
            if (!PanelVisible()) skins = null;
        }

        bool PanelVisible() => onSkinsPage && cfgAutoShow.Value && !userHidden;

        bool IsOnSkinsPage()
        {
            if (extrasType == null || pExtrasActive == null || fCurrentMenu == null) return false;
            var ext = SafeGet(() => UnityEngine.Object.FindObjectOfType(extrasType));
            if (ext == null) return false;
            if (!(SafeGet(() => pExtrasActive.GetValue(ext, null)) is bool b) || !b) return false;
            object menu = SafeGet(() => fCurrentMenu.GetValue(ext));
            return menuSkinSelector == null || Equals(menu, menuSkinSelector);
        }

        // ---- data ---------------------------------------------------------------

        object Mgr() => SafeGet(() => pCorePalettes?.GetValue(null, null));

        string CurrentSkin(object mgr) => SafeGet(() => mGetCurrent?.Invoke(mgr, null)) as string ?? "";

        void BuildList()
        {
            skins = new List<string>();
            var mgr = Mgr();
            if (mgr == null || mGetAllIds == null) return;
            var ids = SafeGet(() => mGetAllIds.Invoke(mgr, null)) as IEnumerable;
            if (ids == null) return;
            foreach (object id in ids) if (id is string s && !string.IsNullOrEmpty(s)) skins.Add(s);
        }

        // The set of currently-unlocked skin ids (covers normal skins AND DLC skins force-unlocked
        // by this mod). Rebuilt each frame; cheap for a menu panel.
        HashSet<string> UnlockedSet(object mgr)
        {
            var set = new HashSet<string>();
            var ids = SafeGet(() => mGetUnlockedIds.Invoke(mgr, null)) as IEnumerable;
            if (ids != null) foreach (object id in ids) if (id is string s) set.Add(s);
            return set;
        }

        // ---- DLC skins (Backer / Deluxe) ----------------------------------------

        // The game gates these two on DLC ownership and never saves an unlock for them, so the mod
        // re-applies its own list to the ownership map once the manager has initialized.
        void EnsureDlcUnlocks()
        {
            var mgr = Mgr();
            if (mgr == null || fDlcPalettes == null) return;
            // currentColorPaletteId is empty until ColorPaletteManager.Initialize() has run.
            if (CurrentSkin(mgr).Length == 0) return;

            bool first = !ReferenceEquals(mgr, repairedMgr);
            repairedMgr = mgr;

            var applied = new List<string>();
            foreach (var id in forcedDlc) if (SetDlcOwned(mgr, id, true)) applied.Add(id);
            if (first && applied.Count > 0) L.LogInfo($"[SkinUnlocker] re-applied DLC skin unlock(s): {string.Join(", ", applied.ToArray())}.");
            if (first) RestoreSavedSkin(mgr);
        }

        // dlcPalettes is the ownership map IsColorPaletteUnlocked / GetAllUnlockedColorPalettesId
        // consult for the DLC skins, so flipping it is what actually unlocks (or relocks) them.
        // Returns true when the value changed.
        bool SetDlcOwned(object mgr, string id, bool owned)
        {
            var map = SafeGet(() => fDlcPalettes?.GetValue(mgr)) as IDictionary;
            if (map == null || !map.Contains(id)) return false;
            if (map[id] is bool b && b == owned) return false;
            SafeSet(() => map[id] = owned);
            return true;
        }

        bool IsDlcSkin(object mgr, string id)
        {
            if (dlcSkinIds == null && mgr != null)
            {
                var set = new HashSet<string>();
                var ids = SafeGet(() => fDlcPaletteIds?.GetValue(mgr)) as IEnumerable;
                if (ids != null) foreach (object o in ids) if (o is string s) set.Add(s);
                if (set.Count > 0) dlcSkinIds = set;
            }
            if (dlcSkinIds != null) return dlcSkinIds.Contains(id);
            return id == "PENITENT_BACKER" || id == "PENITENT_DELUXE";
        }

        // Initialize() falls back to the default skin whenever the saved selection isn't unlocked -
        // which is exactly what a DLC skin looks like before EnsureDlcUnlocks() runs. Put the saved
        // selection back so the skin you were wearing also survives the restart.
        void RestoreSavedSkin(object mgr)
        {
            if (forcedDlc.Count == 0 || mGetSettingsPath == null || mParseSettings == null || mSetCurrent == null) return;
            string path = SafeGet(() => mGetSettingsPath.Invoke(mgr, null)) as string;
            if (string.IsNullOrEmpty(path)) return;
            var settings = SafeGet(() => mParseSettings.Invoke(mgr, new object[] { path })) as IDictionary;
            if (settings == null || !settings.Contains("CURRENT_SKIN")) return;
            object entry = settings["CURRENT_SKIN"];   // FullSerializer.fsData
            string saved = SafeGet(() => entry?.GetType().GetProperty("AsString")?.GetValue(entry, null)) as string;
            // Only the DLC skins need this; every other selection was restored correctly already.
            if (string.IsNullOrEmpty(saved) || !forcedDlc.Contains(saved) || saved == CurrentSkin(mgr)) return;
            SafeSet(() => mSetCurrent.Invoke(mgr, new object[] { saved }));
            L.LogInfo($"[SkinUnlocker] restored selected skin {saved}.");
        }

        // Remember (or forget) a DLC skin unlock in this mod's config, since the game's save can't.
        void RememberForcedDlc(string id, bool forced)
        {
            if (!(forced ? forcedDlc.Add(id) : forcedDlc.Remove(id))) return;
            var ids = new List<string>(forcedDlc);
            ids.Sort();
            cfgForcedDlc.Value = string.Join(",", ids.ToArray());
            Config.Save();
        }

        // ---- toggling -----------------------------------------------------------

        // The extras menu hides the whole Skins page (and with it this panel) while fewer than two
        // skins are unlocked, and the game treats the default skin as permanently unlocked anyway -
        // so locking it would only break the UI we live on.
        static bool CanLock(string id) => id != DefaultSkin;

        void SetUnlocked(string id, bool unlocked)
        {
            var mgr = Mgr();
            if (mgr == null || (!unlocked && !CanLock(id))) return;
            bool dlc = IsDlcSkin(mgr, id);

            if (unlocked)
            {
                if (dlc) { SetDlcOwned(mgr, id, true); RememberForcedDlc(id, true); }
                else SafeSet(() => mUnlock.Invoke(mgr, new object[] { id, false }));   // persists internally, no popup
            }
            else
            {
                if (dlc) { SetDlcOwned(mgr, id, false); RememberForcedDlc(id, false); }
                SafeSet(() => mLock.Invoke(mgr, new object[] { id }));
                // LockColorPalette only writes the file if it was the current skin; force a save,
                // and drop the selection if it still points at the skin we just locked.
                string cur = CurrentSkin(mgr);
                string target = (cur == id || cur.Length == 0) ? DefaultSkin : cur;
                SafeSet(() => mSetSkinSettings?.Invoke(mgr, new object[] { target }));
            }
            L.LogInfo($"[SkinUnlocker] {id} -> {(unlocked ? "UNLOCKED" : "LOCKED")}{(dlc ? " (DLC)" : "")}.");
        }

        void SetAll(bool unlocked)
        {
            if (skins == null) return;
            var mgr = Mgr();
            if (mgr == null) return;
            foreach (var id in skins)
            {
                if (!unlocked && !CanLock(id)) continue;
                if (IsDlcSkin(mgr, id)) { SetDlcOwned(mgr, id, unlocked); RememberForcedDlc(id, unlocked); }
                else if (unlocked) SafeSet(() => mUnlock.Invoke(mgr, new object[] { id, false }));
                else               SafeSet(() => mLock.Invoke(mgr, new object[] { id }));
            }
            // one persist pass at the end; nothing but the default is wearable after "Lock all"
            string cur = CurrentSkin(mgr);
            string target = (!unlocked || cur.Length == 0) ? DefaultSkin : cur;
            SafeSet(() => mSetSkinSettings?.Invoke(mgr, new object[] { target }));
        }

        // ---- UI -----------------------------------------------------------------

        void OnGUI()
        {
            if (!ready || !PanelVisible() || skins == null) return;
            EnsureStyles();
            Cursor.visible = true;

            var mgr = Mgr();
            const float w = 460f, x = 40f, y = 40f;
            float h = Mathf.Min(Screen.height - 80f, 640f);
            GUI.DrawTexture(new Rect(x, y, w, h), panelBg);
            GUILayout.BeginArea(new Rect(x + 12f, y + 10f, w - 24f, h - 20f));

            var unlockedSet = UnlockedSet(mgr);
            int total = skins.Count, unlocked = 0;
            foreach (var id in skins) if (unlockedSet.Contains(id)) unlocked++;
            GUILayout.Label($"Skin Unlocker    {unlocked} / {total} unlocked", titleStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Unlock all", btnStyle, GUILayout.Width(90f))) SetAll(true);
            if (GUILayout.Button("Lock all",   btnStyle, GUILayout.Width(80f))) SetAll(false);
            GUILayout.Label("Filter:", hintStyle, GUILayout.Width(40f));
            filter = GUILayout.TextField(filter ?? "", GUILayout.MinWidth(110f));
            if (GUILayout.Button("Close", btnStyle, GUILayout.Width(64f))) userHidden = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            scroll = GUILayout.BeginScrollView(scroll);
            string f = (filter ?? "").Trim().ToLowerInvariant();
            int i = 0;
            foreach (var id in skins)
            {
                if (f.Length > 0 && id.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0) continue;
                bool isOn = unlockedSet.Contains(id);

                GUILayout.BeginHorizontal(i++ % 2 == 0 ? Bg(rowBg) : Bg(rowBgAlt), GUILayout.Height(22f));
                DrawSwatch(mgr, id);
                rowStyle.normal.textColor = isOn ? new Color(0.6f, 0.9f, 0.6f) : new Color(0.82f, 0.82f, 0.85f);
                GUILayout.Label(id, rowStyle);
                GUILayout.FlexibleSpace();
                if (IsDlcSkin(mgr, id)) GUILayout.Label("DLC", hintStyle, GUILayout.Width(26f));
                if (!CanLock(id) && isOn) GUILayout.Label("always", hintStyle, GUILayout.Width(70f));
                else if (GUILayout.Button(isOn ? "Lock" : "Unlock", btnStyle, GUILayout.Width(70f))) SetUnlocked(id, !isOn);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Label("Changes save to the skin-settings file, except the DLC skins - the game won't keep those, so the mod re-applies them (and your selection) at every launch. New unlocks appear in the carousel next time you open the Skins page.", hintStyle);
            GUILayout.EndArea();
        }

        // Draw the skin's color-palette strip so skins can be told apart without names.
        void DrawSwatch(object mgr, string id)
        {
            var rect = GUILayoutUtility.GetRect(46f, 16f, GUILayout.Width(46f), GUILayout.Height(16f));
            if (mGetSprite == null || Event.current.type != EventType.Repaint) return;
            var sprite = SafeGet(() => mGetSprite.Invoke(mgr, new object[] { id })) as Sprite;
            if (sprite == null || sprite.texture == null) return;
            var t = sprite.texture; var tr = sprite.textureRect;
            var tc = new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height);
            GUI.DrawTextureWithTexCoords(rect, t, tc, false);
        }

        GUIStyle Bg(Texture2D tex) { var s = new GUIStyle(); s.normal.background = tex; s.padding = new RectOffset(4, 4, 2, 2); return s; }

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Color.white;
            rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            rowStyle.normal.textColor = Color.white;
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            hintStyle.normal.textColor = new Color(0.7f, 0.7f, 0.75f);
            btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 12 };
            panelBg  = Tex(new Color(0.09f, 0.09f, 0.12f, 0.97f));
            rowBg    = Tex(new Color(1f, 1f, 1f, 0.03f));
            rowBgAlt = Tex(new Color(1f, 1f, 1f, 0.07f));
        }

        static Texture2D Tex(Color c) { var t = new Texture2D(1, 1, TextureFormat.RGBA32, false); t.SetPixel(0, 0, c); t.Apply(); return t; }

        // ---- helpers ------------------------------------------------------------

        static object SafeGet(Func<object> f) { try { return f(); } catch { return null; } }
        static void SafeSet(Action a) { try { a(); } catch (Exception e) { L.LogWarning($"[SkinUnlocker] action failed: {e.Message}"); } }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t; try { t = asm.GetType(fullName, false); } catch { continue; }
                if (t != null) return t;
            }
            return null;
        }
    }
}
