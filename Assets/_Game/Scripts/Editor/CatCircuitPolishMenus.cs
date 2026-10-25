using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCircuit.EditorTools
{
    /// <summary>
    /// CatCircuit ▸ Create ▸ Village guide     – places "Дядо Ампер" permanently in the open scene at the Scene view
    ///                                           centre (otherwise the game spawns him in front of the start).
    /// CatCircuit ▸ Create ▸ Schematic anchor  – fixes where the wall diagram goes in a house (blue arrow into the room).
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

        [MenuItem("CatCircuit/Create/Schematic anchor (wall diagram position)", priority = 23)]
        private static void CreateSchematicAnchor()
        {
            GameObject existing = GameObject.Find("SchematicAnchor");
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log("[CatCircuit] This scene already has a SchematicAnchor – selected it.");
                return;
            }

            var go = new GameObject("SchematicAnchor");
            SceneView view = SceneView.lastActiveSceneView;
            go.transform.position = view != null ? view.pivot : Vector3.zero;
            if (view != null)
            {
                Vector3 back = -view.rotation * Vector3.forward;   // towards the Scene camera = into the room
                back.y = 0f;
                if (back.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(back.normalized);
            }
            Undo.RegisterCreatedObjectUndo(go, "Create SchematicAnchor");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            Debug.Log("[CatCircuit] SchematicAnchor placed. Put it on the wall at about 1.6 m (centre of the board), " +
                      "with its blue Z arrow pointing INTO the room. The 2.4 x 1.6 m chalkboard appears there in Play mode.");
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
