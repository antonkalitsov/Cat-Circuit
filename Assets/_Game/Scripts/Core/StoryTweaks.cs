using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatCircuit
{
    /// <summary>
    /// Small dialogue changes kept in code (so no scene has to be edited):
    ///   • House 3 – Старият Том tells the player to find Спарки in the village after the puzzle.
    ///   • Village – Спарки's lines about Ohm's law, rewritten: shorter, with the LED voltage included
    ///     (the old example 9 V / 0.02 A = 450 Ω forgot the ~1.8 V the LED takes) and matching his chalkboard.
    /// Max 2 lines per line of dialogue, child-friendly Bulgarian.
    /// </summary>
    public static class StoryTweaks
    {
        private const string SparkyName = "Спарки";

        private const string TomAboutSparky =
            "Искаш ли да разбереш защо точно 1 kΩ?\nНамери Спарки в селото – той се е скрил някъде!";

        private static readonly string[] SparkyGreeting =
        {
            "О, здрасти! Намери ме!\nАз съм Спарки, помощникът на Стария Том.",
            "Обожавам формулите! До мен има дъска\nсъс закона на Ом – разгледай я.",
            "Реши задачата с резисторите в Къща 3\nи после ела пак – ще ти обясня всичко.",
        };

        private static readonly string[] SparkyOhm =
        {
            "Браво, реши задачата при Стария Том!\nСега ще ти обясня защо се получи.",
            "Това е законът на Ом:  U = I · R\nВиж и дъската до мен.",
            "U е напрежението – колко силно батерията\nбута тока. Мери се във волтове (V).",
            "I е токът – колко електричество минава.\nМери се в ампери (A). 1 mA = 0,001 A.",
            "R е съпротивлението – колко резисторът\nпречи на тока. Мери се в омове (Ω).",
            "Номер за запомняне: в триъгълника на дъската\nзакрий с пръст това, което търсиш.",
            "При Том: батерия 12 V и три червени LED-а.\nВсеки LED взима около 1,8 V – общо 5,4 V.",
            "За резистора остават 12 – 5,4 = 6,6 V.\nС 1 kΩ: I = 6,6 V / 1000 Ω ≈ 6,6 mA.",
            "6,6 mA е между 3 и 25 mA – светят безопасно.\nСъс 100 Ω токът е много по-голям и LED-ите изгарят.",
            "По-голям резистор = по-малък ток.\nПо-малък резистор = по-голям ток.",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;

            if (scene.buildIndex == 0)
            {
                foreach (CatNPC n in Object.FindObjectsByType<CatNPC>(FindObjectsSortMode.None))
                    if (n.Puzzle == null && n.NpcName == SparkyName) n.SetInfoDialogue(SparkyGreeting, SparkyOhm);
                return;
            }

            if (HouseInfo.FromScene(scene.name) == 3)
            {
                foreach (CircuitPuzzle p in Object.FindObjectsByType<CircuitPuzzle>(FindObjectsSortMode.None))
                {
                    if (p.ProgressId != 3 || p.Stages.Count == 0) continue;
                    PuzzleStage last = p.Stages[p.Stages.Count - 1];
                    string[] lines = last.completeDialogue ?? new string[0];
                    if (System.Array.IndexOf(lines, TomAboutSparky) >= 0) continue;
                    var list = new System.Collections.Generic.List<string>(lines) { TomAboutSparky };
                    last.completeDialogue = list.ToArray();
                }
            }
        }
    }
}
