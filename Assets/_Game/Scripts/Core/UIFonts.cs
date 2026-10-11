using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Font for the code terminal and pause menu (Unity's immediate-mode GUI).
    /// Windows builds use fonts installed on the PC. WEB builds cannot, so for Bulgarian text in the
    /// browser put a .ttf with Cyrillic letters at  Assets/_Game/Resources/CatCircuitUIFont.ttf
    /// (CatCircuit ▸ Build ▸ Prepare project for building warns if it is missing).
    /// </summary>
    public static class UIFonts
    {
        public const string ResourceName = "CatCircuitUIFont";

        private static Font ui, mono;
        private static bool uiLoaded, monoLoaded;

        /// <summary>Normal text font, or null to use Unity's default.</summary>
        public static Font UI(int size)
        {
            if (!uiLoaded)
            {
                uiLoaded = true;
                ui = Resources.Load<Font>(ResourceName);
                if (ui == null) ui = FromOS(size, "Segoe UI", "Arial", "DejaVu Sans");
            }
            return ui;
        }

        /// <summary>Monospace font for code, falls back to the UI font.</summary>
        public static Font Mono(int size)
        {
            if (!monoLoaded)
            {
                monoLoaded = true;
                mono = FromOS(size, "Consolas", "Courier New", "DejaVu Sans Mono");
                if (mono == null) mono = UI(size);
            }
            return mono;
        }

        private static Font FromOS(int size, params string[] names)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return null;   // browsers don't give access to installed fonts
#else
            try { return Font.CreateDynamicFontFromOSFont(names, size); }
            catch { return null; }
#endif
        }
    }
}
