using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCircuit.EditorTools
{
    /// <summary>
    /// CatCircuit ▸ Create ▸ Village guide     – places "Дядо Ампер" permanently in the open scene at the Scene view
    ///                                           centre (otherwise the game spawns him in front of the start).
    /// </summary>
    public static class CatCircuitPolishMenus
    {
        [MenuItem("CatCircuit/Create/Village guide (Дядо Ампер)", priority = 22)]
        private static void CreateGuide()
        {
            if (Object.FindAnyObjectByType<GuideNPC>() != null &&
                !EditorUtility.DisplayDialog("Village guide", "This scene already has a guide. Add another one?", "Add", "Cancel"))
                return;

            var go = new GameObject("Дядо Ампер");
            go.AddComponent<GuideNPC>();
            PlaceAtPivot(go);
            Selection.activeGameObject = go;
            Debug.Log("[CatCircuit] Village guide placed. Rotate it (blue Z arrow) to face the player's start. " +
                      "Its look (hat, lamp, copied cat model) is built when the game runs.");
        }

        private static void PlaceAtPivot(GameObject go)
        {
            SceneView view = SceneView.lastActiveSceneView;
            Vector3 p = view != null ? view.pivot : Vector3.zero;
            if (Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                p = hit.point;
            go.transform.position = p;
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
