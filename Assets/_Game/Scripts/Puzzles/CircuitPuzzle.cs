using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CatCircuit
{
    /// <summary>
    /// The goals of a circuit (on a Breadboard or a CodeCircuitBuilder).
    /// A puzzle is a list of <see cref="PuzzleStage"/>s solved one after another. Each stage has
    /// conditions ("L1 is lit", "no LED burns" ...), hints, a success message and NPC dialogue.
    ///
    /// Link it to a <see cref="CatNPC"/> (NPC ▸ Puzzle) to get the quest flow:
    /// talk → objective → solve stage → talk again → next stage ...
    /// </summary>
    [AddComponentMenu("CatCircuit/Circuit Puzzle")]
    public class CircuitPuzzle : MonoBehaviour
    {
        [Tooltip("Circuit to watch. Empty = the CircuitHost on this object or its parents.")]
        [SerializeField] private CircuitHost circuit;
        [Tooltip("Number stored in GameProgress when everything is solved (0 = don't store).")]
        [SerializeField] private int progressId;
        [SerializeField] private List<PuzzleStage> stages = new List<PuzzleStage>();

        [Header("Effects")]
        [Tooltip("Electric sparkles at every lit LED when a stage is solved.")]
        [SerializeField] private bool sparkleOnComplete = true;
        [Tooltip("Optional: your own effect prefab instead of the built-in sparkles (e.g. CircuitCompleteVFX).")]
        [SerializeField] private GameObject completeEffect;
        [Tooltip("Optional particle material for the built-in sparkles (set by the upgrade tool).")]
        [SerializeField] private Material sparkMaterial;

        [Header("Events")]
        public UnityEvent OnPuzzleComplete = new UnityEvent();

        /// <summary>Index of the stage that was just finished.</summary>
        public event Action<int> StageCompleted;
        public event Action PuzzleCompleted;

        public int CompletedStages { get; private set; }
        public bool IsComplete => stages.Count > 0 && CompletedStages >= stages.Count;
        /// <summary>True when the puzzle was already solved earlier (GameProgress) and restored on scene load.</summary>
        public bool RestoredFromProgress { get; private set; }
        public PuzzleStage CurrentStage => !IsComplete && CompletedStages < stages.Count ? stages[CompletedStages] : null;
        public List<PuzzleStage> Stages => stages;
        public CircuitHost Circuit => circuit;
        public int ProgressId { get => progressId; set => progressId = value; }

        private string lastHint;
        private int evaluations;

        /// <summary>Edit-mode setup used by editor tools.</summary>
        public void Configure(CircuitHost host, int id)
        {
            circuit = host;
            progressId = id;
        }

        private void Awake()
        {
            if (circuit == null) circuit = GetComponentInParent<CircuitHost>();

            // Coming back to a solved house: show it solved, don't replay the quest.
            if (progressId > 0 && stages.Count > 0 && GameProgress.Instance != null && GameProgress.Instance.IsPuzzleComplete(progressId))
            {
                CompletedStages = stages.Count;
                RestoredFromProgress = true;
            }
        }

        private void OnEnable()
        {
            if (circuit != null) circuit.Solved += OnSolved;
            if (progressId > 0) Telemetry.CurrentPuzzle = progressId;
        }

        private void OnDisable()
        {
            if (circuit != null) circuit.Solved -= OnSolved;
            if (progressId > 0 && Telemetry.CurrentPuzzle == progressId) Telemetry.CurrentPuzzle = 0;
        }

        private void OnSolved(CircuitHost host) => Evaluate();

        /// <summary>Checks the current stage against the latest simulation result.</summary>
        public void Evaluate()
        {
            if (circuit == null) return;
            CircuitResult r = circuit.Result;

            while (!IsComplete && CurrentStage.IsSatisfied(r, circuit))
                CompleteCurrentStage();

            if (IsComplete) return;

            // The first solve happens when the scene starts – don't nag before the player did anything.
            if (evaluations++ == 0) return;

            string hint = null;
            foreach (CircuitHint h in CurrentStage.hints)
            {
                if (h.Applies(r, circuit)) { hint = h.message; break; }
            }
            if (hint != lastHint && !string.IsNullOrEmpty(hint))
            {
                GameUI.Toast(hint, 4f);
                GameAudio.Play(Sfx.Hint);
                Telemetry.Log("hint_shown", Telemetry.Trim(hint, 60), progressId);
            }
            lastHint = hint;
        }

        private void CompleteCurrentStage()
        {
            int index = CompletedStages;
            PuzzleStage stage = stages[index];
            CompletedStages++;
            lastHint = null;

            if (!string.IsNullOrEmpty(stage.successMessage)) GameUI.Toast(stage.successMessage, 4f);
            Debug.Log($"[CircuitPuzzle] '{name}' stage {index + 1}/{stages.Count} '{stage.name}' complete.");

            SpawnEffects();
            GameAudio.Play(CompletedStages >= stages.Count ? Sfx.PuzzleComplete : Sfx.StageComplete);
            stage.onComplete?.Invoke();
            StageCompleted?.Invoke(index);
            Telemetry.Log("puzzle_stage_complete", $"stage={index + 1};sec={Telemetry.SecondsInScene}", progressId);

            if (!IsComplete) return;

            Telemetry.Log("puzzle_complete", $"sec={Telemetry.SecondsInScene}", progressId);
            Telemetry.Flush();
            if (progressId > 0 && GameProgress.Instance != null) GameProgress.Instance.CompletePuzzle(progressId);
            OnPuzzleComplete?.Invoke();
            PuzzleCompleted?.Invoke();
            circuit.NotifyPuzzleComplete();
        }

        /// <summary>Edit-mode setup used by editor tools.</summary>
        public void ConfigureEffects(GameObject effectPrefab, Material particleMaterial)
        {
            if (effectPrefab != null) completeEffect = effectPrefab;
            if (particleMaterial != null) sparkMaterial = particleMaterial;
        }

        private void SpawnEffects()
        {
            if (!sparkleOnComplete || circuit == null) return;

            var points = new List<Vector3>();
            foreach (LEDLight l in circuit.Lights)
                if (l != null && l.isActiveAndEnabled && l.Brightness > 0.01f && points.Count < 6)
                    points.Add(l.transform.position + Vector3.up * 0.2f);
            if (points.Count == 0) points.Add(circuit.EffectCenter);

            foreach (Vector3 p in points)
            {
                if (completeEffect != null)
                {
                    GameObject fx = Instantiate(completeEffect, p, Quaternion.identity);
                    fx.SetActive(true);
                    Destroy(fx, 5f);
                }
                else
                {
                    ElectricSparkle.Spawn(p, 1f, sparkMaterial);
                }
            }
        }

        /// <summary>Debug / cheat: finish the current stage.</summary>
        [ContextMenu("Complete current stage")]
        public void ForceCompleteStage()
        {
            if (!IsComplete) CompleteCurrentStage();
        }
    }
}
