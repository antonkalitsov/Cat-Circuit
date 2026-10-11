using System.Collections.Generic;
using CatCircuit;
using UnityEngine;

/// <summary>
/// Makes an object something the cat can carry.
/// Plug and play: add it to any model, set <see cref="componentData"/>, done.
/// Missing colliders (interaction range / physics box) are created automatically.
///
/// States
///   World  – lying around (optionally with physics after being dropped)
///   Held   – in the cat's mouth
///   Placed – sitting in a BreadboardSlot
/// </summary>
public class PickupableItem : CatInteractable
{
    public enum State { World, Held, Placed }

    [SerializeField] private ComponentData componentData;
    [Tooltip("How close the cat must be to pick it up (only used when the object has no trigger collider).")]
    [SerializeField] private float interactionRadius = 1.5f;

    public ComponentData Data => componentData;
    public State CurrentState { get; private set; } = State.World;
    public string DisplayName => componentData != null ? componentData.DisplayName : name;

    private Vector3 worldScale;
    private readonly List<Collider> colliders = new List<Collider>();
    private readonly List<bool> colliderWasEnabled = new List<bool>();
    private Collider solidCollider;       // created the first time the item is dropped
    private bool initialized;

    public void SetComponentData(ComponentData data) => componentData = data;
    public ComponentData GetComponentData() => componentData;

    private void Awake() => Init();

    private void Init()
    {
        if (initialized) return;
        initialized = true;
        worldScale = transform.lossyScale;

        // Always add a proper pickup range: the models' own trigger boxes are often too small
        // for the cat to reach when the item lies on a table.
        EnsureInteractionTrigger(interactionRadius, true);

        colliders.Clear();
        colliderWasEnabled.Clear();
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            colliders.Add(c);
            colliderWasEnabled.Add(c.enabled);
        }
    }

    // ------------------------------------------------------------------ CatInteractable

    public override string GetPrompt(PlayerCarry player)
    {
        if (CurrentState != State.World || player.IsHoldingItem) return null;
        return UIText.PickUp(DisplayName);
    }

    public override void Interact(PlayerCarry player) => player.PickUp(this);

    // ------------------------------------------------------------------ state changes

    /// <summary>Called by PlayerCarry.</summary>
    public void EnterHeld(Transform mouth)
    {
        Init();
        CurrentState = State.Held;
        SetPhysics(false);
        SetColliders(false);
        PlayerInteractor.RemoveEverywhere(this);

        transform.SetParent(mouth, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        SetWorldScale(worldScale);
    }

    /// <summary>Called by BreadboardSlot. <paramref name="localScale"/> null = keep the normal size.</summary>
    public void EnterPlaced(Transform slot, Vector3 localPosition, Quaternion localRotation, Vector3? localScale)
    {
        Init();
        CurrentState = State.Placed;
        SetPhysics(false);
        SetColliders(false);
        PlayerInteractor.RemoveEverywhere(this);

        transform.SetParent(slot, false);
        transform.localPosition = localPosition;
        transform.localRotation = localRotation;
        if (localScale.HasValue) transform.localScale = localScale.Value;
        else SetWorldScale(worldScale);
    }

    /// <summary>Back into the world (dropped). With physics it falls to the floor.</summary>
    public void EnterWorld(Vector3 position, Quaternion rotation, bool usePhysics)
    {
        Init();
        CurrentState = State.World;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = worldScale;
        SetColliders(true);
        SetPhysics(usePhysics);
    }

    // ------------------------------------------------------------------ helpers

    private void SetColliders(bool on)
    {
        for (int i = 0; i < colliders.Count; i++)
            if (colliders[i] != null) colliders[i].enabled = on && colliderWasEnabled[i];
        if (solidCollider != null) solidCollider.enabled = on;
    }

    private void SetPhysics(bool on)
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (!on)
        {
            if (rb != null) { rb.isKinematic = true; rb.detectCollisions = false; }
            return;
        }
        EnsureSolidCollider();   // needed to land on the floor
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.detectCollisions = true;
        rb.useGravity = true;
        rb.mass = 1f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
    }

    private void SetWorldScale(Vector3 scale)
    {
        Transform parent = transform.parent;
        if (parent == null) { transform.localScale = scale; return; }
        Vector3 p = parent.lossyScale;
        transform.localScale = new Vector3(
            p.x != 0 ? scale.x / p.x : scale.x,
            p.y != 0 ? scale.y / p.y : scale.y,
            p.z != 0 ? scale.z / p.z : scale.z);
    }

    /// <summary>Items need one non-trigger collider so they can lie on the floor after being dropped.</summary>
    private void EnsureSolidCollider()
    {
        if (solidCollider != null) { solidCollider.enabled = true; return; }
        foreach (Collider c in GetComponents<Collider>())
            if (!c.isTrigger) { solidCollider = c; return; }

        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        // Local-space bounds of all renderers.
        var bounds = new Bounds(transform.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
        foreach (Renderer r in renderers)
        {
            Bounds b = r.bounds;
            Vector3 min = b.min, max = b.max;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                bounds.Encapsulate(transform.InverseTransformPoint(corner));
            }
        }

        var box = gameObject.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = Vector3.Max(bounds.size, Vector3.one * 0.01f);
        solidCollider = box;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}
