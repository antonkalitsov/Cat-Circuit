using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CatCircuit.EditorTools
{
    /// <summary>
    /// Debug tool: CatCircuit ▸ Debug ▸ Inspect boards + screenshots (or batch mode:
    ///   Unity.exe -batchmode -projectPath … -executeMethod CatCircuit.EditorTools.CatCircuitInspect.RunBatch -outDir …).
    /// For every house it logs the slots, the board wires next to them, and – for each part that fits a slot – where
    /// the part's + / − contacts end up at every rotation step and how the slot judges it (Forward / Reversed / NotSeated).
    /// It also saves screenshots of the boards, the room and the wall schematic. Changes nothing (scenes are not saved).
    /// </summary>
    public static class CatCircuitInspect
    {
        private static string outDir = "Claude outputs/shots";
        private static StringBuilder log;

        [MenuItem("CatCircuit/Debug/Inspect boards + screenshots", priority = 200)]
        public static void RunMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run();
            EditorUtility.RevealInFinder(outDir);
        }

        public static void RunBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-outDir");
            if (i >= 0 && i + 1 < args.Length) outDir = args[i + 1];
            try { Run(); }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(0);
        }

        private static void Run()
        {
            Directory.CreateDirectory(outDir);
            log = new StringBuilder();
            foreach (string scene in new[] { "House1", "House2", "House3", "House4", "House5", "Outside" })
            {
                try { InspectScene(scene); }
                catch (Exception e) { log.AppendLine($"!! {scene}: {e}"); }
            }
            File.WriteAllText(Path.Combine(outDir, "inspect.txt"), log.ToString());
            Debug.Log("[Inspect] done → " + outDir);
        }

        // ------------------------------------------------------------------ per scene

        private static void InspectScene(string sceneName)
        {
            string path = $"Assets/_Game/Scenes/{sceneName}.unity";
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Physics.SyncTransforms();
            SetUiInstance();
            log.AppendLine($"\n==================== {sceneName}");

            var slots = Object.FindObjectsByType<BreadboardSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var wires = FindWireEnds();
            for (int w = 0; w + 1 < wires.Count; w += 2)
                log.AppendLine($"Wire \u0027{wires[w].name}\u0027 {Fmt(wires[w].end)} -> {Fmt(wires[w + 1].end)}");
            var parts = Object.FindObjectsByType<PickupableItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (Breadboard b in Object.FindObjectsByType<Breadboard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                log.AppendLine($"Board '{b.name}' at {b.transform.position} circuit:\n{new SerializedObject(b).FindProperty("circuit").stringValue}");
                SlotWiring.AlignFixedBatteries(b);   // what Breadboard.Start does in the game
                foreach (PickupableItem it in Object.FindObjectsByType<PickupableItem>(FindObjectsSortMode.None))
                    if (it.Data != null && it.Data.Kind == PartKind.Battery)
                        log.AppendLine($"Battery '{it.name}' at {Fmt(it.transform.position)} + nub points {Fmt(it.transform.TransformDirection(Vector3.up))}");
            }
            foreach (PickupableItem p in parts)
                log.AppendLine($"Part '{p.name}' data={(p.Data != null ? p.Data.name : "-")} at {p.transform.position}");
            foreach (LEDLight l in Object.FindObjectsByType<LEDLight>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                log.AppendLine($"LEDLight '{l.name}' id={new SerializedObject(l).FindProperty("elementId")?.stringValue} at {l.transform.position}");

            foreach (BreadboardSlot s in slots)
            {
                var so = new SerializedObject(s);
                log.AppendLine($"\n-- Slot {s.SlotId} '{s.name}' pos={s.transform.position} right={s.transform.right} fwd={s.transform.forward} lossy={s.transform.lossyScale}" +
                               $" requireOrientation={so.FindProperty("requireOrientation").boolValue} correctY={so.FindProperty("correctRotationY").floatValue}" +
                               $" step={so.FindProperty("rotationStep").floatValue}");
                foreach (var w in wires.Where(w => Flat(w.end - s.transform.position) < 2.5f).OrderBy(w => Flat(w.end - s.transform.position)).Take(6))
                    log.AppendLine($"   wire end '{w.name}' world={w.end} slotLocal={s.transform.InverseTransformPoint(w.end)} dist={Flat(w.end - s.transform.position):0.00}");

                foreach (ComponentData data in parts.Select(p => p.Data).Where(d => d != null && s.Accepts(d)).Distinct())
                    TryPart(s, data);
            }

            Screenshots(sceneName, slots);
            if (sceneName == "House5") CodeStages();
        }

        private static void CodeStages()
        {
            CodeCircuitBuilder builder = Object.FindAnyObjectByType<CodeCircuitBuilder>();
            if (builder == null) { log.AppendLine("!! no CodeCircuitBuilder"); return; }
            string[] codes =
            {
                "battery B 9V\nresistor R1 330\nled L1 red\nB+ -> R1 -> L1 -> B-",
                "battery B 3V\nresistor R1 68\nresistor R2 68\nled L1 red\nled L2 red\nB+ -> R1 -> L1 -> B-\nB+ -> R2 -> L2 -> B-",
                "battery B 3V\nswitch S1 on\nresistor R1 68\nresistor R2 68\nled L1 red\nled L2 red\nB+ -> S1 -> A\nA -> R1 -> L1 -> B-\nA -> R2 -> L2 -> B-",
            };
            FieldInfo rootField = typeof(CodeCircuitBuilder).GetField("root", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < codes.Length; i++)
            {
                if (rootField?.GetValue(builder) is Transform old && old != null) Object.DestroyImmediate(old.gameObject);
                bool ok = builder.Build(codes[i]);
                int lit = 0, leds = 0;
                foreach (PartResult r in builder.Result.Leds) { leds++; if (r.lit) lit++; }
                log.AppendLine($"House5 stage {i + 1}: build={ok} lit {lit}/{leds} short={builder.Result.shortCircuit}");
                Transform root = rootField?.GetValue(builder) as Transform;
                if (root == null) continue;
                Vector3 c = root.position;
                foreach (Transform t in root) if (t.name.StartsWith("Battery")) log.AppendLine($"   {t.name} + nub points {Fmt(t.TransformDirection(Vector3.up))} at {Fmt(t.position)}");
                ShotOrtho($"House5_code_stage{i + 1}_top", c, 4.5f);
                Shot($"House5_code_stage{i + 1}_view", c + new Vector3(0f, 4.5f, -6f), c, 50f);
            }
        }

        private class WireEnd { public string name; public Vector3 end; }

        private static List<WireEnd> FindWireEnds()
        {
            var list = new List<WireEnd>();
            foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (mf.sharedMesh == null) continue;
                bool wire = mf.name.IndexOf("wire", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (mf.transform.parent != null && mf.transform.parent.name.IndexOf("wire", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!wire) continue;
                Bounds lb = mf.sharedMesh.bounds;
                // long local axis = the wire direction
                Vector3 e = lb.extents;
                Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right * e.x : e.y >= e.z ? Vector3.up * e.y : Vector3.forward * e.z;
                Vector3 a = mf.transform.TransformPoint(lb.center + axis), b = mf.transform.TransformPoint(lb.center - axis);
                if ((a - b).magnitude < 0.05f) continue;
                list.Add(new WireEnd { name = mf.name, end = a });
                list.Add(new WireEnd { name = mf.name, end = b });
            }
            return list;
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        // ------------------------------------------------------------------ parts at every angle

        private static void TryPart(BreadboardSlot slot, ComponentData data)
        {
            if (data.worldPrefab == null) return;
            GameObject go = Object.Instantiate(data.worldPrefab);
            PickupableItem item = go.GetComponent<PickupableItem>() ?? go.AddComponent<PickupableItem>();
            item.SetComponentData(data);
            log.AppendLine($"   part {data.name} ({data.Kind}) defaultSlotRotation={data.defaultSlotRotation} offset={data.slotPositionOffset}");
            try
            {
                slot.PlaceItem(item, 0f);
                bool wired = slot.TryGetContacts(data, out Vector3 pc, out Vector3 mc);
                log.AppendLine(wired ? $"     wire contacts: + {Fmt(pc)}  - {Fmt(mc)}  reference={slot.ReferenceAngle(data):0}" : "     (no wires found)");
                var so = new SerializedObject(slot);
                float step = Mathf.Max(1f, so.FindProperty("rotationStep").floatValue);
                int n = Mathf.RoundToInt(360f / step);
                for (int k = 0; k < n; k++)
                {
                    Physics.SyncTransforms();
                    Contacts(item, data, out Vector3 plus, out Vector3 minus);
                    Vector3 pl = slot.transform.InverseTransformPoint(plus);
                    Vector3 dir = pl - slot.transform.InverseTransformPoint(minus); dir.y = 0f;   // − contact → + contact of the part
                    string touch = !wired ? "" : Vector3.Angle(dir, new Vector3(pc.x, 0, pc.z)) < 20f ? "  [+ end at + wire]" :
                                   Vector3.Angle(dir, new Vector3(mc.x, 0, mc.z)) < 20f ? "  [+ end at - wire]" : "  [across the wires]";
                    log.AppendLine($"     rotY={slot.RotationY,6:0} seating={slot.CurrentSeating,-9}{touch} +local={Fmt(pl)} " +
                                   $"-local={Fmt(slot.transform.InverseTransformPoint(minus))} centre={Fmt(slot.transform.InverseTransformPoint(Centre(item)))}");
                    slot.TryRotate(null);
                }
            }
            catch (Exception e) { log.AppendLine("     error: " + e.Message); }
            finally
            {
                typeof(BreadboardSlot).GetField("placedItem", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(slot, null);
                Object.DestroyImmediate(go);
            }
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";

        private static Vector3 Centre(PickupableItem item)
        {
            Bounds b = default; bool any = false;
            foreach (Renderer r in item.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return any ? b.center : item.transform.position;
        }

        /// <summary>+ / − contact points in world space: LED legs (long = +), battery nub (+) / base, resistor ends.</summary>
        private static void Contacts(PickupableItem item, ComponentData data, out Vector3 plus, out Vector3 minus)
        {
            var pts = new List<Vector3>();   // item-local
            foreach (MeshFilter mf in item.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                foreach (Vector3 v in mf.sharedMesh.vertices) pts.Add(item.transform.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            plus = minus = item.transform.position;
            if (pts.Count == 0) return;
            float minY = pts.Min(p => p.y), maxY = pts.Max(p => p.y), range = Mathf.Max(0.0001f, maxY - minY);

            Vector3 lp, lm;
            if (data.Kind == PartKind.LED)
            {
                var legs = pts.Where(p => p.y < minY + 0.25f * range).ToList();
                Vector3 lowest = legs.OrderBy(p => p.y).First();
                float sx = Mathf.Sign(lowest.x);
                var other = legs.Where(p => Mathf.Sign(p.x) != sx).ToList();
                lp = lowest;
                lm = other.Count > 0 ? other.OrderBy(p => p.y).First() : lowest;
            }
            else if (data.Kind == PartKind.Battery)
            {
                var top = pts.Where(p => p.y > maxY - 0.03f * range).ToList();
                var bottom = pts.Where(p => p.y < minY + 0.03f * range).ToList();
                lp = Avg(top); lm = Avg(bottom);
            }
            else
            {
                Vector3 min = new Vector3(pts.Min(p => p.x), minY, pts.Min(p => p.z)), max = new Vector3(pts.Max(p => p.x), maxY, pts.Max(p => p.z));
                Vector3 size = max - min, c = (min + max) / 2f;
                Vector3 axis = size.x >= size.z && size.x >= size.y ? new Vector3(size.x / 2f, 0, 0) : size.z >= size.y ? new Vector3(0, 0, size.z / 2f) : new Vector3(0, size.y / 2f, 0);
                lp = c + axis; lm = c - axis;
            }
            plus = item.transform.TransformPoint(lp);
            minus = item.transform.TransformPoint(lm);
        }

        private static Vector3 Avg(List<Vector3> p) => p.Count == 0 ? Vector3.zero : p.Aggregate(Vector3.zero, (a, b) => a + b) / p.Count;

        // ------------------------------------------------------------------ screenshots

        private static void Screenshots(string scene, BreadboardSlot[] slots)
        {
            CircuitHost host = Object.FindAnyObjectByType<CircuitHost>(FindObjectsInactive.Include);
            PlayerSpawner spawner = Object.FindAnyObjectByType<PlayerSpawner>();
            Vector3 room = spawner != null ? spawner.DefaultSpawnPosition : Vector3.zero;

            if (host != null)
            {
                Vector3 c = host.transform.position;
                if (slots.Length > 0) c = slots.Aggregate(Vector3.zero, (a, s) => a + s.transform.position) / slots.Length;
                ShotOrtho($"{scene}_board_top", c, 3.2f);
                Shot($"{scene}_board_view", c + (room - c).normalized * 3f + Vector3.up * 2.2f, c, 55f);
            }

            Vector3 eye = room + Vector3.up * 1.7f;
            for (int i = 0; i < 4; i++)
                Shot($"{scene}_room_{i}_before", eye, eye + Quaternion.Euler(0, i * 90f, 0) * Vector3.forward * 5f + Vector3.down * 0.5f, 75f);

            foreach (CatNPC npc in Object.FindObjectsByType<CatNPC>(FindObjectsSortMode.None))
            {
                Vector3 p = npc.transform.position + Vector3.up * 1f;
                Shot($"{scene}_npc_{npc.name}_fromPlusX", p + npc.transform.right * 2.5f, p, 50f);
                Shot($"{scene}_npc_{npc.name}_fromPlusZ", p + npc.transform.forward * 2.5f, p, 50f);
                log.AppendLine($"NPC '{npc.NpcName}' ({npc.name}) pos={npc.transform.position} rot={npc.transform.eulerAngles}");
            }

            // Build the runtime extras the game would build, and photograph them.
            if (HouseInfo.FromScene(scene) > 0)
            {
                var ws = new GameObject("WallSchematic").AddComponent<WallSchematic>();
                typeof(WallSchematic).GetField("house", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ws, HouseInfo.FromScene(scene));
                Invoke(ws, "Start");
                ShotFacing($"{scene}_schematic", ws.transform);

                var decor = new GameObject("InteriorDecor").AddComponent<InteriorDecor>();
                decor.Build(HouseInfo.FromScene(scene));
                for (int i = 0; i < 4; i++)
                    Shot($"{scene}_room_{i}_after", eye, eye + Quaternion.Euler(0, i * 90f, 0) * Vector3.forward * 5f + Vector3.down * 0.5f, 75f);
            }
            else
            {
                var hs = new GameObject("HouseSigns").AddComponent<HouseSigns>();
                Invoke(hs, "Start");
                foreach (Transform sign in hs.transform)
                {
                    Vector3 away = room - sign.position; away.y = 0f;
                    Shot($"Outside_{sign.name}", sign.position + away.normalized * 7f + Vector3.down * 1f, sign.position + Vector3.down * 1.5f, 60f);
                }

                foreach (CatNPC sp in Object.FindObjectsByType<CatNPC>(FindObjectsSortMode.None))
                {
                    Vector3 e = sp.transform.position + Vector3.up * 1.5f;
                    for (int k = 0; k < 16; k++)
                    {
                        Vector3 d = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                        if (Physics.Raycast(e, d, out RaycastHit hh, 9f, ~0, QueryTriggerInteraction.Ignore))
                            log.AppendLine($"ray {k * 22.5f:0} from {sp.NpcName}: dist={hh.distance:0.00} normal={Fmt(hh.normal)} collider={hh.collider.name} ({hh.collider.GetType().Name}) dot(face)={Vector3.Dot(d, -sp.transform.right):0.00}");
                    }
                }
                var ohm = new GameObject("OhmBoard").AddComponent<WallSchematic>();
                typeof(WallSchematic).GetField("house", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ohm, 3);
                typeof(WallSchematic).GetField("ohmBoard", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ohm, true);
                Invoke(ohm, "Start");
                if (ohm != null)
                {
                    ShotFacing("Outside_ohm_board", ohm.transform);
                    Vector3 f = -ohm.transform.forward;
                    Shot("Outside_ohm_board_high", ohm.transform.position + f * 7f + Vector3.up * 2.5f + Vector3.Cross(Vector3.up, f) * 2f, ohm.transform.position, 55f);
                }

                var guideGo = new GameObject("Guide");
                guideGo.transform.position = room + Vector3.forward * 3f;
                var guide = guideGo.AddComponent<GuideNPC>();
                Invoke(guide, "Awake");
                Shot("Outside_guide", guideGo.transform.position + new Vector3(2.5f, 1.5f, 2.5f), guideGo.transform.position + Vector3.up * 1f, 50f);
                Shot("Outside_guide_side", guideGo.transform.position + new Vector3(3.5f, 1.2f, 0f), guideGo.transform.position + Vector3.up * 1f, 50f);
                log.AppendLine($"Guide at {guideGo.transform.position}, children: {string.Join(", ", guideGo.GetComponentsInChildren<Transform>().Select(t => t.name + Fmt(t.localEulerAngles)))}");
            }
        }

        private static void ShotFacing(string name, Transform board)
        {
            float s = board.lossyScale.x;
            Vector3 front = -board.forward;
            Shot(name, board.position + front * (3.2f * s) + Vector3.up * 0.1f, board.position, 50f);
            log.AppendLine($"{name}: pos={board.position} scale={s:0.00} facing={front}");
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            try { m?.Invoke(target, null); }
            catch (Exception e) { log.AppendLine($"!! {target.GetType().Name}.{method}: {(e.InnerException ?? e).Message}"); }
        }

        private static void SetUiInstance()
        {
            UIManager ui = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            typeof(UIManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetSetMethod(true)?.Invoke(null, new object[] { ui });
        }

        private static void ShotOrtho(string name, Vector3 centre, float size)
        {
            Shot(name, centre + Vector3.up * 2.2f, centre, 60f, size);   // below the ceiling
        }

        private static void Shot(string name, Vector3 pos, Vector3 lookAt, float fov, float ortho = 0f)
        {
            var go = new GameObject("InspectCam");
            try
            {
                var cam = go.AddComponent<Camera>();
                go.transform.position = pos;
                go.transform.LookAt(lookAt);
                cam.fieldOfView = fov;
                var urp = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() ?? go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                urp.renderPostProcessing = true;
                if (ortho > 0f) { cam.orthographic = true; cam.orthographicSize = ortho; go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); }
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 400f;
                var rt = new RenderTexture(1280, 720, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
            catch (Exception e) { log.AppendLine($"!! shot {name}: {e.Message}"); }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
