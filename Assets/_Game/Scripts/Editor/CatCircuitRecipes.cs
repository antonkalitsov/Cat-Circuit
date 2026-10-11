using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CatCircuit.EditorTools
{
    /// <summary>
    /// Ready-made circuits + puzzle stages for the existing houses and the Mayor's code puzzle.
    /// Used by the upgrade tool and the "Create" menus. Handy as examples for new puzzles too.
    /// </summary>
    public static class CatCircuitRecipes
    {
        // =====================================================================  House 1 & 2 (generic)

        public class SlotInfo
        {
            public string id;
            public string label;
            public PartKind kind;   // what the old slot expected
        }

        /// <summary>
        /// Builds a circuit for an old "fill the slots and the LED lights" board.
        /// Anything the slots don't provide (battery / resistor / LED) is added as a fixed part.
        /// </summary>
        public static string GenericBoardCircuit(List<SlotInfo> slots, out string indicatorId)
        {
            bool hasBattery = slots.Exists(s => s.kind == PartKind.Battery);
            bool hasResistor = slots.Exists(s => s.kind == PartKind.Resistor);
            SlotInfo ledSlot = slots.Find(s => s.kind == PartKind.LED);
            indicatorId = ledSlot != null ? ledSlot.id : "L1";

            var sb = new StringBuilder();
            sb.AppendLine("# Upgraded automatically from the old puzzle – edit freely.");
            if (!hasBattery) sb.AppendLine("battery B 9V");
            if (!hasBattery && !hasResistor) sb.AppendLine("resistor R1 330      # built into the board");
            if (ledSlot == null) sb.AppendLine("led L1 red           # the LED model on the table");
            foreach (SlotInfo s in slots) sb.AppendLine($"slot {s.id}              # {s.label}");
            sb.AppendLine();

            // Battery slots first so the chain reads naturally.
            var ordered = new List<SlotInfo>(slots);
            ordered.Sort((a, b) => (a.kind == PartKind.Battery ? 0 : 1).CompareTo(b.kind == PartKind.Battery ? 0 : 1));
            var chain = new List<string>();
            if (!hasBattery && !hasResistor) chain.Add("R1");
            foreach (SlotInfo s in ordered) chain.Add(s.id);
            if (ledSlot == null) chain.Add("L1");

            sb.AppendLine(hasBattery
                ? "X -> " + string.Join(" -> ", chain) + " -> X"
                : "B+ -> " + string.Join(" -> ", chain) + " -> B-");
            return sb.ToString();
        }

        public static List<PuzzleStage> GenericBoardStages(string objective, string[] completeDialogue, string indicatorId, bool polarity, string npcName)
        {
            var stage = new PuzzleStage
            {
                name = polarity ? "LED polarity" : "Closed circuit",
                objective = objective,
                successMessage = $"LED-ът светна! Говори с {npcName}.",
                completeDialogue = completeDialogue ?? new string[0],
            };
            stage.conditions.Add(CircuitCondition.Make(ConditionType.LedsLit, indicatorId));
            stage.hints.Add(CircuitHint.Make("Твърде много ток – LED-ът ще изгори!",
                CircuitCondition.Make(ConditionType.AnyLedOverloaded)));
            stage.hints.Add(CircuitHint.Make("LED-ът е обърнат наобратно! Завърти го с R.",
                CircuitCondition.Make(ConditionType.AnyLedReversed)));
            if (!polarity)
                stage.hints.Add(CircuitHint.Make("Веригата не е затворена – още нещо липсва!",
                    CircuitCondition.Make(ConditionType.SlotsFilled, invert: true)));
            return new List<PuzzleStage> { stage };
        }

        // =====================================================================  House 3 – Ohm's law

        public const string House3Circuit =
@"# Стария Том: избери правилния резистор за 3 LED-а
battery B 12V
slot SR          # тук се слага резисторът
led L1 red
led L2 red
led L3 red

B+ -> SR -> L1 -> L2 -> L3 -> B-";

        public static List<PuzzleStage> House3Stages(string objective, string[] completeDialogue)
        {
            var stage = new PuzzleStage
            {
                name = "Right resistor",
                objective = objective,
                successMessage = "Перфектно! 1kΩ е точно това, което трябва!",
                completeDialogue = completeDialogue ?? new string[0],
            };
            stage.conditions.Add(CircuitCondition.Make(ConditionType.LedCurrentBetween, "L1,L2,L3", 3f, 25f));
            stage.conditions.Add(CircuitCondition.Make(ConditionType.NoLedOverloaded));
            stage.hints.Add(CircuitHint.Make("Твърде ярко! LED-ите ще изгорят. Опитай с по-голям резистор.",
                CircuitCondition.Make(ConditionType.AnyLedOverloaded)));
            stage.hints.Add(CircuitHint.Make("Твърде тъмно! Опитай с по-малък резистор.",
                CircuitCondition.Make(ConditionType.SlotsFilled, "SR"),
                CircuitCondition.Make(ConditionType.LedCurrentBetween, "L1", 0f, 3f)));
            return new List<PuzzleStage> { stage };
        }

        // =====================================================================  House 4 – series vs parallel

        public const string House4Circuit =
@"# Котетата: горният ред е ПОСЛЕДОВАТЕЛЕН, S4 и S5 са УСПОРЕДНИ
battery B 6V
resistor R1 200     # вградени във всеки клон
resistor R2 200
resistor R3 200
slot S1 wire        # празен слот = парче жица
slot S2 wire
slot S3 wire
slot S4
slot S5

B+ -> R1 -> S1 -> S2 -> S3 -> B-
B+ -> R2 -> S4 -> B-
B+ -> R3 -> S5 -> B-";

        public static List<PuzzleStage> House4Stages(string objective1, string[] dialogue1, string[] dialogue2)
        {
            var series = new PuzzleStage
            {
                name = "Series",
                objective = objective1,
                successMessage = "Последователна верига! Говори с котетата!",
                completeDialogue = dialogue1 ?? new string[0],
            };
            series.conditions.Add(CircuitCondition.Make(ConditionType.LedsLit, "S1,S2,S3"));

            var parallel = new PuzzleStage
            {
                name = "Parallel",
                objective = "Премести два LED-а в успоредните слотове!",
                successMessage = "Успоредна верига! Говори с котетата!",
                completeDialogue = dialogue2 ?? new string[0],
            };
            parallel.conditions.Add(CircuitCondition.Make(ConditionType.LedsLit, "S4,S5"));
            parallel.conditions.Add(CircuitCondition.Make(ConditionType.AnyLedLit, "S1,S2,S3"));

            return new List<PuzzleStage> { series, parallel };
        }

        // =====================================================================  House 5 – the Mayor's code puzzle

        public const string MayorName = "Кмет Флъфингтън";

        public static readonly string[] MayorGreeting =
        {
            "Добре дошъл в кметството! Аз съм кмет Флъфингтън.",
            "Тук не носим части в уста. Тук пишем КОД!",
            "Компютърът до мен построява всяка верига, която напишеш.",
            "Натисни E на компютъра. Вдясно има помощ и примери.",
        };

        public static List<PuzzleStage> MayorStages()
        {
            var first = new PuzzleStage
            {
                name = "First LED",
                objective = "Напиши верига с батерия, резистор и LED. LED-ът трябва да свети, без да изгаря.",
                successMessage = "Първата ти верига от код работи! Говори с кмета.",
                completeDialogue = new[]
                {
                    "Отлично! Кметството е впечатлено.",
                    "Батерията бута тока, резисторът го пази, а LED-ът свети.",
                    "Но сега ще стане по-трудно...",
                },
            };
            first.conditions.Add(CircuitCondition.Make(ConditionType.AnyLedLit));
            first.conditions.Add(CircuitCondition.Make(ConditionType.NoLedOverloaded));
            AddCommonHints(first);
            first.hints.Add(CircuitHint.Make("Не тече ток. Затвори кръга: започни от B+ и завърши в B-.",
                CircuitCondition.Make(ConditionType.NoCurrent)));

            var parallel = new PuzzleStage
            {
                name = "Only 3 volts",
                objective = "Имаме само батерия от 3V. Направи така, че ДВА LED-а да светят едновременно.",
                successMessage = "Двата LED-а светят! Говори с кмета.",
                completeDialogue = new[]
                {
                    "Браво! Това е успоредно свързване.",
                    "Всеки LED получава пълните 3V, затова и двата светят.",
                    "Последователно щяха да си делят напрежението – и нямаше да стигне.",
                },
            };
            parallel.conditions.Add(CircuitCondition.Make(ConditionType.BatteryVoltageAtMost, max: 3f));
            parallel.conditions.Add(CircuitCondition.Make(ConditionType.LitLedCountAtLeast, count: 2));
            parallel.conditions.Add(CircuitCondition.Make(ConditionType.NoLedOverloaded));
            parallel.hints.Add(CircuitHint.Make("Батерията трябва да е най-много 3V, напр.: battery B 3V",
                CircuitCondition.Make(ConditionType.BatteryVoltageAtMost, max: 3f, invert: true)));
            AddCommonHints(parallel);
            parallel.hints.Add(CircuitHint.Make("Два LED-а последователно искат 3.6V – 3V не стигат! Дай на всеки LED свой път (успоредно).",
                CircuitCondition.Make(ConditionType.PartCountAtLeast, count: 2),
                CircuitCondition.Make(ConditionType.LitLedCountAtLeast, count: 2, invert: true)));

            var master = new PuzzleStage
            {
                name = "Master switch",
                objective = "Добави ключ (switch), който гаси ВСИЧКИ LED-ове наведнъж. Щракаш го с E.",
                successMessage = "Главният ключ работи! Говори с кмета.",
                completeDialogue = new[]
                {
                    "Великолепно! Ти си истински електротехник!",
                    "Ключът прекъсва веригата – без затворен кръг няма ток.",
                    "Селото се гордее с теб! Благодаря ти!",
                },
            };
            master.conditions.Add(CircuitCondition.Make(ConditionType.SwitchTurnsAllLedsOff));
            master.conditions.Add(CircuitCondition.Make(ConditionType.PartCountAtLeast, count: 2));
            var noSwitch = CircuitCondition.Make(ConditionType.PartCountAtLeast, count: 1, invert: true);
            noSwitch.partKind = PartKind.Switch;
            master.hints.Add(CircuitHint.Make("Добави ключ, напр.: switch S1 on – и го сложи в пътя на тока.", noSwitch));
            master.hints.Add(CircuitHint.Make("Трябват поне два LED-а.",
                CircuitCondition.Make(ConditionType.PartCountAtLeast, count: 2, invert: true)));
            AddCommonHints(master);

            return new List<PuzzleStage> { first, parallel, master };
        }

        private static void AddCommonHints(PuzzleStage stage)
        {
            stage.hints.Add(CircuitHint.Make("Късо съединение! Токът тече направо от + към -. Сложи резистор и LED по пътя.",
                CircuitCondition.Make(ConditionType.ShortCircuit)));
            stage.hints.Add(CircuitHint.Make("LED-ът изгаря! Добави резистор (напр. 330), за да ограничиш тока.",
                CircuitCondition.Make(ConditionType.AnyLedOverloaded)));
            stage.hints.Add(CircuitHint.Make("LED-ът е обърнат – токът трябва да влиза в неговия + (дългото краче).",
                CircuitCondition.Make(ConditionType.AnyLedReversed)));
        }
    }
}
