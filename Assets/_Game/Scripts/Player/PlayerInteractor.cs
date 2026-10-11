using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CatCircuit
{
    /// <summary>
    /// Lives on the player (added automatically by <see cref="PlayerCarry"/>).
    /// Keeps a list of interactables in range, picks ONE focus, shows its prompt and routes keys:
    ///   E – interact with the focus
    ///   R – rotate the held item, or the focus (e.g. a part in a slot) when paws are empty
    ///   Q – drop the held item
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInteractor : MonoBehaviour
    {
        private static readonly List<PlayerInteractor> all = new List<PlayerInteractor>();

        // interactable -> how many of its triggers the player is touching
        private readonly Dictionary<CatInteractable, int> inRange = new Dictionary<CatInteractable, int>();
        private readonly List<CatInteractable> scratch = new List<CatInteractable>();

        private PlayerCarry carry;
        private string shownPrompt;

        public CatInteractable Focus { get; private set; }

        public static PlayerInteractor For(PlayerCarry player)
        {
            PlayerInteractor i = player.GetComponent<PlayerInteractor>();
            if (i == null) i = player.gameObject.AddComponent<PlayerInteractor>();
            return i;
        }

        public static void RemoveEverywhere(CatInteractable target)
        {
            foreach (PlayerInteractor i in all) i.ForceRemove(target);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => all.Clear();

        private void Awake() => carry = GetComponent<PlayerCarry>();
        private void OnEnable() => all.Add(this);
        private void OnDisable() { all.Remove(this); ShowPrompt(null); }

        public void Add(CatInteractable target)
        {
            inRange.TryGetValue(target, out int n);
            inRange[target] = n + 1;
        }

        public void Remove(CatInteractable target)
        {
            if (!inRange.TryGetValue(target, out int n)) return;
            if (n <= 1) ForceRemove(target);
            else inRange[target] = n - 1;
        }

        private void ForceRemove(CatInteractable target)
        {
            if (!inRange.Remove(target)) return;
            if (Focus == target) Focus = null;
            if (target != null && carry != null) target.OnPlayerExitRange(carry);
        }

        private void Update()
        {
            if (carry == null) return;

            if (GameInput.IsBlocked)
            {
                ShowPrompt(null);
                return;
            }

            Focus = ChooseFocus(out string prompt);

            // While holding something with nothing else to do, remind the player what they carry.
            if (prompt == null && carry.IsHoldingItem)
                prompt = UIText.Holding(carry.GetHeldItemName());
            ShowPrompt(prompt);

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.eKey.wasPressedThisFrame && Focus != null)
            {
                Focus.Interact(carry);
            }
            else if (kb.rKey.wasPressedThisFrame)
            {
                if (carry.IsHoldingItem) carry.RotateHeld();
                else if (Focus != null) Focus.TryRotate(carry);
            }
            else if (kb.qKey.wasPressedThisFrame && carry.IsHoldingItem)
            {
                carry.Drop();
            }
        }

        private CatInteractable ChooseFocus(out string prompt)
        {
            prompt = null;
            CatInteractable best = null;
            int bestPriority = int.MinValue;
            float bestDistance = float.MaxValue;
            Vector3 me = transform.position;

            scratch.Clear();
            scratch.AddRange(inRange.Keys);
            foreach (CatInteractable c in scratch)
            {
                if (c == null || !c.isActiveAndEnabled) { inRange.Remove(c); continue; }

                string p = c.GetPrompt(carry);
                if (p == null) continue;

                int priority = c.InteractPriority;
                float d = (c.InteractionPosition - me).sqrMagnitude;
                if (priority > bestPriority || (priority == bestPriority && d < bestDistance))
                {
                    best = c; bestPriority = priority; bestDistance = d; prompt = p;
                }
            }
            return best;
        }

        private void ShowPrompt(string text)
        {
            if (text == shownPrompt) return;
            shownPrompt = text;
            GameUI.Prompt(text);
        }
    }
}
