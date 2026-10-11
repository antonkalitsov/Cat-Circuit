using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CatCircuit.EditorTools
{
    /// <summary>
    /// One-time upgrade of the project to the new CatCircuit systems.
    /// Run the steps in order from the menu:  CatCircuit ▸ Upgrade ▸ 1 / 2 / 3 / 4.
    /// Every step writes a report to Assets/_Game/CatCircuit_UpgradeReport.txt.
    /// </summary>
    public static class CatCircuitUpgrade
    {
        private const string Root = "Assets/_Game";
        private const string Scripts = Root + "/Scripts";
        private const string Prefabs = Root + "/prefabs";
        private const string ReportPath = Root + "/CatCircuit_UpgradeReport.txt";

        private static readonly StringBuilder report = new StringBuilder();

        // =====================================================================  STEP 1 – files

        private static readonly string[,] ScriptMoves =
        {
            { Scripts + "/GameProgress.cs",            Scripts + "/Core/GameProgress.cs" },
            { Scripts + "/UIManager.cs",               Scripts + "/Core/UIManager.cs" },
            { Scripts + "/Player.cs",                  Scripts + "/Player/Player.cs" },
            { Scripts + "/PlayerSpawner.cs",           Scripts + "/Player/PlayerSpawner.cs" },
            { Scripts + "/PlayerPositionStore.cs",     Scripts + "/Player/PlayerPositionStore.cs" },
            { Scripts + "/items/PlayerCarry.cs",       Scripts + "/Player/PlayerCarry.cs" },
            { Scripts + "/SceneTransition.cs",         Scripts + "/Interaction/SceneTransition.cs" },
            { Scripts + "/items/PickupableItem.cs",    Scripts + "/Interaction/PickupableItem.cs" },
            { Scripts + "/items/PickupTrigger.cs",     Scripts + "/Interaction/PickupTrigger.cs" },
            { Scripts + "/items/ComponentData.cs",     Scripts + "/Circuit/ComponentData.cs" },
            { Scripts + "/QuestData.cs",               Scripts + "/Legacy/QuestData.cs" },
            { Scripts + "/house4/SeriesQuestManager.cs", Scripts + "/Legacy/SeriesQuestManager.cs" },
            { Scripts + "/house4/SeriesParallelLED.cs",  Scripts + "/Legacy/SeriesParallelLED.cs" },
            // These two had a file name that did not match the class – Unity could not load them.
            { Scripts + "/items/LED_Brightness.cs",    Scripts + "/Legacy/LEDBrightness.cs" },
            { Scripts + "/Breadboard/resistor_slot.cs", Scripts + "/Legacy/ResistorSlot.cs" },
            { Scripts + "/items/TestLED.cs",           Scripts + "/Legacy/TestLED.cs" },
            { Scripts + "/items/InteractionGuard.cs",  Scripts + "/Legacy/InteractionGuard.cs" },
        };

        [MenuItem("CatCircuit/Upgrade/1. Organize script files", priority = 1)]
        public static void OrganizeScripts()
        {
            Begin("STEP 1 – organize script files");
            for (int i = 0; i < ScriptMoves.GetLength(0); i++)
                Move(ScriptMoves[i, 0], ScriptMoves[i, 1]);
            DeleteIfEmpty(Scripts + "/items");
            DeleteIfEmpty(Scripts + "/house4");
            AssetDatabase.Refresh();
            End("Scripts are organized. Unity will now recompile.\n\nNext: CatCircuit ▸ Upgrade ▸ 2. Upgrade all scenes");
        }

        // =====================================================================  STEP 2 – scenes

        [MenuItem("CatCircuit/Upgrade/2. Upgrade all scenes", priority = 2)]
        public static void UpgradeAllScenes()
        {
            if (File.Exists(Scripts + "/items/LED_Brightness.cs") &&
                !EditorUtility.DisplayDialog("CatCircuit", "Step 1 has not been run yet (House 3 LEDs cannot be read before it).\nContinue anyway?", "Continue", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Begin("STEP 2 – upgrade scenes");
            string startScene = SceneManager.GetActiveScene().path;

            List<string> scenes = AllGameScenes();
            for (int i = 0; i < scenes.Count; i++)
            {
                EditorUtility.DisplayProgressBar("CatCircuit upgrade", scenes[i], (float)i / scenes.Count);
                try
                {
                    Scene scene = EditorSceneManager.OpenScene(scenes[i], OpenSceneMode.Single);
                    Log($"\n=== {scenes[i]}");
                    UpgradeOpenScene();
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                catch (Exception e)
                {
                    Log($"!! FAILED: {e.Message}\n{e.StackTrace}");
                    Debug.LogException(e);
                }
            }
            EditorUtility.ClearProgressBar();

            if (!string.IsNullOrEmpty(startScene)) EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);
            End("All scenes upgraded – see the report.\n\nNext: CatCircuit ▸ Upgrade ▸ 3. Clean up prefabs & data");
        }

        [MenuItem("CatCircuit/Upgrade/2b. Upgrade only the open scene", priority = 3)]
        public static void UpgradeCurrentSceneMenu()
        {
            Begin("STEP 2b – upgrade open scene");
            UpgradeOpenScene();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            End("Open scene upgraded (not saved yet – press Ctrl+S).");
        }

        private static void UpgradeOpenScene()
        {
            var boards = FindAll<Breadboard>();
            var npcs = FindAll<CatNPC>();

            foreach (Breadboard board in boards)
            {
                if (board.GetComponent<CircuitPuzzle>() != null)
                {
                    Log($"  [skip] {board.name}: already has a CircuitPuzzle.");
                    continue;
                }

                MonoBehaviour seriesManager = FindLegacy("SeriesQuestManager").FirstOrDefault();
                CatNPC questNpc = board.LegacyQuestNPC != null ? board.LegacyQuestNPC
                                : npcs.FirstOrDefault(n => n.LegacyQuest != null || n.LegacySeriesManager != null);

                if (seriesManager != null) UpgradeSeriesParallel(board, seriesManager, questNpc);
                else if (board.LegacyCheckResistorValue) UpgradeResistorBoard(board, questNpc);
                else UpgradeGenericBoard(board, questNpc);

                board.ClearLegacy();
                Touch(board);
            }

            foreach (CatNPC npc in npcs)
            {
                if (npc.Puzzle == null && npc.LegacyQuest == null && npc.LegacyPuzzleToCheck == null &&
                    npc.UnlockedAfterPuzzle == 0 && npc.LegacyCompleteDialogue.Length > 0)
                {
                    npc.ConfigureInfoNpc(3);   // the old code hard-wired "info NPCs" (Sparky) to puzzle 3
                    Log($"  NPC '{npc.NpcName}': info NPC, unlocked after puzzle 3.");
                }
                RemoveBrokenListeners(npc, npc.OnQuestAccepted, "OnQuestAccepted");
                RemoveBrokenListeners(npc, npc.OnQuestCompleted, "OnQuestCompleted");
                if (npc.Puzzle != null) npc.ClearLegacy(true);
                Touch(npc);
            }

            // Old helper components that are no longer needed.
            foreach (string legacy in new[] { "TestLED", "ResistorSlot", "SeriesParallelLED", "SeriesQuestManager", "LEDBrightness" })
                foreach (MonoBehaviour m in FindLegacy(legacy).ToList())
                    RemoveComponent(m);

            foreach (BreadboardSlot slot in FindAll<BreadboardSlot>())
            {
                if (slot.Board == null) continue;
                CircuitDefinition def = slot.Board.ParseCircuit();
                if (def.Find(slot.SlotId) == null)
                    Log($"  NOTE: slot '{slot.name}' (id {slot.SlotId}) is not used in the circuit of '{slot.Board.name}'. Delete it or add 'slot {slot.SlotId}' to the circuit.");
            }
        }

        // ---------------------------------------------------------------- House 1 / House 2 style

        private static void UpgradeGenericBoard(Breadboard board, CatNPC npc)
        {
            List<BreadboardSlot> slots = board.LegacyRequiredSlots.Where(s => s != null).Distinct().ToList();
            if (slots.Count == 0) slots = board.Slots.Where(s => s != null).ToList();

            var infos = new List<CatCircuitRecipes.SlotInfo>();
            bool polarity = slots.Count == 1 && KindOf(slots[0].LegacyRequiredComponent) == PartKind.LED;

            for (int i = 0; i < slots.Count; i++)
            {
                BreadboardSlot s = slots[i];
                ComponentData req = s.LegacyRequiredComponent;
                string id = "S" + (i + 1);

                if (polarity)
                    s.Configure(id, board, true, 180f, 90f);   // placed "backwards" at first – the player has to turn it
                else if (req != null && req.correctPlacementYRotation >= 0f)
                    s.Configure(id, board, true, req.correctPlacementYRotation, 45f);
                else
                    s.Configure(id, board, false, 0f, 45f);
                Touch(s);

                infos.Add(new CatCircuitRecipes.SlotInfo { id = id, label = s.SlotLabel, kind = KindOf(req) });
            }

            string circuit = CatCircuitRecipes.GenericBoardCircuit(infos, out string indicatorId);
            board.CircuitText = circuit;
            board.SerializedSlots.Clear();
            board.SerializedSlots.AddRange(slots);

            // The "LED off / LED on" models become an LEDLight in swap mode.
            FindIndicatorObjects(board, out GameObject off, out GameObject on);
            if (off != null || on != null)
            {
                LEDLight light = board.gameObject.AddComponent<LEDLight>();
                light.Configure(board, indicatorId, LEDLight.Mode.SwapObjects, off, on);
                Touch(light);
                if (off != null) { off.SetActive(true); DisablePickups(off); Touch(off); }
                if (on != null) { on.SetActive(false); DisablePickups(on); Touch(on); }
                Log($"  Indicator LED '{indicatorId}': off='{(off ? off.name : "-")}', on='{(on ? on.name : "-")}'.");
            }
            else if (!polarity)
            {
                Log($"  WARNING: no LED model found for '{indicatorId}'. Add LEDLight (Element Id {indicatorId}) to the LED on the table.");
            }

            string objective = npc != null && npc.LegacyQuest != null ? npc.LegacyQuest.questDescription : "";
            if (string.IsNullOrWhiteSpace(objective) || objective.StartsWith("Put the components"))
                objective = polarity ? "Постави LED-а в правилната посока, за да светне!" : "Сложи батерията и резистора в празните места!";

            string npcName = npc != null ? npc.NpcName : "NPC";
            AddPuzzle(board, board.LegacyPuzzleNumber, npc,
                CatCircuitRecipes.GenericBoardStages(objective.Trim(), npc != null ? npc.LegacyCompleteDialogue : null, indicatorId, polarity, npcName),
                off, on);
            Log($"  Board '{board.name}' → {(polarity ? "polarity" : "closed circuit")} puzzle #{board.LegacyPuzzleNumber}.\n{Indent(circuit)}");
        }

        // ---------------------------------------------------------------- House 3

        private static void UpgradeResistorBoard(Breadboard board, CatNPC npc)
        {
            BreadboardSlot slot = board.LegacyRequiredSlots.FirstOrDefault(s => s != null) ?? board.Slots.FirstOrDefault();
            if (slot != null) { slot.Configure("SR", board, false, 0f, 45f); Touch(slot); }
            board.SerializedSlots.Clear();
            if (slot != null) board.SerializedSlots.Add(slot);
            board.CircuitText = CatCircuitRecipes.House3Circuit;

            // Old LED chain: ResistorSlot.targetLED → its seriesLEDs.
            var leds = new List<MonoBehaviour>();
            MonoBehaviour resistorSlot = FindLegacy("ResistorSlot").FirstOrDefault(r => ReadRef(r, "targetLED") != null);
            if (resistorSlot != null)
            {
                var first = ReadRef(resistorSlot, "targetLED") as MonoBehaviour;
                leds.Add(first);
                var so = new SerializedObject(first);
                SerializedProperty list = so.FindProperty("seriesLEDs");
                if (list != null)
                    for (int i = 0; i < list.arraySize; i++)
                        if (list.GetArrayElementAtIndex(i).objectReferenceValue is MonoBehaviour mb) leds.Add(mb);
            }
            else
            {
                leds = FindLegacy("LEDBrightness").OrderBy(l => l.transform.position.x).ToList();
                Log("  WARNING: ResistorSlot not found – guessed the LED order from positions.");
            }

            for (int i = 0; i < leds.Count && i < 3; i++)
            {
                GameObject go = leds[i].gameObject;
                LEDLight light = go.GetComponent<LEDLight>() ?? go.AddComponent<LEDLight>();
                light.Configure(board, "L" + (i + 1), LEDLight.Mode.Emission);
                DisablePickups(go);
                Touch(light);
                Log($"  LED L{i + 1} = '{go.name}'");
            }
            if (leds.Count == 0) Log("  WARNING: no House 3 LEDs found. Add LEDLight (ids L1, L2, L3) to the three LEDs.");

            string objective = npc != null && npc.LegacyQuest != null ? npc.LegacyQuest.questDescription.Trim() : "Постави правилния резистор във веригата!";
            AddPuzzle(board, board.LegacyPuzzleNumber, npc, CatCircuitRecipes.House3Stages(objective, npc != null ? npc.LegacyCompleteDialogue : null));
            Log($"  Board '{board.name}' → resistor puzzle #{board.LegacyPuzzleNumber}.");
        }

        // ---------------------------------------------------------------- House 4

        private static void UpgradeSeriesParallel(Breadboard board, MonoBehaviour manager, CatNPC npc)
        {
            board.SerializedSlots.Clear();
            for (int i = 1; i <= 5; i++)
            {
                var slot = ReadRef(manager, "slot" + i) as BreadboardSlot;
                if (slot == null) { Log($"  WARNING: slot{i} missing on SeriesQuestManager."); continue; }
                slot.Configure("S" + i, board, false, 0f, 45f);
                Touch(slot);
                board.SerializedSlots.Add(slot);
            }
            board.CircuitText = CatCircuitRecipes.House4Circuit;

            if (npc == null) npc = ReadRef(manager, "kittensNPC") as CatNPC;
            string objective = npc != null && npc.LegacyQuest != null ? npc.LegacyQuest.questDescription.Trim()
                                                                      : "Сложи трите LED-а в горния ред!";
            AddPuzzle(board, board.LegacyPuzzleNumber > 1 ? board.LegacyPuzzleNumber : 4, npc,
                CatCircuitRecipes.House4Stages(objective,
                    npc != null ? npc.LegacyCompleteDialogue : null,
                    npc != null ? npc.LegacyCompleteDialogue2 : null));
            Log($"  Board '{board.name}' → series/parallel puzzle (2 stages).");
        }

        // ---------------------------------------------------------------- helpers for boards

        private static void AddPuzzle(Breadboard board, int progressId, CatNPC npc, List<PuzzleStage> stages, params Object[] swapTargets)
        {
            CircuitPuzzle puzzle = board.gameObject.AddComponent<CircuitPuzzle>();
            puzzle.Configure(board, progressId);
            puzzle.Stages.Clear();
            puzzle.Stages.AddRange(stages);
            Touch(puzzle);

            if (npc != null)
            {
                npc.Configure(npc.NpcName, puzzle, null);
                npc.ClearLegacy(true);
                Touch(npc);
                Log($"  Quest NPC: '{npc.NpcName}'.");
            }
            else Log("  WARNING: no quest NPC found – link the CircuitPuzzle to an NPC by hand.");

            // Old event hookups (LED swap / CompleteQuest / debug) are replaced by LEDLight + CircuitPuzzle.
            RemoveBrokenListeners(board, board.OnCircuitComplete, "OnCircuitComplete", true, swapTargets);
            RemoveBrokenListeners(board, board.OnCircuitIncomplete, "OnCircuitIncomplete", true, swapTargets);
        }

        private static void FindIndicatorObjects(Breadboard board, out GameObject off, out GameObject on)
        {
            off = board.LegacyLedOff;
            on = board.LegacyLedOn;
            if (off != null && on != null) return;

            // Old House 2 style: OnCircuitComplete → GameObject.SetActive(false/true)
            var so = new SerializedObject(board);
            UnityEvent evt = board.OnCircuitComplete;
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            {
                if (evt.GetPersistentMethodName(i) != "SetActive") continue;
                if (!(evt.GetPersistentTarget(i) is GameObject go) || EditorUtility.IsPersistent(go)) continue;
                SerializedProperty arg = so.FindProperty($"OnCircuitComplete.m_PersistentCalls.m_Calls.Array.data[{i}].m_Arguments.m_BoolArgument");
                if (arg == null) continue;
                if (arg.boolValue) { if (on == null) on = go; }
                else if (off == null) off = go;
            }
        }

        /// <summary>
        /// Removes listeners that point at nothing, at assets (e.g. "rename a script"),
        /// or – when <paramref name="oldCircuitCalls"/> – the LED toggles / quest calls the new system replaces.
        /// </summary>
        private static void RemoveBrokenListeners(Object owner, UnityEventBase evt, string label, bool oldCircuitCalls = false, params Object[] swapTargets)
        {
            if (evt == null) return;
            for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                Object target = evt.GetPersistentTarget(i);
                string method = evt.GetPersistentMethodName(i);
                bool broken = target == null || EditorUtility.IsPersistent(target) || string.IsNullOrEmpty(method) || method == "set_name";
                bool isSwap = method == "SetActive" && swapTargets != null && Array.IndexOf(swapTargets, target) >= 0;
                bool replaced = oldCircuitCalls && (isSwap || method == "CompleteQuest" || method.StartsWith("CompletePuzzle"));
                if (!broken && !replaced) continue;

                UnityEventTools.RemovePersistentListener(evt, i);
                Log($"  removed {label} listener #{i} ({(target != null ? target.name : "null")}.{method})");
            }
            Touch(owner);
        }

        private static void DisablePickups(GameObject go)
        {
            foreach (PickupableItem p in go.GetComponentsInChildren<PickupableItem>(true))
            {
                p.enabled = false;
                Touch(p);
            }
        }

        private static PartKind KindOf(ComponentData data) => data != null ? data.Kind : PartKind.None;

        // =====================================================================  STEP 3 – prefabs & data

        private static readonly string[,] PrefabMoves =
        {
            { Prefabs + "/Battery.prefab",          Prefabs + "/Parts/Part_Battery.prefab" },
            { Prefabs + "/LED_OFF_updated.prefab",  Prefabs + "/Parts/Part_LED.prefab" },
            { Prefabs + "/resistor_texture.prefab", Prefabs + "/Parts/Part_Resistor_330.prefab" },
            { Prefabs + "/resistor100.prefab",      Prefabs + "/Parts/Part_Resistor_100.prefab" },
            { Prefabs + "/resistor1k.prefab",       Prefabs + "/Parts/Part_Resistor_1k.prefab" },
            { Prefabs + "/resistor10K.prefab",      Prefabs + "/Parts/Part_Resistor_10k.prefab" },
            { Prefabs + "/LED_off.prefab",          Prefabs + "/Models/Model_LED_Off.prefab" },
            { Prefabs + "/LED_ON_PREFAV.prefab",    Prefabs + "/Models/Model_LED_On.prefab" },
            { Prefabs + "/LED_OFF Variant.prefab",  Prefabs + "/Models/Deco_LED_Indicator.prefab" },
            { Prefabs + "/board.prefab",            Prefabs + "/Board/Board_TwoSlots.prefab" },
            { Prefabs + "/Slot_A.prefab",           Prefabs + "/Board/Slot.prefab" },
            { Prefabs + "/wire.prefab",             Prefabs + "/Board/Deco_Wire.prefab" },
            { Prefabs + "/character.prefab",        Prefabs + "/Characters/Player_Cat.prefab" },
            { Prefabs + "/NPC.prefab",              Prefabs + "/Characters/NPC_Cat.prefab" },
            { Prefabs + "/house.prefab",            Prefabs + "/Environment/House_Interior.prefab" },
            // resistor models (obj + textures) live with the other art
            { Prefabs + "/100",                     Root + "/textures/comp/resistor/100" },
            { Prefabs + "/1K",                      Root + "/textures/comp/resistor/1K" },
            { Prefabs + "/10K",                     Root + "/textures/comp/resistor/10K" },
        };

        [MenuItem("CatCircuit/Upgrade/3. Clean up prefabs & data", priority = 4)]
        public static void CleanupPrefabsAndData()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Begin("STEP 3 – prefabs & data");

            // --- ComponentData: make the electrical kind explicit, give plain resistors a value.
            foreach (ComponentData data in LoadAll<ComponentData>())
            {
                if (data.electricalKind == ElectricalKind.Auto)
                {
                    switch (data.Kind)
                    {
                        case PartKind.Battery: data.electricalKind = ElectricalKind.Battery; break;
                        case PartKind.Resistor: data.electricalKind = ElectricalKind.Resistor; break;
                        case PartKind.LED: data.electricalKind = ElectricalKind.LED; break;
                        case PartKind.Wire: data.electricalKind = ElectricalKind.Wire; break;
                        case PartKind.Switch: data.electricalKind = ElectricalKind.Switch; break;
                    }
                }
                if (data.Kind == PartKind.Resistor && data.resistanceValue <= 0f) data.resistanceValue = CircuitConstants.DefaultResistance;
                EditorUtility.SetDirty(data);
                Log($"  {data.name}: {data.Kind} ({data.DisplayName})");
            }

            // --- remove legacy scripts that live inside prefabs (models untouched)
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                foreach (MonoBehaviour m in contents.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (m == null) continue;
                    string t = m.GetType().Name;
                    if (t != "SeriesParallelLED" && t != "TestLED" && t != "LEDBrightness" && t != "ResistorSlot") continue;
                    if (RemoveComponent(m)) changed = true;
                }
                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    Log($"  cleaned {path}");
                }
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // --- clear names + folders (GUIDs stay the same, so nothing breaks)
            for (int i = 0; i < PrefabMoves.GetLength(0); i++) Move(PrefabMoves[i, 0], PrefabMoves[i, 1]);

            // --- material for the code-puzzle wires
            CatCircuitCreate.GetOrCreateWireMaterial();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            End("Prefabs and data cleaned up.\n\nNext (optional): CatCircuit ▸ Create ▸ House 5 (Mayor code puzzle)\nand CatCircuit ▸ Upgrade ▸ 4. Delete legacy scripts");
        }

        // =====================================================================  STEP 4 – delete legacy scripts

        private static readonly string[] LegacyScripts =
        {
            "SeriesQuestManager.cs", "SeriesParallelLED.cs", "LEDBrightness.cs", "ResistorSlot.cs", "TestLED.cs", "InteractionGuard.cs",
            "LED_Brightness.cs", "resistor_slot.cs",
        };

        [MenuItem("CatCircuit/Upgrade/4. Delete legacy scripts (if unused)", priority = 5)]
        public static void DeleteLegacyScripts()
        {
            Begin("STEP 4 – delete unused legacy scripts");

            var assetFiles = Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".unity") || f.EndsWith(".prefab") || f.EndsWith(".asset"))
                .ToList();

            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { Scripts }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!LegacyScripts.Contains(Path.GetFileName(path))) continue;

                string user = assetFiles.FirstOrDefault(f => File.ReadAllText(f).Contains(guid));
                if (user != null)
                {
                    Log($"  KEEP {path} – still used by {user}");
                    continue;
                }
                AssetDatabase.DeleteAsset(path);
                Log($"  deleted {path}");
            }
            AssetDatabase.Refresh();
            End("Done. Scripts that are still used were kept (see report).");
        }

        // =====================================================================  STEP 5 – fixes after the first test

        [MenuItem("CatCircuit/Upgrade/5. Apply fixes (pickups + sparkles)", priority = 6)]
        public static void ApplyFixes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Begin("STEP 5 – pickups + sparkles");
            Material spark = GetOrCreateSparkMaterial();
            string startScene = SceneManager.GetActiveScene().path;

            foreach (string path in AllGameScenes())
            {
                try
                {
                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    Log($"\n=== {path}");
                    FixOpenScene(spark);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                catch (Exception e)
                {
                    Log($"!! FAILED: {e.Message}");
                    Debug.LogException(e);
                }
            }
            if (!string.IsNullOrEmpty(startScene)) EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);
            End("Fixes applied – see the report.");
        }

        private static void FixOpenScene(Material spark)
        {
            var boards = FindAll<Breadboard>();
            var usedByLights = new HashSet<GameObject>();

            // 1) House 2: the old "LED off" model was really the LED the player must pick up.
            //    An LED that sits in a slot glows by itself, so the on/off swap is not needed there.
            foreach (LEDLight l in FindAll<LEDLight>())
            {
                var board = l.Circuit as Breadboard;
                bool isSlotLed = board != null && board.FindSlot(l.ElementId) != null;
                if (l.LightMode == LEDLight.Mode.SwapObjects && isSlotLed)
                {
                    GameObject pickupLed = l.OffObject;
                    if (pickupLed != null)
                    {
                        foreach (PickupableItem p in pickupLed.GetComponentsInChildren<PickupableItem>(true)) { p.enabled = true; Touch(p); }
                        pickupLed.SetActive(true);
                        Touch(pickupLed);
                        Log($"  '{pickupLed.name}' is a pickup again (it was wrongly used as the 'LED off' model).");
                    }
                    if (l.OnObject != null) { l.OnObject.SetActive(false); Touch(l.OnObject); }
                    l.Configure(board, l.ElementId, LEDLight.Mode.SwapObjects, null, null);
                    l.enabled = false;
                    Touch(l);
                    Log($"  disabled the on/off LED swap for slot '{l.ElementId}' – the placed LED glows itself.");
                    continue;
                }
                if (l.OffObject != null) usedByLights.Add(l.OffObject);
                if (l.OnObject != null) usedByLights.Add(l.OnObject);
                usedByLights.Add(l.gameObject);
            }

            // 2) Parts that the circuit already contains (e.g. the battery + resistor built into the
            //    House 2 board) are only decoration – the player must not carry them away.
            var fixedKinds = new HashSet<PartKind>();
            foreach (Breadboard b in boards)
                foreach (PartDef p in b.ParseCircuit().parts)
                    if (p.kind == PartKind.Battery || p.kind == PartKind.Resistor || p.kind == PartKind.LED)
                        fixedKinds.Add(p.kind);

            if (boards.Count > 0)
            {
                foreach (PickupableItem p in FindAll<PickupableItem>())
                {
                    if (p.Data == null || !p.enabled || usedByLights.Contains(p.gameObject)) continue;
                    if (!fixedKinds.Contains(p.Data.Kind)) continue;
                    // Keep it if some slot of the scene may need this kind.
                    if (SceneNeedsKind(boards, p.Data.Kind)) continue;
                    p.enabled = false;
                    Touch(p);
                    Log($"  '{p.name}' ({p.Data.Kind}) is decoration now – the circuit already has one.");
                }
            }

            // 3) Sparkles on every puzzle.
            foreach (CircuitPuzzle pz in FindAll<CircuitPuzzle>())
            {
                pz.ConfigureEffects(null, spark);
                Touch(pz);
                Log($"  sparkles enabled on puzzle '{pz.name}'.");
            }
        }

        /// <summary>True if an empty slot's role suggests the player must bring this kind (from the old slot data).</summary>
        private static bool SceneNeedsKind(List<Breadboard> boards, PartKind kind)
        {
            foreach (Breadboard b in boards)
            {
                CircuitDefinition def = b.ParseCircuit();
                foreach (BreadboardSlot s in b.Slots)
                {
                    if (s == null || def.Find(s.SlotId) == null) continue;
                    ComponentData req = s.LegacyRequiredComponent;
                    if (req != null && req.Kind == kind) return true;
                    if (kind == PartKind.Resistor && s.SlotId == "SR") return true;   // House 3
                }
            }
            return false;
        }

        private static Material GetOrCreateSparkMaterial()
        {
            const string folder = Root + "/Materials";
            const string matPath = folder + "/Spark_Additive.mat";
            const string texPath = folder + "/SparkDot.png";
            EnsureFolder(folder);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null) return mat;

            if (!File.Exists(texPath))
            {
                const int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
                var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (importer != null) { importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport(); }
            }
            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            mat = new Material(shader) { name = "Spark_Additive" };
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", dot);
            mat.mainTexture = dot;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            AssetDatabase.CreateAsset(mat, matPath);
            return mat;
        }

        // =====================================================================  generic helpers

        public static void Touch(Object o)
        {
            if (o == null) return;
            EditorUtility.SetDirty(o);
            if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }

        private static List<T> FindAll<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();

        /// <summary>Finds components of an old script by NAME, so this file compiles even after the script is deleted.</summary>
        private static IEnumerable<MonoBehaviour> FindLegacy(string typeName) =>
            Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                  .Where(m => m != null && m.GetType().Name == typeName);

        private static Object ReadRef(Object component, string field)
        {
            if (component == null) return null;
            SerializedProperty p = new SerializedObject(component).FindProperty(field);
            return p != null && p.propertyType == SerializedPropertyType.ObjectReference ? p.objectReferenceValue : null;
        }

        /// <summary>Destroys a component if Unity allows it; components owned by a prefab asset are only disabled.</summary>
        private static bool RemoveComponent(MonoBehaviour m)
        {
            bool fromPrefab = PrefabUtility.IsPartOfPrefabInstance(m) && !PrefabUtility.IsAddedComponentOverride(m);
            string where = $"{m.GetType().Name} on '{m.gameObject.name}'";
            if (fromPrefab)
            {
                m.enabled = false;
                Touch(m);
                Log($"  disabled {where} (belongs to a prefab – cleaned by step 3)");
                return false;
            }
            Object.DestroyImmediate(m, true);
            Log($"  removed {where}");
            return true;
        }

        private static IEnumerable<T> LoadAll<T>() where T : Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { Root })
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null);

        private static List<string> AllGameScenes()
        {
            var list = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (!string.IsNullOrEmpty(s.path) && File.Exists(s.path)) list.Add(s.path);
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { Root }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!list.Contains(p)) list.Add(p);
            }
            return list;
        }

        private static void Move(string from, string to)
        {
            bool isFolder = AssetDatabase.IsValidFolder(from);
            if (!isFolder && !File.Exists(from)) return;                                 // already moved / never existed
            if (File.Exists(to) || AssetDatabase.IsValidFolder(to)) { Log($"  [skip] {to} already exists"); return; }

            EnsureFolder(Path.GetDirectoryName(to).Replace('\\', '/'));
            string error = AssetDatabase.MoveAsset(from, to);
            Log(string.IsNullOrEmpty(error) ? $"  {from}  →  {to}" : $"  !! could not move {from}: {error}");
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static void DeleteIfEmpty(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;
            if (Directory.GetFiles(folder).Any(f => !f.EndsWith(".meta")) || Directory.GetDirectories(folder).Length > 0) return;
            AssetDatabase.DeleteAsset(folder);
            Log($"  deleted empty folder {folder}");
        }

        private static string Indent(string s) => "      " + s.Replace("\n", "\n      ");

        private static void Begin(string title)
        {
            report.Clear();
            report.AppendLine($"CatCircuit upgrade – {title} – {DateTime.Now}");
        }

        internal static void Log(string line)
        {
            report.AppendLine(line);
        }

        private static void End(string message)
        {
            string text = report.ToString();
            File.AppendAllText(ReportPath, text + "\n");
            Debug.Log(text);
            AssetDatabase.ImportAsset(ReportPath);
            EditorUtility.DisplayDialog("CatCircuit", message + $"\n\nReport: {ReportPath}", "OK");
        }
    }
}
