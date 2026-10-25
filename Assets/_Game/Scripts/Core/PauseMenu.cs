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

        private bool tipsReset;

        private GUIStyle titleStyle, heroStyle, heroShadowStyle, subtitleStyle, versionStyle, buttonStyle, labelStyle, centerStyle, smallStyle,
                         valueStyle, fieldStyle, smallButtonStyle;
        private Texture2D overlayTex, titleOverlayTex, panelTex, buttonTex, buttonHoverTex, accentTex, wireTex, dotTex, glowTex;

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

            bool titleScreen = titleMode && page == Page.Main;
            GUI.DrawTexture(new Rect(0, 0, w, 1080), titleScreen ? titleOverlayTex : overlayTex);

            Rect panel;
            if (titleScreen)
            {
                DrawTitleHeader(w);
                float h = TeacherMode || teacherBoxOpen ? 640 : 520;   // the teacher parts need more room
                panel = new Rect((w - 600) / 2f, 380, 600, h);
            }
            else
            {
                bool settings = page == Page.Settings;
                float panelW = settings ? 800 : 640, panelH = settings ? 980 : 700;
                panel = new Rect((w - panelW) / 2f, (1080 - panelH) / 2f, panelW, panelH);
            }
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
            if (titleScreen) DrawTitleFooter(w);
            GUI.matrix = old;
        }

        // ------------------------------------------------------------------ title screen

        /// <summary>Big game name + a little animated circuit: electrons run around a loop and light an LED.</summary>
        private void DrawTitleHeader(float w)
        {
            const string name = "Котаракът електротехник";
            GUI.Label(new Rect(4, 84, w, 110), name, heroShadowStyle);
            GUI.Label(new Rect(0, 80, w, 110), name, heroStyle);
            GUI.Label(new Rect(0, 186, w, 40), "образователна игра за електрически вериги", subtitleStyle);

            float loopW = 640f, loopH = 74f;
            float x0 = (w - loopW) / 2f, y0 = 256f;
            float t = Time.unscaledTime;
            const float wire = 4f;

            // Wires: top, bottom, the right side (to the LED) and the left side broken by the battery.
            GUI.DrawTexture(new Rect(x0, y0, loopW, wire), wireTex);
            GUI.DrawTexture(new Rect(x0, y0 + loopH, loopW + wire, wire), wireTex);
            GUI.DrawTexture(new Rect(x0 + loopW, y0, wire, loopH), wireTex);
            GUI.DrawTexture(new Rect(x0, y0, wire, 24f), wireTex);
            GUI.DrawTexture(new Rect(x0, y0 + 50f, wire, loopH - 50f), wireTex);
            GUI.DrawTexture(new Rect(x0 - 22f, y0 + 26f, 48f, 5f), wireTex);   // battery: long plate (+)
            GUI.DrawTexture(new Rect(x0 - 11f, y0 + 41f, 26f, 8f), wireTex);   // short plate (−)

            // Electrons running around the loop.
            float perimeter = 2f * (loopW + loopH);
            for (int i = 0; i < 14; i++)
            {
                float s = Mathf.Repeat(t * 110f + i * perimeter / 14f, perimeter);
                Vector2 p = PointOnLoop(s, loopW, loopH);
                if (p.x < 2f && p.y > 20f && p.y < 54f) continue;   // not inside the battery gap
                GUI.DrawTexture(new Rect(x0 + p.x - 3f, y0 + p.y - 3f, 10f, 10f), dotTex);
            }

            // A glowing LED on the right side.
            float glow = 70f + 14f * Mathf.Sin(t * 3f);
            var c = new Vector2(x0 + loopW + 2f, y0 + loopH * 0.5f);
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 0.75f, 0.2f, 0.9f);
            GUI.DrawTexture(new Rect(c.x - glow / 2f, c.y - glow / 2f, glow, glow), glowTex);
            GUI.color = new Color(1f, 0.95f, 0.7f, 1f);
            GUI.DrawTexture(new Rect(c.x - 13f, c.y - 13f, 26f, 26f), glowTex);
            GUI.color = oldColor;
        }

        private static Vector2 PointOnLoop(float s, float lw, float lh)
        {
            if (s < lw) return new Vector2(s, 0f);                          // top, left → right
            s -= lw;
            if (s < lh) return new Vector2(lw, s);                          // right side, down (through the LED)
            s -= lh;
            if (s < lw) return new Vector2(lw - s, lh);                     // bottom, right → left
            s -= lw;
            return new Vector2(0f, lh - s);                                 // left side, up (through the battery)
        }

        private void DrawTitleFooter(float w)
        {
            GUI.Label(new Rect(24, 1080 - 44, 400, 30), "версия " + Application.version, versionStyle);
            GUI.Label(new Rect(0, 1080 - 44, w, 30), "Играта пази анонимна статистика (без имена). Може да се изключи в Настройки.", smallStyle);
        }

        private void DrawTitle()
        {
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
            GUILayout.Space(6);

            float v;
            if ((v = Slider("Сила на звука", GameSettings.MasterVolume)) != GameSettings.MasterVolume) GameSettings.MasterVolume = v;
            if ((v = Slider("Звукови ефекти", GameSettings.SfxVolume)) != GameSettings.SfxVolume) GameSettings.SfxVolume = v;
            if (GameAudio.HasMusic && (v = Slider("Музика", GameSettings.MusicVolume)) != GameSettings.MusicVolume) GameSettings.MusicVolume = v;
            float sens = GameSettings.MouseSensitivity;
            if ((v = Slider("Чувствителност на мишката", sens, 0.2f, 2f, sens.ToString("0.0") + "x")) != sens) GameSettings.MouseSensitivity = v;

            if (ToggleRow("Обърни вертикалата (мишка)", GameSettings.InvertY)) GameSettings.InvertY = !GameSettings.InvertY;

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Качество на графиката", labelStyle, GUILayout.Width(LabelWidth));
            string[] names = QualitySettings.names;
            int q = QualitySettings.GetQualityLevel();
            if (GUILayout.Button("<", buttonStyle, GUILayout.Width(50), GUILayout.Height(44)) && q > 0) { GameSettings.Quality = q - 1; Click(); }
            GUILayout.Label(names.Length > 0 ? names[Mathf.Clamp(q, 0, names.Length - 1)] : "-", valueStyle, GUILayout.Width(130));
            if (GUILayout.Button(">", buttonStyle, GUILayout.Width(50), GUILayout.Height(44)) && q < names.Length - 1) { GameSettings.Quality = q + 1; Click(); }
            GUILayout.EndHorizontal();

            // Fullscreen: a browser only allows it while the mouse button is being pressed, so on the web
            // it reacts on mouse DOWN (Unity then switches on the following mouse up of the same click).
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Цял екран", labelStyle, GUILayout.Width(LabelWidth));
            Event e = Event.current;
            EventType before = e.type;
            Vector2 mouse = e.mousePosition;
            bool fullscreenClicked = GUILayout.Button(Screen.fullScreen ? "Да" : "Не", buttonStyle, GUILayout.Width(120), GUILayout.Height(44));
#if UNITY_WEBGL && !UNITY_EDITOR
            fullscreenClicked = before == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(mouse);
#endif
            GUILayout.EndHorizontal();
            if (fullscreenClicked) { GameSettings.Fullscreen = !Screen.fullScreen; Click(); }

            if (ToggleRow("Показвай FPS (F3)", GameSettings.ShowFps)) GameSettings.ShowFps = !GameSettings.ShowFps;
            if (ToggleRow("Фон зад текстовете", GameSettings.TextBackgrounds)) GameSettings.TextBackgrounds = !GameSettings.TextBackgrounds;
            if (ToggleRow("Анонимна статистика", GameSettings.Telemetry)) GameSettings.Telemetry = !GameSettings.Telemetry;

            GUILayout.Space(10);
            if (Button(tipsReset ? "Съветите ще се покажат отново" : "Покажи отново съветите"))
            {
                GameSettings.ResetTips();
                tipsReset = true;
                HouseSigns.RefreshObjective();
                Click();
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (Button("Стандартни")) { GameSettings.ResetToDefaults(); Click(); }
            if (Button("Назад")) { GameSettings.Save(); page = Page.Main; tipsReset = false; Click(); }
            GUILayout.EndHorizontal();
        }

        private const float LabelWidth = 380f;

        /// <summary>A "label …… Да/Не" row. Returns true when the button was clicked.</summary>
        private bool ToggleRow(string label, bool value)
        {
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(LabelWidth));
            bool clicked = GUILayout.Button(value ? "Да" : "Не", buttonStyle, GUILayout.Width(120), GUILayout.Height(44));
            GUILayout.EndHorizontal();
            if (clicked) Click();
            return clicked;
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
            GUILayout.Label("Музиката и звуците са създадени с код в самата игра.", smallStyle);
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

        private float Slider(string label, float value) => Slider(label, value, 0f, 1f, null);

        /// <summary>Slider row; <paramref name="text"/> = value shown on the right (null = percent).</summary>
        private float Slider(string label, float value, float min, float max, string text)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(LabelWidth));
            GUILayout.BeginVertical();
            GUILayout.Space(18);
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(200));
            GUILayout.EndVertical();
            GUILayout.Label(text ?? Mathf.RoundToInt(v * 100f) + "%", valueStyle, GUILayout.Width(80));
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
            heroStyle = new GUIStyle(titleStyle) { fontSize = 76 };
            heroShadowStyle = new GUIStyle(heroStyle);
            heroShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.65f);
            subtitleStyle = new GUIStyle(titleStyle) { fontSize = 28, fontStyle = FontStyle.Normal };
            subtitleStyle.normal.textColor = new Color(0.85f, 0.88f, 0.95f);

            titleOverlayTex = Solid(new Color(0.02f, 0.03f, 0.06f, 0.78f));
            wireTex = Solid(new Color(0.75f, 0.8f, 0.9f, 1f));
            dotTex = Solid(new Color(1f, 0.85f, 0.3f, 1f));
            glowTex = Glow(64);

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
            versionStyle = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            smallStyle.wordWrap = true;

            smallButtonStyle = new GUIStyle(buttonStyle) { fontSize = 22 };
            smallButtonStyle.margin = new RectOffset(4, 4, 4, 4);

            fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            fieldStyle.normal.textColor = fieldStyle.focused.textColor = Color.white;
            if (ui != null) fieldStyle.font = ui;
        }

        /// <summary>White round spot that fades out to the edge (tinted with GUI.color for the LED glow).</summary>
        private static Texture2D Glow(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r)) / r;
                    float a = Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            t.Apply();
            return t;
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
