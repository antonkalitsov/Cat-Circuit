using UnityEditor;
using UnityEngine;

namespace CatCircuit.EditorTools
{
    /// <summary>Shows circuit errors while editing and live simulation values in Play mode.</summary>
    [CustomEditor(typeof(CircuitHost), true)]
    public class CircuitHostEditor : Editor
    {
        private bool showResult = true;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var host = (CircuitHost)target;
            EditorGUILayout.Space();

            CircuitDefinition def = host.Definition;
            if (host is Breadboard board && !Application.isPlaying) def = board.ParseCircuit();
            if (host is CodeCircuitBuilder builder && builder.LastParse != null) def = builder.LastParse;

            if (def != null)
            {
                if (def.messages.Count == 0)
                    EditorGUILayout.HelpBox($"Circuit OK – {def.parts.Count} parts, {def.NodeCount} connection points.", MessageType.Info);
                else
                    EditorGUILayout.HelpBox(def.MessagesAsText(), def.HasErrors ? MessageType.Error : MessageType.Warning);
            }
            foreach (CircuitMessage m in host.RuntimeMessages)
                EditorGUILayout.HelpBox(m.ToString(), MessageType.Error);

            if (!Application.isPlaying) return;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Solve now")) host.SolveNow();
            if (host is CodeCircuitBuilder b && GUILayout.Button("Build code")) b.Build(b.Code);
            EditorGUILayout.EndHorizontal();

            showResult = EditorGUILayout.Foldout(showResult, "Simulation (live)", true);
            if (showResult) EditorGUILayout.HelpBox(host.Result.Describe(), MessageType.None);
            Repaint();
        }
    }

    [CustomEditor(typeof(CircuitPuzzle))]
    public class CircuitPuzzleEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var puzzle = (CircuitPuzzle)target;
            if (Application.isPlaying)
            {
                string state = puzzle.IsComplete ? "SOLVED" : $"stage {puzzle.CompletedStages + 1}/{puzzle.Stages.Count}: {puzzle.CurrentStage?.name}";
                EditorGUILayout.HelpBox("Puzzle – " + state, puzzle.IsComplete ? MessageType.Info : MessageType.None);
                if (!puzzle.IsComplete && GUILayout.Button("Complete current stage (cheat)")) puzzle.ForceCompleteStage();

                if (puzzle.CurrentStage != null && puzzle.Circuit != null)
                {
                    foreach (CircuitCondition c in puzzle.CurrentStage.conditions)
                    {
                        bool ok = c.Evaluate(puzzle.Circuit.Result, puzzle.Circuit);
                        EditorGUILayout.LabelField((ok ? "✔ " : "✘ ") + c.Describe());
                    }
                }
                Repaint();
                EditorGUILayout.Space();
            }
            DrawDefaultInspector();
        }
    }
}
