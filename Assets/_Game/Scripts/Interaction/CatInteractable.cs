using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Base class for everything the cat can use with E: pickups, slots, NPCs, doors, terminals...
    ///
    /// How it works
    ///   • Give the object a TRIGGER collider (or a child with a trigger + <see cref="PickupTrigger"/>).
    ///   • When the player walks in, the object registers with the player's <see cref="PlayerInteractor"/>.
    ///   • The interactor picks the best candidate (highest priority, then nearest), shows its prompt
    ///     and calls <see cref="Interact"/> when E is pressed. Only ONE object reacts per key press.
    ///
    /// To make something new interactable: inherit from this class and implement GetPrompt + Interact.
    /// </summary>
    public abstract class CatInteractable : MonoBehaviour
    {
        [Tooltip("Higher wins when several things are in range (e.g. NPC over a pickup).")]
        [SerializeField] protected int interactPriority = 0;

        public virtual int InteractPriority => interactPriority;

        /// <summary>Position used to find the nearest interactable.</summary>
        public virtual Vector3 InteractionPosition => transform.position;

        /// <summary>Text shown at the bottom of the screen, or null if there is nothing to do right now.</summary>
        public abstract string GetPrompt(PlayerCarry player);

        /// <summary>Called when the player presses E while this is the focused object.</summary>
        public abstract void Interact(PlayerCarry player);

        /// <summary>Called when the player presses R with empty paws. Return true if something happened.</summary>
        public virtual bool TryRotate(PlayerCarry player) => false;

        /// <summary>Called when the player walks out of range.</summary>
        public virtual void OnPlayerExitRange(PlayerCarry player) { }

        // ------------------------------------------------------------------ range detection

        protected virtual void OnTriggerEnter(Collider other) => NotifyEnter(other);

        protected virtual void OnTriggerExit(Collider other) => NotifyExit(other);

        protected virtual void OnDisable() => PlayerInteractor.RemoveEverywhere(this);

        /// <summary>Also called by child trigger forwarders (<see cref="PickupTrigger"/>).</summary>
        public void NotifyEnter(Collider other)
        {
            PlayerCarry player = PlayerOf(other);
            if (player != null && isActiveAndEnabled) PlayerInteractor.For(player).Add(this);
        }

        public void NotifyExit(Collider other)
        {
            PlayerCarry player = PlayerOf(other);
            if (player != null) PlayerInteractor.For(player).Remove(this);
        }

        protected static PlayerCarry PlayerOf(Collider other) =>
            other != null ? other.GetComponentInParent<PlayerCarry>() : null;

        /// <summary>
        /// Adds a child sphere trigger if this object has no trigger collider yet, so it is
        /// usable "plug and play". Returns the trigger collider.
        /// </summary>
        /// <param name="alwaysAdd">Add the sphere even if a (maybe tiny) trigger already exists.</param>
        protected Collider EnsureInteractionTrigger(float radius, bool alwaysAdd = false)
        {
            if (!alwaysAdd)
                foreach (Collider c in GetComponentsInChildren<Collider>(true))
                    if (c.isTrigger) return c;

            var go = new GameObject("InteractionRange");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var sphere = go.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            // Keep the radius in world units even if the object is scaled.
            float scale = Mathf.Max(0.0001f, Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z)));
            sphere.radius = radius / scale;
            go.AddComponent<PickupTrigger>().SetTarget(this);
            return sphere;
        }
    }
}
