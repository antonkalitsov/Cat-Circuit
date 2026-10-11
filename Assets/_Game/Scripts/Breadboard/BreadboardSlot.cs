using CatCircuit;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A place on a <see cref="Breadboard"/> where the cat can put a part.
/// Its <see cref="SlotId"/> is the name used in the board's circuit text ("slot S1").
///
///   E (holding a part, slot empty)  – place it
///   E (paws empty, slot full)       – take the part back
///   R (paws empty, slot full)       – rotate the part
///
/// With "Require Orientation" the part only conducts at Correct Rotation Y; at +180° it is
/// connected backwards (matters for LEDs and batteries), any other angle = not plugged in.
/// </summary>
public class BreadboardSlot : CatInteractable
{
    public enum Seating { Forward, Reversed, NotSeated }

    [Header("Identity")]
    [Tooltip("Name of this slot in the Breadboard circuit text. Empty = GameObject name.")]
    [SerializeField] private string slotId = "";
    [SerializeField] private string slotLabel = "Slot";
    [Tooltip("Board this slot belongs to. Empty = Breadboard in the parents.")]
    [SerializeField] private Breadboard board;

    [Header("Rotation")]
    [SerializeField] private float rotationStep = 45f;
    [SerializeField] private bool requireOrientation = false;
    [SerializeField] private float correctRotationY = 0f;

    [Header("Rules")]
    [Tooltip("Only these parts fit. Empty = any part.")]
    [SerializeField] private ComponentData[] acceptOnly = new ComponentData[0];
    [Tooltip("Part already sitting in the slot when the scene starts (spawned from its World Prefab).")]
    [SerializeField] private ComponentData startsWith;
    [Tooltip("The player cannot take the part out.")]
    [SerializeField] private bool locked;

    [Header("Events")]
    public UnityEvent<ComponentData> OnComponentPlaced;
    public UnityEvent<ComponentData> OnComponentRemoved;

    // ---- LEGACY: read once by CatCircuit ▸ Upgrade. ----
    [SerializeField, HideInInspector] private ComponentData requiredComponent;

    private PickupableItem placedItem;
    private float rotationY;

    public string SlotId => string.IsNullOrEmpty(slotId) ? SanitizeId(gameObject.name) : slotId;
    public string SlotLabel => slotLabel;
    public Breadboard Board => board;
    public bool IsOccupied => placedItem != null;
    public ComponentData CurrentComponent => placedItem != null ? placedItem.Data : null;
    public PickupableItem PlacedItem => placedItem;
    public float RotationY => rotationY;
    public bool RequireOrientation => requireOrientation;
    public ComponentData LegacyRequiredComponent => requiredComponent;

    public Seating CurrentSeating
    {
        get
        {
            if (!requireOrientation) return Seating.Forward;
            float d = Mathf.Abs(Mathf.DeltaAngle(rotationY, correctRotationY));
            if (d < 1f) return Seating.Forward;
            if (Mathf.Abs(d - 180f) < 1f) return Seating.Reversed;
            return Seating.NotSeated;
        }
    }

    /// <summary>Edit-mode setup used by the editor tools.</summary>
    public void Configure(string id, Breadboard owner, bool orientation, float correctY, float step)
    {
        slotId = id;
        board = owner;
        requireOrientation = orientation;
        correctRotationY = correctY;
        rotationStep = step;
    }

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (board == null) board = GetComponentInParent<Breadboard>();
        if (board == null)
        {
            Breadboard[] boards = FindObjectsByType<Breadboard>(FindObjectsSortMode.None);
            if (boards.Length == 1) board = boards[0];
        }
        EnsureInteractionTrigger(1f);
    }

    private void OnEnable()
    {
        if (board != null) board.RegisterSlot(this);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (board != null) board.UnregisterSlot(this);
    }

    private void Start()
    {
        if (startsWith != null && !IsOccupied) SpawnStartingPart();
    }

    // ------------------------------------------------------------------ interaction

    public override string GetPrompt(PlayerCarry player)
    {
        if (player.IsHoldingItem)
        {
            if (IsOccupied) return null;                   // let an empty slot nearby take the focus
            if (!Accepts(player.HeldComponentData)) return $"{player.GetHeldItemName()} не става тук.";
            return UIText.Place(player.GetHeldItemName());
        }

        if (IsOccupied && !locked) return UIText.TakeFromSlot(placedItem.DisplayName) + SeatingHint();
        return null;
    }

    public override void Interact(PlayerCarry player)
    {
        if (player.IsHoldingItem)
        {
            if (IsOccupied || !Accepts(player.HeldComponentData)) return;
            float extra = player.HeldRotationY;
            PickupableItem item = player.ReleaseHeldItem();
            PlaceItem(item, extra);
            GameAudio.Play(Sfx.Place, 1f, 0.05f);
            if (item != null) Telemetry.Log("slot_place", $"{SlotId}:{(item.Data != null ? item.Data.name : item.name)}");
        }
        else if (IsOccupied && !locked)
        {
            TakeItem(player);
        }
    }

    public override bool TryRotate(PlayerCarry player)
    {
        if (!IsOccupied || locked) return false;
        SetRotation(rotationY + rotationStep);
        GameAudio.Play(Sfx.Rotate, 1f, 0.08f);
        return true;
    }

    public bool Accepts(ComponentData data)
    {
        if (data == null) return false;
        if (acceptOnly == null || acceptOnly.Length == 0) return true;
        foreach (ComponentData d in acceptOnly) if (d == data) return true;
        return false;
    }

    // ------------------------------------------------------------------ placing / taking

    public void PlaceItem(PickupableItem item, float extraRotationY = 0f)
    {
        if (item == null || IsOccupied) return;
        ComponentData data = item.Data;

        placedItem = item;
        rotationY = Mathf.Repeat((data != null ? data.defaultSlotRotation.y : 0f) + extraRotationY, 360f);

        Vector3 offset = data != null ? data.slotPositionOffset : Vector3.zero;
        Vector3? scale = data != null && data.worldPrefab != null ? data.worldPrefab.transform.localScale : (Vector3?)null;
        item.EnterPlaced(transform, offset, VisualRotation(data), scale);

        if (data != null && data.Kind == PartKind.LED)
        {
            LEDLight light = item.GetComponent<LEDLight>();
            if (light == null) light = item.gameObject.AddComponent<LEDLight>();
            light.Bind(board, SlotId);
        }

        if (board != null) board.MarkDirty();
        OnComponentPlaced?.Invoke(data);
    }

    private void TakeItem(PlayerCarry player)
    {
        PickupableItem item = placedItem;
        ComponentData data = item.Data;
        placedItem = null;

        LEDLight light = item.GetComponent<LEDLight>();
        if (light != null) light.Bind(null, "");

        player.PickUp(item);
        if (board != null) board.MarkDirty();
        OnComponentRemoved?.Invoke(data);
    }

    private void SetRotation(float y)
    {
        rotationY = Mathf.Repeat(y, 360f);
        if (placedItem != null) placedItem.transform.localRotation = VisualRotation(placedItem.Data);
        if (board != null) board.MarkDirty();
    }

    private Quaternion VisualRotation(ComponentData data)
    {
        Vector3 baseRot = data != null ? data.defaultSlotRotation : Vector3.zero;
        return Quaternion.Euler(baseRot.x, rotationY, baseRot.z);
    }

    private void SpawnStartingPart()
    {
        if (startsWith.worldPrefab == null)
        {
            Debug.LogWarning($"[BreadboardSlot] '{startsWith.name}' has no World Prefab to spawn.", this);
            return;
        }
        GameObject go = Instantiate(startsWith.worldPrefab);
        go.SetActive(true);
        PickupableItem item = go.GetComponent<PickupableItem>();
        if (item == null) item = go.AddComponent<PickupableItem>();
        item.SetComponentData(startsWith);
        PlaceItem(item);
    }

    // ------------------------------------------------------------------ simulation

    /// <summary>What this slot contributes to the circuit right now.</summary>
    public SimPart CreateSimPart(PartDef def)
    {
        if (!IsOccupied)
        {
            return new SimPart
            {
                id = def.id, a = def.nodeA, b = def.nodeB, isSlot = true,
                kind = def.wireWhenEmpty ? PartKind.Wire : PartKind.None,
                conducting = def.wireWhenEmpty,
            };
        }

        SimPart sp = CircuitHost.FromComponent(def.id, placedItem.Data, def.nodeA, def.nodeB);
        sp.isSlot = true;
        switch (CurrentSeating)
        {
            case Seating.NotSeated:
                sp.conducting = false;
                sp.seated = false;
                break;
            case Seating.Reversed:
                int t = sp.a; sp.a = sp.b; sp.b = t;
                break;
        }
        return sp;
    }

    private string SeatingHint()
    {
        if (!requireOrientation) return "";
        switch (CurrentSeating)
        {
            case Seating.NotSeated: return "\n(не е наместен – завърти го)";
            default: return "";
        }
    }

    public static string SanitizeId(string raw)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in raw) if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        if (sb.Length == 0 || !char.IsLetter(sb[0])) sb.Insert(0, 'S');
        return sb.ToString();
    }

    // ------------------------------------------------------------------ gizmos

    private void OnDrawGizmos()
    {
        Gizmos.color = IsOccupied ? (CurrentSeating == Seating.Forward ? Color.green : Color.red) : Color.cyan;
        Gizmos.DrawWireCube(transform.position, new Vector3(0.8f, 0.2f, 0.8f));
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, $"{SlotId}  ({slotLabel})");
#endif
    }
}
