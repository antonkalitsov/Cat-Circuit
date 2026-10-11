using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CatCircuit
{
    /// <summary>
    /// Two menus in one, sharing the same pages:
    ///  * Title menu – appears once when the game starts in the village (Нова игра / Продължи / Управление /
    ///    Настройки / Кредити / Изход). It cannot be closed with Esc, you have to pick Start or Continue.
    ///  * Pause menu – Esc (Продължи / Настройки / Управление / Рестартирай / Към селото / Главно меню / Изход).
    /// Creates itself when the game starts – nothing to place in scenes.
    /// Drawn with Unity's immediate-mode GUI so it needs no canvas or prefab.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        private enum Page { Main, Settings, Controls, Credits }

        /// <summary>Editor only: untick CatCircuit ▸ Show main menu in Play Mode to skip the title menu while testing.</summary>
        public const string EditorPrefKey = "cc_show_title_in_editor";

        /// <summary>Put your name here to show it on the Credits page.</summary>
        private const string Author = "";

        /// <summary>Code that unlocks the teacher section (F9 on the title menu). Change it before giving the game to classes.</summary>
        private const string TeacherCode = "teacher";

        private static PauseMenu instance;

        private bool open;
        private bool titleMode;
        private bool confirmNew;
        private Page page;
        private float savedTimeScale = 1f;

        private bool teacherBoxOpen;
        private bool teacherWrong;
        private string teacherInput = "";

        private GUIStyle titleStyle, heroStyle, buttonStyle, labelStyle, centerStyle, smallStyle, valueStyle, fieldStyle, smallButtonStyle;
        private Texture2D overlayTex, panelTex, buttonTex, buttonHoverTex, accentTex;

        public static bool IsOpen => instance != null && instance.open;

        /// <summary>True after the teacher code was entered (this session only, never saved).</summary>
        public static bool TeacherMode { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => TeacherMode = false;

        private static bool ShowTitleOnStart
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(EditorPrefKey, true);
#else
                return true;
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            var go = new GameObject("PauseMenu");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PauseMenu>();

            // The title menu shows once when the game starts in the village (build index 0).
            if (SceneManager.GetActiveScene().buildIndex == 0 && ShowTitleOnStart) instance.OpenTitle();
        }

        private void Update()
        {
            if (open)
            {
                // The cat's scripts may lock the cursor again when a scene starts – keep it free for the menu.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // F9 on the title menu: teacher code box.
            if (open && titleMode && page == Page.Main && !TeacherMode && kb.f9Key.wasPressedThisFrame)
            {
                teacherBoxOpen = !teacherBoxOpen;
                teacherInput = "";
                teacherWrong = false;
                Click();
            }

            if (!kb.escapeKey.wasPressedThisFrame) return;

            if (open)
            {
                if (page != Page.Main) { page = Page.Main; Click(); }
                else if (titleMode && teacherBoxOpen) { teacherBoxOpen = false; Click(); }
                else if (!titleMode) Close();   // the title menu stays until the player picks Start / Continue
            }
            else if (!CodeTerminal.AnyOpen && !EndGameScreen.IsOpen && !ControlsHint.IsOpen)   // Esc in the terminal closes the terminal instead
            {
                Open();
            }
        }

        public void Open()
        {
            if (open) return;
            open = true;
            titleMode = false;
            page = Page.Main;
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            UIFocus.Push();
            Click();
            Telemetry.Flush();
        }

        /// <summary>Opens the title menu (game start, or "Главно меню" in the pause menu).</summary>
        public void OpenTitle()
        {
            if (open) Close();
            open = true;
            titleMode = true;
            confirmNew = false;
            page = Page.Main;
            savedTimeScale = Time.timeScale <= 0f ? 1f : Time.timeScale;
            Time.timeScale = 0f;
            UIFocus.Push();
            teacherBoxOpen = false;
            Telemetry.Flush();
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            titleMode = false;
            Time.timeScale = savedTimeScale <= 0f ? 1f : savedTimeScale;
            UIFocus.Pop();
            GameSettings.Save();
            Click();
        }

        private void OnDisable()
        {
            if (open) Close();
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Нова игра (false) or Продължи (true): load / wipe the save, then start in the village.</summary>
        private void BeginGame(bool continueSave)
        {
            Telemetry.Log(continueSave ? "menu_continue" : "menu_new_game", continueSave ? "solved=" + GameSave.Load().Count : null);
            if (GameProgress.Instance != null) GameProgress.Instance.StartSession(continueSave);
            else if (!continueSave) GameSave.Clear();

            Close();
            GameUI.Reset();
            PlayerPositionStore.ClearSpawn();
            PlayerPositionStore.cameFromScene = "";

            // Reload the village so everything in it reads the freshly loaded progress.
            SceneManager.sceneLoaded += OnVillageLoaded;
            SceneManager.LoadScene(0);
        }

        private static void OnVillageLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnVillageLoaded;
            ControlsHint.ShowIfFirstTime();
        }

        /// <summary>Teacher: load a house (1-5) or the village (0) directly. Reads the save, never changes it.</summary>
        private void TeacherJump(int house)
        {
            string scene = house > 0 ? "House" + house : null;
            if (scene != null ? !Application.CanStreamedLevelBeLoaded(scene) : !Application.CanStreamedLevelBeLoaded(0)) return;

            Telemetry.Log("teacher_jump", house > 0 ? "house=" + house : "village", house);
            if (GameProgress.Instance != null) GameProgress.Instance.StartSession(true);

            Close();
            GameUI.Reset();
            PlayerPositionStore.ClearSpawn();
            PlayerPositionStore.cameFromScene = "";
            if (scene != null) SceneManager.LoadScene(scene);
            else SceneManager.LoadScene(0);
        }

        private void SubmitTeacherCode()
        {
            if (teacherInput.Trim() == TeacherCode)
            {
                TeacherMode = true;
                teacherBoxOpen = false;
                teacherWrong = false;
                Click();
            }
            else
            {
                teacherWrong = true;
                GameAudio.Play(Sfx.BuildError);
            }
            teacherInput = "";
        }

        private void RestartRoom()
        {
            Close();
            GameUI.Reset();
            PlayerPositionStore.ClearSpawn();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void GoToVillage()
        {
            Close();
            GameUI.Reset();
            PlayerPositionStore.ClearSpawn();
            PlayerPositionStore.cameFromScene = SceneManager.GetActiveScene().name;   // spawn at this house's door
            SceneManager.LoadScene(0);
        }

        private void QuitGame()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void Click() => GameAudio.Play(Sfx.UiClick);

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (!open) return;
            EnsureStyles();

            float scale = Screen.height / 1080f;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;

            GUI.DrawTexture(new Rect(0, 0, w, 1080), overlayTex);

            float titleH = TeacherMode || teacherBoxOpen ? 920 : 820;   // the teacher parts need more room
            float panelW = 640, panelH = page == Page.Main && titleMode ? titleH : 700;
            var panel = new Rect((w - panelW) / 2f, (1080 - panelH) / 2f, panelW, panelH);
            GUI.DrawTexture(panel, panelTex);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 6), accentTex);

            GUILayout.BeginArea(new Rect(panel.x + 40, panel.y + 30, panel.width - 80, panel.height - 60));
            switch (page)
            {
                case Page.Main:
                    if (titleMode) DrawTitle(); else DrawMain();
                    break;
                case Page.Settings: DrawSettings(); break;
                case Page.Controls: DrawControls(); break;
                case Page.Credits: DrawCredits(); break;
            }
            GUILayout.EndArea();
            GUI.matrix = old;
        }

        private void DrawTitle()
        {
            GUILayout.Label("Котаракът електротехник", heroStyle);
            GUILayout.Label("образователна игра за електрически вериги", smallStyle);
            GUILayout.Space(20);

            bool hasSave = GameSave.HasSave;
            if (hasSave)
            {
                int solved = GameSave.Load().Count;
                if (Button($"Продължи  ({solved}/{GameSave.TotalPuzzles})")) BeginGame(true);
            }

            string newText = confirmNew ? "Изтрий напредъка и започни?" : "Нова игра";
            if (Button(newText))
            {
                if (hasSave && !confirmNew) { confirmNew = true; Click(); }   // ask once before wiping a save
                else BeginGame(false);
            }

            if (Button("Управление")) { page = Page.Controls; confirmNew = false; Click(); }
            if (Button("Настройки")) { page = Page.Settings; confirmNew = false; Click(); }
            if (Button("Кредити")) { page = Page.Credits; confirmNew = false; Click(); }
#if !UNITY_WEBGL || UNITY_EDITOR
            if (Button("Изход от играта")) QuitGame();   // a browser tab can't be quit – hidden on the web
#endif
            if (teacherBoxOpen) DrawTeacherCodeBox();
            if (TeacherMode) DrawTeacherJumps();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Играта пази анонимна статистика (без имена). Може да се изключи в Настройки.", smallStyle);
        }

        /// <summary>F9 box: the teacher types the code (Enter or "Отключи").</summary>
        private void DrawTeacherCodeBox()
        {
            Event e = Event.current;
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                         && GUI.GetNameOfFocusedControl() == "teacher-code";
            if (enter) e.Use();

            GUILayout.Space(10);
            GUILayout.Label(teacherWrong ? "Код за учител – грешен код, опитай пак" : "Код за учител", centerStyle);
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("teacher-code");
            teacherInput = GUILayout.PasswordField(teacherInput, '*', 32, fieldStyle, GUILayout.Height(52));
            bool unlock = GUILayout.Button("Отключи", smallButtonStyle, GUILayout.Width(170), GUILayout.Height(52));
            GUILayout.EndHorizontal();
            if (e.type == EventType.Repaint && GUI.GetNameOfFocusedControl() == "") GUI.FocusControl("teacher-code");

            if (enter || unlock) SubmitTeacherCode();
        }

        /// <summary>Teacher section: go straight to any house or the village.</summary>
        private void DrawTeacherJumps()
        {
            GUILayout.Space(10);
            GUILayout.Label("Учител: избери къща", valueStyle);
            GUILayout.BeginHorizontal();
            for (int h = 1; h <= 3; h++) TeacherJumpButton(h);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            for (int h = 4; h <= GameSave.TotalPuzzles; h++) TeacherJumpButton(h);
            TeacherJumpButton(0);
            GUILayout.EndHorizontal();
        }

        private void TeacherJumpButton(int house)
        {
            bool exists = house > 0 ? Application.CanStreamedLevelBeLoaded("House" + house) : Application.CanStreamedLevelBeLoaded(0);
            if (!exists) return;
            if (GUILayout.Button(house > 0 ? "Къща " + house : "Селото", smallButtonStyle, GUILayout.Height(48))) TeacherJump(house);
        }

        private void DrawMain()
        {
            GUILayout.Label("Пауза", titleStyle);
            GUILayout.Space(20);
            if (Button("Продължи")) Close();
            if (Button("Настройки")) { page = Page.Settings; Click(); }
            if (Button("Управление")) { page = Page.Controls; Click(); }
            if (Button("Започни стаята отначало")) RestartRoom();
            if (SceneManager.GetActiveScene().buildIndex != 0 && Button("Към селото")) GoToVillage();
            if (Button("Главно меню")) { OpenTitle(); Click(); }
#if !UNITY_WEBGL || UNITY_EDITOR
            if (Button("Изход от играта")) QuitGame();   // a browser tab can't be quit – hidden on the web
#endif
            GUILayout.FlexibleSpace();
            GUILayout.Label("Esc – обратно към играта", smallStyle);
        }

        private void DrawSettings()
        {
            GUILayout.Label("Настройки", titleStyle);
            GUILayout.Space(10);

            float v;
            if ((v = Slider("Сила на звука", GameSettings.MasterVolume)) != GameSettings.MasterVolume) GameSettings.MasterVolume = v;
            if ((v = Slider("Звукови ефекти", GameSettings.SfxVolume)) != GameSettings.SfxVolume) GameSettings.SfxVolume = v;
            if (GameAudio.HasMusic && (v = Slider("Музика", GameSettings.MusicVolume)) != GameSettings.MusicVolume) GameSettings.MusicVolume = v;

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Качество на графиката", labelStyle, GUILayout.Width(300));
            string[] names = QualitySettings.names;
            int q = QualitySettings.GetQualityLevel();
            if (GUILayout.Button("<", buttonStyle, GUILayout.Width(50), GUILayout.Height(44)) && q > 0) { GameSettings.Quality = q - 1; Click(); }
            GUILayout.Label(names.Length > 0 ? names[Mathf.Clamp(q, 0, names.Length - 1)] : "-", valueStyle, GUILayout.Width(130));
            if (GUILayout.Button(">", buttonStyle, GUILayout.Width(50), GUILayout.Height(44)) && q < names.Length - 1) { GameSettings.Quality = q + 1; Click(); }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Цял екран", labelStyle, GUILayout.Width(300));
            if (GUILayout.Button(Screen.fullScreen ? "Да" : "Не", buttonStyle, GUILayout.Width(120), GUILayout.Height(44)))
            {
                GameSettings.Fullscreen = !Screen.fullScreen;
                Click();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Анонимна статистика", labelStyle, GUILayout.Width(300));
            if (GUILayout.Button(GameSettings.Telemetry ? "Да" : "Не", buttonStyle, GUILayout.Width(120), GUILayout.Height(44)))
            {
                GameSettings.Telemetry = !GameSettings.Telemetry;
                Click();
            }
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (Button("Стандартни")) { GameSettings.ResetToDefaults(); Click(); }
            if (Button("Назад")) { GameSettings.Save(); page = Page.Main; Click(); }
            GUILayout.EndHorizontal();
        }

        private void DrawControls()
        {
            GUILayout.Label("Управление", titleStyle);
            GUILayout.Space(10);
            foreach (string[] row in UIText.ControlRows) Row(row[0], row[1]);
            GUILayout.Space(10);
            GUILayout.Label("На компютъра: Ctrl+Enter – построй, Esc – затвори", smallStyle);
            GUILayout.FlexibleSpace();
            if (Button("Назад")) { page = Page.Main; Click(); }
        }

        private void DrawCredits()
        {
            GUILayout.Label("Кредити", titleStyle);
            GUILayout.Space(20);
            GUILayout.Label("Котаракът електротехник", valueStyle);
            GUILayout.Label("образователна 3D игра за електрически вериги", smallStyle);
            GUILayout.Space(16);
            GUILayout.Label("Дипломна работа", centerStyle);
            if (!string.IsNullOrEmpty(Author)) GUILayout.Label(Author, valueStyle);
            GUILayout.Space(16);
            GUILayout.Label("Направена с Unity", smallStyle);
            GUILayout.FlexibleSpace();
            if (Button("Назад")) { page = Page.Main; Click(); }
        }

        private bool Button(string text) => GUILayout.Button(text, buttonStyle, GUILayout.Height(58));

        private void Row(string key, string action)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, valueStyle, GUILayout.Width(170));
            GUILayout.Label(action, labelStyle);
            GUILayout.EndHorizontal();
        }

        private float Slider(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(300));
            GUILayout.BeginVertical();
            GUILayout.Space(18);
            float v = GUILayout.HorizontalSlider(value, 0f, 1f, GUILayout.Width(180));
            GUILayout.EndVertical();
            GUILayout.Label(Mathf.RoundToInt(v * 100f) + "%", valueStyle, GUILayout.Width(70));
            GUILayout.EndHorizontal();
            return v;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;

            overlayTex = Solid(new Color(0f, 0f, 0f, 0.55f));
            panelTex = Solid(new Color(0.07f, 0.08f, 0.12f, 0.96f));
            buttonTex = Solid(new Color(0.16f, 0.18f, 0.26f, 1f));
            buttonHoverTex = Solid(new Color(0.26f, 0.3f, 0.45f, 1f));
            accentTex = Solid(new Color(1f, 0.8f, 0.25f, 1f));

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.35f);
            Font ui = UIFonts.UI(26);   // needed on the web: browsers have no OS fonts for Cyrillic
            if (ui != null) titleStyle.font = ui;

            // The game's name is long, so the title menu uses a slightly smaller heading.
            heroStyle = new GUIStyle(titleStyle) { fontSize = 38 };

            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            buttonStyle.normal.background = buttonTex;
            buttonStyle.hover.background = buttonHoverTex;
            buttonStyle.active.background = buttonHoverTex;
            buttonStyle.normal.textColor = buttonStyle.hover.textColor = buttonStyle.active.textColor = Color.white;
            buttonStyle.margin = new RectOffset(0, 0, 6, 6);
            if (ui != null) buttonStyle.font = ui;

            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            if (ui != null) labelStyle.font = ui;
            centerStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };
            valueStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            valueStyle.normal.textColor = new Color(1f, 0.85f, 0.35f);
            smallStyle = new GUIStyle(labelStyle) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            smallStyle.normal.textColor = new Color(0.65f, 0.67f, 0.72f);
            smallStyle.wordWrap = true;

            smallButtonStyle = new GUIStyle(buttonStyle) { fontSize = 22 };
            smallButtonStyle.margin = new RectOffset(4, 4, 4, 4);

            fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            fieldStyle.normal.textColor = fieldStyle.focused.textColor = Color.white;
            if (ui != null) fieldStyle.font = ui;
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
