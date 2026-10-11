using UnityEditor;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Editor helpers for the title menu and the save:
    ///  * CatCircuit ▸ Show main menu in Play Mode – tick/untick (default on). Off = pressing Play in the
    ///    village goes straight into the game, handy while testing.
    ///  * CatCircuit ▸ Reset saved progress – deletes the saved game (PlayerPrefs) on this computer.
    /// </summary>
    public static class CatCircuitTitleMenu
    {
        private const string ToggleMenu = "CatCircuit/Show main menu in Play Mode";

        [MenuItem(ToggleMenu)]
        private static void Toggle()
        {
            bool now = EditorPrefs.GetBool(PauseMenu.EditorPrefKey, true);
            EditorPrefs.SetBool(PauseMenu.EditorPrefKey, !now);
        }

        [MenuItem(ToggleMenu, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenu, EditorPrefs.GetBool(PauseMenu.EditorPrefKey, true));
            return true;
        }

        [MenuItem("CatCircuit/Reset saved progress")]
        private static void ResetSave()
        {
            GameSave.Clear();
            Debug.Log("[CatCircuit] Saved progress deleted.");
        }
    }
}
