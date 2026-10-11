using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCircuit.EditorTools
{
    /// <summary>
    /// GameObject ▸ CatCircuit ▸ End Game Button  – a pedestal with a big red button (E = end screen + survey QR).
    /// Also keeps the survey QR picture sharp (no compression / blur) when it is imported.
    /// </summary>
    public static class CatCircuitEndGame
    {
        [MenuItem("GameObject/CatCircuit/End Game Button", false, 14)]
        public static void CreateEndGameButton(MenuCommand cmd)
        {
            var root = new GameObject("EndGameButton");
            GameObjectUtility.SetParentAndAlign(root, cmd?.context as GameObject);
            if (root.transform.parent == null && SceneView.lastActiveSceneView != null)
                root.transform.position = SceneView.lastActiveSceneView.pivot;

            Material stand = CatCircuitCreate.GetOrCreateMaterial("EndButton_Stand", new Color(0.22f, 0.24f, 0.3f), Color.black);
            Material red = CatCircuitCreate.GetOrCreateMaterial("EndButton_Red", new Color(0.85f, 0.1f, 0.08f), new Color(0.4f, 0.03f, 0.02f));

            GameObject pillar = Part(PrimitiveType.Cube, "Stand", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.9f, 0.6f), stand);
            Part(PrimitiveType.Cylinder, "Plate", root.transform, new Vector3(0f, 0.93f, 0f), new Vector3(0.5f, 0.03f, 0.5f), stand);
            GameObject cap = Part(PrimitiveType.Cylinder, "ButtonCap", root.transform, new Vector3(0f, 1.0f, 0f), new Vector3(0.36f, 0.06f, 0.36f), red);
            Object.DestroyImmediate(cap.GetComponent<Collider>());   // the pillar is solid, the cap is just visual
            pillar.isStatic = false;

            var button = root.AddComponent<EndGameButton>();
            button.Configure(cap.transform);

            Undo.RegisterCreatedObjectUndo(root, "Create End Game Button");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[CatCircuit] End Game Button created. Put it somewhere the cat can reach in House 5 and save the scene.", root);
        }

        private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }

    /// <summary>Import the survey QR as a crisp, uncompressed UI texture.</summary>
    public class SurveyQrImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.EndsWith("/" + EndGameScreen.QrResource + ".png")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
        }
    }
}
