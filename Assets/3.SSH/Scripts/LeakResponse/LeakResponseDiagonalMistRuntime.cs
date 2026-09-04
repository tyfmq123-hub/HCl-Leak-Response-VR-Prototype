using UnityEngine;
using UnityEngine.Rendering;
using VRProject.Valves;

namespace VRProject.LeakResponse
{
    [DisallowMultipleComponent]
    public sealed class LeakResponseDiagonalMistRuntime : MonoBehaviour
    {
        const string ParticleObjectName = "P102_DiagonalMist_Runtime";
        const string MistMaterialResource = "LeakVFX/M_DiagonalMist";
        const float MaximumEmissionRate = 82f;
        const float StartThreshold = 0.001f;
        static readonly Vector3 LocalSprayDirection =
            new Vector3(0.62f, -0.58f, 0.52f).normalized;

        ValveWheelClosureController leakStateProvider;
        ParticleSystem mistParticles;
        Material runtimeMaterial;
        Texture2D runtimeTexture;
        float intensity01;

        public ParticleSystem MistParticles => mistParticles;
        public float Intensity01 => intensity01;

        public void Configure(
            Transform fallingDropsAnchor,
            ValveWheelClosureController newLeakStateProvider)
        {
            Unsubscribe();
            leakStateProvider = newLeakStateProvider;
            if (fallingDropsAnchor == null)
            {
                Debug.LogError(
                    "[LeakResponse Mist] FallingDrops anchor is missing.", this);
                enabled = false;
                return;
            }

            Transform particleTransform =
                fallingDropsAnchor.Find(ParticleObjectName);
            if (particleTransform == null)
            {
                var particleObject = new GameObject(ParticleObjectName);
                particleTransform = particleObject.transform;
                particleTransform.SetParent(fallingDropsAnchor, false);
            }

            particleTransform.localPosition = Vector3.zero;
            particleTransform.localRotation = Quaternion.LookRotation(
                LocalSprayDirection, Vector3.up);
            particleTransform.localScale = Vector3.one;
            mistParticles = particleTransform.GetComponent<ParticleSystem>();
            if (mistParticles == null)
                mistParticles = particleTransform.gameObject.AddComponent<ParticleSystem>();

            ConfigureParticles(mistParticles);
            Subscribe();
            float initialIntensity = leakStateProvider != null
                ? leakStateProvider.LeakIntensity01
                : 0.7f;
            if (leakStateProvider != null && initialIntensity <= StartThreshold &&
                !leakStateProvider.IsFullyClosed)
            {
                // Scene activation can configure this helper just before the Valve's
                // Start initializes its current leak value. Use the serialized initial
                // closure for that single frame so the mist never starts invisibly.
                initialIntensity = ValveWheelClosureController.CalculateLinearLeakIntensity01(
                    leakStateProvider.InitialClosurePercent);
            }
            SetLeakIntensity01(initialIntensity);
            enabled = true;
        }

        void ConfigureParticles(ParticleSystem particles)
        {
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, 1.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.75f, 1.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.27f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.78f, 1f, 0.86f, 0.48f),
                new Color(1f, 1f, 1f, 0.72f));
            main.gravityModifier = 0.08f;
            main.maxParticles = 320;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 16f;
            shape.radius = 0.025f;
            shape.length = 0.12f;

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 0.14f;
            noise.frequency = 0.52f;
            noise.scrollSpeed = 0.22f;
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
                particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.82f, 1f, 0.9f), 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.72f, 0.08f),
                    new GradientAlphaKey(0.45f, 0.55f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;

            ParticleSystemRenderer renderer =
                particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = 1.5f;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sharedMaterial = ResolveMaterial();
            if (renderer.sharedMaterial == null)
            {
                Debug.LogError(
                    "[LeakResponse Mist] The build-safe mist material is missing.",
                    this);
            }
        }

        Material ResolveMaterial()
        {
            if (runtimeMaterial != null)
                return runtimeMaterial;

            Material template = Resources.Load<Material>(MistMaterialResource);
            if (template == null)
                return null;

            runtimeMaterial = new Material(template)
            {
                name = "P102 Diagonal Mist Material Runtime",
                hideFlags = HideFlags.DontSave,
                renderQueue = (int)RenderQueue.Transparent,
            };
            runtimeTexture = CreateSoftMistTexture();
            if (runtimeMaterial.HasProperty("_BaseMap"))
                runtimeMaterial.SetTexture("_BaseMap", runtimeTexture);
            if (runtimeMaterial.HasProperty("_MainTex"))
                runtimeMaterial.SetTexture("_MainTex", runtimeTexture);
            if (runtimeMaterial.HasProperty("_BaseColor"))
                runtimeMaterial.SetColor(
                    "_BaseColor", Color.white);
            if (runtimeMaterial.HasProperty("_Color"))
                runtimeMaterial.SetColor(
                    "_Color", Color.white);
            if (runtimeMaterial.HasProperty("_Surface"))
                runtimeMaterial.SetFloat("_Surface", 1f);
            if (runtimeMaterial.HasProperty("_SrcBlend"))
                runtimeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (runtimeMaterial.HasProperty("_DstBlend"))
                runtimeMaterial.SetFloat(
                    "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (runtimeMaterial.HasProperty("_ZWrite"))
                runtimeMaterial.SetFloat("_ZWrite", 0f);
            runtimeMaterial.SetOverrideTag("RenderType", "Transparent");
            runtimeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return runtimeMaterial;
        }

        static Texture2D CreateSoftMistTexture()
        {
            const int size = 64;
            var texture = new Texture2D(
                size, size, TextureFormat.RGBA32, false, true)
            {
                name = "P102 Soft Mist Texture Runtime",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float distance = Mathf.Sqrt(nx * nx + ny * ny);
                    float alpha = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(distance));
                    alpha *= Mathf.SmoothStep(0f, 1f, alpha);
                    pixels[y * size + x] = new Color32(
                        232, 255, 240, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public void SetLeakIntensity01(float value)
        {
            intensity01 = Mathf.Clamp01(value);
            if (mistParticles == null)
                return;

            ParticleSystem.EmissionModule emission = mistParticles.emission;
            emission.rateOverTime = ResolveEmissionRate(intensity01);

            if (intensity01 > StartThreshold && isActiveAndEnabled)
            {
                if (!mistParticles.isPlaying)
                    mistParticles.Play(true);
            }
            else if (mistParticles.isPlaying)
            {
                mistParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        public static float ResolveEmissionRate(float leakIntensity) =>
            MaximumEmissionRate * Mathf.Sqrt(Mathf.Clamp01(leakIntensity));

        void OnEnable()
        {
            if (mistParticles != null)
                SetLeakIntensity01(intensity01);
        }

        void OnDisable()
        {
            if (mistParticles != null)
                mistParticles.Stop(
                    true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
            if (runtimeTexture != null)
                Destroy(runtimeTexture);
        }

        void Subscribe()
        {
            if (leakStateProvider != null)
                leakStateProvider.OnLeakIntensityChanged.AddListener(
                    SetLeakIntensity01);
        }

        void Unsubscribe()
        {
            if (leakStateProvider != null)
                leakStateProvider.OnLeakIntensityChanged.RemoveListener(
                    SetLeakIntensity01);
        }
    }
}
