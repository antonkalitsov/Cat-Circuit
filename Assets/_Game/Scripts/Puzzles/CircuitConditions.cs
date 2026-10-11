using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CatCircuit
{
    /// <summary>All the checks a puzzle stage can use. Add new ones at the END (values are saved as numbers).</summary>
    public enum ConditionType
    {
        LedsLit,                 // every target LED is on
        LedsOff,                 // every target LED is off
        AnyLedLit,               // at least one target LED is on
        LitLedCountAtLeast,      // at least Count target LEDs are on
        LedCurrentBetween,       // every target LED current is between Min and Max (mA)
        LedsBrightnessAtLeast,   // every target LED brightness >= Min (0..1)
        LedsEqualBrightness,     // lit target LEDs differ by at most Max (0..1); needs Count lit
        NoLedOverloaded,         // no target LED gets too much current
        AnyLedOverloaded,        // some target LED is burning
        AnyLedReversed,          // some target LED is connected backwards
        ShortCircuit,            // a battery is shorted
        NoCurrent,               // no current flows at all
        SlotsFilled,             // every target slot has a part (empty targets = all slots)
        SlotsContain,            // every target slot holds exactly Component
        BatteryVoltageAtMost,    // every battery is at most Max volts (and there is one)
        PartCountAtLeast,        // the circuit has at least Count parts of Part Kind
        SwitchTurnsAllLedsOff,   // there is a switch that turns ALL LEDs on/off
        CircuitHasErrors,        // the circuit text does not parse (code puzzle)
    }

    [Serializable]
    public class CircuitCondition
    {
        public ConditionType type;
        [Tooltip("Comma separated ids of LEDs / slots / parts (L1, S2). Empty = all LEDs (or all slots).")]
        public string targets = "";
        [Tooltip("mA for currents, 0..1 for brightness, volts for batteries.")]
        public float min;
        public float max;
        public int count = 1;
        public PartKind partKind = PartKind.LED;
        public ComponentData component;
        [Tooltip("NOT – flips the result.")]
        public bool invert;

        public static CircuitCondition Make(ConditionType type, string targets = "", float min = 0f, float max = 0f, int count = 1, bool invert = false)
            => new CircuitCondition { type = type, targets = targets, min = min, max = max, count = count, invert = invert };

        public bool Evaluate(CircuitResult r, CircuitHost host)
        {
            bool v = EvaluateRaw(r ?? CircuitResult.Empty, host);
            return invert ? !v : v;
        }

        private bool EvaluateRaw(CircuitResult r, CircuitHost host)
        {
            if (type == ConditionType.CircuitHasErrors)
                return host != null && host.Definition != null && host.Definition.HasErrors;
            if (!r.valid) return false;

            switch (type)
            {
                case ConditionType.LedsLit:
                    {
                        var leds = TargetLeds(r, out bool allFound);
                        if (!allFound || leds.Count == 0) return false;
                        foreach (PartResult p in leds) if (!p.lit) return false;
                        return true;
                    }
                case ConditionType.LedsOff:
                    {
                        foreach (PartResult p in TargetLeds(r, out _)) if (p.lit) return false;
                        return true;
                    }
                case ConditionType.AnyLedLit:
                    foreach (PartResult p in TargetLeds(r, out _)) if (p.lit) return true;
                    return false;

                case ConditionType.LitLedCountAtLeast:
                    {
                        int n = 0;
                        foreach (PartResult p in TargetLeds(r, out _)) if (p.lit) n++;
                        return n >= count;
                    }
                case ConditionType.LedCurrentBetween:
                    {
                        var leds = TargetLeds(r, out bool allFound);
                        if (!allFound || leds.Count == 0) return false;
                        foreach (PartResult p in leds)
                            if (p.CurrentMilliAmps < min || p.CurrentMilliAmps > max) return false;
                        return true;
                    }
                case ConditionType.LedsBrightnessAtLeast:
                    {
                        var leds = TargetLeds(r, out bool allFound);
                        if (!allFound || leds.Count == 0) return false;
                        foreach (PartResult p in leds) if (p.brightness < min) return false;
                        return true;
                    }
                case ConditionType.LedsEqualBrightness:
                    {
                        float lo = float.MaxValue, hi = float.MinValue; int lit = 0;
                        foreach (PartResult p in TargetLeds(r, out _))
                        {
                            if (!p.lit) continue;
                            lit++;
                            lo = Mathf.Min(lo, p.brightness);
                            hi = Mathf.Max(hi, p.brightness);
                        }
                        float tolerance = max > 0f ? max : 0.1f;
                        return lit >= Mathf.Max(2, count) && hi - lo <= tolerance;
                    }
                case ConditionType.NoLedOverloaded:
                    foreach (PartResult p in TargetLeds(r, out _)) if (p.overloaded) return false;
                    return true;

                case ConditionType.AnyLedOverloaded:
                    foreach (PartResult p in TargetLeds(r, out _)) if (p.overloaded) return true;
                    return false;

                case ConditionType.AnyLedReversed:
                    foreach (PartResult p in TargetLeds(r, out _)) if (p.reversed) return true;
                    return false;

                case ConditionType.ShortCircuit:
                    return r.shortCircuit;

                case ConditionType.NoCurrent:
                    return !r.AnyCurrent;

                case ConditionType.SlotsFilled:
                    {
                        var board = host as Breadboard;
                        if (board == null) return false;
                        foreach (BreadboardSlot s in TargetSlots(board)) if (s == null || !s.IsOccupied) return false;
                        return true;
                    }
                case ConditionType.SlotsContain:
                    {
                        var board = host as Breadboard;
                        if (board == null) return false;
                        foreach (BreadboardSlot s in TargetSlots(board))
                            if (s == null || s.CurrentComponent != component) return false;
                        return true;
                    }
                case ConditionType.BatteryVoltageAtMost:
                    {
                        int batteries = 0;
                        foreach (PartResult p in r.OfKind(PartKind.Battery))
                        {
                            batteries++;
                            if (p.nominalValue > max + 0.001f) return false;
                        }
                        return batteries > 0;
                    }
                case ConditionType.PartCountAtLeast:
                    {
                        int n = 0;
                        foreach (PartResult p in r.parts.Values) if (p.kind == partKind) n++;
                        return n >= count;
                    }
                case ConditionType.SwitchTurnsAllLedsOff:
                    return SwitchControlsEverything(r);
            }
            return false;
        }

        // ------------------------------------------------------------------ helpers

        private List<PartResult> TargetLeds(CircuitResult r, out bool allFound)
        {
            var list = new List<PartResult>();
            allFound = true;
            string[] ids = SplitTargets();
            if (ids.Length == 0)
            {
                list.AddRange(r.Leds);
                return list;
            }
            foreach (string id in ids)
            {
                PartResult p = r.Get(id);
                if (p != null && p.kind == PartKind.LED) list.Add(p);
                else allFound = false;   // missing, or a slot without an LED in it
            }
            return list;
        }

        private IEnumerable<BreadboardSlot> TargetSlots(Breadboard board)
        {
            string[] ids = SplitTargets();
            if (ids.Length == 0)
            {
                foreach (BreadboardSlot s in board.Slots) yield return s;
                yield break;
            }
            foreach (string id in ids) yield return board.FindSlot(id);
        }

        private string[] SplitTargets()
        {
            if (string.IsNullOrWhiteSpace(targets)) return new string[0];
            var parts = targets.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            return parts;
        }

        /// <summary>Re-simulates with each switch forced on and off.</summary>
        private static bool SwitchControlsEverything(CircuitResult r)
        {
            foreach (SimPart sw in r.simParts)
            {
                if (sw.kind != PartKind.Switch) continue;

                var closed = new List<SimPart>();
                var open = new List<SimPart>();
                foreach (SimPart p in r.simParts)
                {
                    SimPart c1 = p.Clone(), c2 = p.Clone();
                    if (p == sw) { c1.conducting = true; c2.conducting = false; }
                    closed.Add(c1); open.Add(c2);
                }
                CircuitResult on = CircuitSolver.Solve(r.nodeCount, closed);
                CircuitResult off = CircuitSolver.Solve(r.nodeCount, open);
                if (on.LitLedCount > 0 && off.LitLedCount == 0) return true;
            }
            return false;
        }

        public string Describe()
        {
            string t = string.IsNullOrWhiteSpace(targets) ? "all" : targets;
            string s;
            switch (type)
            {
                case ConditionType.LedCurrentBetween: s = $"{type} [{t}] {min}-{max} mA"; break;
                case ConditionType.LedsBrightnessAtLeast: s = $"{type} [{t}] >= {min:0.##}"; break;
                case ConditionType.LitLedCountAtLeast: s = $"{type} [{t}] >= {count}"; break;
                case ConditionType.BatteryVoltageAtMost: s = $"{type} <= {max}V"; break;
                case ConditionType.PartCountAtLeast: s = $"{type} {partKind} >= {count}"; break;
                case ConditionType.SlotsContain: s = $"{type} [{t}] = {(component != null ? component.name : "?")}"; break;
                default: s = $"{type} [{t}]"; break;
            }
            return invert ? "NOT " + s : s;
        }
    }

    /// <summary>A message shown while a stage is not solved yet and ALL of its conditions are true.</summary>
    [Serializable]
    public class CircuitHint
    {
        public List<CircuitCondition> when = new List<CircuitCondition>();
        [TextArea(1, 3)] public string message;

        public static CircuitHint Make(string message, params CircuitCondition[] conditions)
        {
            var h = new CircuitHint { message = message };
            h.when.AddRange(conditions);
            return h;
        }

        public bool Applies(CircuitResult r, CircuitHost host)
        {
            if (when.Count == 0) return false;
            foreach (CircuitCondition c in when) if (!c.Evaluate(r, host)) return false;
            return true;
        }
    }

    /// <summary>One step of a puzzle: objective → conditions → success.</summary>
    [Serializable]
    public class PuzzleStage
    {
        public string name = "Stage";
        [Tooltip("Shown top-left while this stage is active.")]
        [TextArea(1, 3)] public string objective;
        [Tooltip("ALL must be true to finish the stage.")]
        public List<CircuitCondition> conditions = new List<CircuitCondition>();
        [Tooltip("Short message when the stage is done.")]
        [TextArea(1, 3)] public string successMessage;
        [Tooltip("What the quest NPC says when the player comes back after this stage.")]
        [TextArea(2, 4)] public string[] completeDialogue = new string[0];
        [Tooltip("First matching hint is shown while the stage is not solved.")]
        public List<CircuitHint> hints = new List<CircuitHint>();
        public UnityEvent onComplete = new UnityEvent();

        public bool IsSatisfied(CircuitResult r, CircuitHost host)
        {
            if (conditions.Count == 0) return false;
            foreach (CircuitCondition c in conditions) if (!c.Evaluate(r, host)) return false;
            return true;
        }
    }
}
