using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace CatCircuit.EditorTools
{
    /// <summary>
    /// CatCircuit ▸ Build ▸ Web size report / Apply web optimizations.
    ///   Web size report        – lists the 40 biggest assets the build scenes use, with their WebGL import settings.
    ///                            Writes Assets/_Game/CatCircuit_WebReport.txt. Changes nothing.
    ///   Apply web optimizations – ONLY for assets the build scenes use: WebGL texture override (max 1024, crunched,
    ///                            quality 50), model mesh compression Medium, and smaller-code WebGL player settings.
    ///                            Asks first, writes what it changed into the same report. Safe to run again.
    /// Windows builds are not affected by the texture changes (they are WebGL-only overrides).
    /// </summary>
    public static class CatCircuitWebOptimize
    {
        private const string ReportPath = "Assets/_Game/CatCircuit_WebReport.txt";
        private const string WebGL = "WebGL";
        private const int MaxSize = 1024;
        private const int BigTerrainSize = 2048;
        private const int CrunchQuality = 50;

        private class TextureChange
        {
            public string path;
            public int fromSize, toSize;
            public bool wasCrunched;
        }

        // ------------------------------------------------------------------ report

        [MenuItem("CatCircuit/Build/Web size report", priority = 70)]
        public static void ReportMenu()
        {
            string report = BuildReport(null, null, null);
            WriteReport(report);
            EditorUtility.DisplayDialog("CatCircuit – web size report",
                "Written to " + ReportPath + "\n(also printed in the Console).", "OK");
        }

        private static string BuildReport(List<TextureChange> textureChanges, List<string> modelChanges, List<string> settingChanges)
        {
            List<string> deps = BuildDependencies();
            var sb = new StringBuilder();
            sb.AppendLine("CatCircuit – web size report  (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
            sb.AppendLine("Assets used by the build-list scenes: " + deps.Count);
            sb.AppendLine("Sizes are the SOURCE files on disk; the build size depends on the import settings shown.");
            sb.AppendLine();

            if (textureChanges != null)
            {
                sb.AppendLine("=== Changes made by 'Apply web optimizations'");
                sb.AppendLine($"Textures ({textureChanges.Count}):");
                foreach (TextureChange c in textureChanges)
                    sb.AppendLine($"  {c.path}: WebGL max {c.fromSize} -> {c.toSize}, crunched{(c.wasCrunched ? " (already)" : "")}, quality {CrunchQuality}");
                sb.AppendLine($"Models ({modelChanges.Count}):");
                foreach (string m in modelChanges) sb.AppendLine("  " + m);
                sb.AppendLine("Player settings:");
                foreach (string s in settingChanges) sb.AppendLine("  " + s);
                sb.AppendLine();
            }

            sb.AppendLine("=== Player settings (WebGL)");
            sb.AppendLine("  Managed stripping: " + PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL));
            sb.AppendLine("  Strip engine code: " + PlayerSettings.stripEngineCode);
            sb.AppendLine("  Exceptions: " + PlayerSettings.WebGL.exceptionSupport);
            sb.AppendLine("  Compression: " + PlayerSettings.WebGL.compressionFormat + ", decompression fallback " + PlayerSettings.WebGL.decompressionFallback);
            sb.AppendLine("  Data caching: " + PlayerSettings.WebGL.dataCaching);
            sb.AppendLine("  Unity splash screen: " + PlayerSettings.SplashScreen.show + " (the Unity logo adds ~2.7 MB uncompressed)");
            sb.AppendLine();

            sb.AppendLine("=== 40 largest assets");
            var sized = deps.Select(p => new { path = p, size = FileSize(p) })
                            .OrderByDescending(x => x.size).Take(40);
            long total = deps.Sum(FileSize);
            foreach (var x in sized)
                sb.AppendLine($"{Mb(x.size),8}  {TypeName(x.path),-14} {Settings(x.path),-48} {x.path}");
            sb.AppendLine();
            sb.AppendLine("Total source size of used assets: " + Mb(total));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ optimize

        [MenuItem("CatCircuit/Build/Apply web optimizations", priority = 71)]
        public static void OptimizeMenu()
        {
            List<string> deps = BuildDependencies();
            var textures = new List<(TextureImporter imp, TextureChange change)>();
            var models = new List<ModelImporter>();

            foreach (string path in deps)
            {
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer is TextureImporter ti)
                {
                    TextureChange c = PlanTexture(ti, path);
                    if (c != null) textures.Add((ti, c));
                }
                else if (importer is ModelImporter mi)
                {
                    if (mi.meshCompression == ModelImporterMeshCompression.Off || mi.meshCompression == ModelImporterMeshCompression.Low)
                        models.Add(mi);
                }
            }

            List<string> settingPlan = PlanPlayerSettings(false);

            string question =
                "This changes ONLY assets used by the build scenes (WebGL overrides – Windows stays the same):\n\n" +
                $"• {textures.Count} textures → WebGL max {MaxSize}, crunched\n" +
                $"• {models.Count} models → mesh compression Medium\n" +
                $"• {settingPlan.Count} WebGL player settings\n\n" +
                "Tip: commit / back up the project first. Continue?";
            if (textures.Count == 0 && models.Count == 0 && settingPlan.Count == 0)
            {
                EditorUtility.DisplayDialog("CatCircuit – web optimizations", "Everything is already optimized. Nothing to change.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("CatCircuit – web optimizations", question, "Apply", "Cancel")) return;

            var textureChanges = new List<TextureChange>();
            var modelChanges = new List<string>();
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < textures.Count; i++)
                {
                    var (imp, change) = textures[i];
                    EditorUtility.DisplayProgressBar("Web optimizations", change.path, (float)i / Mathf.Max(1, textures.Count + models.Count));
                    TextureImporterPlatformSettings s = imp.GetPlatformTextureSettings(WebGL);
                    s.overridden = true;
                    s.maxTextureSize = change.toSize;
                    s.format = TextureImporterFormat.Automatic;
                    s.textureCompression = TextureImporterCompression.Compressed;
                    s.crunchedCompression = true;
                    s.compressionQuality = CrunchQuality;
                    imp.SetPlatformTextureSettings(s);
                    imp.SaveAndReimport();
                    textureChanges.Add(change);
                }
                for (int i = 0; i < models.Count; i++)
                {
                    ModelImporter mi = models[i];
                    EditorUtility.DisplayProgressBar("Web optimizations", mi.assetPath, (float)(textures.Count + i) / Mathf.Max(1, textures.Count + models.Count));
                    modelChanges.Add($"{mi.assetPath}: mesh compression {mi.meshCompression} -> Medium");
                    mi.meshCompression = ModelImporterMeshCompression.Medium;
                    mi.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            List<string> settingChanges = PlanPlayerSettings(true);
            AssetDatabase.SaveAssets();

            string report = BuildReport(textureChanges, modelChanges, settingChanges);
            WriteReport(report);
            EditorUtility.DisplayDialog("CatCircuit – web optimizations",
                $"Done: {textureChanges.Count} textures, {modelChanges.Count} models, {settingChanges.Count} settings.\n" +
                "Details in " + ReportPath + ".\nNow build again with CatCircuit ▸ Build ▸ Build Web.", "OK");
        }

        /// <summary>What the WebGL override of one texture should become, or null if it is fine / must not be touched.</summary>
        private static TextureChange PlanTexture(TextureImporter ti, string path)
        {
            // UI, cursors and lightmaps keep their look; tiny textures are not worth it.
            if (ti.textureType == TextureImporterType.Sprite || ti.textureType == TextureImporterType.GUI ||
                ti.textureType == TextureImporterType.Cursor || ti.textureType == TextureImporterType.Lightmap ||
                ti.textureType == TextureImporterType.DirectionalLightmap || ti.textureType == TextureImporterType.Shadowmask)
                return null;

            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            int source = Mathf.Max(w, h);
            if (source < MaxSize) return null;   // already smaller than the limit

            TextureImporterPlatformSettings web = ti.GetPlatformTextureSettings(WebGL);
            int current = web.overridden ? web.maxTextureSize : ti.GetDefaultPlatformTextureSettings().maxTextureSize;

            int target = Mathf.Min(current, MaxSize);
            string lower = path.ToLowerInvariant();
            bool terrainOrAtlas = lower.Contains("terrain") || lower.Contains("atlas");
            if (source > BigTerrainSize && terrainOrAtlas) target = Mathf.Min(current, BigTerrainSize);

            bool done = web.overridden && web.maxTextureSize == target && web.crunchedCompression &&
                        web.compressionQuality == CrunchQuality && web.textureCompression != TextureImporterCompression.Uncompressed;
            if (done) return null;

            return new TextureChange { path = path, fromSize = current, toSize = target, wasCrunched = web.overridden && web.crunchedCompression };
        }

        /// <summary>Lists (and with apply = true, makes) the WebGL player setting changes.</summary>
        private static List<string> PlanPlayerSettings(bool apply)
        {
            var list = new List<string>();

            ManagedStrippingLevel strip = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL);
            if (strip != ManagedStrippingLevel.Medium)
            {
                list.Add($"Managed stripping {strip} -> Medium");
                if (apply) PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
            }
            if (!PlayerSettings.stripEngineCode)
            {
                list.Add("Strip engine code -> on");
                if (apply) PlayerSettings.stripEngineCode = true;
            }
            if (PlayerSettings.WebGL.exceptionSupport != WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly)
            {
                list.Add($"Exceptions {PlayerSettings.WebGL.exceptionSupport} -> ExplicitlyThrownExceptionsOnly");
                if (apply) PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            }
            if (PlayerSettings.WebGL.compressionFormat != WebGLCompressionFormat.Gzip)
            {
                list.Add($"Compression {PlayerSettings.WebGL.compressionFormat} -> Gzip");
                if (apply) PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            }
            if (!PlayerSettings.WebGL.decompressionFallback)
            {
                list.Add("Decompression fallback -> on");
                if (apply) PlayerSettings.WebGL.decompressionFallback = true;
            }
            if (!PlayerSettings.WebGL.dataCaching)
            {
                list.Add("Data caching -> on");
                if (apply) PlayerSettings.WebGL.dataCaching = true;
            }
            return list;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Every asset (recursive) the enabled build-list scenes depend on, without scripts.</summary>
        private static List<string> BuildDependencies()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) return new List<string>();
            return AssetDatabase.GetDependencies(scenes, true)
                                .Where(p => !p.EndsWith(".cs") && !p.EndsWith(".dll") && !p.EndsWith(".asmdef"))
                                .Distinct().ToList();
        }

        private static string Settings(string path)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer is TextureImporter ti)
            {
                TextureImporterPlatformSettings web = ti.GetPlatformTextureSettings(WebGL);
                TextureImporterPlatformSettings def = ti.GetDefaultPlatformTextureSettings();
                ti.GetSourceTextureWidthAndHeight(out int w, out int h);
                return web.overridden
                    ? $"{w}x{h} WebGL max {web.maxTextureSize} {web.format}{(web.crunchedCompression ? " crunched" : "")}"
                    : $"{w}x{h} default max {def.maxTextureSize} {def.textureCompression}{(def.crunchedCompression ? " crunched" : "")}";
            }
            if (importer is ModelImporter mi)
                return $"mesh compression {mi.meshCompression}, readable {mi.isReadable}";
            return "";
        }

        private static string TypeName(string path)
        {
            Type t = AssetDatabase.GetMainAssetTypeAtPath(path);
            return t != null ? t.Name : Path.GetExtension(path);
        }

        private static long FileSize(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch { return 0; }
        }

        private static string Mb(long bytes) => (bytes / (1024f * 1024f)).ToString("0.00") + " MB";

        private static void WriteReport(string report)
        {
            File.WriteAllText(ReportPath, report, new UTF8Encoding(true));
            AssetDatabase.ImportAsset(ReportPath);
            Debug.Log("[CatCircuit Web]\n" + report);
        }
    }
}
