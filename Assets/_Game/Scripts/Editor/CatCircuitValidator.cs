using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CatCircuit.EditorTools
{
    /// <summary>CatCircuit ▸ Validate open scene – finds the usual setup mistakes before you press Play.</summary>
    public static class CatCircuitValidator
    {
        [MenuItem("CatCircuit/Validate open scene", priority = 40)]
        public static void Validate()
        {
            var problems = new List<string>();
            var notes = new List<string>();

            // ---- player & UI
            var players = FindAll<PlayerCarry>();
            if (players.Count == 0) problems.Add("No player (PlayerCarry) in the scene.");
            foreach (PlayerCarry p in players)
            {
                if (!p.CompareTag("Player")) problems.Add($"Player '{p.name}' is not tagged 'Player'.");
                Player move = p.GetComponent<Player>();
                if (move != null && move.InputActions == null) problems.Add($"Player '{p.name}': Input Actions not assigned.");
            }
            if (FindAll<UIManager>().Count == 0) notes.Add("No UIManager (Canvas) in this scene – fine if the scene is only loaded after one that has it.");
            if (FindAll<PlayerSpawner>().Count == 0) notes.Add("No PlayerSpawner – the player starts where it was placed.");

            // ---- boards
            foreach (Breadboard board in FindAll<Breadboard>())
            {
                CircuitDefinition def = board.ParseCircuit();
                foreach (CircuitMessage m in def.messages)
                    (m.level == MessageLevel.Error ? problems : notes).Add($"Board '{board.name}': {m}");

                var ids = new HashSet<string>();
                foreach (BreadboardSlot s in board.Slots)
                {
                    if (s == null) continue;
                    if (!ids.Add(s.SlotId.ToLowerInvariant())) problems.Add($"Board '{board.name}': two slots have the id '{s.SlotId}'.");
                    if (def.Find(s.SlotId) == null) notes.Add($"Board '{board.name}': slot '{s.SlotId}' is not in the circuit text.");
                }
                foreach (PartDef p in def.OfKind(PartKind.Slot))
                    if (board.FindSlot(p.id) == null) problems.Add($"Board '{board.name}': circuit uses 'slot {p.id}' but no BreadboardSlot has that Slot Id.");
            }
            foreach (BreadboardSlot s in FindAll<BreadboardSlot>())
                if (s.Board == null && FindAll<Breadboard>().Count != 1)
                    problems.Add($"Slot '{s.name}' has no Breadboard (set 'Board' or make it a child of one).");

            // ---- LEDs
            foreach (LEDLight l in FindAll<LEDLight>())
            {
                CircuitHost host = l.Circuit != null ? l.Circuit : l.GetComponentInParent<CircuitHost>();
                if (string.IsNullOrEmpty(l.ElementId)) { problems.Add($"LEDLight '{l.name}' has no Element Id."); continue; }
                if (host == null) { problems.Add($"LEDLight '{l.name}' is not linked to a circuit."); continue; }
                if (host is Breadboard b && b.ParseCircuit().Find(l.ElementId) == null)
                    problems.Add($"LEDLight '{l.name}': '{l.ElementId}' is not in the circuit of '{b.name}'.");
            }

            // ---- puzzles & NPCs
            foreach (CircuitPuzzle pz in FindAll<CircuitPuzzle>())
            {
                if (pz.Circuit == null && pz.GetComponentInParent<CircuitHost>() == null) problems.Add($"Puzzle '{pz.name}' has no circuit.");
                if (pz.Stages.Count == 0) problems.Add($"Puzzle '{pz.name}' has no stages.");
                for (int i = 0; i < pz.Stages.Count; i++)
                    if (pz.Stages[i].conditions.Count == 0) problems.Add($"Puzzle '{pz.name}' stage {i + 1} has no conditions (it can never be solved).");
                if (!FindAll<CatNPC>().Any(n => n.Puzzle == pz)) notes.Add($"Puzzle '{pz.name}' is not linked to any NPC (works, but no quest dialogue).");
            }

            // ---- doors
            var buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)).ToList();
            foreach (SceneTransition door in FindAll<SceneTransition>())
                if (!buildScenes.Contains(door.TargetSceneName))
                    problems.Add($"Door '{door.name}' goes to '{door.TargetSceneName}', which is not in the build scene list.");

            // ---- parts
            foreach (string guid in AssetDatabase.FindAssets("t:ComponentData", new[] { "Assets/_Game" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<ComponentData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                if (data.worldPrefab == null) notes.Add($"ComponentData '{data.name}' has no World Prefab.");
                else if (data.worldPrefab.GetComponent<PickupableItem>() == null) problems.Add($"ComponentData '{data.name}': World Prefab has no PickupableItem (use the prefab, not the .obj).");
                if (data.Kind == PartKind.None) notes.Add($"ComponentData '{data.name}': simulator does not know what it is – set Electrical Kind.");
            }

            var sb = new StringBuilder();
            sb.AppendLine(problems.Count == 0 ? "No problems found." : $"{problems.Count} problem(s):");
            foreach (string p in problems) sb.AppendLine("  ✘ " + p);
            if (notes.Count > 0) sb.AppendLine("Notes:");
            foreach (string n in notes) sb.AppendLine("  • " + n);

            Debug.Log("[CatCircuit] Validation\n" + sb);
            EditorUtility.DisplayDialog("CatCircuit – validation", sb.Length > 1500 ? sb.ToString(0, 1500) + "\n… (full list in the Console)" : sb.ToString(), "OK");
        }

        private static List<T> FindAll<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();
    }
}
