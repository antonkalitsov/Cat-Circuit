using CatCircuit;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The cat's mouth: holds ONE <see cref="PickupableItem"/> at a time.
/// Keys are handled by <see cref="PlayerInteractor"/> (added automatically).
///
/// The held object is the real world object – it is moved, never destroyed/re-spawned, so every
/// component on it survives being picked up, dropped or placed in a slot.
/// </summary>
[RequireComponent(typeof(PlayerInteractor))]
public class PlayerCarry : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Where held items sit. Created automatically if empty.")]
    [SerializeField] private Transform mouthHoldPoint;

    [Header("Rotation")]
    [SerializeField] private float rotationStep = 45f;

    [Header("Dropping")]
    [SerializeField] private float dropDistance = 1.5f;
    [SerializeField] private float dropHeight = 1.0f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    [Header("Events")]
    public UnityEvent<ComponentData> OnItemPickedUp;
    public UnityEvent<ComponentData> OnItemPlacedDown;
    public UnityEvent OnItemDropFailed;

    public PickupableItem HeldItem { get; private set; }
    public bool IsHoldingItem => HeldItem != null;
    public ComponentData HeldComponentData => HeldItem != null ? HeldItem.Data : null;
    /// <summary>Extra Y rotation the player gave the held item with R (carried over into slots).</summary>
    public float HeldRotationY { get; private set; }

    private void Awake()
    {
        if (mouthHoldPoint == null)
        {
            var mouth = new GameObject("MouthHoldPoint");
            mouth.transform.SetParent(transform, false);
            mouth.transform.localPosition = new Vector3(0, 1.5f, 1.2f);
            mouthHoldPoint = mouth.transform;
        }
        if (GetComponent<PlayerInteractor>() == null) gameObject.AddComponent<PlayerInteractor>();
    }

    public bool PickUp(PickupableItem item)
    {
        if (item == null) return false;
        if (IsHoldingItem)
        {
            OnItemDropFailed?.Invoke();
            return false;
        }

        HeldItem = item;
        HeldRotationY = 0f;
        item.EnterHeld(mouthHoldPoint);
        GameAudio.Play(Sfx.Pickup, 1f, 0.06f);

        GameUI.HeldItem(GetHeldItemName());
        if (debugLogs) Debug.Log($"[PlayerCarry] Picked up {GetHeldItemName()}");
        OnItemPickedUp?.Invoke(item.Data);
        return true;
    }

    /// <summary>Hands the held item to someone else (e.g. a slot). The caller decides where it goes.</summary>
    public PickupableItem ReleaseHeldItem()
    {
        PickupableItem item = HeldItem;
        if (item == null) return null;

        HeldItem = null;
        HeldRotationY = 0f;
        GameUI.HeldItem("");
        OnItemPlacedDown?.Invoke(item.Data);
        return item;
    }

    /// <summary>Drops the held item in front of the cat with physics.</summary>
    public void Drop()
    {
        PickupableItem item = HeldItem;
        if (item == null) return;

        HeldItem = null;
        Vector3 pos = transform.position + transform.forward * dropDistance + Vector3.up * dropHeight;
        Quaternion rot = Quaternion.Euler(0f, transform.eulerAngles.y + HeldRotationY, 0f);
        HeldRotationY = 0f;
        item.EnterWorld(pos, rot, true);
        GameAudio.Play(Sfx.Drop, 1f, 0.05f);

        GameUI.HeldItem("");
        if (debugLogs) Debug.Log($"[PlayerCarry] Dropped {item.name}");
    }

    public void RotateHeld()
    {
        if (!IsHoldingItem) return;
        HeldRotationY = Mathf.Repeat(HeldRotationY + rotationStep, 360f);
        HeldItem.transform.localRotation = Quaternion.Euler(0f, HeldRotationY, 0f);
        GameAudio.Play(Sfx.Rotate, 1f, 0.08f);
    }

    public string GetHeldItemName() => HeldItem != null ? HeldItem.DisplayName : "";
}
