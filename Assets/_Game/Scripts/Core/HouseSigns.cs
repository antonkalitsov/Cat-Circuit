using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatCircuit
{
    /// <summary>
    /// Village only (build index 0): a big numbered sign above every door that leads to House1..House5,
    /// so the player sees the order 1 → 5 and recognises each house by its colour.
    ///   • coloured disc with the number + short title (colours/titles in <see cref="HouseInfo"/>)
    ///   • a flag in the house colour next to the door
    ///   • solved house: green tick + "готово"
    ///   • NEXT unsolved house: pulsing halo, bouncing arrow and a tall light beam visible across the village
    /// Nothing is locked – teachers can still go in any order. Also sets the village objective
    /// ("Говори с Дядо Ампер" until the guide was heard, then "Следваща: Къща N – ...").
    /// Creates itself when the village loads – nothing to place in the scene.
    /// </summary>
    public class HouseSigns : MonoBehaviour
    {
        private class Sign
        {
            public int house;
            public Transform root, halo, arrow;
        }

        private readonly List<Sign> signs = new List<Sign>();
        private Sign next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single && scene.buildIndex == 0)
                new GameObject("HouseSigns").AddComponent<HouseSigns>();   // lives in the village scene only
        }

        /// <summary>Sets the village objective text (called on load and after the guide's intro).</summary>
        public static void RefreshObjective()
        {
            if (SceneManager.GetActiveScene().buildIndex != 0) return;
            int n = HouseInfo.NextUnsolved();
            if (!HouseInfo.GuideIntroHeard) GameUI.Objective(UIText.TalkToGuide);
            else if (n > 0) GameUI.Objective(UIText.NextHouse(n, HouseInfo.Title(n)));
            else GameUI.Objective(UIText.AllHousesDone);
        }

        private void Start()
        {
            int nextHouse = HouseInfo.NextUnsolved();
            var done = new HashSet<int>();
            foreach (SceneTransition door in FindObjectsByType<SceneTransition>(FindObjectsSortMode.None))
            {
                int house = HouseInfo.FromScene(door.TargetSceneName);
                if (house == 0 || done.Contains(house)) continue;   // one sign per house
                done.Add(house);
                Sign s = Build(door, house, house == nextHouse);
                signs.Add(s);
                if (house == nextHouse) next = s;
            }
            RefreshObjective();
        }

        private void Update()
        {
            if (next == null) return;
            float t = Time.time;
            if (next.halo != null)
            {
                float k = 1f + 0.12f * Mathf.Sin(t * 3f);
                next.halo.localScale = new Vector3(k, k, 1f);
            }
            if (next.arrow != null) next.arrow.localPosition = new Vector3(0f, 1.45f + 0.18f * Mathf.Sin(t * 4f), 0f);
        }

        // ------------------------------------------------------------------ building

        private Sign Build(SceneTransition door, int house, bool isNext)
        {
            Color color = HouseInfo.Color(house);
            bool solved = HouseInfo.IsSolved(house);

            // Where is the door? Use its collider (the trigger) if it has one.
            Vector3 basePos = door.transform.position;
            float top = basePos.y + 2f;
            Collider col = null;   // prefer the door's own (trigger) collider; ignore big ones like a whole house
            foreach (Collider c in door.GetComponentsInChildren<Collider>())
            {
                Vector3 s = c.bounds.size;
                if (Mathf.Max(s.x, s.z) > 6f) continue;
                if (col == null || (c.isTrigger && !col.isTrigger)) col = c;
            }
            if (col != null)
            {
                Bounds b = col.bounds;
                basePos = new Vector3(b.center.x, b.min.y, b.center.z);
                top = Mathf.Max(b.max.y, b.min.y + 2f);
            }
            basePos.y = WorldBuild.GroundY(basePos + Vector3.up, basePos.y, 1.2f);

            // "Outward" = towards where the player arrives when leaving the house.
            Vector3 outward = door.transform.forward;
            if (door.ArrivalPoint != null)
            {
                Vector3 d = door.ArrivalPoint.position - door.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) outward = d.normalized;
            }
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.forward;
            outward.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, outward);

            var root = new GameObject($"HouseSign_{house}").transform;
            root.SetParent(transform, false);
            root.position = new Vector3(basePos.x, top + 1.6f, basePos.z) + outward * 0.6f;

            // --- the sign itself (always faces the camera)
            var face = new GameObject("Face").transform;
            face.SetParent(root, false);
            face.gameObject.AddComponent<Billboard>();

            Transform halo = null;
            if (isNext)
            {
                halo = WorldBuild.Disc(face, new Vector3(0f, 0f, 0.06f), 0.95f, 0.02f, WorldBuild.Additive(new Color(color.r, color.g, color.b, 0.55f)), "Halo").transform;
                halo.localRotation = Quaternion.Euler(90f, 0f, 0f);
                // Disc() made a scaled child; wrap it so the pulse scales X/Y only.
                var haloPivot = new GameObject("HaloPivot").transform;
                haloPivot.SetParent(face, false);
                halo.SetParent(haloPivot, true);
                halo = haloPivot;
            }

            WorldBuild.Disc(face, new Vector3(0f, 0f, 0.02f), 0.72f, 0.06f, WorldBuild.Unlit(Color.Lerp(color, Color.black, 0.55f)), "Rim");
            WorldBuild.Disc(face, Vector3.zero, 0.62f, 0.08f, WorldBuild.Unlit(color), "Disc");
            TextMeshPro num = WorldBuild.Text(face, house.ToString(), 10f, new Color(0.08f, 0.08f, 0.1f), new Vector3(0f, -0.05f, -0.06f), TextAlignmentOptions.Center, 2f);
            num.fontStyle = FontStyles.Bold;

            string title = HouseInfo.Title(house);
            TextMeshPro label = WorldBuild.Text(face, solved ? $"{title}\n<color=#5CFF6E>готово</color>" : title,
                                                3.2f, Color.white, new Vector3(0f, -1.05f, -0.02f), TextAlignmentOptions.Top, 9f);
            label.richText = true;
            label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0.25f;
            label.outlineColor = new Color32(0, 0, 0, 255);

            if (solved) BuildTick(face);

            Transform arrow = null;
            if (isNext)
            {
                arrow = BuildArrow(face, color);
                // Tall soft beam straight up from the door, visible from far away.
                Vector3 beamPos = new Vector3(basePos.x, basePos.y, basePos.z) + outward * 0.6f;
                GameObject beam = WorldBuild.Primitive(PrimitiveType.Cylinder, root, Vector3.zero, new Vector3(0.9f, 18f, 0.9f),
                                                       WorldBuild.Additive(new Color(color.r, color.g, color.b, 0.22f)), "Beam");
                beam.transform.position = beamPos + Vector3.up * 18f;
            }

            // Flag beside the door, on whichever side is free (not inside a wall).
            Vector3 eye = basePos + Vector3.up * 1f + outward * 1.2f;
            Vector3 flagPos = basePos + side * 1.4f + outward * 1.2f;
            if (Physics.Linecast(eye, flagPos + Vector3.up * 1f, ~0, QueryTriggerInteraction.Ignore))
                flagPos = basePos - side * 1.4f + outward * 1.2f;
            BuildFlag(root, flagPos, basePos.y, color);

            return new Sign { house = house, root = root, halo = halo, arrow = arrow };
        }

        private static void BuildTick(Transform face)
        {
            var tick = new GameObject("Tick").transform;
            tick.SetParent(face, false);
            tick.localPosition = new Vector3(0.55f, 0.45f, -0.08f);
            WorldBuild.Disc(tick, new Vector3(0f, 0f, 0.02f), 0.32f, 0.04f, WorldBuild.Unlit(Color.white), "TickBack");
            Material green = WorldBuild.Unlit(new Color(0.15f, 0.75f, 0.25f));
            WorldBuild.Bar(tick, new Vector2(-0.16f, 0.0f), new Vector2(-0.04f, -0.13f), 0.07f, green, -0.03f, 0.02f);
            WorldBuild.Bar(tick, new Vector2(-0.04f, -0.13f), new Vector2(0.18f, 0.15f), 0.07f, green, -0.03f, 0.02f);
        }

        private static Transform BuildArrow(Transform face, Color color)
        {
            var arrow = new GameObject("Arrow").transform;
            arrow.SetParent(face, false);
            arrow.localPosition = new Vector3(0f, 1.45f, 0f);
            Material m = WorldBuild.Unlit(Color.Lerp(color, Color.white, 0.35f));
            WorldBuild.Bar(arrow, new Vector2(0f, 0.55f), new Vector2(0f, 0.05f), 0.16f, m, 0f, 0.08f);
            WorldBuild.Bar(arrow, new Vector2(-0.28f, 0.22f), new Vector2(0f, -0.06f), 0.16f, m, 0f, 0.08f);
            WorldBuild.Bar(arrow, new Vector2(0.28f, 0.22f), new Vector2(0f, -0.06f), 0.16f, m, 0f, 0.08f);
            return arrow;
        }

        private static void BuildFlag(Transform parent, Vector3 groundPos, float doorFloorY, Color color)
        {
            var flag = new GameObject("Flag").transform;
            flag.SetParent(parent, true);
            groundPos.y = WorldBuild.GroundY(new Vector3(groundPos.x, doorFloorY + 1f, groundPos.z), doorFloorY, 1.2f);
            flag.position = groundPos;
            WorldBuild.Primitive(PrimitiveType.Cylinder, flag, new Vector3(0f, 1.3f, 0f), new Vector3(0.07f, 1.3f, 0.07f),
                                 WorldBuild.Unlit(new Color(0.25f, 0.22f, 0.2f)), "Pole");
            WorldBuild.Primitive(PrimitiveType.Cube, flag, new Vector3(0.38f, 2.3f, 0f), new Vector3(0.7f, 0.45f, 0.03f),
                                 WorldBuild.Unlit(color), "Cloth");
        }
    }
}
