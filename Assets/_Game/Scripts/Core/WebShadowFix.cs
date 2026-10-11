using UnityEngine;
using UnityEngine.Rendering;

namespace CatCircuit
{
    /// <summary>
    /// Fix for "the web build shows only the sky".
    ///
    /// URP's Lit shader always samples _AdditionalLightsShadowmapTexture. When the active URP asset has
    /// "Additional Lights ▸ Cast Shadows" turned off (the Mobile_RPAsset used by the Web quality level),
    /// URP never sets that texture, so Unity binds a plain white colour texture instead. Browsers (WebGL)
    /// refuse to draw with a colour texture in a shadow sampler, so EVERY object fails to draw and only the
    /// skybox is left. (Windows/DirectX does not check this, so the .exe was fine.)
    ///
    /// This binds a tiny empty shadow map once at start. When URP does render additional-light shadows it
    /// overwrites the global itself, so this never changes how the game looks.
    /// </summary>
    public static class WebShadowFix
    {
        private static RenderTexture emptyShadowmap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            if (emptyShadowmap != null) return;
            bool needed = Application.platform == RuntimePlatform.WebGLPlayer ||
                          SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;
            if (!needed) return;   // DirectX/Vulkan/Metal don't have the problem

            var desc = new RenderTextureDescriptor(1, 1, RenderTextureFormat.Shadowmap, 16)
            {
                shadowSamplingMode = ShadowSamplingMode.CompareDepths,
                msaaSamples = 1,
                useMipMap = false,
            };
            emptyShadowmap = new RenderTexture(desc) { name = "CatCircuit Empty Shadowmap", filterMode = FilterMode.Bilinear };
            emptyShadowmap.Create();

            // Depth 1 = "far away" = nothing is in shadow.
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = emptyShadowmap;
            GL.Clear(true, false, Color.black, 1f);
            RenderTexture.active = previous;

            Bind();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Bind();   // in case anything cleared it
            Debug.Log("[CatCircuit] WebShadowFix: bound an empty additional-lights shadow map.");
        }

        private static void Bind()
        {
            if (emptyShadowmap != null) Shader.SetGlobalTexture("_AdditionalLightsShadowmapTexture", emptyShadowmap);
        }
    }
}
