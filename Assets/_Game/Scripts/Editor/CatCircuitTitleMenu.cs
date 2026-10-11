using UnityEditor;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Editor helpers for the title menu and the save:
    ///  * CatCircuit ▸ Show main menu in Play Mode – tick/untick (default on). Off = pressing Play in the
    ///    village goes straight into the game, handy while testing.
    ///  * CatCircuit ▸ Reset saved progress – deletes the saved game (PlayerPrefs) on this computer.
    ///  * CatCircuit ▸ Reset tutorial and first-start hints – House 1 tips and the controls card show again.
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

        [MenuItem("CatCircuit/Reset tutorial and first-start hints")]
        private static void ResetTutorial()
        {
            PlayerPrefs.DeleteKey("cc_tutorial_done");
            PlayerPrefs.DeleteKey("cc_controls_seen");
            PlayerPrefs.Save();
            Debug.Log("[CatCircuit] House 1 tutorial and controls card will show again.");
        }
    }
}
