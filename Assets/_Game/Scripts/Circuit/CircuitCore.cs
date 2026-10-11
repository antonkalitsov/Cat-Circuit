using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// =====================================================================================
//  CatCircuit – shared circuit types.
//  Pure C# (no MonoBehaviours) so the same engine is used by breadboards (slots in the
//  world) and by the code puzzle (circuit typed by the player).
// =====================================================================================
namespace CatCircuit
{
    /// <summary>Every kind of part the simulator understands.</summary>
    public enum PartKind
    {
        Battery,
        Resistor,
        LED,
        Wire,
        Switch,
        /// <summary>A place on a breadboard where the player puts a part. Only allowed in board circuits.</summary>
        Slot,
        /// <summary>Something that does not conduct (empty slot, unknown item).</summary>
        None
    }

    public enum LedColor { Red, Orange, Yellow, Green, Blue, White }

    /// <summary>Tuning numbers for the simulation. Change them here, not in the solver.</summary>
    public static class CircuitConstants
    {
        public const float BatteryInternalResistance = 0.5f;   // Ω – keeps short circuits finite
        public const float WireResistance = 0.01f;              // Ω
        public const float LedOnResistance = 15f;               // Ω – slope of an LED once it is on
        public const float DefaultResistance = 330f;            // Ω – used when a resistor has no value
        public const float DefaultBatteryVoltage = 9f;          // V

        public const float LedFullBrightnessCurrent = 0.020f;   // A – 20 mA = 100 % brightness
        public const float LedMaxCurrent = 0.030f;              // A – above this the LED "burns" (overloaded)
        public const float LedLitCurrent = 0.0003f;             // A – below this we call the LED "off"
        public const float LedReverseVoltage = 0.5f;            // V – LED counts as "backwards" above this
        public const float ShortCircuitCurrent = 1.0f;          // A – battery current that means "short circuit"
    }

    public static class LedColors
    {
        public static float ForwardVoltage(LedColor c)
        {
            switch (c)
            {
                case LedColor.Red: return 1.8f;
                case LedColor.Orange: return 2.0f;
                case LedColor.Yellow: return 2.1f;
                case LedColor.Green: return 2.2f;
                case LedColor.Blue: return 3.0f;
                case LedColor.White: return 3.0f;
                default: return 2f;
            }
        }

        public static Color ToColor(LedColor c)
        {
            switch (c)
            {
                case LedColor.Red: return new Color(1f, 0.05f, 0.05f);
                case LedColor.Orange: return new Color(1f, 0.45f, 0f);
                case LedColor.Yellow: return new Color(1f, 0.9f, 0.1f);
                case LedColor.Green: return new Color(0.1f, 1f, 0.2f);
                case LedColor.Blue: return new Color(0.15f, 0.35f, 1f);
                case LedColor.White: return Color.white;
                default: return Color.red;
            }
        }

        /// <summary>Accepts English and Bulgarian colour names.</summary>
        public static bool TryParse(string word, out LedColor color)
        {
            switch (word.Trim().ToLowerInvariant())
            {
                case "red": case "червен": case "червено": color = LedColor.Red; return true;
                case "orange": case "оранжев": case "оранжево": color = LedColor.Orange; return true;
                case "yellow": case "жълт": case "жълто": color = LedColor.Yellow; return true;
                case "green": case "зелен": case "зелено": color = LedColor.Green; return true;
                case "blue": case "син": case "синьо": color = LedColor.Blue; return true;
                case "white": case "бял": case "бяло": color = LedColor.White; return true;
                default: color = LedColor.Red; return false;
            }
        }
    }

    /// <summary>Parses and prints values like "9V", "220", "1k", "4.7kΩ", "2k2", "1M".</summary>
    public static class CircuitUnits
    {
        public static bool TryParseValue(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim().ToLowerInvariant().Replace(',', '.');
            string[] units = { "ohms", "ohm", "ом", "ω", "v", "в" };
            foreach (string u in units)
            {
                if (s.EndsWith(u)) { s = s.Substring(0, s.Length - u.Length); break; }
            }
            if (s.Length == 0) return false;

            float multiplier = 1f;
            int letterIndex = -1;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == 'k' || c == 'к') { multiplier = 1e3f; letterIndex = i; break; }
                if (c == 'm' || c == 'м') { multiplier = 1e6f; letterIndex = i; break; }
            }

            if (letterIndex >= 0)
            {
                // "2k2" -> "2.2", "4.7k" -> "4.7"
                string before = s.Substring(0, letterIndex);
                string after = s.Substring(letterIndex + 1);
                s = after.Length > 0 ? before + "." + after : before;
            }

            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)) return false;
            value = number * multiplier;
            return true;
        }

        public static string Ohms(float ohms)
        {
            if (ohms >= 1e6f) return (ohms / 1e6f).ToString("0.##", CultureInfo.InvariantCulture) + "MΩ";
            if (ohms >= 1e3f) return (ohms / 1e3f).ToString("0.##", CultureInfo.InvariantCulture) + "kΩ";
            return ohms.ToString("0.##", CultureInfo.InvariantCulture) + "Ω";
        }

        public static string Volts(float v) => v.ToString("0.##", CultureInfo.InvariantCulture) + "V";

        public static string MilliAmps(float amps) => (amps * 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "mA";
    }

    public enum MessageLevel { Warning, Error }

    [Serializable]
    public class CircuitMessage
    {
        public MessageLevel level;
        public int line;      // 1-based, 0 = no specific line
        public string text;

        public CircuitMessage(MessageLevel level, int line, string text)
        {
            this.level = level; this.line = line; this.text = text;
        }

        public override string ToString() => line > 0 ? $"Ред {line}: {text}" : text;
    }

    /// <summary>One part declared in a circuit script (e.g. "resistor R1 220").</summary>
    public class PartDef
    {
        public string id;
        public PartKind kind;
        public int line;

        public float value;                       // volts for batteries, ohms for resistors
        public LedColor ledColor = LedColor.Red;
        public bool closed = true;                // switches
        public bool wireWhenEmpty;                // slots: an empty slot behaves like a piece of wire

        /// <summary>Node the current ENTERS the part from (battery "-", LED anode "+").</summary>
        public int nodeA = -1;
        /// <summary>Node the current LEAVES the part to (battery "+", LED cathode "-").</summary>
        public int nodeB = -1;

        public bool usedInChain;                  // appears in the middle of a "->" chain
        public bool terminalReferenced;           // referenced as ID+ / ID-

        public bool IsConnected => usedInChain || terminalReferenced;
    }

    /// <summary>One element of a chain line, used by the floor builder to lay the circuit out.</summary>
    public class ChainItem
    {
        public bool isPart;
        public PartDef part;      // when isPart
        public int node = -1;     // when !isPart (junction or terminal reference)
        public string label;      // text as written by the player
    }

    public class ChainDef
    {
        public int line;
        public readonly List<ChainItem> items = new List<ChainItem>();
    }

    /// <summary>Result of parsing a circuit script.</summary>
    public class CircuitDefinition
    {
        public string sourceText = "";
        public readonly List<PartDef> parts = new List<PartDef>();
        public readonly List<ChainDef> chains = new List<ChainDef>();
        public readonly List<CircuitMessage> messages = new List<CircuitMessage>();
        /// <summary>Human-readable name of every node ("A", "B1+", "n3").</summary>
        public readonly List<string> nodeNames = new List<string>();
        /// <summary>Named junction -> node index.</summary>
        public readonly Dictionary<string, int> junctions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public int NodeCount => nodeNames.Count;

        public bool HasErrors
        {
            get
            {
                foreach (CircuitMessage m in messages) if (m.level == MessageLevel.Error) return true;
                return false;
            }
        }

        public PartDef Find(string id)
        {
            foreach (PartDef p in parts)
                if (string.Equals(p.id, id, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public IEnumerable<PartDef> OfKind(PartKind kind)
        {
            foreach (PartDef p in parts) if (p.kind == kind) yield return p;
        }

        public string MessagesAsText(bool errorsOnly = false)
        {
            var sb = new System.Text.StringBuilder();
            foreach (CircuitMessage m in messages)
            {
                if (errorsOnly && m.level != MessageLevel.Error) continue;
                sb.AppendLine((m.level == MessageLevel.Error ? "[X] " : "[!] ") + m);
            }
            return sb.ToString();
        }
    }
}
