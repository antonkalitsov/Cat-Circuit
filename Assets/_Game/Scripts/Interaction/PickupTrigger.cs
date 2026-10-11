using CatCircuit;
using UnityEngine;

/// <summary>
/// Put on a CHILD object that has a trigger collider to extend the interaction range of the
/// parent's interactable (pickup, slot, NPC, door ...). Created automatically when needed.
/// (Kept the old name so existing scenes/prefabs keep working.)
/// </summary>
public class PickupTrigger : MonoBehaviour
{
    [Tooltip("Empty = first CatInteractable in the parents.")]
    [SerializeField] private CatInteractable target;

    private CatInteractable Target
    {
        get
        {
            if (target == null) target = GetComponentInParent<CatInteractable>();
            return target;
        }
    }

    public void SetTarget(CatInteractable interactable) => target = interactable;

    /// <summary>Old API, still works.</summary>
    public void SetPickupableItem(PickupableItem pickup) => target = pickup;

    private void OnTriggerEnter(Collider other)
    {
        if (Target != null) Target.NotifyEnter(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (Target != null) Target.NotifyExit(other);
    }
}
