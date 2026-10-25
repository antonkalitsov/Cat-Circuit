using UnityEngine;
using UnityEngine.InputSystem;

namespace CatCircuit
{
    /// <summary>
    /// F3 (or Настройки ▸ Показвай FPS) shows / hides a small FPS read-out in the bottom-left corner
    /// (for checking school laptops). Off by default, remembered. Hidden = it only checks the F3 key; the text is rebuilt twice a second, not every frame.
    /// Creates itself when the game starts – nothing to place in scenes.
    /// </summary>
    public class FpsCounter : MonoBehaviour
    {
        private const float UpdateInterval = 0.5f;

        private static FpsCounter instance;

        private bool visible;
        private float smoothedDelta;
        private float timer;
        private string text = "";
        private GUIStyle style;
        private Texture2D backTex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            var go = new GameObject("FpsCounter");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FpsCounter>();
            instance.useGUILayout = false;
            instance.visible = GameSettings.ShowFps;
        }

        /// <summary>Shown / hidden from the settings page (Показвай FPS) or with F3.</summary>
        public static void SetVisible(bool show)
        {
            if (instance == null || instance.visible == show) return;
            instance.visible = show;
            instance.smoothedDelta = 0f;
            instance.timer = UpdateInterval;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) GameSettings.ShowFps = !visible;
            if (!visible) return;

            float dt = Time.unscaledDeltaTime;
            smoothedDelta = smoothedDelta <= 0f ? dt : Mathf.Lerp(smoothedDelta, dt, 0.1f);

            timer += dt;
            if (timer < UpdateInterval) return;
            timer = 0f;
            float fps = smoothedDelta > 0f ? 1f / smoothedDelta : 0f;
            text = $"{fps:0} FPS   {smoothedDelta * 1000f:0.0} ms";
        }

        private void OnGUI()
        {
            if (!visible || Event.current.type != EventType.Repaint) return;
            EnsureStyle();

            float scale = Screen.height / 1080f;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var box = new Rect(16, 1080 - 56, 240, 40);
            GUI.DrawTexture(box, backTex);
            style.normal.textColor = smoothedDelta > 0f && smoothedDelta > 1f / 30f ? new Color(1f, 0.55f, 0.45f) : new Color(0.7f, 1f, 0.7f);
            GUI.Label(box, text, style);

            GUI.matrix = old;
        }

        private void EnsureStyle()
        {
            if (style != null) return;
            backTex = new Texture2D(1, 1);
            backTex.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.12f, 0.8f));
            backTex.Apply();

            style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Font ui = UIFonts.UI(22);
            if (ui != null) style.font = ui;
        }
    }
}
