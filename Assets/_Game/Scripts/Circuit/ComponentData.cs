using CatCircuit;
using UnityEngine;

/// <summary>
/// Describes one kind of electronic part the cat can carry (battery, resistor, LED ...).
/// Create new ones with  Assets ▸ Create ▸ CatCircuit ▸ Component Data.
///
/// To add a NEW part to the game:
///   1. Make a prefab of the model, add PickupableItem and point it at this asset.
///   2. Fill in the Electrical section so the simulator knows what it does.
///   3. Drag the prefab into <see cref="worldPrefab"/>.
/// </summary>
[CreateAssetMenu(fileName = "NewComponent", menuName = "CatCircuit/Component Data")]
public class ComponentData : ScriptableObject
{
    [Header("Basic Info")]
    public string componentName = "Unnamed Component";
    [TextArea(2, 4)] public string description = "A basic electronic component.";

    [Header("Visuals")]
    [Tooltip("The PREFAB (not the .obj model) that is spawned for this part.")]
    public GameObject worldPrefab;
    public Sprite uiIcon;
    public Color componentColor = Color.white;

    [Header("Electrical")]
    [Tooltip("What the simulator treats this part as. Auto = guessed from Type / name.")]
    public ElectricalKind electricalKind = ElectricalKind.Auto;
    public ComponentType type = ComponentType.Passive;
    [Tooltip("Resistors: resistance in ohms. 0 = default 330 Ω.")]
    public float resistanceValue = 0f;
    [Tooltip("Batteries: voltage in volts.")]
    public float voltage = CircuitConstants.DefaultBatteryVoltage;
    [Tooltip("LEDs: colour (also decides the forward voltage).")]
    public LedColor ledColor = LedColor.Red;

    [Header("Placement in a slot")]
    [Tooltip("Rotation of the model when it sits in a slot (Y is the starting rotation).")]
    public Vector3 defaultSlotRotation = Vector3.zero;
    public Vector3 slotPositionOffset = new Vector3(0, 0.3f, 0);
    [Tooltip("LEGACY – orientation checks now live on BreadboardSlot. Read once by the upgrade tool.")]
    public float correctPlacementYRotation = -1f;

    /// <summary>What the circuit simulator does with this part.</summary>
    public PartKind Kind
    {
        get
        {
            switch (electricalKind)
            {
                case ElectricalKind.Battery: return PartKind.Battery;
                case ElectricalKind.Resistor: return PartKind.Resistor;
                case ElectricalKind.LED: return PartKind.LED;
                case ElectricalKind.Wire: return PartKind.Wire;
                case ElectricalKind.Switch: return PartKind.Switch;
                case ElectricalKind.None: return PartKind.None;
            }

            // Auto: guess from the old fields so existing assets keep working.
            string n = (componentName + " " + name).ToLowerInvariant();
            if (type == ComponentType.PowerSource || n.Contains("batter") || n.Contains("батер")) return PartKind.Battery;
            if (n.Contains("led") || n.Contains("светодиод") || type == ComponentType.Output) return PartKind.LED;
            if (n.Contains("resist") || n.Contains("резист") || resistanceValue > 0f) return PartKind.Resistor;
            if (n.Contains("wire") || n.Contains("жица")) return PartKind.Wire;
            if (n.Contains("switch") || n.Contains("ключ")) return PartKind.Switch;
            return PartKind.None;
        }
    }

    public float Resistance => resistanceValue > 0f ? resistanceValue : CircuitConstants.DefaultResistance;
    public float Voltage => voltage > 0f ? voltage : CircuitConstants.DefaultBatteryVoltage;

    /// <summary>Name shown to the player, with the value for resistors/batteries ("Resistor 1kΩ").</summary>
    public string DisplayName
    {
        get
        {
            switch (Kind)
            {
                case PartKind.Resistor: return componentName + " " + CircuitUnits.Ohms(Resistance);
                case PartKind.Battery: return componentName + " " + CircuitUnits.Volts(Voltage);
                default: return componentName;
            }
        }
    }
}

public enum ComponentType
{
    PowerSource,
    Passive,
    Semiconductor,
    Input,
    Output
}

public enum ElectricalKind
{
    Auto,
    Battery,
    Resistor,
    LED,
    Wire,
    Switch,
    None
}
