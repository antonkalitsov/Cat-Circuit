using UnityEngine;
using UnityEngine.InputSystem;

namespace CatCircuit
{
    /// <summary>
    /// A small "how to play" card shown once, the first time a game is started from the title menu.
    /// Closes with E / Enter / Space or the button, and is remembered (PlayerPrefs) so it never nags again.
    /// The same keys are always available in Esc ▸ Управление.
    /// Drawn with immediate-mode GUI like the pause menu, so nothing has to be placed in scenes.
    /// </summary>
    public class ControlsHint : MonoBehaviour
    {
        private const string KeySeen = "cc_controls_seen";

        private static ControlsHint instance;

        private bool open;
        private float openedAt;
        private GUIStyle titleStyle, buttonStyle, labelStyle, valueStyle, smallStyle;
        private Texture2D overlayTex, panelTex, buttonTex, buttonHoverTex, accentTex;

        public static bool IsOpen => instance != null && instance.open;

        /// <summary>Shows the card only if the player has never closed it before.</summary>
        public static void ShowIfFirstTime()
        {
            if (PlayerPrefs.GetInt(KeySeen, 0) == 1) return;
            Show();
        }

        public static void Show()
        {
            if (instance == null)
            {
                var go = new GameObject("ControlsHint");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<ControlsHint>();
            }
            instance.Open();
        }

        private void Open()
        {
            if (open) return;
            open = true;
            openedAt = Time.unscaledTime;
            UIFocus.Push();
        }

        private void Close()
        {
            if (!open) return;
            open = false;
            UIFocus.Pop();
            PlayerPrefs.SetInt(KeySeen, 1);
            PlayerPrefs.Save();
            GameAudio.Play(Sfx.UiClick);
        }

        private void OnDisable()
        {
            if (open) Close();
        }

        private void Update()
        {
            if (!open) return;

            // The cat's scripts may lock the cursor when the scene starts – keep it free for the button.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Keyboard kb = Keyboard.current;
            if (kb == null || Time.unscaledTime - openedAt < 0.4f) return;   // ignore the key that started the game
            if (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                Close();
        }

        private void OnGUI()
        {
            if (!open) return;
            EnsureStyles();

            float scale = Screen.height / 1080f;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;

            GUI.DrawTexture(new Rect(0, 0, w, 1080), overlayTex);

            float panelW = 640, panelH = 620;
            var panel = new Rect((w - panelW) / 2f, (1080 - panelH) / 2f, panelW, panelH);
            GUI.DrawTexture(panel, panelTex);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 6), accentTex);

            GUILayout.BeginArea(new Rect(panel.x + 40, panel.y + 30, panel.width - 80, panel.height - 60));
            GUILayout.Label("Как се играе", titleStyle);
            GUILayout.Space(14);
            foreach (string[] row in UIText.ControlRows)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(row[0], valueStyle, GUILayout.Width(170));
                GUILayout.Label(row[1], labelStyle);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(14);
            GUILayout.Label("Тези клавиши са винаги в менюто (Esc ▸ Управление).", smallStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Разбрах  [E]", buttonStyle, GUILayout.Height(58))) Close();
            GUILayout.EndArea();

            GUI.matrix = old;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;

            overlayTex = Solid(new Color(0f, 0f, 0f, 0.55f));
            panelTex = Solid(new Color(0.07f, 0.08f, 0.12f, 0.96f));
            buttonTex = Solid(new Color(0.16f, 0.18f, 0.26f, 1f));
            buttonHoverTex = Solid(new Color(0.26f, 0.3f, 0.45f, 1f));
            accentTex = Solid(new Color(1f, 0.8f, 0.25f, 1f));

            Font ui = UIFonts.UI(26);   // Cyrillic on the web needs the bundled font

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.35f);
            if (ui != null) titleStyle.font = ui;

            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            buttonStyle.normal.background = buttonTex;
            buttonStyle.hover.background = buttonHoverTex;
            buttonStyle.active.background = buttonHoverTex;
            buttonStyle.normal.textColor = buttonStyle.hover.textColor = buttonStyle.active.textColor = Color.white;
            if (ui != null) buttonStyle.font = ui;

            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            if (ui != null) labelStyle.font = ui;
            valueStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            valueStyle.normal.textColor = new Color(1f, 0.85f, 0.35f);
            smallStyle = new GUIStyle(labelStyle) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            smallStyle.normal.textColor = new Color(0.65f, 0.67f, 0.72f);
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
