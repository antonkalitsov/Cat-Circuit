using System.Collections.Generic;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// The saved game: which puzzles are solved. Stored in PlayerPrefs, so it survives closing the game
    /// and also works in the web build (the browser keeps it between visits).
    /// The title menu reads it for "Продължи"; <see cref="GameProgress"/> writes it when a puzzle is solved.
    /// </summary>
    public static class GameSave
    {
        private const string KeyProgress = "cc_progress";

        /// <summary>Number of puzzles (houses) in the game, shown as "3/5" on the Continue button.</summary>
        public const int TotalPuzzles = 5;

        public static bool HasSave => Load().Count > 0;

        public static List<int> Load()
        {
            var ids = new List<int>();
            string raw = PlayerPrefs.GetString(KeyProgress, "");
            foreach (string part in raw.Split(','))
                if (int.TryParse(part, out int id) && id > 0 && !ids.Contains(id)) ids.Add(id);
            return ids;
        }

        public static void Save(IReadOnlyList<int> solvedPuzzles)
        {
            PlayerPrefs.SetString(KeyProgress, string.Join(",", solvedPuzzles));
            PlayerPrefs.Save();   // on the web this writes to the browser's storage
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(KeyProgress);
            PlayerPrefs.Save();
        }
    }
}
