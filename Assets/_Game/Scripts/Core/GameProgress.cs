using System;
using System.Collections.Generic;
using CatCircuit;
using UnityEngine;

/// <summary>
/// Remembers which puzzles are solved, across scenes.
/// Creates itself the first time it is used – it no longer has to be placed in the Village scene
/// (an existing one in a scene still works).
/// Puzzle numbers are just ids: CircuitPuzzle.progressId decides which number a puzzle writes.
///
/// Every solved puzzle is also written to the save (<see cref="GameSave"/>). The title menu calls
/// <see cref="StartSession"/> to either load that save ("Продължи") or wipe it ("Нова игра").
/// Starting a scene directly in the editor does NOT load the save, so testing stays predictable.
/// </summary>
public class GameProgress : MonoBehaviour
{
    private static GameProgress instance;

    public static GameProgress Instance
    {
        get
        {
            if (instance == null && Application.isPlaying)
                new GameObject("GameProgress").AddComponent<GameProgress>();   // Awake sets instance
            return instance;
        }
    }

    [SerializeField] private List<int> completedPuzzles = new List<int>();

    /// <summary>Fired with the puzzle number when a puzzle is completed for the first time.</summary>
    public event Action<int> PuzzleCompleted;

    public IReadOnlyList<int> CompletedPuzzles => completedPuzzles;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public void CompletePuzzle(int puzzleNumber)
    {
        if (puzzleNumber <= 0 || completedPuzzles.Contains(puzzleNumber)) return;
        completedPuzzles.Add(puzzleNumber);
        GameSave.Save(completedPuzzles);
        Debug.Log($"[GameProgress] Puzzle {puzzleNumber} complete.");
        PuzzleCompleted?.Invoke(puzzleNumber);
    }

    public bool IsPuzzleComplete(int puzzleNumber) => completedPuzzles.Contains(puzzleNumber);

    /// <summary>
    /// Called by the title menu. true = load the saved game, false = start from nothing and delete the save.
    /// </summary>
    public void StartSession(bool continueSave)
    {
        completedPuzzles.Clear();
        if (continueSave) completedPuzzles.AddRange(GameSave.Load());
        else GameSave.Clear();
        Debug.Log($"[GameProgress] Session started, {completedPuzzles.Count} puzzle(s) restored.");
    }

    public void ResetAll()
    {
        completedPuzzles.Clear();
        GameSave.Clear();
        Debug.Log("[GameProgress] All progress reset.");
    }
}
