using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatCircuit
{
    /// <summary>
    /// "Дядо Ампер" – the old village electrician who welcomes the player (village only).
    ///   First talk: who you are, follow the numbers 1 → 5 and the glowing arrow, the schematic on the wall, the keys.
    ///   Later talks: a hint for the next unsolved house (what it teaches + its colour), or the way to the end screen.
    /// Until the first talk was heard (PlayerPrefs 'cc_guide_intro') the objective is "Говори с Дядо Ампер".
    ///
    /// If the village scene has no guide, one is spawned a few metres in front of the start position
    /// (CatCircuit ▸ Create ▸ Village guide places a permanent one instead). The look is a copy of another
    /// village cat's model (tinted, with a miner's hat and lamp), or a simple figure if there is none.
    /// </summary>
    public class GuideNPC : CatInteractable
    {
        private const float SpawnDistance = 4f;

        private static readonly string[] Intro =
        {
            "Здравей, малки електротехнико!\nАз съм Дядо Ампер.",
            "Цял живот поправям жици и лампички\nв това село.",
            "В селото има пет къщи. Във всяка има развалена верига\nи съсед, който чака твоята помощ.",
            "Следвай номерата от 1 до 5.\nСветещата стрелка показва коя къща е следващата.",
            "Вътре на стената има схема на веригата.\nТя показва какво трябва да построиш.",
            "С E вземаш и поставяш части, с R ги завърташ,\nа с Q пускаш това, което носиш.",
            "Esc отваря менюто.\nХайде, започни от Къща 1!",
        };

        private static readonly string[] AllDone =
        {
            "Браво! Оправи и петте къщи!\nСега си истински електротехник.",
            "Натисни големия червен бутон в кметството (Къща 5),\nза да видиш края на играта и анкетата.",
        };

        [Tooltip("Build the hat/lamp look at runtime if this object has no renderers of its own.")]
        [SerializeField] private bool buildLook = true;

        private string[] activeLines;
        private int lineIndex;
        private bool introConversation;
        private Transform marker;

        // ------------------------------------------------------------------ spawning

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single || scene.buildIndex != 0) return;
            if (FindAnyObjectByType<GuideNPC>() != null) return;   // a permanent one was placed in the scene

            // Remember the player's start rotation now – PlayerSpawner moves the player later in Start.
            PlayerCarry player = FindAnyObjectByType<PlayerCarry>();
            PlayerSpawner spawner = FindAnyObjectByType<PlayerSpawner>();
            Vector3 start = spawner != null ? spawner.DefaultSpawnPosition : player != null ? player.transform.position : Vector3.zero;
            Vector3 forward = player != null ? player.transform.forward : Vector3.forward;

            var go = new GameObject(UIText.GuideName);
            go.SetActive(false);
            var guide = go.AddComponent<GuideNPC>();
            go.transform.position = FindSpot(start, forward, out Vector3 face);
            go.transform.rotation = Quaternion.LookRotation(face);
            go.SetActive(true);
            guide.interactPriority = 5;
        }

        /// <summary>A free spot ~4 m from the start (tries 8 directions), on the ground, facing the start.</summary>
        private static Vector3 FindSpot(Vector3 start, Vector3 forward, out Vector3 faceDir)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 eye = start + Vector3.up * 1f;
            Vector3 best = start + forward * SpawnDistance;
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i % 2 == 0 ? i * 22.5f : -(i + 1) * 22.5f, 0f) * forward;
                if (!Physics.Raycast(eye, dir, SpawnDistance + 1f, ~0, QueryTriggerInteraction.Ignore))
                {
                    best = start + dir * SpawnDistance;
                    break;
                }
            }
            best.y = WorldBuild.GroundY(best + Vector3.up, start.y);
            faceDir = start - best;
            faceDir.y = 0f;
            if (faceDir.sqrMagnitude < 0.01f) faceDir = -forward;
            return best;
        }

        // ------------------------------------------------------------------ setup

        private void Awake()
        {
            EnsureInteractionTrigger(2.2f);
            if (GetComponent<Collider>() == null)
            {
                var body = gameObject.AddComponent<CapsuleCollider>();   // so the cat can't walk through him
                body.center = new Vector3(0f, 0.6f, 0f);
                body.height = 1.2f;
                body.radius = 0.4f;
            }
            if (buildLook && GetComponentInChildren<Renderer>() == null) BuildLook();
        }

        private void Start()
        {
            HouseSigns.RefreshObjective();
        }

        private void Update()
        {
            if (marker == null) return;
            bool show = !HouseInfo.GuideIntroHeard && activeLines == null;
            if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
            if (show) marker.localPosition = new Vector3(0f, markerHeight + 0.12f * Mathf.Sin(Time.time * 4f), 0f);
        }

        // ------------------------------------------------------------------ talking

        public override int InteractPriority => activeLines != null ? 1000 : interactPriority;

        public override string GetPrompt(PlayerCarry player) =>
            activeLines != null ? UIText.DialogueNext : UIText.Talk(UIText.GuideName);

        public override void Interact(PlayerCarry player)
        {
            if (activeLines == null) StartConversation();
            else NextLine();
        }

        public override void OnPlayerExitRange(PlayerCarry player)
        {
            if (activeLines != null) EndConversation(false);   // walking away cancels (it restarts next time)
        }

        private void StartConversation()
        {
            introConversation = !HouseInfo.GuideIntroHeard;
            if (introConversation)
            {
                activeLines = Intro;
                Telemetry.Log("guide_talk", "intro");
            }
            else
            {
                int next = HouseInfo.NextUnsolved();
                activeLines = next > 0 ? HintFor(next) : AllDone;
                Telemetry.Log("guide_talk", next > 0 ? "hint=" + next : "all_done");
            }
            lineIndex = 0;
            ShowLine();
        }

        private static string[] HintFor(int house)
        {
            var lines = new List<string>();
            int solved = HouseInfo.SolvedCount();
            if (solved > 0) lines.Add($"Вече си оправил {solved} от {HouseInfo.Count} къщи.\nБраво, продължавай така!");
            lines.Add($"Следващата е Къща {house} – „{HouseInfo.Title(house)}“.\n{HouseInfo.Lesson(house)}");
            lines.Add($"Търси {HouseInfo.ColorName(house)} знак с номер {house}\nи светещата стрелка над вратата.");
            if (house == 1) lines.Add("Вътре погледни схемата на стената –\nтя показва как да свържеш частите.");
            return lines.ToArray();
        }

        private void NextLine()
        {
            lineIndex++;
            if (lineIndex >= activeLines.Length) EndConversation(true);
            else ShowLine();
        }

        private void ShowLine()
        {
            GameUI.Dialogue(UIText.GuideName, activeLines[lineIndex]);
            GameAudio.Play(Sfx.Blip, 1f, 0.12f);
        }

        private void EndConversation(bool completed)
        {
            activeLines = null;
            GameUI.HideDialogue();
            if (completed && introConversation)
            {
                PlayerPrefs.SetInt(HouseInfo.GuideIntroKey, 1);
                PlayerPrefs.Save();
                HouseSigns.RefreshObjective();
            }
        }

        /// <summary>True while a conversation with the guide is on screen.</summary>
        public bool IsTalking => activeLines != null;

        // ------------------------------------------------------------------ look

        private float markerHeight = 2.2f;

        private void BuildLook()
        {
            var model = new GameObject("Model").transform;
            model.SetParent(transform, false);

            float height = CloneVillageCat(model);
            if (height <= 0f) height = BuildSimpleCat(model);

            // Miner's hat with a little lamp – the village electrician.
            var hat = new GameObject("Hat").transform;
            hat.SetParent(model, false);
            hat.localPosition = new Vector3(0f, height, 0f);
            Material hatMat = WorldBuild.Unlit(new Color(0.85f, 0.65f, 0.15f));
            WorldBuild.Primitive(PrimitiveType.Cylinder, hat, new Vector3(0f, 0.02f, 0f), new Vector3(0.55f, 0.02f, 0.55f), hatMat, "Brim");
            WorldBuild.Primitive(PrimitiveType.Sphere, hat, new Vector3(0f, 0.1f, 0f), new Vector3(0.4f, 0.28f, 0.4f), hatMat, "Crown");
            WorldBuild.Primitive(PrimitiveType.Cylinder, hat, new Vector3(0f, 0.12f, 0.2f), new Vector3(0.12f, 0.03f, 0.12f),
                                 WorldBuild.Unlit(new Color(0.2f, 0.2f, 0.22f)), "LampBase").transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            WorldBuild.Primitive(PrimitiveType.Sphere, hat, new Vector3(0f, 0.12f, 0.24f), Vector3.one * 0.09f,
                                 WorldBuild.Unlit(new Color(1f, 0.95f, 0.6f)), "Lamp");

            // Name above the head + a bouncing "!" until the player has talked to him.
            var label = new GameObject("Label").transform;
            label.SetParent(transform, false);
            label.localPosition = new Vector3(0f, height + 0.55f, 0f);
            label.gameObject.AddComponent<Billboard>();
            TextMeshPro name = WorldBuild.Text(label, UIText.GuideName, 2.2f, new Color(1f, 0.85f, 0.35f), Vector3.zero, TextAlignmentOptions.Center, 4f);
            name.outlineWidth = 0.25f;
            name.outlineColor = new Color32(0, 0, 0, 255);

            markerHeight = height + 1.05f;
            marker = new GameObject("Marker").transform;
            marker.SetParent(transform, false);
            marker.localPosition = new Vector3(0f, markerHeight, 0f);
            marker.gameObject.AddComponent<Billboard>();
            TextMeshPro bang = WorldBuild.Text(marker, "!", 6f, new Color(1f, 0.8f, 0.2f), Vector3.zero, TextAlignmentOptions.Center, 1f);
            bang.fontStyle = FontStyles.Bold;
            bang.outlineWidth = 0.3f;
            bang.outlineColor = new Color32(0, 0, 0, 255);
        }

        /// <summary>Copies the model of another cat in the scene (renderers only). Returns its height, or 0.</summary>
        private float CloneVillageCat(Transform parent)
        {
            CatNPC source = null;
            foreach (CatNPC n in FindObjectsByType<CatNPC>(FindObjectsSortMode.None))
                if (n.GetComponentInChildren<Renderer>() != null) { source = n; break; }
            if (source == null) return 0f;

            // Copy under an inactive holder so no script on the copy wakes up, then strip everything but the looks.
            var holder = new GameObject("CloneHolder");
            holder.SetActive(false);
            GameObject copy = Instantiate(source.gameObject, holder.transform);
            foreach (TMP_Text t in copy.GetComponentsInChildren<TMP_Text>(true)) DestroyImmediate(t.gameObject);
            foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) DestroyImmediate(mb);
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (Rigidbody rb in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            foreach (Animator a in copy.GetComponentsInChildren<Animator>(true)) a.applyRootMotion = false;

            copy.transform.SetParent(parent, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = source.transform.lossyScale;
            Destroy(holder);

            // Grey-blue tint: an old cat, clearly not the same character.
            var tint = new Color(0.72f, 0.78f, 0.95f);
            var block = new MaterialPropertyBlock();
            float top = 0f;
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].HasProperty("_BaseColor")) continue;
                    r.GetPropertyBlock(block, i);
                    block.SetColor("_BaseColor", mats[i].GetColor("_BaseColor") * tint);
                    r.SetPropertyBlock(block, i);
                }
                top = Mathf.Max(top, r.bounds.max.y - transform.position.y);
            }
            return top > 0.2f ? top : 0f;
        }

        /// <summary>Fallback: a simple cat made of primitives. Returns its height.</summary>
        private static float BuildSimpleCat(Transform parent)
        {
            Material fur = WorldBuild.Unlit(new Color(0.55f, 0.58f, 0.68f));
            Material dark = WorldBuild.Unlit(new Color(0.15f, 0.15f, 0.18f));
            WorldBuild.Primitive(PrimitiveType.Capsule, parent, new Vector3(0f, 0.5f, 0f), new Vector3(0.6f, 0.5f, 0.6f), fur, "Body");
            WorldBuild.Primitive(PrimitiveType.Sphere, parent, new Vector3(0f, 1.15f, 0.05f), Vector3.one * 0.5f, fur, "Head");
            WorldBuild.Primitive(PrimitiveType.Cube, parent, new Vector3(-0.14f, 1.42f, 0.05f), new Vector3(0.1f, 0.18f, 0.06f), fur, "EarL");
            WorldBuild.Primitive(PrimitiveType.Cube, parent, new Vector3(0.14f, 1.42f, 0.05f), new Vector3(0.1f, 0.18f, 0.06f), fur, "EarR");
            WorldBuild.Primitive(PrimitiveType.Sphere, parent, new Vector3(-0.1f, 1.2f, 0.27f), Vector3.one * 0.07f, dark, "EyeL");
            WorldBuild.Primitive(PrimitiveType.Sphere, parent, new Vector3(0.1f, 1.2f, 0.27f), Vector3.one * 0.07f, dark, "EyeR");
            return 1.4f;
        }
    }
}
