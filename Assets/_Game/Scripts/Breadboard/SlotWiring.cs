using System.Collections.Generic;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Works out, for one <see cref="BreadboardSlot"/>, where the board's wires touch it and which of the two
    /// contacts is "+":
    ///   • wires = objects named "...wire..." (or children of one) – long thin meshes on the board
    ///   • the contact axis = the wire that passes through / ends next to the slot
    ///   • "+" from the circuit text: an LED's long leg (and a resistor's first end) faces the part the current
    ///     comes FROM, a battery's + faces the part the current goes TO.
    /// Only geometry from the scene is read; nothing is changed.
    /// </summary>
    public static class SlotWiring
    {
        public struct Result
        {
            public bool found;
            public Vector3 plusLocal;      // slot-local, horizontal unit vector towards the + contact
            public float contactDistance;  // slot-local distance from the slot centre to a contact
        }

        private struct Segment { public Vector3 a, b; }

        public static Result Find(BreadboardSlot slot, bool battery)
        {
            var result = new Result();
            Transform st = slot.transform;
            Vector3 c = st.position;

            // 1. The wire direction next to the slot.
            Vector3 axis = Vector3.zero;
            float best = float.MaxValue, contact = 0.5f;
            foreach (Segment s in Wires(slot.gameObject))
            {
                Vector3 a = Flat(s.a - c), b = Flat(s.b - c);
                Vector3 dir = (b - a).normalized;
                float dEnd = Mathf.Min(a.magnitude, b.magnitude);
                float dLine = DistanceToSegment(Vector3.zero, a, b);
                // A wire ending at the slot, or one running underneath it.
                float score = dEnd < 1.0f ? dEnd : dLine < 0.35f ? dLine : float.MaxValue;
                if (score >= best) continue;
                best = score;
                axis = dir;   // the wire's own direction (not centre→end, wires are often a bit off-centre)
                contact = dEnd < 1.0f ? Mathf.Clamp(dEnd, 0.3f, 0.6f) : 0.45f;
            }
            if (axis == Vector3.zero) return result;

            Vector3 axisLocal = Flat(st.InverseTransformDirection(axis)).normalized;
            if (axisLocal == Vector3.zero) return result;
            // Boards are built on a grid: snap to the slot's own X / Z axis when close.
            foreach (Vector3 cardinal in new[] { Vector3.right, Vector3.forward })
                if (Mathf.Abs(Vector3.Dot(axisLocal, cardinal)) > 0.94f) axisLocal = cardinal * Mathf.Sign(Vector3.Dot(axisLocal, cardinal));
            axis = Flat(st.TransformDirection(axisLocal)).normalized;

            // 2. Which end is "+"? Ask the circuit who comes before / after this slot.
            float sign = PlusSign(slot, axis, battery, out bool known);
            result.found = true;
            result.plusLocal = axisLocal * (known ? sign : 1f);
            result.contactDistance = contact / Mathf.Max(0.0001f, st.lossyScale.x);
            return result;
        }

        /// <summary>+1 if the + contact is on the +axis side.</summary>
        private static float PlusSign(BreadboardSlot slot, Vector3 axis, bool battery, out bool known)
        {
            known = false;
            Breadboard board = slot.Board;
            if (board == null) return 1f;
            CircuitDefinition def = board.ParseCircuit();
            if (def == null) return 1f;

            foreach (ChainDef chain in def.chains)
            {
                for (int i = 0; i < chain.items.Count; i++)
                {
                    ChainItem it = chain.items[i];
                    if (!it.isPart || it.part == null || !string.Equals(it.part.id, slot.SlotId, System.StringComparison.OrdinalIgnoreCase)) continue;

                    // LED/resistor: + faces the previous element. Battery: + faces the next element.
                    Vector3? towards = Position(board, i > 0 ? chain.items[i - 1] : null);
                    Vector3? awayFrom = Position(board, i + 1 < chain.items.Count ? chain.items[i + 1] : null);
                    if (battery) { Vector3? t = towards; towards = awayFrom; awayFrom = t; }

                    Vector3 c = slot.transform.position;
                    float d = 0f;
                    if (towards.HasValue) d = Vector3.Dot(Flat(towards.Value - c), axis);
                    if (Mathf.Abs(d) < 0.2f && awayFrom.HasValue) d = -Vector3.Dot(Flat(awayFrom.Value - c), axis);
                    if (Mathf.Abs(d) < 0.05f) return 1f;
                    known = true;
                    return d > 0f ? 1f : -1f;
                }
            }
            return 1f;
        }

        /// <summary>World position of a chain element: another slot, or a part model of that kind near the board.</summary>
        private static Vector3? Position(Breadboard board, ChainItem item)
        {
            if (item == null) return null;
            if (item.isPart && item.part != null)
            {
                foreach (BreadboardSlot s in board.Slots)
                    if (s != null && string.Equals(s.SlotId, item.part.id, System.StringComparison.OrdinalIgnoreCase)) return s.transform.position;
                return NearestPart(board, item.part.kind);
            }
            // Terminal like "B+" / "B-": the battery.
            string label = item.label ?? "";
            if (label.EndsWith("+") || label.EndsWith("-"))
            {
                PartDef p = board.Definition != null ? board.Definition.Find(label.Substring(0, label.Length - 1)) : null;
                if (p == null) p = board.ParseCircuit().Find(label.Substring(0, label.Length - 1));
                if (p != null) return NearestPart(board, p.kind);
            }
            return null;
        }

        private static Vector3? NearestPart(Breadboard board, PartKind kind)
        {
            Vector3 c = board.transform.position;
            float best = 8f;
            Vector3? found = null;
            foreach (PickupableItem p in Object.FindObjectsByType<PickupableItem>(FindObjectsSortMode.None))
            {
                if (p.Data == null || p.Data.Kind != kind || p.CurrentState != PickupableItem.State.World) continue;
                float d = Vector3.Distance(p.transform.position, c);
                if (d < best) { best = d; found = p.transform.position; }
            }
            return found;
        }

        // ------------------------------------------------------------------ fixed batteries on the boards

        /// <summary>
        /// Turns the battery models lying on a board (not in a slot) so their + nub points along the wire towards
        /// the part the circuit's "B+" leads to – e.g. House 2: B+ → R1, so + faces the resistor.
        /// </summary>
        public static void AlignFixedBatteries(Breadboard board)
        {
            CircuitDefinition def = board.ParseCircuit();
            if (def == null || def.HasErrors) return;
            PartDef battery = null;
            foreach (PartDef p in def.OfKind(PartKind.Battery)) { battery = p; break; }
            if (battery == null) return;   // battery is a slot part (House 1) – the player places it

            // What does the current reach first after leaving "+"?
            Vector3? target = null;
            foreach (ChainDef chain in def.chains)
            {
                for (int i = 0; i + 1 < chain.items.Count && target == null; i++)
                {
                    ChainItem it = chain.items[i];
                    if (it.isPart || !string.Equals(it.label, battery.id + "+", System.StringComparison.OrdinalIgnoreCase)) continue;
                    for (int j = i + 1; j < chain.items.Count && target == null; j++)
                    {
                        ChainItem next = chain.items[j];
                        if (!next.isPart || next.part == null || next.part.kind == PartKind.Battery) continue;
                        target = Position(board, next);
                    }
                }
                if (target != null) break;
            }
            if (target == null) return;

            Vector3 bc = board.transform.position;
            foreach (PickupableItem item in Object.FindObjectsByType<PickupableItem>(FindObjectsSortMode.None))
            {
                if (item.Data == null || item.Data.Kind != PartKind.Battery || item.CurrentState != PickupableItem.State.World) continue;
                if (Flat(item.transform.position - bc).magnitude > 7f) continue;
                Vector3 plusNow = item.transform.TransformDirection(Vector3.up);   // the model's + nub is at +Y
                if (Mathf.Abs(plusNow.y) > 0.5f) continue;                          // standing up – leave it
                Vector3 centre = VisualCentre(item);
                if (!TryAxisAt(item.gameObject, centre, out Vector3 axis)) axis = Flat(plusNow).normalized;
                Vector3 want = axis * Mathf.Sign(Vector3.Dot(Flat(target.Value - centre), axis));
                if (want == Vector3.zero) continue;
                float angle = Vector3.SignedAngle(Flat(plusNow), want, Vector3.up);
                if (Mathf.Abs(angle) < 1f) continue;
                item.transform.RotateAround(centre, Vector3.up, angle);   // turn on the spot
                Debug.Log($"[SlotWiring] Turned battery '{item.name}' by {angle:0}° so its + faces the circuit's + side.");
            }
        }

        private static Vector3 VisualCentre(PickupableItem item)
        {
            Bounds b = default; bool any = false;
            foreach (Renderer r in item.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b.center : item.transform.position;
        }

        /// <summary>Direction of the board wire that runs under / ends next to a point (horizontal, unit).</summary>
        public static bool TryAxisAt(GameObject inScene, Vector3 c, out Vector3 axis)
        {
            axis = Vector3.zero;
            float best = float.MaxValue;
            foreach (Segment s in Wires(inScene))
            {
                Vector3 a = Flat(s.a - c), b = Flat(s.b - c);
                float dEnd = Mathf.Min(a.magnitude, b.magnitude);
                float dLine = DistanceToSegment(Vector3.zero, a, b);
                float score = dLine < 0.4f ? dLine : dEnd < 1.0f ? dEnd : float.MaxValue;
                if (score >= best) continue;
                best = score;
                axis = (b - a).normalized;
            }
            if (axis == Vector3.zero) return false;
            if (Mathf.Abs(axis.x) > 0.94f) axis = new Vector3(Mathf.Sign(axis.x), 0f, 0f);
            else if (Mathf.Abs(axis.z) > 0.94f) axis = new Vector3(0f, 0f, Mathf.Sign(axis.z));
            return true;
        }

        // ------------------------------------------------------------------ wires in the scene

        private static List<Segment> cache;
        private static int cacheFrame = -1, cacheScene;

        private static List<Segment> Wires(GameObject inScene)
        {
            int scene = inScene.scene.handle;
            if (cache != null && cacheFrame == Time.frameCount && cacheScene == scene) return cache;
            cache = new List<Segment>();
            cacheFrame = Time.frameCount;
            cacheScene = scene;
            foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (mf.sharedMesh == null || !IsWire(mf.transform)) continue;
                Bounds lb = mf.sharedMesh.bounds;
                Vector3 e = lb.extents;
                Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right * e.x : e.y >= e.z ? Vector3.up * e.y : Vector3.forward * e.z;
                Vector3 a = mf.transform.TransformPoint(lb.center + axis), b = mf.transform.TransformPoint(lb.center - axis);
                if (Flat(a - b).magnitude < 0.1f) continue;   // not a horizontal wire
                cache.Add(new Segment { a = a, b = b });
            }
            return cache;
        }

        private static bool IsWire(Transform t)
        {
            for (int i = 0; i < 2 && t != null; i++, t = t.parent)
                if (t.name.IndexOf("wire", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }
    }
}
