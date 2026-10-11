using CatCircuit;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// A cat you can talk to (E = next line).
///
/// QUEST NPC  – set <see cref="puzzle"/>. Flow:
///     greetingDialogue → quest accepted (objective shown)
///     → player solves a stage → objective "Talk to X" → stage.completeDialogue → next stage ...
///
/// INFO NPC   – leave Puzzle empty. Says greetingDialogue, or unlockedDialogue once
///     GameProgress has puzzle number <see cref="unlockedAfterPuzzle"/> (e.g. Sparky after puzzle 3).
/// </summary>
public class CatNPC : CatInteractable
{
    [Header("NPC")]
    [SerializeField] private string npcName = "Professor Whiskers";

    [Header("Quest (leave empty for an info NPC)")]
    [SerializeField] private CircuitPuzzle puzzle;

    [Header("Dialogue")]
    [TextArea(2, 4)][SerializeField] private string[] greetingDialogue = new string[0];

    [Header("Info NPC")]
    [Tooltip("Puzzle number (GameProgress) that unlocks 'Unlocked Dialogue'. 0 = never.")]
    [SerializeField] private int unlockedAfterPuzzle;
    [FormerlySerializedAs("questCompleteDialogue")]
    [TextArea(2, 4)][SerializeField] private string[] unlockedDialogue = new string[0];

    [Header("Events")]
    public UnityEvent OnQuestAccepted;
    [Tooltip("Fires when the whole puzzle is solved.")]
    public UnityEvent OnQuestCompleted;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;

    // ---- LEGACY: read once by CatCircuit ▸ Upgrade, then cleared. ----
    [SerializeField, HideInInspector] private QuestData questToGive;
    [SerializeField, HideInInspector] private Breadboard puzzleToCheck;
    [SerializeField, HideInInspector] private MonoBehaviour seriesManager;   // old SeriesQuestManager
    [SerializeField, HideInInspector] private string[] questCompleteDialogue2 = new string[0];

    private bool questAccepted;
    private int stagesHeard;
    private string[] activeLines;
    private int lineIndex;
    private System.Action onConversationFinished;

    public string NpcName => npcName;
    public CircuitPuzzle Puzzle => puzzle;
    public bool QuestAccepted => questAccepted;

    // ------------------------------------------------------------------ setup

    private void Awake()
    {
        EnsureInteractionTrigger(2f);
    }

    private void OnEnable()
    {
        if (puzzle == null) return;
        puzzle.StageCompleted += HandleStageCompleted;
        puzzle.PuzzleCompleted += HandlePuzzleCompleted;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (puzzle == null) return;
        puzzle.StageCompleted -= HandleStageCompleted;
        puzzle.PuzzleCompleted -= HandlePuzzleCompleted;
    }

    private void Start()
    {
        if (puzzle != null && puzzle.RestoredFromProgress)
        {
            questAccepted = true;
            stagesHeard = puzzle.Stages.Count;
        }
    }

    /// <summary>Edit-mode setup used by editor tools.</summary>
    public void Configure(string displayName, CircuitPuzzle quest, string[] greeting)
    {
        npcName = displayName;
        puzzle = quest;
        if (greeting != null) greetingDialogue = greeting;
    }

    // ------------------------------------------------------------------ interaction

    /// <summary>While talking, E always goes to this NPC.</summary>
    public override int InteractPriority => activeLines != null ? 1000 : interactPriority;

    public override string GetPrompt(PlayerCarry player)
    {
        return activeLines != null ? UIText.DialogueNext : UIText.Talk(npcName);
    }

    public override void Interact(PlayerCarry player)
    {
        if (activeLines == null) StartConversation();
        else NextLine();
    }

    public override void OnPlayerExitRange(PlayerCarry player)
    {
        // Walking away cancels the conversation (it restarts next time).
        if (activeLines != null) EndConversation(false);
    }

    private void StartConversation()
    {
        string[] lines;
        System.Action finished = null;

        if (puzzle == null)
        {
            bool unlocked = unlockedAfterPuzzle > 0 && GameProgress.Instance != null &&
                            GameProgress.Instance.IsPuzzleComplete(unlockedAfterPuzzle);
            lines = unlocked && unlockedDialogue.Length > 0 ? unlockedDialogue : greetingDialogue;
        }
        else if (!questAccepted)
        {
            lines = greetingDialogue;
            finished = AcceptQuest;
        }
        else if (stagesHeard < puzzle.CompletedStages)
        {
            int stage = stagesHeard;
            lines = puzzle.Stages[stage].completeDialogue;
            finished = () => FinishStageDialogue(stage);
        }
        else if (puzzle.IsComplete)
        {
            // Everything done – repeat the last "well done".
            lines = puzzle.Stages.Count > 0 ? puzzle.Stages[puzzle.Stages.Count - 1].completeDialogue : greetingDialogue;
        }
        else
        {
            string objective = puzzle.CurrentStage != null ? puzzle.CurrentStage.objective : "";
            lines = string.IsNullOrEmpty(objective) ? greetingDialogue : new[] { objective };
        }

        if (lines == null || lines.Length == 0)
        {
            finished?.Invoke();
            return;
        }

        activeLines = lines;
        lineIndex = 0;
        onConversationFinished = finished;
        ShowLine();
    }

    private void NextLine()
    {
        lineIndex++;
        if (lineIndex >= activeLines.Length) EndConversation(true);
        else ShowLine();
    }

    private void ShowLine()
    {
        GameUI.Dialogue(npcName, activeLines[lineIndex]);
        GameAudio.Play(Sfx.Blip, 1f, 0.12f);
        if (showDebugLogs) Debug.Log($"{npcName}: {activeLines[lineIndex]}");
    }

    private void EndConversation(bool completed)
    {
        activeLines = null;
        GameUI.HideDialogue();
        System.Action finished = onConversationFinished;
        onConversationFinished = null;
        if (completed) finished?.Invoke();
    }

    // ------------------------------------------------------------------ quest flow

    private void AcceptQuest()
    {
        questAccepted = true;
        if (showDebugLogs) Debug.Log($"[CatNPC] {npcName}: quest accepted");
        OnQuestAccepted?.Invoke();
        RefreshObjective();
    }

    private void FinishStageDialogue(int stage)
    {
        stagesHeard = Mathf.Max(stagesHeard, stage + 1);
        RefreshObjective();
    }

    private void HandleStageCompleted(int stage)
    {
        if (questAccepted) GameUI.Objective(UIText.TalkTo(npcName));
    }

    private void HandlePuzzleCompleted() => OnQuestCompleted?.Invoke();

    private void RefreshObjective()
    {
        if (puzzle == null) return;
        if (stagesHeard < puzzle.CompletedStages) GameUI.Objective(UIText.TalkTo(npcName));
        else if (!puzzle.IsComplete && puzzle.CurrentStage != null) GameUI.Objective(puzzle.CurrentStage.objective);
        else GameUI.Objective("");
    }

    /// <summary>Old API – finishes the current stage of the linked puzzle.</summary>
    public void CompleteQuest()
    {
        if (puzzle != null) puzzle.ForceCompleteStage();
    }

    public bool IsQuestCompleted() => puzzle != null && puzzle.IsComplete;

    // ------------------------------------------------------------------ legacy access (editor upgrade tool)

    public QuestData LegacyQuest => questToGive;
    public Breadboard LegacyPuzzleToCheck => puzzleToCheck;
    public MonoBehaviour LegacySeriesManager => seriesManager;
    public int UnlockedAfterPuzzle => unlockedAfterPuzzle;
    public string[] LegacyCompleteDialogue => unlockedDialogue;
    public string[] LegacyCompleteDialogue2 => questCompleteDialogue2;
    public string[] GreetingDialogue => greetingDialogue;

    public void ConfigureInfoNpc(int unlockPuzzle) => unlockedAfterPuzzle = unlockPuzzle;

    public void ClearLegacy(bool clearUnlockedDialogue)
    {
        questToGive = null;
        puzzleToCheck = null;
        seriesManager = null;
        questCompleteDialogue2 = new string[0];
        if (clearUnlockedDialogue) unlockedDialogue = new string[0];
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireCube(transform.position, new Vector3(1, 2, 1));
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, npcName);
#endif
    }
}
