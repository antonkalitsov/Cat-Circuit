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
                partPos[p] = new Vector3(-1.5f * spacing, 0f, -k * rowSpacing - rowSpacing * 0.5f);
                vertical.Add(p);
                k++;
            }

            // 3) centre everything around the builder
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (Vector3 v in partPos.Values) { if (first) { bounds = new Bounds(v, Vector3.zero); first = false; } else bounds.Encapsulate(v); }
            foreach (var m in markers) bounds.Encapsulate(m.Key);
            Vector3 shift = new Vector3(-bounds.center.x, surfaceY, -bounds.center.z);
            if (showBoard) CreateBoard(bounds.size);

            // 4) spawn parts and remember their terminal positions
            float half = spacing * 0.3f;
            foreach (var kv in partPos)
            {
                PartDef p = kv.Key;
                Vector3 pos = kv.Value + shift;
                bool isVertical = vertical.Contains(p);
                Vector3 dir = isVertical ? Vector3.back : Vector3.right;   // from A (entry) to B (exit)
                // Vertical batteries: "+" on top (further away), so current visibly flows "down" the rows.
                if (isVertical && p.kind == PartKind.Battery) dir = Vector3.forward;

                AddPoint(nodePoints, p.nodeA, pos - dir * half);
                AddPoint(nodePoints, p.nodeB, pos + dir * half);
                SpawnPart(p, pos, isVertical, holder.transform);
            }

            // 5) chain points (junctions / terminal references) with a dot and a label
            for (int i = 0; i < markers.Count; i++)
            {
                Vector3 pos = markers[i].Key + shift;
                AddPoint(nodePoints, markerNodes[i], pos);

                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(dot.GetComponent<Collider>());
                dot.name = "Point " + markers[i].Value;
                dot.transform.SetParent(root, false);
                dot.transform.localPosition = pos;
                dot.transform.localScale = Vector3.one * Mathf.Max(0.14f, wireThickness * 1.8f);
                Tint(dot, new Color(0.15f, 0.15f, 0.15f));
                if (showLabels) CreateLabel(markers[i].Value, pos + new Vector3(0f, 0.01f, 0.3f), 0.8f);
            }

            // 6) wires: connect all points of a node with a minimum spanning tree of L-shaped lines
            foreach (var kv in nodePoints) DrawNodeWires(kv.Key, kv.Value);

            Destroy(holder);
        }

        private static void AddPoint(Dictionary<int, List<Vector3>> d, int node, Vector3 p)
        {
            if (node < 0) return;
            if (!d.TryGetValue(node, out List<Vector3> list)) d[node] = list = new List<Vector3>();
            list.Add(p);
        }

        private void SpawnPart(PartDef p, Vector3 localPos, bool vertical, Transform inactiveHolder)
        {
            ComponentData data = PickModel(p);
            GameObject go;
            Quaternion yaw = Quaternion.Euler(0f, vertical ? 90f : 0f, 0f);

            if (data != null && data.worldPrefab != null)
            {
                go = Instantiate(data.worldPrefab, inactiveHolder);
                StripGameplayComponents(go);
                go.transform.SetParent(root, false);
                go.transform.localRotation = yaw * Quaternion.Euler(data.defaultSlotRotation);
                go.transform.localScale = data.worldPrefab.transform.localScale * partScale;
                go.SetActive(true);
            }
            else
            {
                go = CreatePrimitivePart(p.kind);
                go.transform.SetParent(root, false);
                go.transform.localRotation = yaw;
            }

            go.name = $"{p.kind} {p.id}";
            go.transform.localPosition = localPos;
            SitOnSurface(go);

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

            if (showLabels) CreateLabel(LabelFor(p), localPos + new Vector3(0f, 0.01f, -0.45f), 1f);
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
