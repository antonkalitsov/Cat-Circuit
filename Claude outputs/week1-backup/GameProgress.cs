using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Remembers which puzzles are solved, across scenes.
/// Creates itself the first time it is used – it no longer has to be placed in the Village scene
/// (an existing one in a scene still works).
/// Puzzle numbers are just ids: CircuitPuzzle.progressId decides which number a puzzle writes.
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
        Debug.Log($"[GameProgress] Puzzle {puzzleNumber} complete.");
        PuzzleCompleted?.Invoke(puzzleNumber);
    }

    public bool IsPuzzleComplete(int puzzleNumber) => completedPuzzles.Contains(puzzleNumber);

    public void ResetAll()
    {
        completedPuzzles.Clear();
        Debug.Log("[GameProgress] All progress reset.");
    }
}
