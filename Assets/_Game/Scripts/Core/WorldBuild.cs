using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Small helpers for things the scripts build in the 3D world at runtime (house signs, the guide's hat,
    /// wall schematics): plain coloured materials, primitives without colliders and 3D TextMeshPro text.
    /// Materials use URP Unlit / Particles-Unlit, which builds keep via Resources/ShaderKeep.
    /// </summary>
    public static class WorldBuild
    {
        private static readonly Dictionary<Color, Material> unlit = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> additive = new Dictionary<Color, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            unlit.Clear();
            additive.Clear();
        }

        /// <summary>Flat colour, not affected by light.</summary>
        public static Material Unlit(Color c)
        {
            if (unlit.TryGetValue(c, out Material m) && m != null) return m;
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");
            m = new Material(s) { name = "Runtime_Unlit" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            unlit[c] = m;
            return m;
        }

        /// <summary>Glowing see-through colour (light beams, halos). Alpha = strength.</summary>
        public static Material Additive(Color c)
        {
            if (additive.TryGetValue(c, out Material m) && m != null) return m;
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s == null) return Unlit(c);
            m = new Material(s) { name = "Runtime_Additive" };
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            additive[c] = m;
            return m;
        }

        /// <summary>A Unity primitive without collider and shadows.</summary>
        public static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, string name = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            if (!string.IsNullOrEmpty(name)) go.name = name;
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>A flat disc facing -Z (towards the viewer of a billboard).</summary>
        public static GameObject Disc(Transform parent, Vector3 localPos, float radius, float thickness, Material mat, string name = "Disc")
        {
            GameObject go = Primitive(PrimitiveType.Cylinder, parent, localPos, new Vector3(radius * 2f, thickness * 0.5f, radius * 2f), mat, name);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return go;
        }

        /// <summary>A thin bar from a to b in the parent's XY plane (z = depth offset, negative = towards the viewer).</summary>
        public static GameObject Bar(Transform parent, Vector2 a, Vector2 b, float width, Material mat, float z = 0f, float depth = 0.01f)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            GameObject go = Primitive(PrimitiveType.Cube, parent, new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, z),
                                      new Vector3(len + width, width, depth), mat, "Bar");
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            return go;
        }

        /// <summary>3D text using the game's UI font (the one that has Cyrillic letters, also on the web).</summary>
        public static TextMeshPro Text(Transform parent, string text, float fontSize, Color color, Vector3 localPos,
                                       TextAlignmentOptions align = TextAlignmentOptions.Center, float width = 8f)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            TMP_FontAsset font = UIManager.Instance != null ? UIManager.Instance.Font : null;
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(width, fontSize * 0.25f);
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tmp;
        }

        /// <summary>Highest point under (x, z) found by a downward ray, or fallbackY.</summary>
        public static float GroundY(Vector3 above, float fallbackY, Transform ignore = null)
        {
            Vector3 start = above + Vector3.up * 20f;
            RaycastHit[] hits = Physics.RaycastAll(start, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (RaycastHit h in hits)
            {
                if (ignore != null && h.transform.IsChildOf(ignore)) continue;
                if (h.point.y > best && h.point.y < above.y + 3f) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? fallbackY : best;
        }
    }
}
