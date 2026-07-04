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
    // Toggling uses the manager's own methods (which persist to the skin-settings file):
    //   ON  -> ColorPaletteManager.UnlockColorPalette(id, showPopup:false)   (persists internally)
    //   OFF -> ColorPaletteManager.LockColorPalette(id) + SetCurrentSkinToSkinSettings(current)
    //          to force the lock to be written.
    [BepInPlugin("local.blasphemous.skinunlocker", "Blasphemous Skin Unlocker", "1.2.0")]
    public class SkinUnlocker : BaseUnityPlugin
    {
        const string CoreName   = "Framework.Managers.Core";
        const string ExtrasName = "Gameplay.UI.Others.MenuLogic.ExtrasMenuWidget";

        static ManualLogSource L;

        ConfigEntry<KeyCode> cfgToggleKey;
        ConfigEntry<bool>    cfgAutoShow;

        PropertyInfo pCorePalettes;   // static Core.ColorPaletteManager
        MethodInfo mGetAllIds, mGetUnlockedIds, mUnlock, mLock, mGetCurrent, mSetSkinSettings, mGetSprite;

        Type extrasType;
        PropertyInfo pExtrasActive;
        FieldInfo fCurrentMenu;
        object menuSkinSelector;

        bool ready;
        float gateAccum;
        bool onSkinsPage;
        bool userHidden;   // hid the panel with the hotkey while on the Skins page

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
                mSetSkinSettings = mgrType.GetMethod("SetCurrentSkinToSkinSettings", ip, null, new[] { typeof(string) }, null);
                mGetSprite       = mgrType.GetMethod("GetColorPaletteById", ip, null, new[] { typeof(string) }, null);
            }

            if (extrasType != null)
            {
                pExtrasActive = extrasType.GetProperty("currentlyActive", ip);
                fCurrentMenu  = extrasType.GetField("currentMenu", np);
                var menuType = extrasType.GetNestedType("MENU", BindingFlags.Public | BindingFlags.NonPublic);
                if (menuType != null) menuSkinSelector = SafeGet(() => Enum.Parse(menuType, "SKINSELECTOR"));
            }

            ready = pCorePalettes != null && mGetAllIds != null && mGetUnlockedIds != null && mUnlock != null && mLock != null;
            L.LogInfo($"[SkinUnlocker] v1.2 ready={ready}. skinsPage={extrasType != null && fCurrentMenu != null}. {cfgToggleKey.Value}=toggle panel.");
            if (!ready) L.LogWarning("[SkinUnlocker] color palette API not fully resolved - toggling may be unavailable.");
        }

        void Update()
        {
            gateAccum += Time.unscaledDeltaTime;
            if (gateAccum >= 0.3f) { gateAccum = 0f; onSkinsPage = IsOnSkinsPage(); }

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

        void SetUnlocked(string id, bool unlocked)
        {
            var mgr = Mgr();
            if (mgr == null) return;
            if (unlocked)
            {
                SafeSet(() => mUnlock.Invoke(mgr, new object[] { id, false }));   // persists internally, no popup
            }
            else
            {
                SafeSet(() => mLock.Invoke(mgr, new object[] { id }));
                // LockColorPalette only writes the file if it was the current skin; force a save.
                string cur = SafeGet(() => mGetCurrent?.Invoke(mgr, null)) as string ?? "PENITENT_DEFAULT";
                SafeSet(() => mSetSkinSettings?.Invoke(mgr, new object[] { cur }));
            }
            L.LogInfo($"[SkinUnlocker] {id} -> {(unlocked ? "UNLOCKED" : "LOCKED")}.");
        }

        void SetAll(bool unlocked)
        {
            if (skins == null) return;
            var mgr = Mgr();
            if (mgr == null) return;
            foreach (var id in skins)
            {
                if (unlocked) SafeSet(() => mUnlock.Invoke(mgr, new object[] { id, false }));
                else          SafeSet(() => mLock.Invoke(mgr, new object[] { id }));
            }
            // one persist pass at the end
            string cur = SafeGet(() => mGetCurrent?.Invoke(mgr, null)) as string ?? "PENITENT_DEFAULT";
            SafeSet(() => mSetSkinSettings?.Invoke(mgr, new object[] { cur }));
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
                if (GUILayout.Button(isOn ? "Lock" : "Unlock", btnStyle, GUILayout.Width(70f))) SetUnlocked(id, !isOn);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Label("Changes save to the skin-settings file. New unlocks appear in the carousel next time you open the Skins page.", hintStyle);
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
