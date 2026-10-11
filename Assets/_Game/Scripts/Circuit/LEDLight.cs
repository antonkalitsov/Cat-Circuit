using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Makes an LED model glow according to the simulated current.
    ///
    /// Put it on any LED in the scene and set <see cref="elementId"/> to the LED's name in the
    /// circuit text (e.g. "L1"), or to a slot name to show the LED that is placed in that slot.
    /// LEDs placed in slots / built by the code terminal get this component automatically.
    ///
    /// Modes
    ///   Emission    – brightens the model's material (+ optional small point light)
    ///   SwapObjects – switches between an "off" model and an "on" model (old Houses 1 & 2 style)
    /// </summary>
    [AddComponentMenu("CatCircuit/LED Light")]
    public class LEDLight : MonoBehaviour
    {
        public enum Mode { Emission, SwapObjects }

        [Tooltip("Circuit that drives this LED. Empty = first CircuitHost found in the parents.")]
        [SerializeField] private CircuitHost circuit;
        [Tooltip("Name of the LED (or slot) in the circuit text, e.g. L1 or S3.")]
        [SerializeField] private string elementId = "";
        [SerializeField] private Mode mode = Mode.Emission;

        [Header("Emission mode")]
        [Tooltip("Empty = the first renderer found on this object or its children.")]
        [SerializeField] private Renderer[] renderers = new Renderer[0];
        [Tooltip("Use the colour written in the circuit (led L1 green). Otherwise use 'Color' below.")]
        [SerializeField] private bool useCircuitColor = true;
        [SerializeField] private Color color = Color.red;
        [SerializeField] private float maxEmission = 4f;
        [Tooltip("< 1 makes dim LEDs easier to see.")]
        [SerializeField, Range(0.2f, 1f)] private float gamma = 0.7f;

        [Header("Glow light (emission mode)")]
        [SerializeField] private bool glowLight = true;
        [SerializeField] private float glowRange = 1.5f;
        [SerializeField] private float glowIntensity = 1.5f;

        [Header("Swap mode")]
        [SerializeField] private GameObject offObject;
        [SerializeField] private GameObject onObject;
        [SerializeField, Range(0f, 1f)] private float onThreshold = 0.02f;

        public string ElementId => elementId;
        public Mode LightMode => mode;
        public GameObject OffObject => offObject;
        public GameObject OnObject => onObject;
        public CircuitHost Circuit => circuit;
        public float Brightness { get; private set; }
        public bool Overloaded { get; private set; }

        private Material[] materials;
        private Light glow;
        private Color currentColor;
        private bool initialized;

        // ------------------------------------------------------------------ setup

        /// <summary>Configure from code (used by slots, the code builder and editor tools).</summary>
        public void Setup(Mode newMode, GameObject off = null, GameObject on = null)
        {
            mode = newMode;
            offObject = off;
            onObject = on;
            if (!Application.isPlaying) return;
            initialized = false;
            Init();
            Apply(1f);
        }

        /// <summary>Edit-mode friendly: only stores the settings (used by the editor tools).</summary>
        public void Configure(CircuitHost host, string id, Mode newMode, GameObject off = null, GameObject on = null, Color? ledColor = null)
        {
            circuit = host;
            elementId = id ?? "";
            mode = newMode;
            offObject = off;
            onObject = on;
            if (ledColor.HasValue) { color = ledColor.Value; }
        }

        /// <summary>Connect this LED to a circuit element. Pass null to disconnect (LED goes dark).</summary>
        public void Bind(CircuitHost host, string id)
        {
            if (circuit != null) circuit.UnregisterLight(this);
            circuit = host;
            elementId = id ?? "";
            if (circuit != null && isActiveAndEnabled) circuit.RegisterLight(this);
            else SetState(0f, false, null);
        }

        private void Awake() => Init();

        private void OnEnable()
        {
            if (circuit == null && !string.IsNullOrEmpty(elementId))
                circuit = GetComponentInParent<CircuitHost>();
            if (circuit != null) circuit.RegisterLight(this);
        }

        private void OnDisable()
        {
            if (circuit != null) circuit.UnregisterLight(this);
        }

        private void Init()
        {
            // Never create material copies / lights in edit mode (editor tools only set fields).
            if (initialized || !Application.isPlaying) return;
            initialized = true;
            currentColor = color;

            if (mode != Mode.Emission) return;

            if (renderers == null || renderers.Length == 0)
            {
                Renderer r = GetComponent<Renderer>();
                if (r == null) r = GetComponentInChildren<Renderer>(true);
                renderers = r != null ? new[] { r } : new Renderer[0];
            }

            var list = new System.Collections.Generic.List<Material>();
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                foreach (Material m in r.materials)   // .materials = per-object copies, shared assets stay untouched
                {
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    m.SetColor("_EmissionColor", Color.black);
                    list.Add(m);
                }
            }
            materials = list.ToArray();

            if (glowLight && glow == null)
            {
                var go = new GameObject("LED Glow");
                go.transform.SetParent(transform, false);
                glow = go.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.range = glowRange;
                glow.intensity = 0f;
                glow.shadows = LightShadows.None;
                glow.enabled = false;
            }
        }

        // ------------------------------------------------------------------ state

        public void ApplyResult(CircuitResult result)
        {
            PartResult p = result != null ? result.Get(elementId) : null;
            if (p != null && p.kind == PartKind.LED && p.conducting)
                SetState(p.brightness, p.overloaded, useCircuitColor ? LedColors.ToColor(p.ledColor) : (Color?)null);
            else
                SetState(0f, false, null);
        }

        public void SetState(float brightness, bool overloaded, Color? newColor)
        {
            Init();
            Brightness = Mathf.Clamp01(brightness);
            Overloaded = overloaded;
            if (newColor.HasValue) currentColor = newColor.Value;
            Apply(1f);
        }

        private void Update()
        {
            // A burning LED flickers so players notice something is wrong.
            if (Overloaded) Apply(Random.Range(0.6f, 1.6f));
        }

        private void Apply(float flicker)
        {
            if (!Application.isPlaying) return;
            float visual = Brightness > 0f ? Mathf.Pow(Brightness, gamma) * flicker : 0f;

            if (mode == Mode.SwapObjects)
            {
                bool on = Brightness > onThreshold;
                if (offObject != null && offObject.activeSelf == on) offObject.SetActive(!on);
                if (onObject != null && onObject.activeSelf != on) onObject.SetActive(on);
                return;
            }

            Color emission = currentColor * (visual * maxEmission);
            if (materials != null)
                foreach (Material m in materials)
                    if (m != null) m.SetColor("_EmissionColor", emission);

            if (glow != null)
            {
                glow.enabled = visual > 0.01f;
                glow.color = currentColor;
                glow.intensity = visual * glowIntensity;
            }
        }
    }
}
