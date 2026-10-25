using System.Collections.Generic;
using CatCircuit;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A breadboard puzzle: a circuit written as text + <see cref="BreadboardSlot"/>s the player fills.
///
/// HOW TO MAKE A NEW BOARD PUZZLE
///   1. GameObject ▸ CatCircuit ▸ Circuit Board   (or add this component + some slots as children)
///   2. Give every slot a Slot Id (S1, S2 ...).
///   3. Write the circuit below using those ids, e.g.
///          battery B 9V
///          led L1 red
///          slot S1
///          B+ -> S1 -> L1 -> B-
///   4. Add LEDLight to LED models and set their Element Id (L1).
///   5. Add a CircuitPuzzle with the stages / goals and link it to an NPC.
/// The circuit is simulated for real, so anything that works physically works in the game.
/// </summary>
public class Breadboard : CircuitHost
{
    public const string ExampleCircuit =
@"# Example: put a resistor in slot S1 to light L1
battery B 9V
led L1 red
slot S1
B+ -> S1 -> L1 -> B-";

    [Header("Circuit (see README / CircuitScript.cs for the language)")]
    [TextArea(6, 30)]
    [SerializeField] private string circuit = ExampleCircuit;

    [Tooltip("Slots used by this board. Empty = every BreadboardSlot in the children (and slots that point to this board).")]
    [SerializeField] private List<BreadboardSlot> slots = new List<BreadboardSlot>();

    [Header("Events")]
    [Tooltip("Fires when the CircuitPuzzle on this board is fully solved.")]
    public UnityEvent OnCircuitComplete;
    [Tooltip("Not used by the new system – kept for old scene hookups.")]
    public UnityEvent OnCircuitIncomplete;

    // ---- LEGACY: read once by CatCircuit ▸ Upgrade, then cleared. Do not use in new code. ----
    [SerializeField, HideInInspector] private List<BreadboardSlot> requiredSlots = new List<BreadboardSlot>();
    [SerializeField, HideInInspector] private bool useLEDToggle;
    [SerializeField, HideInInspector] private GameObject ledOff;
    [SerializeField, HideInInspector] private GameObject ledOn;
    [SerializeField, HideInInspector] private bool checkResistorValue;
    [SerializeField, HideInInspector] private float targetResistanceValue = 1000f;
    [SerializeField, HideInInspector] private CatNPC questNPC;
    [SerializeField, HideInInspector] private int puzzleNumber = 1;

    private readonly List<BreadboardSlot> runtimeSlots = new List<BreadboardSlot>();
    private string cachedText;
    private CircuitDefinition cachedDefinition;

    public string CircuitText
    {
        get => circuit;
        set { circuit = value; cachedText = null; MarkDirty(); }
    }

    public IReadOnlyList<BreadboardSlot> Slots
    {
        get { if (runtimeSlots.Count == 0) CollectSlots(); return runtimeSlots; }
    }

    // ------------------------------------------------------------------ lifecycle

    private void Awake() => CollectSlots();

    private void CollectSlots()
    {
        foreach (BreadboardSlot s in slots) AddSlot(s);
        foreach (BreadboardSlot s in GetComponentsInChildren<BreadboardSlot>(true)) AddSlot(s);
    }

    private void AddSlot(BreadboardSlot s)
    {
        if (s != null && !runtimeSlots.Contains(s)) runtimeSlots.Add(s);
    }

    public void RegisterSlot(BreadboardSlot slot) { AddSlot(slot); MarkDirty(); }

    public void UnregisterSlot(BreadboardSlot slot) { runtimeSlots.Remove(slot); MarkDirty(); }

    public BreadboardSlot FindSlot(string id)
    {
        foreach (BreadboardSlot s in Slots)
            if (s != null && string.Equals(s.SlotId, id, System.StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }

    // ------------------------------------------------------------------ circuit

    /// <summary>Parses the circuit text (cached until the text changes).</summary>
    public CircuitDefinition ParseCircuit()
    {
        if (cachedDefinition == null || cachedText != circuit)
        {
            cachedText = circuit;
            cachedDefinition = CircuitScript.Parse(circuit, true);
            if (cachedDefinition.HasErrors && Application.isPlaying)
                Debug.LogError($"[Breadboard] Circuit on '{name}' has errors:\n{cachedDefinition.MessagesAsText(true)}", this);
        }
        return cachedDefinition;
    }

    protected override bool BuildSimulation(List<SimPart> parts, out int nodeCount)
    {
        CircuitDefinition def = ParseCircuit();
        Definition = def;
        nodeCount = def.NodeCount;
        if (def.HasErrors) return false;

        foreach (PartDef p in def.parts)
        {
            if (p.kind != PartKind.Slot)
            {
                parts.Add(FromDefinition(p));
                continue;
            }

            BreadboardSlot slot = FindSlot(p.id);
            if (slot == null)
            {
                RuntimeMessages.Add(new CircuitMessage(MessageLevel.Error, p.line, $"No BreadboardSlot with Slot Id '{p.id}' belongs to this board."));
                parts.Add(new SimPart { id = p.id, kind = PartKind.None, a = p.nodeA, b = p.nodeB, conducting = false, isSlot = true });
                continue;
            }
            parts.Add(slot.CreateSimPart(p));
        }
        return true;
    }

    public override void NotifyPuzzleComplete() => OnCircuitComplete?.Invoke();

    /// <summary>A part turned sideways (legs don't reach the contacts) or a battery turned round.</summary>
    public override string PlacementHint()
    {
        foreach (BreadboardSlot s in Slots)
        {
            if (s == null || s.PlacedItem == null) continue;
            ComponentData d = s.PlacedItem.Data;
            BreadboardSlot.Seating seat = s.CurrentSeating;
            if (seat == BreadboardSlot.Seating.NotSeated)
                return d != null && d.Kind == PartKind.Resistor
                    ? "Резисторът не е свързан – завърти го с R, така че крачетата да стигнат контактите."
                    : $"{s.PlacedItem.DisplayName} не е наместен – завърти го с R.";
            if (seat == BreadboardSlot.Seating.Reversed && d != null && d.Kind == PartKind.Battery)
                return "Батерията е обърната! Завърти я с R, така че + на батерията да е при червения +.";
        }
        return null;
    }

    /// <summary>True when the CircuitPuzzle on this board is solved.</summary>
    public bool IsComplete()
    {
        CircuitPuzzle puzzle = GetComponent<CircuitPuzzle>();
        return puzzle != null && puzzle.IsComplete;
    }

    // ------------------------------------------------------------------ legacy access (editor upgrade tool)

    public List<BreadboardSlot> LegacyRequiredSlots => requiredSlots;
    public bool LegacyUseLEDToggle => useLEDToggle;
    public GameObject LegacyLedOff => ledOff;
    public GameObject LegacyLedOn => ledOn;
    public bool LegacyCheckResistorValue => checkResistorValue;
    public float LegacyTargetResistance => targetResistanceValue;
    public CatNPC LegacyQuestNPC => questNPC;
    public int LegacyPuzzleNumber => puzzleNumber;
    public List<BreadboardSlot> SerializedSlots => slots;

    public void ClearLegacy()
    {
        requiredSlots.Clear();
        useLEDToggle = false;
        ledOff = ledOn = null;
        checkResistorValue = false;
        questNPC = null;
    }
}
