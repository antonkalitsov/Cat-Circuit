using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// A switch built by the <see cref="CodeCircuitBuilder"/>. The cat can flip it with E.
    /// Creates its own simple lever visual so it works without a model.
    /// </summary>
    public class CircuitSwitch : CatInteractable
    {
        private CodeCircuitBuilder builder;
        private string switchId;
        private Transform lever;

        public void Setup(CodeCircuitBuilder owner, string id, bool closed)
        {
            builder = owner;
            switchId = id;
            EnsureInteractionTrigger(1.2f);
            BuildLever();
            ShowState(closed);
        }

        public override string GetPrompt(PlayerCarry player) =>
            builder != null ? UIText.ToggleSwitch(switchId, builder.IsSwitchClosed(switchId)) : null;

        public override void Interact(PlayerCarry player)
        {
            if (builder != null) builder.ToggleSwitch(switchId);
            GameAudio.Play(Sfx.Switch);
        }

        public void ShowState(bool closed)
        {
            if (lever != null) lever.localRotation = Quaternion.Euler(0f, 0f, closed ? 0f : 35f);
        }

        private void BuildLever()
        {
            if (lever != null) return;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = "Lever";
            lever = go.transform;
            lever.SetParent(transform, false);
            lever.localPosition = new Vector3(0f, 0.12f, 0f);
            lever.localScale = new Vector3(0.4f, 0.04f, 0.08f);
            Renderer r = go.GetComponent<Renderer>();
            if (r != null) r.material.color = new Color(0.9f, 0.3f, 0.2f);
        }
    }
}
