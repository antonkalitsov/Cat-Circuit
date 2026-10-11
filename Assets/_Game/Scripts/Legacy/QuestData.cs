using UnityEngine;

/// <summary>
/// LEGACY quest description asset. Quests now live in CircuitPuzzle stages (objective, dialogue, goals).
/// Kept so old assets still load; the upgrade tool copies questDescription into the first stage.
/// </summary>
[CreateAssetMenu(fileName = "NewQuest", menuName = "CatCircuit/Legacy/Quest Data")]
public class QuestData : ScriptableObject
{
    [Header("Quest Info")]
    public string questName = "New Quest";
    [TextArea(3, 5)] public string questDescription = "Complete this task.";

    [Header("Dialogue (unused)")]
    [TextArea(2, 4)] public string[] acceptDialogue;
    [TextArea(2, 4)] public string[] completionDialogue;
    [TextArea(2, 4)] public string[] idleDialogue;
}
