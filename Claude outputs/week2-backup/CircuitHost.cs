using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Base class for anything that owns a circuit and simulates it:
    ///   • <see cref="Breadboard"/>          – circuit text + slots the player fills in the world
    ///   • <see cref="CodeCircuitBuilder"/>  – circuit typed by the player in the code terminal
    ///
    /// Call <see cref="MarkDirty"/> whenever something changes; the circuit is re-solved once at the
    /// end of the frame and <see cref="Solved"/> fires (puzzles and LEDs listen to it).
    /// </summary>
    public abstract class CircuitHost : MonoBehaviour
    {
        public CircuitDefinition Definition { get; protected set; }
        public CircuitResult Result { get; private set; } = CircuitResult.Empty;

        /// <summary>Problems found while building the simulation (e.g. a slot from the text is missing in the scene).</summary>
        public readonly List<CircuitMessage> RuntimeMessages = new List<CircuitMessage>();

        /// <summary>Fired after every solve.</summary>
        public event Action<CircuitHost> Solved;

        private readonly List<LEDLight> lights = new List<LEDLight>();
        private bool dirty = true;
        private bool wasDangerous;
        private int solveCount;

        public void MarkDirty() => dirty = true;

        /// <summary>Re-simulate right now (normally you just call MarkDirty).</summary>
        public void SolveNow()
        {
            dirty = false;
            RuntimeMessages.Clear();

            var sim = new List<SimPart>();
            Result = BuildSimulation(sim, out int nodeCount)
                ? CircuitSolver.Solve(nodeCount, sim)
                : CircuitResult.Empty;

            for (int i = lights.Count - 1; i >= 0; i--)
            {
                if (lights[i] == null) { lights.RemoveAt(i); continue; }
                lights[i].ApplyResult(Result);
            }

            // Crackle when a short circuit or a burning LED appears (not on the very first solve).
            bool dangerous = Result.shortCircuit;
            foreach (PartResult p in Result.Leds) if (p.overloaded) { dangerous = true; break; }
            if (dangerous && !wasDangerous && solveCount > 0) GameAudio.Play(Sfx.Zap);
            wasDangerous = dangerous;
            solveCount++;

            OnSolved(Result);
            Solved?.Invoke(this);
        }

        /// <summary>Fill <paramref name="parts"/> with everything to simulate. Return false if the circuit is broken.</summary>
        protected abstract bool BuildSimulation(List<SimPart> parts, out int nodeCount);

        protected virtual void OnSolved(CircuitResult result) { }

        /// <summary>Called by <see cref="CircuitPuzzle"/> when all of its stages are done.</summary>
        public virtual void NotifyPuzzleComplete() { }

        protected virtual void Start() => SolveNow();

        protected virtual void LateUpdate()
        {
            if (dirty) SolveNow();
        }

        // ---------------------------------------------------------------- LEDs

        public void RegisterLight(LEDLight light)
        {
            if (light == null) return;
            if (!lights.Contains(light)) lights.Add(light);
            light.ApplyResult(Result);
        }

        public void UnregisterLight(LEDLight light) => lights.Remove(light);

        /// <summary>All LEDs currently driven by this circuit.</summary>
        public IReadOnlyList<LEDLight> Lights => lights;

        /// <summary>Where effects (sparkles) appear if no LED is lit.</summary>
        public virtual Vector3 EffectCenter => transform.position + Vector3.up * 0.3f;

        // ---------------------------------------------------------------- helpers

        /// <summary>Turns a part from the circuit text into a simulator element.</summary>
        protected static SimPart FromDefinition(PartDef d)
        {
            var sp = new SimPart { id = d.id, kind = d.kind, a = d.nodeA, b = d.nodeB, ledColor = d.ledColor };
            switch (d.kind)
            {
                case PartKind.Battery: sp.voltage = d.value; break;
                case PartKind.Resistor: sp.resistance = d.value; break;
                case PartKind.Switch: sp.conducting = d.closed; break;
            }
            return sp;
        }

        /// <summary>Turns a physical part (ComponentData) into a simulator element between two nodes.</summary>
        public static SimPart FromComponent(string id, ComponentData data, int a, int b)
        {
            var sp = new SimPart { id = id, a = a, b = b, component = data };
            if (data == null) { sp.kind = PartKind.None; sp.conducting = false; return sp; }

            sp.kind = data.Kind;
            switch (sp.kind)
            {
                case PartKind.Battery: sp.voltage = data.Voltage; break;
                case PartKind.Resistor: sp.resistance = data.Resistance; break;
                case PartKind.LED: sp.ledColor = data.ledColor; break;
                case PartKind.Wire: break;
                case PartKind.Switch: break;
                default: sp.conducting = false; break;
            }
            return sp;
        }
    }
}
