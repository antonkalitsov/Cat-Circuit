using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Player options, saved with PlayerPrefs (kept between game sessions).
    /// Changed in the pause menu (Esc ▸ Настройки).
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

        /// <summary>Called once when the game starts.</summary>
        public static void Apply()
        {
            if (PlayerPrefs.HasKey(KeyQuality)) QualitySettings.SetQualityLevel(Quality, true);
            if (PlayerPrefs.HasKey(KeyFullscreen)) Screen.fullScreen = Fullscreen;
            GameAudio.RefreshVolumes();
        }

        public static void Save() => PlayerPrefs.Save();

        public static void ResetToDefaults()
        {
            foreach (string k in new[] { KeyMaster, KeySfx, KeyMusic, KeyQuality, KeyFullscreen, KeyBackdrops, KeyTelemetry })
                PlayerPrefs.DeleteKey(k);
            GameAudio.RefreshVolumes();
        }
    }
}
