using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CatCircuit
{
    /// <summary>
    /// Makes the house rooms (House1..House5) cosier, built at runtime so no scene is edited
    /// (delete this file to undo everything):
    ///   • a warm hanging lamp with a real point light (no shadows – cheap on the web)
    ///   • a rug in the house colour under the puzzle board
    ///   • a few small posters about U / I / R and potted plants in free corners
    ///   • gentle post-processing: bloom (so lit LEDs glow), a little warmth and a soft vignette
    /// Everything avoids doors, the wall schematic and existing furniture (free-space checks).
    /// </summary>
    public class InteriorDecor : MonoBehaviour
    {
        private int house;
        private Color houseColor;
        private float k = 1f;   // decoration size: the houses are big halls for a small cat, so things grow with the ceiling height

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            int h = HouseInfo.FromScene(scene.name);
            if (h == 0) return;
            new GameObject("InteriorDecor").AddComponent<InteriorDecor>().house = h;
        }

        private IEnumerator Start()
        {
            yield return null;   // let the wall schematic place itself first
            Build();
        }

        /// <summary>Builds everything (also called by the editor inspector tool).</summary>
        public void Build(int houseNumber = 0)
        {
            if (houseNumber > 0) house = houseNumber;
            houseColor = HouseInfo.Color(house);

            CircuitHost host = FindAnyObjectByType<CircuitHost>();
            Vector3 centre = host != null ? host.transform.position : Vector3.zero;
            // Floor height from the open floor where the player starts (not the board's top surface).
            PlayerSpawner spawner = FindAnyObjectByType<PlayerSpawner>();
            Vector3 open = spawner != null ? spawner.DefaultSpawnPosition : centre;
            float floor = WorldBuild.GroundY(open + Vector3.up * 0.5f, centre.y - 0.15f, 1f);
            Vector3 eye = new Vector3(centre.x, floor + 1.5f, centre.z);

            // Room box from the walls around the board.
            float minX = Hit(eye, Vector3.left, centre.x - 6f).x, maxX = Hit(eye, Vector3.right, centre.x + 6f).x;
            float minZ = Hit(eye, Vector3.back, centre.z - 6f).z, maxZ = Hit(eye, Vector3.forward, centre.z + 6f).z;
            float ceiling = Physics.Raycast(eye, Vector3.up, out RaycastHit up, 15f, ~0, QueryTriggerInteraction.Ignore) ? up.point.y : floor + 3.5f;
            var room = new Vector3((minX + maxX) / 2f, floor, (minZ + maxZ) / 2f);

            Debug.Log($"[InteriorDecor] House {house}: floor={floor:0.00} ceiling={ceiling:0.00} room x {minX:0.0}..{maxX:0.0} z {minZ:0.0}..{maxZ:0.0}");
            k = Mathf.Clamp((ceiling - floor) / 3.2f, 1f, 4f);
            BuildRug(host, floor);
            BuildLamp(new Vector3(room.x, ceiling, room.z), ceiling - floor, Mathf.Max(maxX - minX, maxZ - minZ));
            BuildPlants(minX, maxX, minZ, maxZ, floor);
            BuildPosters(eye, floor);
            BuildPostProcessing();
        }

        private static Vector3 Hit(Vector3 from, Vector3 dir, float fallback)
        {
            if (Physics.Raycast(from, dir, out RaycastHit h, 25f, ~0, QueryTriggerInteraction.Ignore)) return h.point;
            return from + dir * Mathf.Abs(fallback - Vector3.Dot(from, dir));
        }

        // ------------------------------------------------------------------ pieces

        private void BuildRug(CircuitHost host, float floor)
        {
            if (host == null) return;
            Bounds b = default; bool any = false;
            foreach (Renderer r in host.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return;
            Vector3 size = new Vector3(b.size.x + 1.4f * k, 0.01f, b.size.z + 1.4f * k);
            var rug = new GameObject("Rug").transform;
            rug.SetParent(transform, false);
            rug.position = new Vector3(b.center.x, floor + 0.006f, b.center.z);
            WorldBuild.Primitive(PrimitiveType.Cube, rug, Vector3.zero, size, WorldBuild.Lit(Color.Lerp(houseColor, Color.black, 0.45f)), "Border");
            WorldBuild.Primitive(PrimitiveType.Cube, rug, new Vector3(0f, 0.004f, 0f), new Vector3(size.x - 0.4f * k, 0.01f, size.z - 0.4f * k),
                                 WorldBuild.Lit(Color.Lerp(houseColor, new Color(0.95f, 0.9f, 0.8f), 0.6f)), "Inner");
        }

        private void BuildLamp(Vector3 ceilingPoint, float roomHeight, float roomSize)
        {
            var lamp = new GameObject("CeilingLamp").transform;
            lamp.SetParent(transform, false);
            lamp.position = ceilingPoint;
            lamp.localScale = Vector3.one * k;
            float drop = Mathf.Clamp(roomHeight * 0.35f / k, 0.5f, 1.4f);
            Material dark = WorldBuild.Lit(new Color(0.15f, 0.13f, 0.12f));
            WorldBuild.Primitive(PrimitiveType.Cylinder, lamp, new Vector3(0f, -drop / 2f, 0f), new Vector3(0.03f, drop / 2f, 0.03f), dark, "Cord");
            WorldBuild.Primitive(PrimitiveType.Cylinder, lamp, new Vector3(0f, -drop, 0f), new Vector3(1.1f, 0.16f, 1.1f), WorldBuild.Lit(Color.Lerp(houseColor, Color.white, 0.2f)), "Shade");
            WorldBuild.Primitive(PrimitiveType.Sphere, lamp, new Vector3(0f, -drop - 0.12f, 0f), Vector3.one * 0.36f, WorldBuild.Unlit(new Color(1f, 0.93f, 0.75f)), "Bulb");

            var lightGo = new GameObject("LampLight");
            lightGo.transform.SetParent(lamp, false);
            lightGo.transform.localPosition = new Vector3(0f, -drop - 0.3f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.82f, 0.6f);
            l.range = Mathf.Clamp(roomSize * 0.8f, 6f, 40f);
            l.intensity = 2.5f + 1.5f * k;
            l.shadows = LightShadows.None;
        }

        private void BuildPlants(float minX, float maxX, float minZ, float maxZ, float floor)
        {
            Material pot = WorldBuild.Lit(new Color(0.62f, 0.33f, 0.2f));
            Material leaf = WorldBuild.Lit(new Color(0.25f, 0.6f, 0.28f));
            float inset = 0.8f * k;
            foreach (var (x, z) in new[] { (minX + inset, minZ + inset), (maxX - inset, maxZ - inset), (minX + inset, maxZ - inset), (maxX - inset, minZ + inset) })
            {
                var p = new Vector3(x, floor, z);
                if (NearDoor(p, 2f) || Physics.CheckBox(p + Vector3.up * 0.8f * k, new Vector3(0.45f, 0.6f, 0.45f) * k, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                var plant = new GameObject("Plant").transform;
                plant.SetParent(transform, false);
                plant.position = p;
                plant.localScale = Vector3.one * k;
                WorldBuild.Primitive(PrimitiveType.Cylinder, plant, new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.3f, 0.6f), pot, "Pot");
                WorldBuild.Primitive(PrimitiveType.Sphere, plant, new Vector3(0f, 1.0f, 0f), new Vector3(1.0f, 1.1f, 1.0f), leaf, "Leaves");
                WorldBuild.Primitive(PrimitiveType.Sphere, plant, new Vector3(0.18f, 1.5f, 0.08f), new Vector3(0.65f, 0.7f, 0.65f), leaf, "Leaves2");
            }
        }

        /// <summary>Small framed posters (U in V, I in A, R in Ω) on free wall spots away from doors and the schematic.</summary>
        private void BuildPosters(Vector3 eye, float floor)
        {
            string[][] posters =
            {
                new[] { "U", "напрежение\nволт (V)" },
                new[] { "I", "ток\nампер (A)" },
                new[] { "R", "съпротивление\nом (Ω)" },
            };
            Transform schematic = GameObject.Find("WallSchematic")?.transform;
            int placed = 0;
            for (int i = 0; i < 8 && placed < posters.Length; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, 22.5f + i * 45f, 0f) * Vector3.forward;
                if (!Physics.Raycast(eye, dir, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.normal.y) > 0.3f) continue;
                Vector3 pos = new Vector3(hit.point.x, floor + 1.9f * k, hit.point.z);
                if (NearDoor(pos, 2.2f * k)) continue;
                if (schematic != null && Vector3.Distance(pos, schematic.position) < 2.5f + 2.4f * schematic.lossyScale.x / 2f) continue;
                if (!FlatWall(hit, 1.5f * k)) continue;

                Vector3 n = hit.normal; n.y = 0f; n.Normalize();
                var poster = new GameObject("Poster").transform;
                poster.SetParent(transform, false);
                poster.position = pos + n * 0.03f;
                poster.rotation = Quaternion.LookRotation(-n);
                poster.localScale = Vector3.one * k;
                WorldBuild.Primitive(PrimitiveType.Cube, poster, Vector3.zero, new Vector3(1.3f, 1.7f, 0.03f), WorldBuild.Lit(Color.Lerp(houseColor, Color.black, 0.4f)), "Frame");
                WorldBuild.Primitive(PrimitiveType.Cube, poster, new Vector3(0f, 0f, -0.01f), new Vector3(1.18f, 1.58f, 0.03f), WorldBuild.Lit(new Color(0.96f, 0.93f, 0.85f)), "Paper");
                TextMeshPro big = WorldBuild.Text(poster, posters[placed][0], 8f, Color.Lerp(houseColor, Color.black, 0.25f), new Vector3(0f, 0.28f, -0.03f), TextAlignmentOptions.Center, 1.1f);
                big.fontStyle = FontStyles.Bold;
                WorldBuild.Text(poster, posters[placed][1], 1.7f, new Color(0.2f, 0.2f, 0.22f), new Vector3(0f, -0.45f, -0.03f), TextAlignmentOptions.Center, 1.1f);
                placed++;
            }
        }

        private static bool FlatWall(RaycastHit hit, float width)
        {
            Vector3 n = hit.normal; n.y = 0f; n.Normalize();
            Vector3 along = Vector3.Cross(Vector3.up, n);
            foreach (float s in new[] { -0.5f, 0.5f })
            {
                Vector3 from = hit.point + n * 0.4f + along * (s * width) + Vector3.up * 0.3f;
                if (!Physics.Raycast(from, -n, out RaycastHit h2, 0.8f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(h2.distance - 0.4f) > 0.12f) return false;
            }
            return true;
        }

        private static bool NearDoor(Vector3 p, float radius)
        {
            foreach (SceneTransition door in FindObjectsByType<SceneTransition>(FindObjectsSortMode.None))
            {
                Vector3 d = door.transform.position - p;
                d.y = 0f;
                if (d.sqrMagnitude < radius * radius) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ post-processing

        private void BuildPostProcessing()
        {
            var go = new GameObject("CosyVolume");
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.6f);
            bloom.scatter.Override(0.6f);

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);

            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(8f);
            color.colorFilter.Override(new Color(1f, 0.97f, 0.92f));

            // Post-processing must be switched on for the camera.
            foreach (Camera cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data != null) data.renderPostProcessing = true;
            }
        }
    }
}
