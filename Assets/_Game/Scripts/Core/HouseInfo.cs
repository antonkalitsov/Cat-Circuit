using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Number, colour and short title of every house. The same colour is used on the village sign,
    /// the door flag and the schematic frame inside, so the player links "the yellow 1" outside and inside.
    /// House N = scene "HouseN" = puzzle progressId N.
    /// </summary>
    public static class HouseInfo
    {
        public const int Count = 5;

        private static readonly Color[] colors =
        {
            new Color(1f, 0.82f, 0.15f),    // 1 yellow
            new Color(1f, 0.55f, 0.12f),    // 2 orange
            new Color(0.92f, 0.22f, 0.2f),  // 3 red
            new Color(0.62f, 0.32f, 0.9f),  // 4 purple
            new Color(0.2f, 0.55f, 1f),     // 5 blue
        };

        private static readonly string[] titles =
        {
            "Затворена верига",
            "Посоката на LED",
            "Законът на Ом",
            "Последователно и успоредно",
            "Кметството: код",
        };

        /// <summary>One line about what the house teaches (used by the village guide).</summary>
        private static readonly string[] lessons =
        {
            "Професорът ще те научи, че токът тече само в затворена верига.",
            "Госпожа Пурингтън има LED, който свети само ако е обърнат правилно.",
            "Старият Том иска да избереш правилния резистор – тук помага законът на Ом.",
            "Котенцата ще ти покажат разликата между последователно и успоредно свързване.",
            "Кметът иска да НАПИШЕШ веригата като код и тя ще се появи пред теб.",
        };

        /// <summary>PlayerPrefs key: 1 after the village guide's first talk.</summary>
        public const string GuideIntroKey = "cc_guide_intro";

        public static bool GuideIntroHeard => PlayerPrefs.GetInt(GuideIntroKey, 0) == 1;

        private static readonly string[] colorNames = { "жълтия", "оранжевия", "червения", "лилавия", "синия" };

        public static bool IsHouse(int house) => house >= 1 && house <= Count;
        /// <summary>"жълтия" … – for sentences like "Търси жълтия знак".</summary>
        public static string ColorName(int house) => IsHouse(house) ? colorNames[house - 1] : "";
        public static Color Color(int house) => IsHouse(house) ? colors[house - 1] : UnityEngine.Color.white;
        public static string Title(int house) => IsHouse(house) ? titles[house - 1] : "";
        public static string Lesson(int house) => IsHouse(house) ? lessons[house - 1] : "";
        public static string SceneName(int house) => "House" + house;

        /// <summary>"House3" → 3, anything else → 0.</summary>
        public static int FromScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || !sceneName.StartsWith("House")) return 0;
            return int.TryParse(sceneName.Substring(5), out int n) && IsHouse(n) ? n : 0;
        }

        public static bool IsSolved(int house) =>
            GameProgress.Instance != null && GameProgress.Instance.IsPuzzleComplete(house);

        /// <summary>The first house that is not solved yet, or 0 when all are done.</summary>
        public static int NextUnsolved()
        {
            for (int h = 1; h <= Count; h++) if (!IsSolved(h)) return h;
            return 0;
        }

        public static int SolvedCount()
        {
            int n = 0;
            for (int h = 1; h <= Count; h++) if (IsSolved(h)) n++;
            return n;
        }
    }
}
