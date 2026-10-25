using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Turns circuit CODE into a real circuit on the floor and simulates it.
    /// The player types the code in a <see cref="CodeTerminal"/>; this component
    ///   1. parses it (CircuitScript),
    ///   2. lays the parts out on the floor (rows = chains of the code) and draws the wires,
    ///   3. simulates it – LEDs glow, burn or stay dark, wires glow where current flows.
    /// Put a <see cref="CircuitPuzzle"/> next to it to give the player goals.
    /// </summary>
    [AddComponentMenu("CatCircuit/Code Circuit Builder")]
    public class CodeCircuitBuilder : CircuitHost
    {
        public const string ExampleCode =
@"# Моята първа верига
battery B 9V
resistor R1 330
led L1 red

B+ -> R1 -> L1 -> B-";

        [Header("Code")]
        [TextArea(6, 30)]
        [SerializeField] private string code = ExampleCode;
        [Tooltip("Build the code above when the scene starts.")]
        [SerializeField] private bool buildOnStart = false;

        [Header("Part models")]
        [Tooltip("Models for batteries / resistors / LEDs. For resistors the one with the closest value is used. Empty = simple shapes.")]
        [SerializeField] private List<ComponentData> partLibrary = new List<ComponentData>();
        [SerializeField] private float partScale = 1f;

        [Header("Layout")]
        [SerializeField] private float spacing = 1.4f;
        [SerializeField] private float rowSpacing = 1.6f;
        [SerializeField] private bool snapToFloor = true;
        [SerializeField] private float floorOffset = 0.03f;

        [Header("Board under the circuit")]
        [Tooltip("Draw a breadboard under the parts so the circuit is easy to see.")]
        [SerializeField] private bool showBoard = true;
        [Tooltip("Optional. Empty = a breadboard look is generated.")]
        [SerializeField] private Material boardMaterial;
        [SerializeField] private float boardMargin = 0.8f;
        [SerializeField] private float boardThickness = 0.06f;

        [Header("Wires")]
        [Tooltip("Optional. Empty = a simple unlit material is created.")]
        [SerializeField] private Material wireMaterial;
        [SerializeField] private float wireThickness = 0.09f;
        [SerializeField] private Color wireIdleColor = new Color(0.25f, 0.25f, 0.28f);
        [SerializeField] private Color wireActiveColor = new Color(1f, 0.85f, 0.2f);

        [Header("Labels")]
        [SerializeField] private bool showLabels = true;
        [SerializeField] private float labelFontSize = 3f;

        public string Code { get => code; set => code = value; }
        public List<ComponentData> PartLibrary => partLibrary;
        /// <summary>Result of the last Build() call, including errors (Definition only changes on success).</summary>
        public CircuitDefinition LastParse { get; private set; }
        public event Action<CodeCircuitBuilder, bool> Built;

        private CircuitDefinition built;
        private Transform root;
        private readonly Dictionary<string, bool> switchState = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CircuitSwitch> switchVisuals = new Dictionary<string, CircuitSwitch>(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<Renderer, int>> wires = new List<KeyValuePair<Renderer, int>>();
        private Material runtimeWireMaterial;
        private Material runtimeBoardMaterial;
        private float surfaceY;   // local height of the top of the board

        /// <summary>Where the "circuit complete" sparkles appear.</summary>
        public override Vector3 EffectCenter => root != null ? root.position + Vector3.up * 0.3f : transform.position;

        /// <summary>Edit-mode setup used by editor tools.</summary>
        public void Configure(string startCode, List<ComponentData> library, Material wires)
        {
            code = startCode;
            partLibrary = library ?? new List<ComponentData>();
            wireMaterial = wires;
        }

        protected override void Start()
        {
            if (buildOnStart) Build(code);
            else base.Start();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Parse + build + simulate. Returns false (and keeps the old circuit) if the code has errors.</summary>
        public bool Build(string newCode)
        {
            code = newCode ?? "";
            CircuitDefinition def = CircuitScript.Parse(code, false);
            LastParse = def;

            if (def.HasErrors)
            {
                Built?.Invoke(this, false);
                return false;
            }

            built = def;
            Definition = def;
            switchState.Clear();
            foreach (PartDef p in def.OfKind(PartKind.Switch)) switchState[p.id] = p.closed;

            RebuildVisuals();
            SolveNow();
            Built?.Invoke(this, true);
            return true;
        }

        public void Clear()
        {
            built = null;
            Definition = null;
            if (root != null) Destroy(root.gameObject);
            root = null;
            wires.Clear();
            switchVisuals.Clear();
            SolveNow();
        }

        public bool IsSwitchClosed(string id) => switchState.TryGetValue(id, out bool c) && c;

        public void ToggleSwitch(string id)
        {
            if (!switchState.ContainsKey(id)) return;
            switchState[id] = !switchState[id];
            if (switchVisuals.TryGetValue(id, out CircuitSwitch sw) && sw != null) sw.ShowState(switchState[id]);
            SolveNow();
        }

        // ------------------------------------------------------------------ simulation

        protected override bool BuildSimulation(List<SimPart> parts, out int nodeCount)
        {
            if (built == null) { nodeCount = 0; return true; }   // nothing built yet = valid, empty circuit

            nodeCount = built.NodeCount;
            foreach (PartDef p in built.parts)
            {
                SimPart sp = FromDefinition(p);
                if (p.kind == PartKind.Switch) sp.conducting = IsSwitchClosed(p.id);
                parts.Add(sp);
            }
            return true;
        }

        protected override void OnSolved(CircuitResult result)
        {
            if (built == null || wires.Count == 0) return;

            // Wire glow: strongest current of any part touching that node.
            var nodeCurrent = new Dictionary<int, float>();
            foreach (PartDef p in built.parts)
            {
                PartResult r = result.Get(p.id);
                float i = r != null ? Mathf.Abs(r.current) : 0f;
                Bump(nodeCurrent, p.nodeA, i);
                Bump(nodeCurrent, p.nodeB, i);
            }

            foreach (var w in wires)
            {
                if (w.Key == null) continue;
                nodeCurrent.TryGetValue(w.Value, out float i);
                float t = Mathf.Clamp01(i / CircuitConstants.LedFullBrightnessCurrent);
                Color c = result.shortCircuit ? Color.red : Color.Lerp(wireIdleColor, wireActiveColor, t);
                SetWireColor(w.Key, c);
            }
        }

        private static void Bump(Dictionary<int, float> d, int node, float value)
        {
            d.TryGetValue(node, out float old);
            d[node] = Mathf.Max(old, value);
        }

        // ------------------------------------------------------------------ building the visuals

        private void RebuildVisuals()
        {
            if (root != null) Destroy(root.gameObject);
            wires.Clear();
            switchVisuals.Clear();

            root = new GameObject("Built Circuit").transform;
            root.SetParent(transform, false);
            if (snapToFloor) root.position = FloorPoint(transform.position);
            surfaceY = showBoard ? boardThickness : 0f;

            // Parts are spawned under an inactive holder first so their Awake() (pickup logic) never runs.
            var holder = new GameObject("spawn-holder");
            holder.SetActive(false);

            var partPos = new Dictionary<PartDef, Vector3>();
            var vertical = new HashSet<PartDef>();
            var nodePoints = new Dictionary<int, List<Vector3>>();
            var markers = new List<KeyValuePair<Vector3, string>>();
            var markerNodes = new List<int>();

            // 1) every chain is a row
            for (int row = 0; row < built.chains.Count; row++)
            {
                ChainDef chain = built.chains[row];
                for (int col = 0; col < chain.items.Count; col++)
                {
                    ChainItem item = chain.items[col];
                    var pos = new Vector3(col * spacing, 0f, -row * rowSpacing);
                    if (item.isPart) partPos[item.part] = pos;
                    else
                    {
                        markers.Add(new KeyValuePair<Vector3, string>(pos, item.label));
                        markerNodes.Add(item.node);
                    }
                }
            }

            // 2) parts only used through their terminals (e.g. the battery in "B+ -> ... -> B-") go in a column on the left
            int k = 0;
            foreach (PartDef p in built.parts)
            {
                if (partPos.ContainsKey(p)) continue;
                // centred next to the rows (a single battery sits halfway between the first and the last line)
                float middle = -(Mathf.Max(1, built.chains.Count) - 1) * rowSpacing * 0.5f;
                partPos[p] = new Vector3(-1.5f * spacing, 0f, middle - k * rowSpacing);
                vertical.Add(p);
                k++;
            }

            // 3) centre everything around the builder (with room for the side wires and the top / bottom rails)
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (Vector3 v in partPos.Values) { if (first) { bounds = new Bounds(v, Vector3.zero); first = false; } else bounds.Encapsulate(v); }
            foreach (var m in markers) bounds.Encapsulate(m.Key);
            Vector3 shift = new Vector3(-bounds.center.x, surfaceY, -bounds.center.z);
            if (showBoard) CreateBoard(bounds.size + new Vector3(spacing * 1.4f, 0f, rowSpacing * 1.4f));

            // 4) spawn parts, turned so their + side matches the circuit, and find their REAL ends
            float half = spacing * 0.3f;
            var terminals = new List<Vector3>();
            var partEnds = new Dictionary<PartDef, KeyValuePair<Vector3, Vector3>>();
            foreach (var kv in partPos)
            {
                PartDef p = kv.Key;
                Vector3 pos = kv.Value + shift;
                bool isVertical = vertical.Contains(p);
                Vector3 dir = isVertical ? Vector3.back : Vector3.right;   // from A (entry) to B (exit)
                // Vertical batteries: "+" on top (further away), so current visibly flows "down" the rows.
                if (isVertical && p.kind == PartKind.Battery) dir = Vector3.forward;

                GameObject go = SpawnPart(p, pos, dir, holder.transform);
                float reach = TerminalReach(go, dir, half);
                Vector3 a = pos - dir * reach, b = pos + dir * reach;
                partEnds[p] = new KeyValuePair<Vector3, Vector3>(a, b);
                terminals.Add(a);
                terminals.Add(b);
            }

            // 5) chain points (junctions / terminal references) with a dot and a label
            for (int i = 0; i < markers.Count; i++)
            {
                Vector3 pos = markers[i].Key + shift;
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediateSafe(dot.GetComponent<Collider>());
                dot.name = "Point " + markers[i].Value;
                dot.transform.SetParent(root, false);
                dot.transform.localPosition = pos;
                dot.transform.localScale = Vector3.one * Mathf.Max(0.14f, wireThickness * 1.8f);
                Tint(dot, new Color(0.15f, 0.15f, 0.15f));
                if (showLabels) CreateLabel(markers[i].Value, pos + new Vector3(0f, 0.01f, 0.3f), 0.8f);
            }

            // 6) wires, drawn like a real schematic
            RouteWires(shift, partEnds, vertical);

            // 7) a small solder dot where every wire meets a part
            foreach (Vector3 t in terminals) SolderDot(t);

            DestroyImmediateSafe(holder);
        }

        private static void AddPoint(Dictionary<int, List<Vector3>> d, int node, Vector3 p)
        {
            if (node < 0) return;
            if (!d.TryGetValue(node, out List<Vector3> list)) d[node] = list = new List<Vector3>();
            list.Add(p);
        }

        /// <summary>
        /// Spawns the model lying along <paramref name="dir"/> (A → B) with its + side the right way:
        /// battery + nub towards B (the "+" terminal), LED long leg (anode) towards A where the current enters.
        /// The model is centred on its visual middle (model pivots are often at one end).
        /// </summary>
        private GameObject SpawnPart(PartDef p, Vector3 localPos, Vector3 dir, Transform inactiveHolder)
        {
            ComponentData data = PickModel(p);
            GameObject go;

            if (data != null && data.worldPrefab != null)
            {
                go = Instantiate(data.worldPrefab, inactiveHolder);
                StripGameplayComponents(go);
                go.transform.SetParent(root, false);
                Quaternion baseRot = Quaternion.Euler(data.defaultSlotRotation);
                // + axis of the models, measured in the editor: battery nub +Y, LED long leg +X, resistor along X.
                Vector3 plus = baseRot * (p.kind == PartKind.Battery ? Vector3.up : Vector3.right);
                plus.y = 0f;
                Vector3 want = p.kind == PartKind.LED ? -dir : dir;
                float yawAngle = plus.sqrMagnitude > 0.0001f ? Vector3.SignedAngle(plus, want, Vector3.up) : 0f;
                go.transform.localRotation = Quaternion.Euler(0f, yawAngle, 0f) * baseRot;
                go.transform.localScale = data.worldPrefab.transform.localScale * partScale;
                go.SetActive(true);
            }
            else
            {
                go = CreatePrimitivePart(p.kind);
                go.transform.SetParent(root, false);
                go.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir));
            }

            go.name = $"{p.kind} {p.id}";
            go.transform.localPosition = localPos;
            SitOnSurface(go);
            CentreOn(go, localPos);

            if (p.kind == PartKind.LED)
            {
                LEDLight light = go.AddComponent<LEDLight>();
                light.Bind(this, p.id);
            }
            else if (p.kind == PartKind.Switch)
            {
                CircuitSwitch sw = go.AddComponent<CircuitSwitch>();
                sw.Setup(this, p.id, IsSwitchClosed(p.id));
                switchVisuals[p.id] = sw;
            }

            // Labels under row parts; next to the battery column (so the battery doesn't cover its label).
            bool columnPart = Mathf.Abs(dir.z) > 0.5f;
            if (showLabels) CreateLabel(LabelFor(p), localPos + (columnPart ? new Vector3(-0.85f, 0.01f, 0f) : new Vector3(0f, 0.01f, -0.45f)), 1f);
            return go;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            Bounds b = default; bool any = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b : new Bounds(go.transform.position, Vector3.zero);
        }

        /// <summary>Moves the part sideways so its visual middle (not its pivot) is at <paramref name="localPos"/>.</summary>
        private void CentreOn(GameObject go, Vector3 localPos)
        {
            Vector3 c = root.InverseTransformPoint(RendererBounds(go).center);
            go.transform.localPosition += new Vector3(localPos.x - c.x, 0f, localPos.z - c.z);
        }

        /// <summary>How far the part reaches from its centre along <paramref name="dir"/> – where the wires attach.</summary>
        private float TerminalReach(GameObject go, Vector3 dir, float fallback)
        {
            Bounds b = RendererBounds(go);
            Vector3 w = root.TransformDirection(dir);
            float reach = Mathf.Abs(w.x) * b.extents.x + Mathf.Abs(w.z) * b.extents.z;
            return reach > 0.05f ? reach : fallback;
        }

        private void SolderDot(Vector3 localPos)
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediateSafe(dot.GetComponent<Collider>());
            dot.name = "Solder";
            dot.transform.SetParent(root, false);
            dot.transform.localPosition = new Vector3(localPos.x, surfaceY + wireThickness * 0.5f, localPos.z);
            dot.transform.localScale = Vector3.one * wireThickness * 1.8f;
            Tint(dot, new Color(0.55f, 0.56f, 0.6f));
        }

        private static void DestroyImmediateSafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        private static string LabelFor(PartDef p)
        {
            switch (p.kind)
            {
                case PartKind.Battery: return $"{p.id} {CircuitUnits.Volts(p.value)}";
                case PartKind.Resistor: return $"{p.id} {CircuitUnits.Ohms(p.value)}";
                case PartKind.LED: return $"{p.id}";
                case PartKind.Switch: return $"{p.id}";
                default: return p.id;
            }
        }

        private ComponentData PickModel(PartDef p)
        {
            ComponentData best = null;
            float bestScore = float.MaxValue;
            foreach (ComponentData d in partLibrary)
            {
                if (d == null || d.worldPrefab == null || d.Kind != p.kind) continue;
                float score = 0f;
                if (p.kind == PartKind.Resistor) score = Mathf.Abs(Mathf.Log10(d.Resistance) - Mathf.Log10(Mathf.Max(p.value, 0.001f)));
                if (score < bestScore) { bestScore = score; best = d; }
            }
            return best;
        }

        private static void StripGameplayComponents(GameObject go)
        {
            foreach (CatInteractable c in go.GetComponentsInChildren<CatInteractable>(true)) DestroyImmediate(c);
            foreach (PickupTrigger c in go.GetComponentsInChildren<PickupTrigger>(true)) DestroyImmediate(c);
            foreach (LEDLight c in go.GetComponentsInChildren<LEDLight>(true)) DestroyImmediate(c);
            foreach (Rigidbody c in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(c);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        }

        private static GameObject CreatePrimitivePart(PartKind kind)
        {
            GameObject go;
            switch (kind)
            {
                case PartKind.Battery:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.transform.localScale = new Vector3(0.35f, 0.3f, 0.35f);
                    Tint(go, new Color(0.9f, 0.8f, 0.1f));
                    break;
                case PartKind.Resistor:
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.transform.localScale = new Vector3(0.15f, 0.25f, 0.15f);
                    go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    Tint(go, new Color(0.8f, 0.6f, 0.4f));
                    break;
                case PartKind.LED:
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.transform.localScale = Vector3.one * 0.25f;
                    Tint(go, new Color(0.5f, 0.1f, 0.1f));
                    break;
                default:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.localScale = new Vector3(0.35f, 0.1f, 0.25f);
                    Tint(go, new Color(0.3f, 0.3f, 0.35f));
                    break;
            }
            Destroy(go.GetComponent<Collider>());

            // Keep the primitive's own scale on a child so the part root stays at scale 1.
            var parent = new GameObject();
            go.transform.SetParent(parent.transform, false);
            return parent;
        }

        private static void Tint(GameObject go, Color c)
        {
            Renderer r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.material.color = c;
            if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
        }

        private void CreateLabel(string text, Vector3 localPos, float sizeFactor)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // lying flat, readable from above

            var tmp = go.AddComponent<TextMeshPro>();
            if (UIManager.Instance != null && UIManager.Instance.Font != null) tmp.font = UIManager.Instance.Font;
            tmp.text = text;
            tmp.fontSize = labelFontSize * sizeFactor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = showBoard ? new Color(0.1f, 0.1f, 0.18f) : Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(3f, 0.6f);
        }

        // ------------------------------------------------------------------ schematic-style wiring

        /// <summary>
        /// Wires like a drawn schematic instead of "shortest path":
        ///   • inside a row (one code line) each part is wired straight to the next one
        ///   • the ends of the rows and the battery (left column) join their node through a vertical side wire (bus):
        ///     row STARTS and the battery on a bus left of the rows, row ENDS on a bus right of the rows
        ///   • a node that needs both sides (e.g. B− at the end of a row and on the battery) gets a rail above or
        ///     below all rows, so no wire runs over a part
        ///   • solder dots only where three or more wires really meet
        /// Anything unusual (a part's terminal referenced in another line, like R1+) falls back to the old L-wires.
        /// </summary>
        private void RouteWires(Vector3 shift, Dictionary<PartDef, KeyValuePair<Vector3, Vector3>> partEnds, HashSet<PartDef> column)
        {
            var left = new Dictionary<int, List<Vector3>>();
            var right = new Dictionary<int, List<Vector3>>();
            var fallback = new Dictionary<int, List<Vector3>>();
            float topZ = float.MinValue, bottomZ = float.MaxValue, rowStartX = float.MaxValue, rowEndX = float.MinValue;

            // a) inside each row
            for (int row = 0; row < built.chains.Count; row++)
            {
                ChainDef chain = built.chains[row];
                var aPts = new List<Vector3>();
                var bPts = new List<Vector3>();
                foreach (ChainItem it in chain.items)
                {
                    if (it.isPart && it.part != null && partEnds.TryGetValue(it.part, out var ends)) { aPts.Add(ends.Key); bPts.Add(ends.Value); }
                    else
                    {
                        int col = aPts.Count;
                        Vector3 p = new Vector3(col * spacing, 0f, -row * rowSpacing) + shift;
                        aPts.Add(p); bPts.Add(p);
                    }
                }
                for (int i = 0; i + 1 < chain.items.Count; i++)
                {
                    int node = chain.items[i].isPart ? chain.items[i].part.nodeB : chain.items[i].node;
                    CreateWire(node, bPts[i], aPts[i + 1]);
                }

                for (int i = 0; i < chain.items.Count; i++)
                {
                    ChainItem it = chain.items[i];
                    if (it.isPart) continue;
                    if (i == 0) AddPoint(left, it.node, aPts[i]);
                    else if (i == chain.items.Count - 1) AddPoint(right, it.node, aPts[i]);
                    else AddPoint(fallback, it.node, aPts[i]);   // a junction in the middle of a line
                    topZ = Mathf.Max(topZ, aPts[i].z);
                    bottomZ = Mathf.Min(bottomZ, aPts[i].z);
                    rowStartX = Mathf.Min(rowStartX, aPts[0].x);
                    rowEndX = Mathf.Max(rowEndX, bPts[bPts.Count - 1].x);
                }
            }

            // b) parts in the left column (the battery): both terminals go to the left side
            var columnNodes = new HashSet<int>();
            foreach (PartDef p in column)
            {
                if (!partEnds.TryGetValue(p, out var ends)) continue;
                AddPoint(left, p.nodeA, ends.Key);
                AddPoint(left, p.nodeB, ends.Value);
                columnNodes.Add(p.nodeA);
                columnNodes.Add(p.nodeB);
                topZ = Mathf.Max(topZ, Mathf.Max(ends.Key.z, ends.Value.z));
                bottomZ = Mathf.Min(bottomZ, Mathf.Min(ends.Key.z, ends.Value.z));
            }
            if (rowStartX == float.MaxValue) { rowStartX = shift.x; rowEndX = shift.x; }

            // c) nodes that need a part's terminal from another line → old L-wires for that node
            foreach (var kv in fallback)
            {
                var pts = new List<Vector3>(kv.Value);
                if (left.TryGetValue(kv.Key, out var l)) { pts.AddRange(l); left.Remove(kv.Key); }
                if (right.TryGetValue(kv.Key, out var r)) { pts.AddRange(r); right.Remove(kv.Key); }
                DrawNodeWires(kv.Key, pts);
            }

            // d) buses and rails
            int leftIndex = 0, rightIndex = 0, topIndex = 0, bottomIndex = 0;
            var nodeSet = new HashSet<int>(left.Keys);
            nodeSet.UnionWith(right.Keys);
            // Left side wires nearest the rows first; the battery's wires further out (towards the battery), the ones
            // that also need a rail outermost – this keeps crossings to a minimum.
            var nodes = new List<int>(nodeSet);
            nodes.Sort((x, y) => Rank(x).CompareTo(Rank(y)));
            int Rank(int n) => (columnNodes.Contains(n) ? 1 : 0) + (left.ContainsKey(n) && right.ContainsKey(n) ? 2 : 0);
            float midZ = (topZ + bottomZ) * 0.5f;
            foreach (int node in nodes)
            {
                left.TryGetValue(node, out var lp);
                right.TryGetValue(node, out var rp);
                int count = (lp?.Count ?? 0) + (rp?.Count ?? 0);
                if (count < 2) continue;

                float? railZ = null;
                if (lp != null && rp != null && lp.Count > 0 && rp.Count > 0)
                {
                    // Rail on the side where the left-hand points are (e.g. the battery's − at the bottom → bottom rail).
                    float maxLeft = float.MinValue;
                    foreach (Vector3 v in lp) maxLeft = Mathf.Max(maxLeft, v.z);
                    bool top = maxLeft >= midZ;
                    railZ = top ? topZ + rowSpacing * 0.55f + topIndex++ * 0.25f
                                : bottomZ - rowSpacing * 0.55f - bottomIndex++ * 0.25f;
                }

                float lx = rowStartX - spacing * 0.35f - leftIndex * 0.3f;
                float rx = rowEndX + spacing * 0.35f + rightIndex * 0.3f;
                if (lp != null && lp.Count > 0) { Bus(node, lp, lx, railZ, count); leftIndex++; }
                if (rp != null && rp.Count > 0) { Bus(node, rp, rx, railZ, count); rightIndex++; }
                if (railZ.HasValue) CreateWire(node, new Vector3(lx, 0f, railZ.Value), new Vector3(rx, 0f, railZ.Value));
            }
        }

        /// <summary>A vertical side wire at x = <paramref name="busX"/>, with a short straight wire to each point.</summary>
        private void Bus(int node, List<Vector3> points, float busX, float? railZ, int connections)
        {
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector3 p in points)
            {
                CreateWire(node, p, new Vector3(busX, 0f, p.z));
                if (connections >= 3) SolderDot(new Vector3(busX, 0f, p.z));
                minZ = Mathf.Min(minZ, p.z);
                maxZ = Mathf.Max(maxZ, p.z);
            }
            if (railZ.HasValue) { minZ = Mathf.Min(minZ, railZ.Value); maxZ = Mathf.Max(maxZ, railZ.Value); }
            if (maxZ - minZ > 0.001f) CreateWire(node, new Vector3(busX, 0f, minZ), new Vector3(busX, 0f, maxZ));
        }

        // ------------------------------------------------------------------ wires

        private void DrawNodeWires(int node, List<Vector3> points)
        {
            if (points.Count < 2) return;

            // Prim's algorithm with Manhattan distance.
            var inTree = new bool[points.Count];
            inTree[0] = true;
            for (int added = 1; added < points.Count; added++)
            {
                int bestFrom = -1, bestTo = -1;
                float best = float.MaxValue;
                for (int i = 0; i < points.Count; i++)
                {
                    if (!inTree[i]) continue;
                    for (int j = 0; j < points.Count; j++)
                    {
                        if (inTree[j]) continue;
                        float d = Mathf.Abs(points[i].x - points[j].x) + Mathf.Abs(points[i].z - points[j].z);
                        if (d < best) { best = d; bestFrom = i; bestTo = j; }
                    }
                }
                if (bestTo < 0) break;
                inTree[bestTo] = true;
                CreateWire(node, points[bestFrom], points[bestTo]);
            }
        }

        private void CreateWire(int node, Vector3 a, Vector3 b)
        {
            // L-shaped wire made of two thin boxes lying on the board.
            float y = surfaceY + wireThickness * 0.5f;
            var pa = new Vector3(a.x, y, a.z);
            var corner = new Vector3(b.x, y, a.z);
            var pb = new Vector3(b.x, y, b.z);
            CreateWireSegment(node, pa, corner);
            CreateWireSegment(node, corner, pb);
        }

        private void CreateWireSegment(int node, Vector3 from, Vector3 to)
        {
            float length = Vector3.Distance(from, to);
            if (length < 0.001f) return;

            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(seg.GetComponent<Collider>());
            seg.name = "Wire";
            seg.transform.SetParent(root, false);
            seg.transform.localPosition = (from + to) * 0.5f;
            seg.transform.localRotation = Quaternion.LookRotation(to - from);
            // extra length so the corners overlap nicely
            seg.transform.localScale = new Vector3(wireThickness, wireThickness, length + wireThickness);

            Renderer r = seg.GetComponent<Renderer>();
            r.sharedMaterial = wireMaterial != null ? wireMaterial : GetRuntimeWireMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetWireColor(r, wireIdleColor);
            wires.Add(new KeyValuePair<Renderer, int>(r, node));
        }

        private Material GetRuntimeWireMaterial()
        {
            if (runtimeWireMaterial != null) return runtimeWireMaterial;
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            runtimeWireMaterial = new Material(s);
            return runtimeWireMaterial;
        }

        private static void SetWireColor(Renderer r, Color c)
        {
            Material m = r.material;   // per-wire copy
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        // ------------------------------------------------------------------ board + grounding

        private void CreateBoard(Vector3 layoutSize)
        {
            GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(board.GetComponent<Collider>());
            board.name = "Breadboard Base";
            board.transform.SetParent(root, false);
            var size = new Vector3(layoutSize.x + boardMargin * 2f, boardThickness, layoutSize.z + boardMargin * 2f);
            board.transform.localPosition = new Vector3(0f, boardThickness * 0.5f, 0f);
            board.transform.localScale = size;

            Renderer r = board.GetComponent<Renderer>();
            if (boardMaterial != null)
            {
                r.sharedMaterial = boardMaterial;
                return;
            }
            r.material = GetRuntimeBoardMaterial();
            Material m = r.material;
            var tiling = new Vector2(size.x / 0.4f, size.z / 0.4f);   // one hole pattern every 40 cm
            if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", tiling);
            m.mainTextureScale = tiling;
        }

        private Material GetRuntimeBoardMaterial()
        {
            if (runtimeBoardMaterial != null) return runtimeBoardMaterial;
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            runtimeBoardMaterial = new Material(s);

            // Breadboard look: off-white plastic with rows of dark holes.
            const int size = 64;
            var tex = new Texture2D(size, size) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var plastic = new Color(0.93f, 0.92f, 0.87f);
            var hole = new Color(0.25f, 0.25f, 0.27f);
            var groove = new Color(0.82f, 0.81f, 0.77f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color c = plastic;
                    if (y >= 30 && y <= 33) c = groove;                                  // middle groove
                    int hx = x % 8, hy = y % 8;
                    if (hx >= 3 && hx <= 5 && hy >= 3 && hy <= 5 && !(y >= 28 && y <= 35)) c = hole;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();

            runtimeBoardMaterial.mainTexture = tex;
            if (runtimeBoardMaterial.HasProperty("_BaseMap")) runtimeBoardMaterial.SetTexture("_BaseMap", tex);
            if (runtimeBoardMaterial.HasProperty("_BaseColor")) runtimeBoardMaterial.SetColor("_BaseColor", Color.white);
            if (runtimeBoardMaterial.HasProperty("_Smoothness")) runtimeBoardMaterial.SetFloat("_Smoothness", 0.2f);
            return runtimeBoardMaterial;
        }

        /// <summary>Moves a part up/down so its lowest point touches the board (models have different pivots).</summary>
        private void SitOnSurface(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            float minY = float.MaxValue;
            foreach (Renderer r in renderers) minY = Mathf.Min(minY, r.bounds.min.y);
            float targetY = root.TransformPoint(new Vector3(0f, surfaceY, 0f)).y;
            go.transform.position += Vector3.up * (targetY - minY);
        }

        private Vector3 FloorPoint(Vector3 from)
        {
            RaycastHit[] hits = Physics.RaycastAll(from + Vector3.up * 2f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
            float bestY = float.MinValue;
            bool found = false;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.GetComponentInParent<PlayerCarry>() != null) continue;
                if (h.point.y > from.y + 0.5f) continue;   // ignore tables/ceiling above the build area
                if (!found || h.point.y > bestY) { bestY = h.point.y; found = true; }
            }
            return found ? new Vector3(from.x, bestY + floorOffset, from.z) : from;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(6f, 0.05f, 4f));
            Gizmos.matrix = Matrix4x4.identity;
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, "Code circuit build area");
#endif
        }
    }
}
