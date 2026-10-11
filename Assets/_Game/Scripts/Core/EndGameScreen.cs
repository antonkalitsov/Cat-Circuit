using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatCircuit
{
    /// <summary>
    /// Final "thank you + survey" screen with the survey QR code.
    /// Open it from anywhere with  EndGameScreen.Show();  (the EndGameButton in House 5 does this).
    ///
    /// Texts, link and QR are set in the fields below / in <see cref="EndGameButton"/>.
    /// The QR picture is Assets/_Game/Resources/CatCircuitSurveyQR.png – replace that file to change it.
    /// Drawn with Unity's immediate-mode GUI (like the pause menu), so no canvas or prefab is needed.
    /// </summary>
    public class EndGameScreen : MonoBehaviour
    {
        public const string QrResource = "CatCircuitSurveyQR";
        public const string DefaultSurveyUrl = "https://forms.gle/A5Dgwe5prmS2b45C8";

        private static EndGameScreen instance;

        public static bool IsOpen => instance != null && instance.open;

        private bool open;
        private string surveyUrl = DefaultSurveyUrl;
        private Texture2D qr;
        private float savedTimeScale = 1f;

        private GUIStyle titleStyle, headingStyle, textStyle, linkStyle, buttonStyle, smallStyle;
        private Texture2D overlayTex, panelTex, accentTex, buttonTex, buttonHoverTex, whiteTex;

        // ------------------------------------------------------------------ public API

        /// <summary>Shows the end screen (creates it if needed).</summary>
        public static void Show(string url = null)
        {
            if (instance == null)
            {
                var go = new GameObject("EndGameScreen");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<EndGameScreen>();
            }
            if (!string.IsNullOrEmpty(url)) instance.surveyUrl = url;
            instance.Open();
        }

        public static void Hide()
        {
            if (instance != null) instance.Close();
        }

        private void Open()
        {
            if (open) return;
            open = true;
            if (qr == null) qr = Resources.Load<Texture2D>(QrResource);
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            UIFocus.Push();
            GameUI.HideDialogue();
            GameAudio.Play(Sfx.PuzzleComplete);
        }

        private void Close()
        {
            if (!open) return;
            open = false;
            Time.timeScale = savedTimeScale <= 0f ? 1f : savedTimeScale;
            UIFocus.Pop();
        }

        private void OnDisable()
        {
            if (open) Close();
        }

        // ------------------------------------------------------------------ actions

        private void OpenSurvey()
        {
            GameAudio.Play(Sfx.UiClick);
            Application.OpenURL(surveyUrl);
        }

        private void PlayAgain()
        {
            GameAudio.Play(Sfx.UiClick);
            Close();
            if (GameProgress.Instance != null) GameProgress.Instance.ResetAll();
            GameUI.Reset();
            PlayerPositionStore.ClearSpawn();
            PlayerPositionStore.cameFromScene = "";
            SceneManager.LoadScene(0);
        }

        private void BackToGame()
        {
            GameAudio.Play(Sfx.UiClick);
            Close();
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (!open) return;
            EnsureStyles();
            GUI.depth = -100;   // above everything else

            // Design at 1920x1080, scaled to the window height.
            float scale = Screen.height / 1080f;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;

            GUI.DrawTexture(new Rect(0, 0, w, 1080), overlayTex);

            float panelW = Mathf.Min(1500f, w - 80f), panelH = 820f;
            var panel = new Rect((w - panelW) / 2f, (1080f - panelH) / 2f, panelW, panelH);
            GUI.DrawTexture(panel, panelTex);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 8), accentTex);

            // Title
            GUI.Label(new Rect(panel.x, panel.y + 40, panel.width, 80), "Поздравления! Ти завърши играта!", titleStyle);
            GUI.Label(new Rect(panel.x, panel.y + 120, panel.width, 40),
                "Помогна на всички котки в селото да оправят своите електрически вериги.", smallStyle);

            // QR on the right
            float qrSize = 400f;
            var qrRect = new Rect(panel.xMax - qrSize - 80f, panel.y + 200f, qrSize, qrSize);
            GUI.DrawTexture(new Rect(qrRect.x - 16, qrRect.y - 16, qrRect.width + 32, qrRect.height + 32), whiteTex);
            if (qr != null) GUI.DrawTexture(qrRect, qr, ScaleMode.ScaleToFit);
            else GUI.Label(qrRect, "QR", headingStyle);

            // Survey text on the left
            float textX = panel.x + 90f, textW = qrRect.x - 60f - textX;
            GUI.Label(new Rect(textX, panel.y + 210, textW, 70), "АНКЕТА", headingStyle);
            GUI.Label(new Rect(textX, panel.y + 290, textW, 170),
                "Моля да споделиш мнението си за играта,\nкато отговориш на въпросите в анкетата\n" +
                "като сканираш QR кода вдясно или въведеш адреса:", textStyle);
            GUI.Label(new Rect(textX, panel.y + 460, textW, 50), surveyUrl, linkStyle);

            // Buttons
            float by = panel.yMax - 140f, bw = 340f, bh = 76f, gap = 30f;
            float totalW = bw * 3 + gap * 2;
            float bx = panel.x + (panel.width - totalW) / 2f;
            if (GUI.Button(new Rect(bx, by, bw, bh), "Отвори анкетата", buttonStyle)) OpenSurvey();
            if (GUI.Button(new Rect(bx + bw + gap, by, bw, bh), "Играй отначало", buttonStyle)) PlayAgain();
            if (GUI.Button(new Rect(bx + (bw + gap) * 2, by, bw, bh), "Обратно в играта", buttonStyle)) BackToGame();

            GUI.matrix = old;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;

            overlayTex = Solid(new Color(0f, 0f, 0f, 0.7f));
            panelTex = Solid(new Color(0.07f, 0.08f, 0.12f, 0.97f));
            accentTex = Solid(new Color(1f, 0.8f, 0.25f, 1f));
            buttonTex = Solid(new Color(0.16f, 0.18f, 0.26f, 1f));
            buttonHoverTex = Solid(new Color(0.26f, 0.3f, 0.45f, 1f));
            whiteTex = Solid(Color.white);

            Font ui = UIFonts.UI(32);

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.35f);

            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            smallStyle.normal.textColor = new Color(0.8f, 0.82f, 0.88f);

            headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 52, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            headingStyle.normal.textColor = Color.white;

            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 32, wordWrap = true, alignment = TextAnchor.UpperLeft };
            textStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);

            linkStyle = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            linkStyle.normal.textColor = new Color(0.45f, 0.8f, 1f);

            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            buttonStyle.normal.background = buttonTex;
            buttonStyle.hover.background = buttonHoverTex;
            buttonStyle.active.background = buttonHoverTex;
            buttonStyle.normal.textColor = buttonStyle.hover.textColor = buttonStyle.active.textColor = Color.white;

            if (ui != null)
                foreach (GUIStyle s in new[] { titleStyle, smallStyle, headingStyle, textStyle, linkStyle, buttonStyle })
                    s.font = ui;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;
    }
}
