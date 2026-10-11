using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CatCircuit
{
    /// <summary>
    /// First-time tips in House 1 (no scene setup needed). Created when the scene "House1" loads, only if the
    /// tutorial was never finished/skipped (PlayerPrefs 'cc_tutorial_done') and puzzle 1 is not solved yet.
    /// Steps advance when the player really does the thing:
    ///   1 move a few metres → 2 talk to the professor (quest accepted) → 3 hold a part → 4 place it in a slot
    ///   → 5 "put in all parts" (hides when the puzzle's first stage is solved or after ~8 s).
    /// Solving the puzzle at any step finishes it. Tab skips. Hidden while a menu, terminal or dialogue is open.
    /// Small box at the top-centre (IMGUI), between the held-item (top-left) and objective (top-right) texts.
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        private const string KeyDone = "cc_tutorial_done";
        private const string SceneName = "House1";
        private const int PuzzleId = 1;
        private const float MoveDistance = 3f;
        private const float LastStepSeconds = 8f;

        private static readonly string[] Steps =
        {
            "Движи се с W A S D.",
            "Отиди при професора и натисни E,\nза да говориш с него.",
            "Вземи една от частите с E.",
            "Занеси я до платката и натисни E,\nза да я поставиш.",
            "Браво! Сложи всички части,\nза да светне лампичката.",
        };

        private static readonly string[] Footers =
        {
            "Съвет 1/5    Tab – пропусни", "Съвет 2/5    Tab – пропусни", "Съвет 3/5    Tab – пропусни",
            "Съвет 4/5    Tab – пропусни", "Съвет 5/5    Tab – пропусни",
        };

        private int step = -1;
        private float stepStarted;
        private bool placedSomething;
        private bool finished;
        private bool hasStart;
        private Vector3 startPos;

        private PlayerCarry player;
        private CatNPC npc;
        private CircuitPuzzle puzzle;

        private GUIStyle textStyle, smallStyle;
        private Texture2D panelTex, accentTex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneName || PlayerPrefs.GetInt(KeyDone, 0) == 1) return;
            if (GameProgress.Instance != null && GameProgress.Instance.IsPuzzleComplete(PuzzleId)) return;
            new GameObject("Tutorial").AddComponent<Tutorial>();   // lives in House1 only
        }

        private void Start()
        {
            player = FindAnyObjectByType<PlayerCarry>();
            foreach (CircuitPuzzle p in FindObjectsByType<CircuitPuzzle>(FindObjectsSortMode.None))
                if (p.ProgressId == PuzzleId) { puzzle = p; break; }
            if (puzzle != null)
                foreach (CatNPC n in FindObjectsByType<CatNPC>(FindObjectsSortMode.None))
                    if (n.Puzzle == puzzle) { npc = n; break; }

            if (player == null || puzzle == null || puzzle.IsComplete)
            {
                Destroy(gameObject);
                return;
            }

            puzzle.StageCompleted += OnStageCompleted;
            if (player.OnItemPlacedDown != null) player.OnItemPlacedDown.AddListener(OnPlaced);
            GoTo(0);
        }

        private void OnDestroy()
        {
            if (puzzle != null) puzzle.StageCompleted -= OnStageCompleted;
            if (player != null && player.OnItemPlacedDown != null) player.OnItemPlacedDown.RemoveListener(OnPlaced);
        }

        private void OnPlaced(ComponentData data) => placedSomething = true;

        private void OnStageCompleted(int stage) => Finish(false);

        private void Update()
        {
            if (finished || step < 0 || player == null) return;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame && !Blocked)
            {
                Finish(true);
                return;
            }

            switch (step)
            {
                case 0:
                    if (!hasStart) { startPos = player.transform.position; hasStart = true; }
                    Vector3 d = player.transform.position - startPos;
                    d.y = 0f;
                    if (d.sqrMagnitude >= MoveDistance * MoveDistance) GoTo(1);
                    break;
                case 1:
                    if (npc == null || npc.QuestAccepted) GoTo(2);
                    break;
                case 2:
                    if (player.IsHoldingItem) GoTo(3);
                    break;
                case 3:
                    if (placedSomething) GoTo(4);
                    break;
                case 4:
                    if (Time.unscaledTime - stepStarted >= LastStepSeconds) Finish(false);
                    break;
            }
        }

        private void GoTo(int next)
        {
            step = next;
            stepStarted = Time.unscaledTime;
            if (next == 3) placedSomething = false;
            Telemetry.Log("tutorial_step", (next + 1).ToString(), PuzzleId);
        }

        private void Finish(bool skipped)
        {
            if (finished) return;
            finished = true;
            if (skipped) Telemetry.Log("tutorial_skip", (step + 1).ToString(), PuzzleId);
            PlayerPrefs.SetInt(KeyDone, 1);
            PlayerPrefs.Save();
            Destroy(gameObject);
        }

        /// <summary>A menu, the code terminal, the end screen or a conversation is on screen.</summary>
        private bool Blocked =>
            PauseMenu.IsOpen || CodeTerminal.AnyOpen || EndGameScreen.IsOpen || ControlsHint.IsOpen ||
            UIFocus.IsActive || (npc != null && npc.IsTalking);

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (finished || step < 0 || Blocked) return;
            EnsureStyles();

            float scale = Screen.height / 1080f;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;

            // Between the two top corner texts; on narrow screens move down below them.
            float boxW = Mathf.Min(640f, w - 64f);
            bool roomBetween = w - 2f * 560f >= boxW;
            var box = new Rect((w - boxW) / 2f, roomBetween ? 24f : 170f, boxW, 128f);

            GUI.DrawTexture(box, panelTex);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 5f), accentTex);
            GUI.Label(new Rect(box.x + 20f, box.y + 10f, box.width - 40f, 84f), Steps[step], textStyle);
            GUI.Label(new Rect(box.x + 20f, box.y + 94f, box.width - 40f, 26f), Footers[step], smallStyle);

            GUI.matrix = old;
        }

        private void EnsureStyles()
        {
            if (textStyle != null) return;

            panelTex = Solid(new Color(0.07f, 0.08f, 0.12f, 0.92f));
            accentTex = Solid(new Color(1f, 0.8f, 0.25f, 1f));

            Font ui = UIFonts.UI(26);   // Cyrillic on the web needs the bundled font
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            textStyle.normal.textColor = Color.white;
            if (ui != null) textStyle.font = ui;
            smallStyle = new GUIStyle(textStyle) { fontSize = 17 };
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
