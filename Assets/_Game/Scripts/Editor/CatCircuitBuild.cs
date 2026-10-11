using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CatCircuit.EditorTools
{
    /// <summary>
    /// CatCircuit ▸ Build menu.
    ///   Prepare project for building – run once before the first build (safe to run again).
    ///   Build Windows (.exe)          – Builds/Windows/CatCircuit.exe
    ///   Build Web                     – Builds/Web (upload the CONTENTS of that folder, zipped, to itch.io)
    /// The same can be done by hand in File ▸ Build Profiles; see README §12.
    /// </summary>
    public static class CatCircuitBuild
    {
        private const string KeepFolder = "Assets/_Game/Resources/ShaderKeep";
        private const string FontPath = "Assets/_Game/Resources/" + UIFonts.ResourceName;
        private const string WindowsOut = "Builds/Windows/CatCircuit.exe";
        private const string WebOut = "Builds/Web";

        // ------------------------------------------------------------------ prepare

        [MenuItem("CatCircuit/Build/Prepare project for building", priority = 60)]
        public static void PrepareMenu()
        {
            string report = Prepare(out bool ok);
            Debug.Log("[CatCircuit Build]\n" + report);
            EditorUtility.DisplayDialog("CatCircuit – prepare build", report, "OK");
        }

        private static string Prepare(out bool ok)
        {
            ok = true;
            var sb = new StringBuilder();

            // 1. Materials that force Unity to keep the shader variants the scripts create at runtime
            //    (sparkles, glowing LEDs, glowing wires). Without them these can be pink/invisible in a build.
            CreateKeepMaterials();
            sb.AppendLine("✔ Shader keep-materials in " + KeepFolder);

            // 2. Scenes
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            if (scenes.Count == 0) { ok = false; sb.AppendLine("✘ No scenes in the build list (File ▸ Build Profiles ▸ Scene List)."); }
            else
            {
                string first = Path.GetFileNameWithoutExtension(scenes[0]);
                if (first != "Outside") { ok = false; sb.AppendLine($"✘ First scene is '{first}' – drag 'Outside' to the top of the Scene List."); }
                foreach (string need in new[] { "Outside", "House1", "House2", "House3", "House4", "House5" })
                    if (!scenes.Any(p => Path.GetFileNameWithoutExtension(p) == need))
                    { ok = false; sb.AppendLine($"✘ Scene '{need}' is missing from the Scene List."); }
                if (ok) sb.AppendLine($"✔ {scenes.Count} scenes, Outside first");
            }

            // 3. Font for the pause menu / code terminal in the browser
            bool hasFont = File.Exists(FontPath + ".ttf") || File.Exists(FontPath + ".otf");
            sb.AppendLine(hasFont
                ? "✔ Web font found (" + UIFonts.ResourceName + ")"
                : "! WEB ONLY: put a font with Cyrillic letters at " + FontPath + ".ttf\n" +
                  "  (e.g. copy the .ttf your TextMeshPro font was made from) – otherwise the\n" +
                  "  pause menu and code terminal show empty boxes in the browser. Windows is fine without it.");

            // 4. Web player settings (harmless for Windows)
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;   // works on any web host (itch.io, GitHub Pages)
            PlayerSettings.runInBackground = true;
            if (string.IsNullOrEmpty(PlayerSettings.productName) || PlayerSettings.productName == "My project")
                PlayerSettings.productName = "CatCircuit";
            sb.AppendLine("✔ Web: Gzip compression + decompression fallback");

            AssetDatabase.SaveAssets();
            sb.AppendLine();
            sb.AppendLine(ok ? "Ready to build." : "Fix the ✘ items, then build.");
            return sb.ToString();
        }

        private static void CreateKeepMaterials()
        {
            CatCircuitUpgrade.EnsureFolder(KeepFolder);

            MakeKeep("Keep_Lit", "Universal Render Pipeline/Lit", m => { });
            MakeKeep("Keep_Lit_Emission", "Universal Render Pipeline/Lit", m =>
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            });
            MakeKeep("Keep_Unlit", "Universal Render Pipeline/Unlit", m => { });
            MakeKeep("Keep_Particles_Additive", "Universal Render Pipeline/Particles/Unlit", m =>
            {
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            });
            MakeKeep("Keep_Sprites", "Sprites/Default", m => { });
        }

        private static void MakeKeep(string name, string shaderName, System.Action<Material> setup)
        {
            string path = KeepFolder + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            Shader shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogWarning("[CatCircuit Build] shader not found: " + shaderName); return; }
            var mat = new Material(shader) { name = name };
            setup(mat);
            AssetDatabase.CreateAsset(mat, path);
        }

        // ------------------------------------------------------------------ build

        [MenuItem("CatCircuit/Build/Build Windows (.exe)", priority = 61)]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, WindowsOut);

        [MenuItem("CatCircuit/Build/Build Web", priority = 62)]
        public static void BuildWeb() => Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, WebOut);

        private static void Build(BuildTarget target, BuildTargetGroup group, string output)
        {
            string prep = Prepare(out bool ok);
            if (!ok) { EditorUtility.DisplayDialog("CatCircuit – build", prep, "OK"); return; }

            if (!BuildPipeline.IsBuildTargetSupported(group, target))
            {
                EditorUtility.DisplayDialog("CatCircuit – build",
                    $"The {target} build module is not installed.\nUnity Hub ▸ Installs ▸ ⚙ ▸ Add modules ▸ " +
                    (target == BuildTarget.WebGL ? "Web Build Support" : "Windows Build Support") + ".", "OK");
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = target,
                targetGroup = group,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            string folder = Path.GetFullPath(target == BuildTarget.WebGL ? output : Path.GetDirectoryName(output));

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[CatCircuit Build] {target} OK – {summary.totalSize / (1024 * 1024)} MB in {folder}");
                EditorUtility.RevealInFinder(folder);
            }
            else
            {
                EditorUtility.DisplayDialog("CatCircuit – build", $"Build {summary.result}. See the Console for the errors.", "OK");
            }
        }
    }
}
