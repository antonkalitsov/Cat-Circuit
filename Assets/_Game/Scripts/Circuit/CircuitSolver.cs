using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================================
//  CircuitSolver – a small but real DC circuit simulator (nodal analysis).
//
//  Batteries  = ideal voltage + tiny internal resistance
//  Resistors / wires / closed switches = resistors
//  LEDs       = "off" until the voltage across them reaches their forward voltage,
//               then a voltage drop (1.8V red ... 3V blue) plus a small resistance.
//
//  That is enough for everything an 8th-grader meets: series vs parallel, Ohm's law,
//  LED polarity, burnt LEDs, short circuits, switches.
// =====================================================================================
namespace CatCircuit
{
    /// <summary>One element handed to the solver. Built by a CircuitHost from its definition.</summary>
    public class SimPart
    {
        public string id;
        public PartKind kind;
        public int a;                 // entry node (battery "-", LED anode)
        public int b;                 // exit node  (battery "+", LED cathode)
        public float resistance;      // resistors (Ω)
        public float voltage;         // batteries (V)
        public LedColor ledColor = LedColor.Red;
        public bool conducting = true;  // false = open: empty slot, switch off, part not seated

        public bool isSlot;
        public bool seated = true;      // slots only: part sits in the right orientation
        public ComponentData component; // slots only: what is in the slot

        public float ForwardVoltage => LedColors.ForwardVoltage(ledColor);

        public SimPart Clone() => (SimPart)MemberwiseClone();
    }

    /// <summary>What happened to one part after solving.</summary>
    public class PartResult
    {
        public string id;
        public PartKind kind;
        public float current;      // A, positive = flows from A to B (the "arrow" direction)
        public float voltage;      // V across the part (A minus B)
        public float brightness;   // LEDs: 0..1
        public bool lit;           // LEDs: visibly on
        public bool overloaded;    // LEDs: too much current (would burn)
        public bool reversed;      // LEDs: connected backwards
        public bool conducting;
        public bool isSlot;
        public bool seated = true;
        public ComponentData component;
        public LedColor ledColor;
        public float nominalValue; // battery V / resistor Ω

        public float CurrentMilliAmps => current * 1000f;
    }

    public class CircuitResult
    {
        public static readonly CircuitResult Empty = new CircuitResult();

        public readonly Dictionary<string, PartResult> parts =
            new Dictionary<string, PartResult>(StringComparer.OrdinalIgnoreCase);
        public float[] nodeVoltages = new float[0];
        public bool shortCircuit;
        public bool valid;

        // Kept so conditions can ask "what if?" (e.g. what if this switch were off).
        public int nodeCount;
        public List<SimPart> simParts = new List<SimPart>();

        public PartResult Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            parts.TryGetValue(id.Trim(), out PartResult r);
            return r;
        }

        public IEnumerable<PartResult> OfKind(PartKind kind)
        {
            foreach (PartResult p in parts.Values) if (p.kind == kind) yield return p;
        }

        public IEnumerable<PartResult> Leds => OfKind(PartKind.LED);

        public int LitLedCount
        {
            get { int n = 0; foreach (PartResult p in Leds) if (p.lit) n++; return n; }
        }

        public bool AnyCurrent
        {
            get
            {
                foreach (PartResult p in parts.Values)
                    if (p.kind == PartKind.Battery && Mathf.Abs(p.current) > CircuitConstants.LedLitCurrent) return true;
                return false;
            }
        }

        /// <summary>One-line summary for debugging and the inspector.</summary>
        public string Describe()
        {
            if (!valid) return "(no result)";
            var sb = new System.Text.StringBuilder();
            if (shortCircuit) sb.AppendLine("!! SHORT CIRCUIT");
            foreach (PartResult p in parts.Values)
            {
                sb.Append(p.id).Append(" [").Append(p.kind).Append("] ");
                if (!p.conducting) { sb.AppendLine(p.isSlot && p.component == null ? "empty" : "open"); continue; }
                sb.Append(CircuitUnits.MilliAmps(p.current)).Append("  ").Append(p.voltage.ToString("0.00")).Append("V");
                if (p.kind == PartKind.LED)
                {
                    sb.Append("  bright ").Append((p.brightness * 100f).ToString("0")).Append('%');
                    if (p.overloaded) sb.Append("  OVERLOAD");
                    if (p.reversed) sb.Append("  REVERSED");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    public static class CircuitSolver
    {
        private const double Gmin = 1e-9;       // tiny leak to ground so floating nodes don't break the maths
        private const int MaxIterations = 50;

        public static CircuitResult Solve(int nodeCount, List<SimPart> parts)
        {
            var result = new CircuitResult { nodeCount = nodeCount, simParts = parts, valid = true };
            int n = Mathf.Max(nodeCount, 0);

            // LED state: start with everything ON, then switch off LEDs that would carry negative current.
            var ledOn = new Dictionary<SimPart, bool>();
            foreach (SimPart p in parts)
                if (p.kind == PartKind.LED && p.conducting) ledOn[p] = true;

            double[] v = new double[n];
            for (int iter = 0; iter < MaxIterations; iter++)
            {
                v = SolveLinear(n, parts, ledOn);

                bool changed = false;
                foreach (SimPart p in parts)
                {
                    if (!ledOn.ContainsKey(p)) continue;
                    double vd = V(v, p.a) - V(v, p.b);
                    if (ledOn[p])
                    {
                        double i = (vd - p.ForwardVoltage) / CircuitConstants.LedOnResistance;
                        if (i < -1e-9) { ledOn[p] = false; changed = true; }
                    }
                    else if (vd > p.ForwardVoltage + 1e-6)
                    {
                        ledOn[p] = true; changed = true;
                    }
                }
                if (!changed) break;
            }

            result.nodeVoltages = new float[n];
            for (int i = 0; i < n; i++) result.nodeVoltages[i] = (float)v[i];

            foreach (SimPart p in parts)
            {
                var r = new PartResult
                {
                    id = p.id,
                    kind = p.kind,
                    conducting = p.conducting,
                    isSlot = p.isSlot,
                    seated = p.seated,
                    component = p.component,
                    ledColor = p.ledColor,
                    nominalValue = p.kind == PartKind.Battery ? p.voltage : p.resistance,
                };

                double va = V(v, p.a), vb = V(v, p.b);
                // Batteries report their terminal voltage (+ minus -), everything else A minus B.
                r.voltage = (float)(p.kind == PartKind.Battery ? vb - va : va - vb);

                if (p.conducting)
                {
                    switch (p.kind)
                    {
                        case PartKind.Battery:
                            // Current flowing inside the battery from "-" (A) to "+" (B).
                            r.current = (float)((p.voltage - (vb - va)) / CircuitConstants.BatteryInternalResistance);
                            if (Mathf.Abs(r.current) > CircuitConstants.ShortCircuitCurrent) result.shortCircuit = true;
                            break;
                        case PartKind.Resistor:
                            r.current = (float)((va - vb) / Mathf.Max(p.resistance, 1e-3f));
                            break;
                        case PartKind.Wire:
                        case PartKind.Switch:
                            r.current = (float)((va - vb) / CircuitConstants.WireResistance);
                            break;
                        case PartKind.LED:
                            bool on = ledOn.TryGetValue(p, out bool o) && o;
                            r.current = on ? Mathf.Max(0f, (float)((va - vb - p.ForwardVoltage) / CircuitConstants.LedOnResistance)) : 0f;
                            r.brightness = Mathf.Clamp01(r.current / CircuitConstants.LedFullBrightnessCurrent);
                            r.lit = r.current > CircuitConstants.LedLitCurrent;
                            r.overloaded = r.current > CircuitConstants.LedMaxCurrent;
                            r.reversed = (vb - va) > CircuitConstants.LedReverseVoltage;
                            break;
                    }
                }

                result.parts[p.id] = r;
            }

            return result;
        }

        private static double V(double[] v, int node) => node >= 0 && node < v.Length ? v[node] : 0.0;

        /// <summary>Builds the conductance matrix for the current LED states and solves G·v = i.</summary>
        private static double[] SolveLinear(int n, List<SimPart> parts, Dictionary<SimPart, bool> ledOn)
        {
            var G = new double[n, n];
            var I = new double[n];
            for (int i = 0; i < n; i++) G[i, i] += Gmin;

            foreach (SimPart p in parts)
            {
                if (!p.conducting || p.a == p.b) continue;
                switch (p.kind)
                {
                    case PartKind.Resistor:
                        StampConductance(G, p.a, p.b, 1.0 / Math.Max(p.resistance, 1e-3));
                        break;
                    case PartKind.Wire:
                    case PartKind.Switch:
                        StampConductance(G, p.a, p.b, 1.0 / CircuitConstants.WireResistance);
                        break;
                    case PartKind.Battery:
                        {
                            // Norton equivalent: conductance g between - and +, and a push of g·V into "+".
                            double g = 1.0 / CircuitConstants.BatteryInternalResistance;
                            StampConductance(G, p.a, p.b, g);
                            Inject(I, p.b, g * p.voltage);
                            Inject(I, p.a, -g * p.voltage);
                            break;
                        }
                    case PartKind.LED:
                        if (ledOn.TryGetValue(p, out bool on) && on)
                        {
                            // Current A->B = g·(Va - Vb - Vf)
                            double g = 1.0 / CircuitConstants.LedOnResistance;
                            StampConductance(G, p.a, p.b, g);
                            Inject(I, p.a, g * p.ForwardVoltage);
                            Inject(I, p.b, -g * p.ForwardVoltage);
                        }
                        break;
                }
            }

            return Gauss(G, I, n);
        }

        private static void StampConductance(double[,] G, int a, int b, double g)
        {
            if (a >= 0) G[a, a] += g;
            if (b >= 0) G[b, b] += g;
            if (a >= 0 && b >= 0) { G[a, b] -= g; G[b, a] -= g; }
        }

        private static void Inject(double[] I, int node, double amps)
        {
            if (node >= 0) I[node] += amps;
        }

        /// <summary>Gaussian elimination with partial pivoting. Matrices here are tiny (&lt; 50 nodes).</summary>
        private static double[] Gauss(double[,] A, double[] bIn, int n)
        {
            var a = (double[,])A.Clone();
            var b = (double[])bIn.Clone();

            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                double best = Math.Abs(a[col, col]);
                for (int r = col + 1; r < n; r++)
                {
                    double val = Math.Abs(a[r, col]);
                    if (val > best) { best = val; pivot = r; }
                }
                if (best < 1e-18) continue;

                if (pivot != col)
                {
                    for (int c = 0; c < n; c++) { double t = a[col, c]; a[col, c] = a[pivot, c]; a[pivot, c] = t; }
                    double tb = b[col]; b[col] = b[pivot]; b[pivot] = tb;
                }

                for (int r = col + 1; r < n; r++)
                {
                    double f = a[r, col] / a[col, col];
                    if (f == 0) continue;
                    for (int c = col; c < n; c++) a[r, c] -= f * a[col, c];
                    b[r] -= f * b[col];
                }
            }

            var x = new double[n];
            for (int r = n - 1; r >= 0; r--)
            {
                double sum = b[r];
                for (int c = r + 1; c < n; c++) sum -= a[r, c] * x[c];
                x[r] = Math.Abs(a[r, r]) < 1e-18 ? 0 : sum / a[r, r];
            }
            return x;
        }
    }
}
