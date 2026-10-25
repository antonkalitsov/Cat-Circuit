using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Player options, saved with PlayerPrefs (kept between game sessions).
    /// Changed in the menu (title menu or Esc ▸ Настройки).
    /// </summary>
    public static class GameSettings
    {
        private const string KeyMaster = "cc_master";
        private const string KeySfx = "cc_sfx";
        private const string KeyMusic = "cc_music";
        private const string KeyQuality = "cc_quality";
        private const string KeyFullscreen = "cc_fullscreen";
        private const string KeyBackdrops = "cc_text_backgrounds";
        private const string KeyTelemetry = "cc_telemetry";
        private const string KeySensitivity = "cc_mouse_sensitivity";
        private const string KeyInvertY = "cc_invert_y";
        private const string KeyShowFps = "cc_show_fps";

        /// <summary>One-time tips that "Покажи отново съветите" brings back.</summary>
        private static readonly string[] TipKeys = { "cc_tutorial_done", "cc_controls_seen", HouseInfo.GuideIntroKey };

        private static bool defaultsCaptured;
        private static int defaultQuality;
        private static bool defaultFullscreen;

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(KeyMaster, 0.9f);
            set { PlayerPrefs.SetFloat(KeyMaster, Mathf.Clamp01(value)); GameAudio.RefreshVolumes(); }
        }

        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(KeySfx, 0.8f);
            set { PlayerPrefs.SetFloat(KeySfx, Mathf.Clamp01(value)); GameAudio.RefreshVolumes(); }
        }

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(KeyMusic, 0.6f);
            set { PlayerPrefs.SetFloat(KeyMusic, Mathf.Clamp01(value)); GameAudio.RefreshVolumes(); }
        }

        public static int Quality
        {
            get => PlayerPrefs.GetInt(KeyQuality, QualitySettings.GetQualityLevel());
            set
            {
                int q = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1);
                PlayerPrefs.SetInt(KeyQuality, q);
                QualitySettings.SetQualityLevel(q, true);
            }
        }

        /// <summary>On the web the browser only allows this right after a click – the menu sets it on mouse down.</summary>
        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
            set { PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0); Screen.fullScreen = value; }
        }

        /// <summary>Anonymous statistics on/off (see <see cref="CatCircuit.Telemetry"/>). Default on.</summary>
        public static bool Telemetry
        {
            get => PlayerPrefs.GetInt(KeyTelemetry, 1) == 1;
            set => PlayerPrefs.SetInt(KeyTelemetry, value ? 1 : 0);
        }

        /// <summary>Mouse / camera speed, 0.2 – 2 (1 = as set up in the scene).</summary>
        public static float MouseSensitivity
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(KeySensitivity, 1f), 0.2f, 2f);
            set { PlayerPrefs.SetFloat(KeySensitivity, Mathf.Clamp(value, 0.2f, 2f)); CameraSettings.Apply(); }
        }

        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
            set { PlayerPrefs.SetInt(KeyInvertY, value ? 1 : 0); CameraSettings.Apply(); }
        }

        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt(KeyShowFps, 0) == 1;
            set { PlayerPrefs.SetInt(KeyShowFps, value ? 1 : 0); FpsCounter.SetVisible(value); }
        }

        /// <summary>Dark boxes behind the prompt / objective / held-item texts.</summary>
        public static bool TextBackgrounds
        {
            get => PlayerPrefs.GetInt(KeyBackdrops, 1) == 1;
            set { PlayerPrefs.SetInt(KeyBackdrops, value ? 1 : 0); ApplyTextBackgrounds(); }
        }

        /// <summary>Called once when the game starts.</summary>
        public static void Apply()
        {
            if (!defaultsCaptured)
            {
                defaultsCaptured = true;
                defaultQuality = QualitySettings.GetQualityLevel();
                defaultFullscreen = Screen.fullScreen;
            }
            if (PlayerPrefs.HasKey(KeyQuality)) QualitySettings.SetQualityLevel(Quality, true);
#if !UNITY_WEBGL || UNITY_EDITOR
            if (PlayerPrefs.HasKey(KeyFullscreen)) Screen.fullScreen = Fullscreen;   // the web needs a click for this
#endif
            GameAudio.RefreshVolumes();
        }

        public static void Save() => PlayerPrefs.Save();

        /// <summary>"Стандартни": everything back to how the game started, and applied right away.</summary>
        public static void ResetToDefaults()
        {
            foreach (string k in new[] { KeyMaster, KeySfx, KeyMusic, KeyQuality, KeyFullscreen, KeyBackdrops, KeyTelemetry,
                                         KeySensitivity, KeyInvertY, KeyShowFps })
                PlayerPrefs.DeleteKey(k);

            if (defaultsCaptured)
            {
                QualitySettings.SetQualityLevel(Mathf.Clamp(defaultQuality, 0, QualitySettings.names.Length - 1), true);
                if (Screen.fullScreen != defaultFullscreen) Screen.fullScreen = defaultFullscreen;
            }
            GameAudio.RefreshVolumes();
            CameraSettings.Apply();
            FpsCounter.SetVisible(false);
            ApplyTextBackgrounds();
            PlayerPrefs.Save();
        }

        /// <summary>The House 1 tips, the controls card and the guide's welcome show again.</summary>
        public static void ResetTips()
        {
            foreach (string k in TipKeys) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
        }

        private static void ApplyTextBackgrounds()
        {
            bool on = TextBackgrounds;
            foreach (TextBackdrop b in Object.FindObjectsByType<TextBackdrop>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                b.enabled = on;
        }
    }
}
