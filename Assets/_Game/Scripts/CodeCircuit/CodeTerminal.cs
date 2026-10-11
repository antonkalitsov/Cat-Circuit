using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// An in-game computer. Press E next to it to open a code editor where the player writes a
    /// circuit in CircuitScript. "Build" sends it to the <see cref="CodeCircuitBuilder"/>, which
    /// builds it on the floor and simulates it.
    ///
    /// Uses Unity's immediate-mode GUI, so it needs NO canvas / prefab setup – drop it on any object.
    /// Keys in the editor:  Ctrl+Enter = build,  Esc = close.
    /// </summary>
    [AddComponentMenu("CatCircuit/Code Terminal")]
    public class CodeTerminal : CatInteractable
    {
        [SerializeField] private CodeCircuitBuilder builder;
        [Tooltip("Optional – shows the current objective inside the terminal.")]
        [SerializeField] private CircuitPuzzle puzzle;
        [SerializeField] private string terminalName = "Компютър";
        [SerializeField] private int fontSize = 20;

        private bool isOpen;
        private string draft = "";
        private string status = "";
        private bool statusIsError;
        private Vector2 codeScroll, helpScroll, statusScroll;
        private int openedFrame;
        private int focusCountdown;


        private GUIStyle codeStyle, textStyle, titleStyle, statusStyle, buttonStyle;
        private Texture2D backgroundTexture;

        public bool IsOpen => isOpen;

        private static int openCount;
        /// <summary>True while any code terminal is open (the pause menu waits for it).</summary>
        public static bool AnyOpen => openCount > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => openCount = 0;

        /// <summary>Edit-mode setup used by editor tools.</summary>
        public void Configure(CodeCircuitBuilder target, CircuitPuzzle goals, string displayName)
        {
            builder = target;
            puzzle = goals;
            terminalName = displayName;
        }

        private void Awake()
        {
            if (builder == null) builder = FindAnyObjectByType<CodeCircuitBuilder>();
            if (puzzle == null && builder != null) puzzle = builder.GetComponent<CircuitPuzzle>();
            EnsureInteractionTrigger(1.8f);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (isOpen) Close();
        }

        // ------------------------------------------------------------------ CatInteractable

        public override string GetPrompt(PlayerCarry player) => isOpen ? null : UIText.UseTerminal(terminalName);

        public override void Interact(PlayerCarry player) => Open();

        // ------------------------------------------------------------------ open / close

        public void Open()
        {
            if (isOpen || builder == null) return;
            isOpen = true;
            openedFrame = Time.frameCount;
            focusCountdown = 2;
            draft = builder.Code ?? "";
            status = "Напиши верига и натисни Построй (Ctrl+Enter).";
            statusIsError = false;

            openCount++;
            UIFocus.Push();
            GameAudio.Play(Sfx.UiClick);
        }

        public void Close()
        {
            if (!isOpen) return;
            isOpen = false;
            openCount = Mathf.Max(0, openCount - 1);
            UIFocus.Pop();
            GameAudio.Play(Sfx.UiClick);
        }

        // ------------------------------------------------------------------ build

        private void RunBuild()
        {
            bool ok = builder.Build(draft);
            CircuitDefinition parse = builder.LastParse;
            var sb = new StringBuilder();
            LogBuild(ok, parse);

            if (!ok)
            {
                statusIsError = true;
                GameAudio.Play(Sfx.BuildError);
                sb.AppendLine("Кодът има грешки – нищо не е построено:");
                sb.Append(parse != null ? parse.MessagesAsText(true) : "");
                status = sb.ToString();
                return;
            }

            statusIsError = false;
            GameAudio.Play(Sfx.BuildOk);
            CircuitResult r = builder.Result;
            int leds = 0;
            foreach (PartResult p in r.Leds) leds++;
            sb.AppendLine($"Построено!  Светят {r.LitLedCount} от {leds} LED.");
            if (r.shortCircuit) sb.AppendLine("КЪСО СЪЕДИНЕНИЕ! Токът минава без да срещне съпротивление.");

            // A tiny "multimeter" read-out – great for learning Ohm's law.
            foreach (PartResult p in r.parts.Values)
            {
                if (p.kind == PartKind.LED)
                {
                    string state = p.overloaded ? "ИЗГАРЯ!" : p.reversed ? "обърнат" : p.lit ? "свети" : "не свети";
                    sb.AppendLine($"  {p.id}: {CircuitUnits.MilliAmps(p.current)}  ({state})");
                }
                else if (p.kind == PartKind.Resistor)
                {
                    sb.AppendLine($"  {p.id}: {CircuitUnits.MilliAmps(Mathf.Abs(p.current))}, {Mathf.Abs(p.voltage):0.00}V");
                }
            }

            if (parse != null && parse.messages.Count > 0) sb.Append(parse.MessagesAsText());
            status = sb.ToString();
        }

        /// <summary>Statistics: result of a build (never the code or error texts, only counts, length and line).</summary>
        private void LogBuild(bool ok, CircuitDefinition parse)
        {
            int len = draft != null ? draft.Length : 0;
            if (ok)
            {
                Telemetry.Log("code_build", $"ok;len={len}");
                return;
            }

            // Only counts and the line number – error texts can contain names the player typed.
            int errors = 0;
            int firstLine = 0;
            if (parse != null)
            {
                foreach (CircuitMessage m in parse.messages)
                {
                    if (m.level != MessageLevel.Error) continue;
                    if (errors++ == 0) firstLine = m.line;
                }
            }
            Telemetry.Log("code_build", $"error;errors={errors};len={len};line={firstLine}");
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            if (!isOpen) return;
            EnsureStyles();

            Event e = Event.current;
            // Swallow the E press that opened the terminal so it is not typed into the editor.
            if (Time.frameCount == openedFrame && (e.type == EventType.KeyDown || e.type == EventType.KeyUp)) { e.Use(); return; }
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape) { e.Use(); Close(); return; }
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && (e.control || e.command))
                {
                    e.Use(); RunBuild(); return;
                }
            }

            // Draw in a virtual 1920x1080 space so it looks the same on every screen.
            float scale = Screen.height / 1080f;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;

            var window = new Rect(60, 40, w - 120, 860);
            GUI.DrawTexture(window, backgroundTexture);
            GUILayout.BeginArea(new Rect(window.x + 20, window.y + 15, window.width - 40, window.height - 30));

            GUILayout.Label($"{terminalName}", titleStyle);
            if (puzzle != null && puzzle.CurrentStage != null && !string.IsNullOrEmpty(puzzle.CurrentStage.objective))
                GUILayout.Label("Задача: " + puzzle.CurrentStage.objective, textStyle);
            else if (puzzle != null && puzzle.IsComplete)
                GUILayout.Label("Всички задачи са решени! Можеш да експериментираш свободно.", textStyle);

            GUILayout.BeginHorizontal();

            // --- code editor
            GUILayout.BeginVertical(GUILayout.Width((window.width - 60) * 0.6f));
            codeScroll = GUILayout.BeginScrollView(codeScroll, GUILayout.Height(520));
            GUI.SetNextControlName("circuit-code");
            draft = GUILayout.TextArea(draft, codeStyle, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Построй  (Ctrl+Enter)", buttonStyle, GUILayout.Height(48))) RunBuild();
            if (GUILayout.Button("Пример", buttonStyle, GUILayout.Height(48), GUILayout.Width(160))) draft = CodeCircuitBuilder.ExampleCode;
            if (GUILayout.Button("Изчисти", buttonStyle, GUILayout.Height(48), GUILayout.Width(160))) { draft = ""; builder.Clear(); }
            if (GUILayout.Button("Затвори (Esc)", buttonStyle, GUILayout.Height(48), GUILayout.Width(200))) Close();
            GUILayout.EndHorizontal();

            statusScroll = GUILayout.BeginScrollView(statusScroll, GUILayout.Height(170));
            statusStyle.normal.textColor = statusIsError ? new Color(1f, 0.55f, 0.5f) : new Color(0.7f, 1f, 0.7f);
            GUILayout.Label(status, statusStyle);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(20);

            // --- help
            GUILayout.BeginVertical();
            helpScroll = GUILayout.BeginScrollView(helpScroll, GUILayout.Height(760));
            GUILayout.Label(CircuitScript.HelpText, textStyle);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.matrix = old;

            if (focusCountdown > 0 && e.type == EventType.Repaint && --focusCountdown == 0)
                GUI.FocusControl("circuit-code");
        }

        private void EnsureStyles()
        {
            if (codeStyle != null) return;

            backgroundTexture = new Texture2D(1, 1);
            backgroundTexture.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.1f, 0.96f));
            backgroundTexture.Apply();

            Font mono = UIFonts.Mono(fontSize);   // OS mono font on PC, Resources font on the web
            Font ui = UIFonts.UI(fontSize);

            codeStyle = new GUIStyle(GUI.skin.textArea) { fontSize = fontSize, wordWrap = false, richText = false };
            if (mono != null) codeStyle.font = mono;
            codeStyle.normal.textColor = codeStyle.focused.textColor = new Color(0.85f, 0.95f, 1f);
            codeStyle.padding = new RectOffset(10, 10, 10, 10);

            textStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize - 2, wordWrap = true, richText = true };
            if (mono != null) textStyle.font = mono;
            textStyle.normal.textColor = new Color(0.9f, 0.9f, 0.85f);

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize + 8, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
            if (ui != null) titleStyle.font = ui;

            statusStyle = new GUIStyle(textStyle);
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = fontSize };
            if (ui != null) buttonStyle.font = ui;
        }
    }
}
