using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatCircuit
{
    /// <summary>
    /// Applies Настройки ▸ Чувствителност на мишката and Обърни вертикалата to every Cinemachine 3
    /// CinemachineInputAxisController (the orbit X / Y look axes; zoom is left alone).
    /// The gains set up in the scene are remembered, so 1.0 = the original feel. Re-applied after every scene load.
    /// </summary>
    public static class CameraSettings
    {
        private static readonly Dictionary<object, float> baseGains = new Dictionary<object, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => baseGains.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        public static void Apply()
        {
            float sens = GameSettings.MouseSensitivity;
            bool invert = GameSettings.InvertY;

            foreach (CinemachineInputAxisController c in
                     Object.FindObjectsByType<CinemachineInputAxisController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c == null || c.Controllers == null) continue;
                foreach (var axis in c.Controllers)
                {
                    if (axis == null || axis.Input == null || string.IsNullOrEmpty(axis.Name)) continue;
                    bool isX = axis.Name.EndsWith(" X");
                    bool isY = axis.Name.EndsWith(" Y");
                    if (!isX && !isY) continue;   // e.g. "Orbit Scale" (zoom)

                    if (!baseGains.TryGetValue(axis, out float baseGain))
                    {
                        baseGain = axis.Input.Gain;
                        baseGains[axis] = baseGain;
                    }
                    axis.Input.Gain = baseGain * sens * (isY && invert ? -1f : 1f);
                }
            }
        }
    }
}
