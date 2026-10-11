using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CatCircuit.EditorTools
{
    /// <summary>
    /// "Plug and play" menus:
    ///   GameObject ▸ CatCircuit ▸ ...   – ready-to-use puzzle pieces in the open scene
    ///   CatCircuit ▸ Create ▸ House 5   – the Mayor's code puzzle scene
    /// </summary>
    public static class CatCircuitCreate
    {
        private const string MaterialsFolder = "Assets/_Game/Materials";

        // =====================================================================  GameObject menu

        [MenuItem("GameObject/CatCircuit/Circuit Board Puzzle", false, 10)]
        public static void CreateBoard(MenuCommand cmd)
        {
            var root = new GameObject("Circuit Board");
            Place(root, cmd);
            Breadboard board = root.AddComponent<Breadboard>();
            board.CircuitText = Breadboard.ExampleCircuit;

            // one slot
            var slotGo = new GameObject("Slot S1");
            slotGo.transform.SetParent(root.transform, false);
            var box = slotGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.5f, 0.5f, 1.5f);
            BreadboardSlot slot = slotGo.AddComponent<BreadboardSlot>();
            slot.Configure("S1", board, false, 0f, 45f);
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(pad.GetComponent<Collider>());
            pad.name = "Slot visual";
            pad.transform.SetParent(slotGo.transform, false);
            pad.transform.localScale = new Vector3(0.6f, 0.05f, 0.6f);

            // one LED
            ComponentData ledData = FindComponentData(PartKind.LED);
            GameObject led;
            if (ledData != null && ledData.worldPrefab != null)
            {
                led = (GameObject)PrefabUtility.InstantiatePrefab(ledData.worldPrefab, root.transform);
                foreach (PickupableItem p in led.GetComponentsInChildren<PickupableItem>(true)) p.enabled = false;
            }
            else
            {
                led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                led.transform.SetParent(root.transform, false);
                led.transform.localScale = Vector3.one * 0.3f;
            }
            led.name = "LED L1";
            led.transform.localPosition = new Vector3(1.5f, 0.2f, 0f);
            led.AddComponent<LEDLight>().Configure(board, "L1", LEDLight.Mode.Emission);

            CircuitPuzzle puzzle = root.AddComponent<CircuitPuzzle>();
            puzzle.Configure(board, 0);
            var stage = new PuzzleStage { name = "Light the LED", objective = "Сложи резистор в слота, за да светне LED-ът!", successMessage = "Браво!" };
            stage.conditions.Add(CircuitCondition.Make(ConditionType.LedsLit, "L1"));
            stage.conditions.Add(CircuitCondition.Make(ConditionType.NoLedOverloaded));
            puzzle.Stages.Add(stage);

            Finish(root, "Circuit board created. Edit the circuit text on the Breadboard, add slots (Slot Id!), then link the CircuitPuzzle to an NPC.");
        }

        [MenuItem("GameObject/CatCircuit/Code Circuit Station", false, 11)]
        public static void CreateCodeStation(MenuCommand cmd)
        {
            var root = new GameObject("Code Circuit Station");
            Place(root, cmd);
            CreateCodeStationContent(root.transform, CodeCircuitBuilder.ExampleCode, null, 0);
            Finish(root, "Code station created: a terminal + a build area. Move the 'Build Area' where the circuit should appear.");
        }

        [MenuItem("GameObject/CatCircuit/Quest NPC (on selection)", false, 12)]
        public static void CreateNpc(MenuCommand cmd)
        {
            GameObject go = Selection.activeGameObject;
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "NPC";
                Place(go, cmd);
            }
            if (go.GetComponent<CatNPC>() == null) Undo.AddComponent<CatNPC>(go);
            Finish(go, "CatNPC added. Set its name, greeting and drag a CircuitPuzzle into 'Puzzle'.");
        }

        [MenuItem("GameObject/CatCircuit/Door (Scene Transition)", false, 13)]
        public static void CreateDoor(MenuCommand cmd)
        {
            var go = new GameObject("Door");
            Place(go, cmd);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2f, 3f, 1.5f);
            go.AddComponent<SceneTransition>();
            var arrival = new GameObject("Arrival Point");
            arrival.transform.SetParent(go.transform, false);
            arrival.transform.localPosition = new Vector3(0f, 0f, 2f);
            Finish(go, "Door created. Set Target Scene Name (and make sure the scene is in the build list).");
        }

        // =====================================================================  House 5

        [MenuItem("CatCircuit/Create/House 5 – Mayor code puzzle scene", priority = 20)]
        public static void CreateHouse5()
        {
            const string source = "Assets/_Game/Scenes/House2.unity";
            const string target = "Assets/_Game/Scenes/House5.unity";

            if (File.Exists(target))
            {
                EditorUtility.DisplayDialog("CatCircuit", target + " already exists. Delete it first if you want to recreate it.", "OK");
                return;
            }
            if (!File.Exists(source))
            {
                EditorUtility.DisplayDialog("CatCircuit", "House 5 is made from a copy of " + source + ", which was not found.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            AssetDatabase.CopyAsset(source, target);
            Scene scene = EditorSceneManager.OpenScene(target, OpenSceneMode.Single);

            // Where the old breadboard was = where the computer goes.
            Breadboard oldBoard = Object.FindObjectsByType<Breadboard>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            Vector3 anchor = oldBoard != null ? oldBoard.transform.position : Vector3.zero;
            PlayerSpawner spawner = Object.FindAnyObjectByType<PlayerSpawner>();
            Vector3 spawn = spawner != null ? spawner.transform.position : anchor + Vector3.back * 5f;
            var so = spawner != null ? new SerializedObject(spawner) : null;
            if (so != null) spawn = so.FindProperty("defaultSpawnPosition").vector3Value;

            // Remove the House 2 puzzle: pickups, LEDs, the board.
            foreach (PickupableItem p in FindAll<PickupableItem>()) DestroyRoot(p != null ? p.gameObject : null);
            foreach (LEDLight l in FindAll<LEDLight>()) DestroyRoot(l != null ? l.gameObject : null);
            foreach (Breadboard b in FindAll<Breadboard>()) DestroyRoot(b != null ? b.gameObject : null);

            // Code station
            var station = new GameObject("Mayor Code Station");
            station.transform.position = anchor;
            CircuitPuzzle puzzle = CreateCodeStationContent(station.transform, CodeCircuitBuilder.ExampleCode, CatCircuitRecipes.MayorStages(), 5);

            Transform area = station.transform.Find("Build Area");
            if (area != null)
            {
                Vector3 mid = Vector3.Lerp(anchor, spawn, 0.45f);
                area.position = new Vector3(mid.x, anchor.y, mid.z);
            }
            Transform terminal = station.transform.Find("Terminal");
            if (terminal != null)
            {
                Vector3 look = spawn - terminal.position; look.y = 0f;
                if (look.sqrMagnitude > 0.01f) terminal.rotation = Quaternion.LookRotation(-look.normalized);
            }

            // The NPC becomes the Mayor.
            CatNPC npc = FindAll<CatNPC>().FirstOrDefault();
            if (npc != null)
            {
                npc.Configure(CatCircuitRecipes.MayorName, puzzle, CatCircuitRecipes.MayorGreeting);
                npc.ClearLegacy(true);
                npc.gameObject.name = "Mayor Fluffington";
                CatCircuitUpgrade.Touch(npc);
                CatCircuitUpgrade.Touch(npc.gameObject);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuildSettings(target);
            Selection.activeGameObject = station;

            EditorUtility.DisplayDialog("CatCircuit",
                "House 5 created (copy of House 2 with the Mayor's code puzzle).\n\n" +
                "Still to do by hand:\n" +
                "• Move 'Build Area' to a free spot on the floor (yellow box gizmo).\n" +
                "• Swap the NPC model for the Mayor if you have one.\n" +
                "• In the Outside scene: duplicate a door, set Target Scene Name = House5 and put it on a house.",
                "OK");
        }

        /// <summary>Terminal + build area + puzzle. Returns the puzzle.</summary>
        private static CircuitPuzzle CreateCodeStationContent(Transform root, string code, List<PuzzleStage> stages, int progressId)
        {
            // Build area (where the circuit appears)
            var areaGo = new GameObject("Build Area");
            areaGo.transform.SetParent(root, false);
            areaGo.transform.localPosition = new Vector3(0f, 0f, 3f);
            CodeCircuitBuilder builder = areaGo.AddComponent<CodeCircuitBuilder>();
            builder.Configure(code, LoadAll<ComponentData>().Where(d => d.worldPrefab != null && d.Kind != PartKind.None).ToList(), GetOrCreateWireMaterial());

            CircuitPuzzle puzzle = areaGo.AddComponent<CircuitPuzzle>();
            puzzle.Configure(builder, progressId);
            if (stages != null) puzzle.Stages.AddRange(stages);
            else
            {
                var stage = new PuzzleStage { name = "Light an LED", objective = "Напиши верига, която свети LED.", successMessage = "Браво!" };
                stage.conditions.Add(CircuitCondition.Make(ConditionType.AnyLedLit));
                stage.conditions.Add(CircuitCondition.Make(ConditionType.NoLedOverloaded));
                puzzle.Stages.Add(stage);
            }

            // Terminal (simple computer made of primitives – replace the visuals with a model any time)
            var terminal = new GameObject("Terminal");
            terminal.transform.SetParent(root, false);
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(terminal.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            body.transform.localScale = new Vector3(0.9f, 1f, 0.5f);
            body.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Terminal_Body", new Color(0.2f, 0.22f, 0.25f), Color.black);
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.name = "Screen";
            screen.transform.SetParent(terminal.transform, false);
            screen.transform.localPosition = new Vector3(0f, 0.65f, -0.26f);
            screen.transform.localScale = new Vector3(0.75f, 0.5f, 0.02f);
            screen.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Terminal_Screen", new Color(0.1f, 0.4f, 0.3f), new Color(0.2f, 0.9f, 0.6f));

            CodeTerminal term = terminal.AddComponent<CodeTerminal>();
            term.Configure(builder, puzzle, "Компютър");
            return puzzle;
        }

        [MenuItem("CatCircuit/Create/Sound Library (use your own sound files)", priority = 21)]
        public static void CreateSoundLibrary()
        {
            const string folder = "Assets/_Game/Resources";
            string path = $"{folder}/{SoundLibrary.ResourceName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<SoundLibrary>(path);
            if (existing == null)
            {
                CatCircuitUpgrade.EnsureFolder(folder);
                existing = ScriptableObject.CreateInstance<SoundLibrary>();
                AssetDatabase.CreateAsset(existing, path);
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            EditorUtility.DisplayDialog("CatCircuit",
                "Sound Library: " + path + "\n\nAdd an entry per sound you want to replace (the others stay synthesized) and optionally a Music clip.",
                "OK");
        }

        // =====================================================================  helpers

        public static Material GetOrCreateWireMaterial() =>
            GetOrCreateMaterial("CircuitWire", new Color(0.25f, 0.25f, 0.28f), Color.black, unlit: true);

        public static Material GetOrCreateMaterial(string name, Color color, Color emission, bool unlit = false)
        {
            CatCircuitUpgrade.EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find(unlit ? "Unlit/Color" : "Standard");
            mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (emission != Color.black && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission);
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static ComponentData FindComponentData(PartKind kind) =>
            LoadAll<ComponentData>().FirstOrDefault(d => d.Kind == kind && d.worldPrefab != null);

        private static IEnumerable<T> LoadAll<T>() where T : Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets/_Game" })
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null);

        private static List<T> FindAll<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();

        private static void DestroyRoot(GameObject go)
        {
            if (go == null) return;
            GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) root = go;
            try { Object.DestroyImmediate(root); }
            catch { root.SetActive(false); }
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void Place(GameObject go, MenuCommand cmd)
        {
            GameObjectUtility.SetParentAndAlign(go, cmd?.context as GameObject);
            if (go.transform.parent == null && SceneView.lastActiveSceneView != null)
                go.transform.position = SceneView.lastActiveSceneView.pivot;
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        }

        private static void Finish(GameObject go, string message)
        {
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[CatCircuit] " + message, go);
        }
    }
}
