using System;
using System.Collections.Generic;

// =====================================================================================
//  CircuitScript – the tiny circuit language used by breadboards AND the code puzzle.
//
//      # comments start with # or //
//      battery B1 9V          (батерия)   value in volts, default 9V
//      resistor R1 220        (резистор)  220, 1k, 4.7k, 2k2, 10kΩ ...
//      led L1 red             (лед)       red/green/blue/yellow/orange/white
//      wire W1                (жица)
//      switch S1 on           (ключ)      on / off
//      slot SA wire           (слот)      only in breadboard circuits; "wire" = acts like a wire when empty
//
//      B1+ -> R1 -> L1 -> B1-      a chain: arrows show the direction the current flows
//      B1+ -> A                    A is a junction (any new name)
//      A -> L2 -> B1-              two chains sharing A = parallel
//
//  Rules
//    * A chain starts and ends with a POINT: a junction name (A, X, top ...) or a terminal (B1+, L1-).
//    * Every part may sit in the MIDDLE of one chain only. Use ID+ / ID- to connect more wires to it.
//    * Batteries: current goes in at "-" and out at "+".  LEDs: in at "+" (long leg), out at "-".
// =====================================================================================
namespace CatCircuit
{
    public static class CircuitScript
    {
        private static readonly Dictionary<string, PartKind> Keywords =
            new Dictionary<string, PartKind>(StringComparer.OrdinalIgnoreCase)
            {
                { "battery", PartKind.Battery }, { "bat", PartKind.Battery }, { "батерия", PartKind.Battery },
                { "resistor", PartKind.Resistor }, { "res", PartKind.Resistor }, { "резистор", PartKind.Resistor },
                { "led", PartKind.LED }, { "лед", PartKind.LED }, { "светодиод", PartKind.LED }, { "диод", PartKind.LED },
                { "wire", PartKind.Wire }, { "жица", PartKind.Wire }, { "проводник", PartKind.Wire },
                { "switch", PartKind.Switch }, { "ключ", PartKind.Switch }, { "прекъсвач", PartKind.Switch },
                { "slot", PartKind.Slot }, { "слот", PartKind.Slot },
            };

        /// <summary>Parse a circuit script. Never throws; problems end up in <see cref="CircuitDefinition.messages"/>.</summary>
        /// <param name="allowSlots">Breadboards may declare slots; the code puzzle may not.</param>
        public static CircuitDefinition Parse(string text, bool allowSlots)
        {
            var def = new CircuitDefinition { sourceText = text ?? "" };
            var chainLines = new List<KeyValuePair<int, string>>();

            string[] lines = def.sourceText.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                string line = StripComment(lines[i]).Trim();
                if (line.Length == 0) continue;

                string normalized = NormalizeArrows(line);
                if (normalized.Contains("->"))
                {
                    chainLines.Add(new KeyValuePair<int, string>(lineNo, normalized));
                    continue;
                }
                ParseDeclaration(def, lineNo, line, allowSlots);
            }

            BuildConnections(def, chainLines);
            AddWarnings(def);
            return def;
        }

        // ------------------------------------------------------------------ declarations

        private static void ParseDeclaration(CircuitDefinition def, int lineNo, string line, bool allowSlots)
        {
            var tokens = new List<string>();
            foreach (string t in line.Split(new[] { ' ', '\t', ',', '=', ':' }, StringSplitOptions.RemoveEmptyEntries))
                tokens.Add(t);
            if (tokens.Count == 0) return;

            if (!Keywords.TryGetValue(tokens[0], out PartKind kind))
            {
                Error(def, lineNo, $"Не познавам думата '{tokens[0]}'. Започни реда с battery, resistor, led, wire или switch – или напиши връзка със стрелки (->).");
                return;
            }
            if (kind == PartKind.Slot && !allowSlots)
            {
                Error(def, lineNo, "Слотове (slot) има само на платките в къщите.");
                return;
            }
            if (tokens.Count < 2)
            {
                Error(def, lineNo, $"Дай име на частта, например: {tokens[0]} {DefaultIdFor(kind)}");
                return;
            }

            string id = tokens[1];
            if (!IsIdentifier(id))
            {
                Error(def, lineNo, $"'{id}' не е добро име. Използвай букви и цифри, напр. R1, L2, B.");
                return;
            }
            if (def.Find(id) != null)
            {
                Error(def, lineNo, $"Вече има част с име '{id}'. Всяко име трябва да е различно.");
                return;
            }

            var part = new PartDef { id = id, kind = kind, line = lineNo };
            var extra = tokens.GetRange(2, tokens.Count - 2);

            switch (kind)
            {
                case PartKind.Battery:
                    part.value = CircuitConstants.DefaultBatteryVoltage;
                    if (extra.Count > 0)
                    {
                        if (!CircuitUnits.TryParseValue(extra[0], out float v) || v <= 0f || v > 48f)
                        {
                            Error(def, lineNo, $"Напрежението '{extra[0]}' не става. Пиши напр. 9V или 4.5V (най-много 48V).");
                            return;
                        }
                        part.value = v;
                        extra.RemoveAt(0);
                    }
                    break;

                case PartKind.Resistor:
                    if (extra.Count == 0 || !CircuitUnits.TryParseValue(extra[0], out float r) || r <= 0f)
                    {
                        Error(def, lineNo, $"Резисторът {id} има нужда от стойност, напр. resistor {id} 220 или resistor {id} 1k.");
                        return;
                    }
                    part.value = r;
                    extra.RemoveAt(0);
                    break;

                case PartKind.LED:
                    if (extra.Count > 0)
                    {
                        if (!LedColors.TryParse(extra[0], out LedColor c))
                        {
                            Error(def, lineNo, $"Не познавам цвят '{extra[0]}'. Има: red, green, blue, yellow, orange, white.");
                            return;
                        }
                        part.ledColor = c;
                        extra.RemoveAt(0);
                    }
                    break;

                case PartKind.Switch:
                    if (extra.Count > 0)
                    {
                        string s = extra[0].ToLowerInvariant();
                        if (s == "on" || s == "closed" || s == "вкл" || s == "включен") part.closed = true;
                        else if (s == "off" || s == "open" || s == "изкл" || s == "изключен") part.closed = false;
                        else
                        {
                            Error(def, lineNo, $"Ключът може да е on или off, а не '{extra[0]}'.");
                            return;
                        }
                        extra.RemoveAt(0);
                    }
                    break;

                case PartKind.Slot:
                    if (extra.Count > 0)
                    {
                        string s = extra[0].ToLowerInvariant();
                        if (s == "wire" || s == "жица" || s == "short")
                        {
                            part.wireWhenEmpty = true;
                            extra.RemoveAt(0);
                        }
                    }
                    break;
            }

            foreach (string e in extra)
                Warn(def, lineNo, $"Не разбирам '{e}' – пропускам го.");

            def.parts.Add(part);
        }

        // ------------------------------------------------------------------ connections

        private class UnionFind
        {
            private readonly List<int> parent = new List<int>();
            public int Add() { parent.Add(parent.Count); return parent.Count - 1; }
            public int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            public void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
        }

        private static void BuildConnections(CircuitDefinition def, List<KeyValuePair<int, string>> chainLines)
        {
            var uf = new UnionFind();
            // Two terminals per part: index 2*i = A (entry), 2*i+1 = B (exit)
            var termA = new Dictionary<PartDef, int>();
            var termB = new Dictionary<PartDef, int>();
            foreach (PartDef p in def.parts) { termA[p] = uf.Add(); termB[p] = uf.Add(); }

            var junctionIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var junctionUses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var junctionFirstLine = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var partChainLine = new Dictionary<PartDef, int>();
            var rawChains = new List<KeyValuePair<ChainDef, List<int>>>();  // chain + union index per item (-1 for parts)

            foreach (var entry in chainLines)
            {
                int lineNo = entry.Key;
                string[] rawTokens = entry.Value.Split(new[] { "->" }, StringSplitOptions.None);
                if (rawTokens.Length < 2) continue;

                var chain = new ChainDef { line = lineNo };
                var unionIdx = new List<int>();
                bool ok = true;

                for (int t = 0; t < rawTokens.Length; t++)
                {
                    string token = rawTokens[t].Trim();
                    if (token.Length == 0)
                    {
                        Error(def, lineNo, "Има празно място между стрелките.");
                        ok = false; break;
                    }

                    // Terminal reference: ID+ or ID-
                    char last = token[token.Length - 1];
                    if ((last == '+' || last == '-') && token.Length > 1)
                    {
                        string baseId = token.Substring(0, token.Length - 1).Trim();
                        PartDef target = def.Find(baseId);
                        if (target == null)
                        {
                            Error(def, lineNo, $"Няма част с име '{baseId}' (от '{token}'). Първо я обяви, напр. battery {baseId} 9V.");
                            ok = false; break;
                        }
                        bool plus = last == '+';
                        // Battery: current leaves through "+", so "+" is the exit side (B).
                        // Everything else: "+" is the entry side (A) – for LEDs that is the anode.
                        int idx = target.kind == PartKind.Battery ? (plus ? termB[target] : termA[target])
                                                                   : (plus ? termA[target] : termB[target]);
                        target.terminalReferenced = true;
                        chain.items.Add(new ChainItem { isPart = false, label = baseId + last });
                        unionIdx.Add(idx);
                        continue;
                    }

                    PartDef part = def.Find(token);
                    if (part != null)
                    {
                        if (t == 0 || t == rawTokens.Length - 1)
                        {
                            Error(def, lineNo, $"Веригата не може да започва или завършва с частта '{token}'. Използвай {token}+ или {token}-, или име на точка (напр. A).");
                            ok = false; break;
                        }
                        if (part.usedInChain)
                        {
                            Error(def, lineNo, $"'{token}' вече стои в средата на верига на ред {partChainLine[part]}. За още връзки използвай {token}+ или {token}-.");
                            ok = false; break;
                        }
                        part.usedInChain = true;
                        partChainLine[part] = lineNo;
                        chain.items.Add(new ChainItem { isPart = true, part = part, label = part.id });
                        unionIdx.Add(-1);
                        continue;
                    }

                    if (!IsIdentifier(token))
                    {
                        Error(def, lineNo, $"Не разбирам '{token}'. Точките трябва да имат име от букви и цифри (напр. A, X1).");
                        ok = false; break;
                    }
                    if (Keywords.ContainsKey(token))
                    {
                        Error(def, lineNo, $"'{token}' е запазена дума – избери друго име за точката.");
                        ok = false; break;
                    }

                    if (!junctionIndex.TryGetValue(token, out int j))
                    {
                        j = uf.Add();
                        junctionIndex[token] = j;
                        junctionUses[token] = 0;
                        junctionFirstLine[token] = lineNo;
                    }
                    junctionUses[token]++;
                    chain.items.Add(new ChainItem { isPart = false, label = token });
                    unionIdx.Add(j);
                }

                if (!ok) continue;

                // Walk the chain and connect neighbours.
                int prev = unionIdx[0];
                for (int k = 1; k < chain.items.Count; k++)
                {
                    ChainItem item = chain.items[k];
                    if (item.isPart)
                    {
                        uf.Union(prev, termA[item.part]);
                        prev = termB[item.part];
                    }
                    else
                    {
                        uf.Union(prev, unionIdx[k]);
                        prev = unionIdx[k];
                    }
                }

                rawChains.Add(new KeyValuePair<ChainDef, List<int>>(chain, unionIdx));
            }

            // ---- compress union roots into node numbers
            var rootToNode = new Dictionary<int, int>();
            int NodeOf(int unionIndex)
            {
                int root = uf.Find(unionIndex);
                if (!rootToNode.TryGetValue(root, out int n))
                {
                    n = def.nodeNames.Count;
                    rootToNode[root] = n;
                    def.nodeNames.Add("n" + n);
                }
                return n;
            }

            foreach (PartDef p in def.parts)
            {
                p.nodeA = NodeOf(termA[p]);
                p.nodeB = NodeOf(termB[p]);
            }
            foreach (var kv in junctionIndex)
            {
                int n = NodeOf(kv.Value);
                def.junctions[kv.Key] = n;
                def.nodeNames[n] = kv.Key;   // junction names are the nicest labels
            }
            // Name battery nodes after their terminals when no junction name was given.
            foreach (PartDef p in def.parts)
            {
                if (p.kind != PartKind.Battery) continue;
                if (def.nodeNames[p.nodeB].StartsWith("n")) def.nodeNames[p.nodeB] = p.id + "+";
                if (def.nodeNames[p.nodeA].StartsWith("n")) def.nodeNames[p.nodeA] = p.id + "-";
            }

            foreach (var kv in rawChains)
            {
                ChainDef chain = kv.Key;
                for (int k = 0; k < chain.items.Count; k++)
                    if (!chain.items[k].isPart) chain.items[k].node = NodeOf(kv.Value[k]);
                def.chains.Add(chain);
            }

            foreach (var kv in junctionUses)
            {
                if (kv.Value == 1)
                    Warn(def, junctionFirstLine[kv.Key], $"Точката '{kv.Key}' е използвана само веднъж – грешно име ли е? Ако е част, първо я обяви.");
            }
        }

        private static void AddWarnings(CircuitDefinition def)
        {
            if (def.HasErrors) return;

            bool hasSource = false;
            foreach (PartDef p in def.parts)
            {
                if (p.kind == PartKind.Battery || p.kind == PartKind.Slot) hasSource = true;

                if (!p.IsConnected)
                    Warn(def, p.line, $"'{p.id}' не е свързан с нищо.");
                else if (p.nodeA == p.nodeB && p.kind != PartKind.Slot)
                    Warn(def, p.line, $"Двата края на '{p.id}' са свързани един с друг – през него няма да тече ток.");
            }
            if (!hasSource && def.parts.Count > 0)
                Warn(def, 0, "Няма батерия – нищо няма да светне.");
        }

        // ------------------------------------------------------------------ helpers

        private static string StripComment(string line)
        {
            int hash = line.IndexOf('#');
            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            int cut = -1;
            if (hash >= 0) cut = hash;
            if (slashes >= 0 && (cut < 0 || slashes < cut)) cut = slashes;
            return cut >= 0 ? line.Substring(0, cut) : line;
        }

        private static string NormalizeArrows(string line)
        {
            return line.Replace("→", "->").Replace("—>", "->").Replace("–>", "->").Replace("=>", "->");
        }

        public static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s) || !(char.IsLetter(s[0]) || s[0] == '_')) return false;
            foreach (char c in s)
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }

        private static string DefaultIdFor(PartKind k)
        {
            switch (k)
            {
                case PartKind.Battery: return "B1 9V";
                case PartKind.Resistor: return "R1 220";
                case PartKind.LED: return "L1 red";
                case PartKind.Switch: return "S1 on";
                case PartKind.Slot: return "S1";
                default: return "W1";
            }
        }

        private static void Error(CircuitDefinition d, int line, string text) =>
            d.messages.Add(new CircuitMessage(MessageLevel.Error, line, text));

        private static void Warn(CircuitDefinition d, int line, string text) =>
            d.messages.Add(new CircuitMessage(MessageLevel.Warning, line, text));

        /// <summary>Short syntax help shown in the code terminal and the README.</summary>
        public const string HelpText =
@"ЧАСТИ (една на ред)
  battery B 9V        батерия
  resistor R1 220     резистор (220, 1k, 4.7k)
  led L1 red          светодиод (red, green, blue...)
  switch S1 on        ключ (on / off)
  wire W1             жица

ВРЪЗКИ (стрелката = посоката на тока)
  B+ -> R1 -> L1 -> B-

УСПОРЕДНО: дай име на точка
  B+ -> R1 -> A
  A -> L1 -> B-
  A -> L2 -> B-

ПРАВИЛА
  • Токът излиза от B+ и се връща в B-
  • LED: токът влиза в + (дългото краче)
  • Всяка част – в средата само на една
    верига. За още връзки: R1+ или R1-
  • # започва коментар";
    }
}
