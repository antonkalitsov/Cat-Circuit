using TMPro;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Shows a red "+" and a dark "−" next to a <see cref="BreadboardSlot"/> while a polar part (LED, battery)
    /// sits in it or the cat carries one that fits. The "+" is where the part's + end must point:
    /// the LED's long leg (anode) or the battery's + nub. So what the player sees always matches what lights up.
    /// Added automatically by BreadboardSlot – nothing to set up.
    /// </summary>
    public class SlotPolarityMarker : MonoBehaviour
    {
        private BreadboardSlot slot;
        private PlayerCarry player;
        private Transform plus, minus;
        private ComponentData shownFor;
        private float shownAngle = float.NaN;
        private bool shownPlaced;

        /// <summary>+1 / −1: side (local X) of the LED model's long leg. Found from the mesh when it is readable.</summary>
        private static float ledAnodeSign;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ledAnodeSign = 0f;

        public void Init(BreadboardSlot owner) => slot = owner;

        private void Update()
        {
            if (slot == null) return;
            if (player == null) player = FindAnyObjectByType<PlayerCarry>();

            ComponentData data = null;
            if (slot.PlacedItem != null) data = slot.PlacedItem.Data;
            else if (player != null && player.IsHoldingItem && slot.Accepts(player.HeldComponentData)) data = player.HeldComponentData;

            bool show = BreadboardSlot.IsPolar(data);
            if (!show)
            {
                if (plus != null && plus.gameObject.activeSelf) { plus.gameObject.SetActive(false); minus.gameObject.SetActive(false); }
                return;
            }

            if (plus == null) Build();
            float angle = slot.ReferenceAngle(data);
            bool placed = slot.PlacedItem != null;
            if (data != shownFor || placed != shownPlaced || !Mathf.Approximately(angle, shownAngle) || !plus.gameObject.activeSelf)
            {
                shownFor = data;
                shownPlaced = placed;
                shownAngle = angle;
                Position(data, angle);
            }
        }

        private void Build()
        {
            plus = MakeMark("+", new Color(0.95f, 0.2f, 0.15f));
            minus = MakeMark("−", new Color(0.25f, 0.45f, 1f));
        }

        private Transform MakeMark(string sign, Color color)
        {
            var root = new GameObject("Polarity " + sign).transform;
            root.SetParent(null, false);
            root.gameObject.AddComponent<Billboard>();
            WorldBuild.Disc(root, new Vector3(0f, 0f, 0.01f), 0.13f, 0.02f, WorldBuild.Unlit(Color.white), "Back");
            WorldBuild.Disc(root, Vector3.zero, 0.11f, 0.03f, WorldBuild.Unlit(color), "Disc");
            TextMeshPro t = WorldBuild.Text(root, sign, 2.6f, Color.white, new Vector3(0f, -0.02f, -0.03f), TextAlignmentOptions.Center, 0.3f);
            t.fontStyle = FontStyles.Bold;
            root.SetParent(transform, true);
            root.localScale = Vector3.one;
            SetWorldScale(root, 1f);
            return root;
        }

        private static void SetWorldScale(Transform t, float s)
        {
            Vector3 p = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(s / Mathf.Max(0.0001f, Mathf.Abs(p.x)), s / Mathf.Max(0.0001f, Mathf.Abs(p.y)), s / Mathf.Max(0.0001f, Mathf.Abs(p.z)));
        }

        /// <summary>Puts "+" where the part's + end points at the correct angle, "−" opposite.</summary>
        private void Position(ComponentData data, float angle)
        {
            Vector3 plusLocal = PlusDirection(data);
            Vector3 rot = data.defaultSlotRotation;
            Vector3 dirSlot = Quaternion.Euler(rot.x, angle, rot.z) * plusLocal;
            Vector3 dirWorld = slot.transform.TransformDirection(dirSlot);
            dirWorld.y = 0f;
            if (dirWorld.sqrMagnitude < 0.0001f) dirWorld = slot.transform.right;
            dirWorld.Normalize();

            float reach = 0.45f;
            float height = 0.35f;
            if (slot.PlacedItem != null)
            {
                Bounds b = default;
                bool any = false;
                foreach (Renderer r in slot.PlacedItem.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (any)
                {
                    reach = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(dirWorld.x), 0f, Mathf.Abs(dirWorld.z)))) + 0.18f;
                    height = b.max.y - slot.transform.position.y + 0.2f;
                }
            }

            Vector3 c = slot.transform.position;
            plus.position = c + dirWorld * reach + Vector3.up * height;
            minus.position = c - dirWorld * reach + Vector3.up * height;
            plus.gameObject.SetActive(true);
            minus.gameObject.SetActive(true);
        }

        /// <summary>Direction of the part's + end in its own model space.</summary>
        private Vector3 PlusDirection(ComponentData data)
        {
            if (data.Kind == PartKind.Battery) return Vector3.up;   // the battery model's + nub is at the top (+Y)
            if (ledAnodeSign == 0f && slot.PlacedItem != null) ledAnodeSign = DetectLedAnodeSign(slot.PlacedItem);
            return Vector3.right * (ledAnodeSign == 0f ? 1f : ledAnodeSign);
        }

        /// <summary>
        /// The LED model's legs are spread along X; the long leg (anode) reaches lowest. Read it from the mesh if
        /// possible; otherwise +X (Unity mirrors X when importing the .obj, where the long leg is at −X).
        /// </summary>
        private static float DetectLedAnodeSign(PickupableItem led)
        {
            if (led == null) return 0f;
            float lowestY = float.MaxValue, sideX = 0f;
            foreach (MeshFilter mf in led.GetComponentsInChildren<MeshFilter>())
            {
                Mesh m = mf.sharedMesh;
                if (m == null || !m.isReadable) continue;
                foreach (Vector3 v in m.vertices)
                {
                    Vector3 local = led.transform.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (local.y < lowestY) { lowestY = local.y; sideX = local.x; }
                }
            }
            if (lowestY == float.MaxValue) return 1f;
            float sign = sideX >= 0f ? 1f : -1f;
            Debug.Log($"[SlotPolarityMarker] LED long leg found at local {(sign > 0 ? "+X" : "-X")}.");
            return sign;
        }
    }
}
