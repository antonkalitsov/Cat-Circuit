using System.Collections.Generic;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// "Circuit complete!" effect: a burst of sparks, a few crackling electric arcs and a flash of light.
    /// Built entirely from code, so it needs no prefab, texture or material setup:
    ///     ElectricSparkle.Spawn(position);
    /// Used by <see cref="CircuitPuzzle"/> on every solved stage (at each lit LED).
    /// </summary>
    public class ElectricSparkle : MonoBehaviour
    {
        [SerializeField] private float lifetime = 1.6f;
        [SerializeField] private float arcDuration = 0.7f;
        [SerializeField] private int arcCount = 4;
        [SerializeField] private float radius = 0.5f;
        [SerializeField] private Color sparkColor = new Color(1f, 0.9f, 0.4f);
        [SerializeField] private Color arcColor = new Color(0.55f, 0.8f, 1f);

        private readonly List<LineRenderer> arcs = new List<LineRenderer>();
        private Light flash;
        private float age;

        private static Material sharedParticleMaterial;
        private static Material sharedArcMaterial;
        private static Texture2D sharedDot;

        /// <summary>Creates a sparkle at <paramref name="position"/>. <paramref name="scale"/> 1 = LED sized.</summary>
        public static ElectricSparkle Spawn(Vector3 position, float scale = 1f, Material materialOverride = null)
        {
            if (materialOverride != null) { sharedParticleMaterial = materialOverride; sharedArcMaterial = materialOverride; }
            var go = new GameObject("Electric Sparkle");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            var s = go.AddComponent<ElectricSparkle>();
            s.radius *= scale;
            s.Build();
            return s;
        }

        private void Build()
        {
            BuildSparks();
            for (int i = 0; i < arcCount; i++) arcs.Add(BuildArc());

            flash = gameObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = arcColor;
            flash.range = radius * 6f;
            flash.intensity = 4f;
            flash.shadows = LightShadows.None;
        }

        private void BuildSparks()
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // must be stopped to change duration

            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(sparkColor, Color.white);
            main.gravityModifier = 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 200;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 40),
                new ParticleSystem.Burst(0.15f, 20),
                new ParticleSystem.Burst(0.3f, 15),
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(sparkColor, 0.4f), new GradientColorKey(arcColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 3f;
            renderer.velocityScale = 0.05f;
            renderer.sharedMaterial = ParticleMaterial();

            ps.Play();
        }

        private LineRenderer BuildArc()
        {
            var go = new GameObject("Arc");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 7;
            lr.widthMultiplier = 0.025f * transform.localScale.x;
            lr.numCapVertices = 1;
            lr.sharedMaterial = ArcMaterial();
            lr.startColor = lr.endColor = arcColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        private void Update()
        {
            age += Time.deltaTime;

            // Arcs: jump to new random zig-zags every frame (that "crackle" look), then vanish.
            bool arcsOn = age < arcDuration;
            foreach (LineRenderer lr in arcs)
            {
                lr.enabled = arcsOn && Random.value > 0.25f;
                if (!lr.enabled) continue;
                Vector3 a = transform.position + Random.insideUnitSphere * radius * 0.3f;
                Vector3 b = transform.position + Random.onUnitSphere * radius;
                for (int i = 0; i < lr.positionCount; i++)
                {
                    float t = i / (float)(lr.positionCount - 1);
                    Vector3 p = Vector3.Lerp(a, b, t);
                    if (i > 0 && i < lr.positionCount - 1) p += Random.insideUnitSphere * radius * 0.15f;
                    lr.SetPosition(i, p);
                }
            }

            if (flash != null) flash.intensity = Mathf.Max(0f, 4f * (1f - age / arcDuration)) * Random.Range(0.6f, 1.2f);
            if (age >= lifetime) Destroy(gameObject);
        }

        // ------------------------------------------------------------------ materials (created once)

        private static Material ParticleMaterial()
        {
            if (sharedParticleMaterial != null) return sharedParticleMaterial;
            sharedParticleMaterial = new Material(FindShader("Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit", "Sprites/Default"));
            sharedParticleMaterial.mainTexture = Dot();
            if (sharedParticleMaterial.HasProperty("_BaseMap")) sharedParticleMaterial.SetTexture("_BaseMap", Dot());
            MakeAdditive(sharedParticleMaterial);
            return sharedParticleMaterial;
        }

        private static Material ArcMaterial()
        {
            if (sharedArcMaterial != null) return sharedArcMaterial;
            sharedArcMaterial = new Material(FindShader("Universal Render Pipeline/Particles/Unlit", "Sprites/Default", "Universal Render Pipeline/Unlit"));
            MakeAdditive(sharedArcMaterial);
            return sharedArcMaterial;
        }

        private static Shader FindShader(params string[] names)
        {
            foreach (string n in names)
            {
                Shader s = Shader.Find(n);
                if (s != null) return s;
            }
            return Shader.Find("Hidden/InternalErrorShader");
        }

        private static void MakeAdditive(Material m)
        {
            // URP particle shader: transparent + additive blending.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
        }

        private static Texture2D Dot()
        {
            if (sharedDot != null) return sharedDot;
            const int size = 32;
            sharedDot = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    sharedDot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            sharedDot.Apply();
            return sharedDot;
        }
    }
}
