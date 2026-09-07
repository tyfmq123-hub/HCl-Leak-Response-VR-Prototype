using System;
using System.Collections.Generic;
using UnityEngine;
using VRProject.ObjectInstallation;
using VRProject.Valves;

namespace VRProject.LeakResponse
{
    /// <summary>
    /// Observes completed preplaced absorbent installations and animates their HCl tint
    /// together with the remaining puddle area. It does not participate in installation,
    /// grabbing, or Rigidbody state changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HClAbsorptionVisualController : MonoBehaviour
    {
        const string BaseColorPropertyName = "_BaseColor";
        const string LegacyColorPropertyName = "_Color";

        static readonly int s_BaseColorPropertyId = Shader.PropertyToID(BaseColorPropertyName);
        static readonly int s_LegacyColorPropertyId = Shader.PropertyToID(LegacyColorPropertyName);

        [Header("Puddle")]
        [Tooltip("Existing Scene puddle whose current local scale is captured at runtime.")]
        [SerializeField] Transform puddle;

        [Tooltip("Fraction of the initial X/Z scale remaining after every registered item is absorbed.")]
        [SerializeField, Range(0.01f, 1f)] float minPuddleScaleRatio = 0.1f;

        [Tooltip("Seconds used to move smoothly from the current puddle scale to each new target.")]
        [SerializeField, Min(0.01f)] float puddleShrinkDuration = 2f;

        [Header("Leak Source")]
        [Tooltip("DIS valve state provider. Its existing LeakIntensity01 calculation drives both drops and spread.")]
        [SerializeField] ValveWheelClosureController valveStateProvider;

        [Tooltip("Existing FallingDrops system. Its authored emission rate is captured and scaled by LeakIntensity01.")]
        [SerializeField] ParticleSystem fallingDrops;

        [Tooltip("Existing WaterSplashBurst controller on the floor impact point. It keeps the current splash alive while stopping future bursts when the leak reaches zero.")]
        [SerializeField] WaterSplashBurst splashBurst;

        [Tooltip("Leak values at or below this threshold stop new particle emission and puddle spreading.")]
        [SerializeField, Range(0f, 0.1f)] float leakStopThreshold = 0.001f;

        [Header("Puddle Spread")]
        [Tooltip("Largest X/Z scale multiplier reached while the leak continues without containment.")]
        [SerializeField, Min(1f)] float puddleMaxSpreadMultiplier = 3f;

        [Tooltip("Seconds required to reach the maximum spread while LeakIntensity01 remains at 1.")]
        [SerializeField, Min(0.01f)] float puddleSpreadDuration = 20f;

        [Tooltip("Largest spread multiplier allowed after any containment group is installed. Existing spread is never reduced by this clamp.")]
        [SerializeField, Min(1f)] float containedPuddleMaxSpreadMultiplier = 2f;

        [Tooltip("Groups that establish the containment boundary (normally the Sock group). No group name or Scene position is assumed.")]
        [SerializeField] PreplacedGhostInstallGroup[] containmentGroups =
            Array.Empty<PreplacedGhostInstallGroup>();

        [Header("Absorbent Color")]
        [Tooltip("Wet gray material used after absorption. A runtime instance is created per Renderer material slot; this asset is never modified.")]
        [SerializeField] Material absorbedMaterial;

        [Tooltip("Final wet gray base color. Defaults to #4B5256 and is applied only to runtime material instances or through a MaterialPropertyBlock fallback.")]
        [SerializeField] Color absorbedFinalColor = new Color32(75, 82, 86, 255);

        [Tooltip("Seconds used to blend each newly installed item from its installed material color.")]
        [SerializeField, Min(0.01f)] float absorbColorDuration = 7f;

        [Header("Install Groups")]
        [Tooltip("Optional common root searched when Install Groups is empty. This Transform is used when omitted.")]
        [SerializeField] Transform absorbentGroupsRoot;

        [Tooltip("Optional explicit Pad/Sock groups. Assign the two groups instead of all seven individual items.")]
        [SerializeField] PreplacedGhostInstallGroup[] installGroups =
            Array.Empty<PreplacedGhostInstallGroup>();

        [Header("Runtime State (Read Only)")]
        [SerializeField] int totalAbsorbentCount;
        [SerializeField] int absorbedCount;
        [SerializeField] float totalAbsorptionWeight;
        [SerializeField] float absorbedWeight;
        [SerializeField, Range(0f, 1f)] float currentLeakIntensity;
        [SerializeField, Range(0f, 1f)] float spreadProgress;
        [SerializeField, Range(0f, 1f)] float absorptionRemainingRatio = 1f;
        [SerializeField] bool containmentActive;

        readonly List<PreplacedGhostInstallGroup> resolvedGroups =
            new List<PreplacedGhostInstallGroup>();
        readonly HashSet<PreplacedGhostInstallItem> registeredItems =
            new HashSet<PreplacedGhostInstallItem>();
        readonly HashSet<PreplacedGhostInstallItem> absorbedItems =
            new HashSet<PreplacedGhostInstallItem>();
        readonly HashSet<PreplacedGhostInstallGroup> absorbedGroups =
            new HashSet<PreplacedGhostInstallGroup>();
        readonly List<ColorTransition> colorTransitions = new List<ColorTransition>();

        Vector3 initialPuddleScale;
        float absorptionStartRatio = 1f;
        float absorptionTargetRatio = 1f;
        float absorptionTransitionElapsed;
        float authoredEmissionRateMultiplier;
        float containmentSpreadCeilingMultiplier;
        bool puddleScaleCaptured;
        bool absorptionIsTransitioning;
        bool particleEmissionCaptured;
        ParticleSystem capturedEmissionSource;
        bool completionCleanupApplied;

        public event Action AbsorptionCompleted;

        public int TotalAbsorbentCount => totalAbsorbentCount;
        public int AbsorbedCount => absorbedCount;
        public float TotalAbsorptionWeight => totalAbsorptionWeight;
        public float AbsorbedWeight => absorbedWeight;
        public float CurrentLeakIntensity => currentLeakIntensity;
        public float SpreadProgress => spreadProgress;
        public float AbsorptionRemainingRatio => absorptionRemainingRatio;
        public bool IsAbsorptionComplete =>
            totalAbsorbentCount > 0 &&
            absorbedCount >= totalAbsorbentCount &&
            totalAbsorptionWeight > 0f &&
            absorbedWeight >= totalAbsorptionWeight - 0.0001f &&
            !absorptionIsTransitioning &&
            colorTransitions.Count == 0;
        public bool IsFinalCleanupComplete =>
            completionCleanupApplied &&
            IsAbsorptionComplete &&
            currentLeakIntensity <= leakStopThreshold &&
            (puddle == null || !puddle.gameObject.activeSelf);

        public void ConfigureRuntime(
            Transform newPuddle,
            ValveWheelClosureController newValveStateProvider,
            ParticleSystem newFallingDrops,
            WaterSplashBurst newSplashBurst,
            Transform newAbsorbentGroupsRoot,
            PreplacedGhostInstallGroup[] newInstallGroups,
            PreplacedGhostInstallGroup[] newContainmentGroups)
        {
            UnsubscribeFromValve();
            UnsubscribeFromGroups();

            puddle = newPuddle;
            valveStateProvider = newValveStateProvider;
            fallingDrops = newFallingDrops;
            splashBurst = newSplashBurst;
            absorbentGroupsRoot = newAbsorbentGroupsRoot;
            installGroups = newInstallGroups ?? Array.Empty<PreplacedGhostInstallGroup>();
            containmentGroups = newContainmentGroups ?? Array.Empty<PreplacedGhostInstallGroup>();
            minPuddleScaleRatio = 0.1f;
            puddleShrinkDuration = 2f;
            puddleMaxSpreadMultiplier = 3f;
            puddleSpreadDuration = 20f;
            absorbColorDuration = 7f;
            completionCleanupApplied = false;

            DisableLegacyPuddleScaleWriter();
            CaptureInitialPuddleScale();
            CaptureFallingDropsEmission();
            ResolveInstallGroups();
            SubscribeToValve();
            SubscribeToGroups();
            SynchronizeInstalledGroups();

            if (valveStateProvider != null)
                SetLeakIntensity01(valveStateProvider.LeakIntensity01);
        }

        sealed class ColorTarget
        {
            public readonly Renderer Renderer;
            public readonly int MaterialIndex;
            public readonly int SourceColorPropertyId;
            public readonly Color OriginalColor;
            public readonly Color TransitionColor;
            public readonly Material RuntimeAbsorbedMaterial;
            public readonly int AbsorbedColorPropertyId;
            public readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();

            public ColorTarget(
                Renderer renderer,
                int materialIndex,
                int sourceColorPropertyId,
                Color originalColor,
                Color transitionColor,
                Material runtimeAbsorbedMaterial,
                int absorbedColorPropertyId)
            {
                Renderer = renderer;
                MaterialIndex = materialIndex;
                SourceColorPropertyId = sourceColorPropertyId;
                OriginalColor = originalColor;
                TransitionColor = transitionColor;
                RuntimeAbsorbedMaterial = runtimeAbsorbedMaterial;
                AbsorbedColorPropertyId = absorbedColorPropertyId;
            }
        }

        sealed class ColorTransition
        {
            public readonly List<ColorTarget> Targets;
            public float Elapsed;

            public ColorTransition(List<ColorTarget> targets)
            {
                Targets = targets;
            }
        }

        void Awake()
        {
            DisableLegacyPuddleScaleWriter();
            CaptureInitialPuddleScale();
            CaptureFallingDropsEmission();
            ResolveInstallGroups();

            if (absorbedMaterial == null)
            {
                Debug.LogWarning(
                    "[HCl Absorption] Absorbed Material is not assigned. Falling back to tint-only mode.",
                    this);
            }
        }

        void OnEnable()
        {
            SubscribeToValve();
            SubscribeToGroups();
        }

        void Start()
        {
            // Covers an installation completed before this component subscribed or started.
            SynchronizeInstalledGroups();

            if (puddle != null)
                puddle.gameObject.SetActive(true);

            if (valveStateProvider != null)
                SetLeakIntensity01(valveStateProvider.LeakIntensity01);
            else
                Debug.LogWarning(
                    "[HCl Leak Response] Valve State Provider is not assigned. Drops and spread remain stopped until SetLeakIntensity01 is called.",
                    this);

            ApplyPuddleScale();
        }

        void OnDisable()
        {
            UnsubscribeFromValve();
            UnsubscribeFromGroups();
        }

        void OnDestroy()
        {
            UnsubscribeFromValve();
            UnsubscribeFromGroups();
        }

        void OnValidate()
        {
            minPuddleScaleRatio = Mathf.Clamp(minPuddleScaleRatio, 0.01f, 1f);
            puddleShrinkDuration = Mathf.Max(0.01f, puddleShrinkDuration);
            leakStopThreshold = Mathf.Clamp(leakStopThreshold, 0f, 0.1f);
            puddleMaxSpreadMultiplier = Mathf.Max(1f, puddleMaxSpreadMultiplier);
            puddleSpreadDuration = Mathf.Max(0.01f, puddleSpreadDuration);
            containedPuddleMaxSpreadMultiplier = Mathf.Clamp(
                containedPuddleMaxSpreadMultiplier,
                1f,
                puddleMaxSpreadMultiplier);
            absorbColorDuration = Mathf.Max(0.01f, absorbColorDuration);
        }

        void Update()
        {
            UpdateSpreadProgress();
            UpdateAbsorptionRemainingRatio();
            ApplyPuddleScale();
            UpdateAbsorbentColors();

            if (!completionCleanupApplied && IsAbsorptionComplete)
            {
                completionCleanupApplied = true;
                CleanupLeakVisuals();
                AbsorptionCompleted?.Invoke();
                Debug.Log("[P102 Flow] Absorption cleanup complete", this);
            }
        }

        public void CleanupLeakVisuals()
        {
            currentLeakIntensity = 0f;
            if (fallingDrops != null)
                fallingDrops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            splashBurst?.StopLeak();
            if (puddle != null)
                puddle.gameObject.SetActive(false);
        }

        void DisableLegacyPuddleScaleWriter()
        {
            if (puddle == null)
                return;

            WaterPuddleGrow legacyWriter = puddle.GetComponentInParent<WaterPuddleGrow>(true);
            if (legacyWriter == null || !legacyWriter.enabled)
                return;

            legacyWriter.enabled = false;
            Debug.Log(
                $"[HCl Leak Response] Disabled legacy {nameof(WaterPuddleGrow)} on '{legacyWriter.name}' so only {nameof(HClAbsorptionVisualController)} writes Puddle scale.",
                legacyWriter);
        }

        void CaptureInitialPuddleScale()
        {
            if (puddle == null)
            {
                Debug.LogWarning(
                    "[HCl Absorption] Puddle is not assigned. Absorbent colors can still animate, but the puddle cannot shrink.",
                    this);
                return;
            }

            initialPuddleScale = puddle.localScale;
            absorptionRemainingRatio = 1f;
            absorptionStartRatio = 1f;
            absorptionTargetRatio = 1f;
            containmentSpreadCeilingMultiplier = puddleMaxSpreadMultiplier;
            puddleScaleCaptured = true;
        }

        void CaptureFallingDropsEmission()
        {
            // Awake/OnEnable can already apply the initial valve intensity before the
            // Integration bootstrap binds this same system again. Never capture our
            // own scaled (possibly zero) value as the authored emission rate.
            if (particleEmissionCaptured && capturedEmissionSource == fallingDrops)
                return;
            if (fallingDrops == null)
            {
                Debug.LogWarning(
                    "[HCl Leak Response] Falling Drops is not assigned. Puddle spread and absorption still work.",
                    this);
                return;
            }

            ParticleSystem.EmissionModule emission = fallingDrops.emission;
            authoredEmissionRateMultiplier = Mathf.Max(0f, emission.rateOverTimeMultiplier);
            capturedEmissionSource = fallingDrops;
            particleEmissionCaptured = true;
        }

        void ResolveInstallGroups()
        {
            resolvedGroups.Clear();
            registeredItems.Clear();

            if (installGroups != null)
            {
                for (int i = 0; i < installGroups.Length; ++i)
                    AddGroupIfUnique(installGroups[i]);
            }

            if (resolvedGroups.Count == 0)
            {
                Transform searchRoot = absorbentGroupsRoot != null ? absorbentGroupsRoot : transform;
                PreplacedGhostInstallGroup[] discoveredGroups =
                    searchRoot.GetComponentsInChildren<PreplacedGhostInstallGroup>(true);

                for (int i = 0; i < discoveredGroups.Length; ++i)
                    AddGroupIfUnique(discoveredGroups[i]);
            }

            // Explicit containment groups also participate in absorption. Add them after
            // auto-discovery so assigning only the Sock clamp does not exclude Pad groups.
            if (containmentGroups != null)
            {
                for (int i = 0; i < containmentGroups.Length; ++i)
                    AddGroupIfUnique(containmentGroups[i]);
            }

            for (int groupIndex = 0; groupIndex < resolvedGroups.Count; ++groupIndex)
            {
                PreplacedGhostInstallItem[] items = resolvedGroups[groupIndex].Items;
                if (items == null)
                    continue;

                for (int itemIndex = 0; itemIndex < items.Length; ++itemIndex)
                {
                    if (items[itemIndex] != null)
                        registeredItems.Add(items[itemIndex]);
                }
            }

            totalAbsorbentCount = registeredItems.Count;
            totalAbsorptionWeight = 0f;
            for (int i = 0; i < resolvedGroups.Count; ++i)
                totalAbsorptionWeight += resolvedGroups[i].AbsorptionWeight;

            if (totalAbsorbentCount == 0)
            {
                Debug.LogWarning(
                    "[HCl Absorption] No preplaced absorbent items were found. Assign Install Groups or a common Groups Root.",
                    this);
            }

            if (totalAbsorptionWeight <= 0f)
            {
                Debug.LogWarning(
                    "[HCl Absorption] Total group Absorption Weight is zero. Assign a positive weight to at least one install group.",
                    this);
            }
        }

        void AddGroupIfUnique(PreplacedGhostInstallGroup group)
        {
            if (group != null && !resolvedGroups.Contains(group))
                resolvedGroups.Add(group);
        }

        void SubscribeToGroups()
        {
            for (int i = 0; i < resolvedGroups.Count; ++i)
            {
                resolvedGroups[i].Installed -= OnGroupInstalled;
                resolvedGroups[i].Installed += OnGroupInstalled;
            }
        }

        void UnsubscribeFromGroups()
        {
            for (int i = 0; i < resolvedGroups.Count; ++i)
            {
                if (resolvedGroups[i] != null)
                    resolvedGroups[i].Installed -= OnGroupInstalled;
            }
        }

        void SynchronizeInstalledGroups()
        {
            for (int i = 0; i < resolvedGroups.Count; ++i)
            {
                PreplacedGhostInstallGroup group = resolvedGroups[i];
                if (group != null && group.IsInstalled)
                    ProcessInstalledGroup(group);
            }
        }

        void OnGroupInstalled(PreplacedGhostInstallGroup group)
        {
            ProcessInstalledGroup(group);
        }

        void ProcessInstalledGroup(PreplacedGhostInstallGroup group)
        {
            if (group == null || !resolvedGroups.Contains(group))
                return;

            ActivateContainmentIfNeeded(group);

            if (!absorbedGroups.Add(group))
                return;

            bool addedAny = false;
            PreplacedGhostInstallItem[] items = group.Items;
            if (items == null)
                return;

            for (int i = 0; i < items.Length; ++i)
            {
                PreplacedGhostInstallItem item = items[i];
                if (item == null || !item.IsInstalled || !registeredItems.Contains(item) ||
                    !absorbedItems.Add(item))
                {
                    continue;
                }

                BeginAbsorbentColorTransition(item);
                addedAny = true;
            }

            if (!addedAny)
            {
                absorbedGroups.Remove(group);
                return;
            }

            absorbedCount = absorbedItems.Count;
            absorbedWeight = Mathf.Min(totalAbsorptionWeight,
                absorbedWeight + group.AbsorptionWeight);
            RetargetPuddleScale();
        }

        void ActivateContainmentIfNeeded(PreplacedGhostInstallGroup group)
        {
            if (containmentActive || containmentGroups == null ||
                Array.IndexOf(containmentGroups, group) < 0)
            {
                return;
            }

            containmentActive = true;
            // Installing containment may stop future growth, but it must never erase liquid
            // that had already spread outside the configured approximation.
            containmentSpreadCeilingMultiplier = Mathf.Max(
                CurrentUncontainedSpreadMultiplier(),
                Mathf.Min(containedPuddleMaxSpreadMultiplier, puddleMaxSpreadMultiplier));
        }

        void RetargetPuddleScale()
        {
            if (!puddleScaleCaptured || puddle == null || totalAbsorptionWeight <= 0f)
                return;

            float progress = Mathf.Clamp01(absorbedWeight / totalAbsorptionWeight);
            float remainingRatio = Mathf.Lerp(1f, minPuddleScaleRatio, progress);

            absorptionStartRatio = absorptionRemainingRatio;
            absorptionTargetRatio = remainingRatio;
            absorptionTransitionElapsed = 0f;
            absorptionIsTransitioning = true;
        }

        void UpdateSpreadProgress()
        {
            if (currentLeakIntensity <= leakStopThreshold || spreadProgress >= 1f)
                return;

            spreadProgress = Mathf.Clamp01(
                spreadProgress + currentLeakIntensity * Time.deltaTime / puddleSpreadDuration);
        }

        void UpdateAbsorptionRemainingRatio()
        {
            if (!absorptionIsTransitioning)
                return;

            absorptionTransitionElapsed += Time.deltaTime;
            float duration = Mathf.Max(0.01f, puddleShrinkDuration);
            float t = Mathf.Clamp01(absorptionTransitionElapsed / duration);
            float easedT = Mathf.SmoothStep(0f, 1f, t);
            absorptionRemainingRatio = Mathf.LerpUnclamped(
                absorptionStartRatio,
                absorptionTargetRatio,
                easedT);

            if (t >= 1f)
                absorptionIsTransitioning = false;
        }

        float CurrentUncontainedSpreadMultiplier()
        {
            float easedProgress = Mathf.SmoothStep(0f, 1f, spreadProgress);
            return Mathf.Lerp(1f, puddleMaxSpreadMultiplier, easedProgress);
        }

        float CurrentSpreadMultiplier()
        {
            float multiplier = CurrentUncontainedSpreadMultiplier();
            return containmentActive
                ? Mathf.Min(multiplier, containmentSpreadCeilingMultiplier)
                : multiplier;
        }

        void ApplyPuddleScale()
        {
            if (!puddleScaleCaptured || puddle == null)
                return;

            float finalRatio = CurrentSpreadMultiplier() * absorptionRemainingRatio;
            puddle.localScale = new Vector3(
                initialPuddleScale.x * finalRatio,
                initialPuddleScale.y,
                initialPuddleScale.z * finalRatio);
        }

        void SubscribeToValve()
        {
            if (valveStateProvider == null)
                return;

            valveStateProvider.OnLeakIntensityChanged.RemoveListener(SetLeakIntensity01);
            valveStateProvider.OnLeakIntensityChanged.AddListener(SetLeakIntensity01);
        }

        void UnsubscribeFromValve()
        {
            if (valveStateProvider != null)
                valveStateProvider.OnLeakIntensityChanged.RemoveListener(SetLeakIntensity01);
        }

        /// <summary>
        /// Applies the existing valve's normalized leak result without recalculating wheel angles.
        /// This public entry point also remains compatible with ValveLeakOutput's optional controller API.
        /// </summary>
        public void SetLeakIntensity01(float value)
        {
            currentLeakIntensity = Mathf.Clamp01(value);
            UpdateFallingDropsEmission();
            UpdateSplashBurst();
        }

        void UpdateFallingDropsEmission()
        {
            if (!particleEmissionCaptured || fallingDrops == null)
                return;

            bool shouldEmit = currentLeakIntensity > leakStopThreshold;
            float normalizedLeak = shouldEmit ? currentLeakIntensity : 0f;

            ParticleSystem.EmissionModule emission = fallingDrops.emission;
            emission.rateOverTimeMultiplier = authoredEmissionRateMultiplier * normalizedLeak;

            if (shouldEmit)
            {
                emission.enabled = true;
                if (fallingDrops.gameObject.activeInHierarchy && !fallingDrops.isPlaying)
                    fallingDrops.Play(false);
            }
            else if (fallingDrops.isPlaying || fallingDrops.isEmitting)
            {
                fallingDrops.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        void UpdateSplashBurst()
        {
            if (splashBurst == null)
                return;

            if (currentLeakIntensity > leakStopThreshold)
                splashBurst.StartLeak();
            else
                splashBurst.StopLeak();
        }

        void BeginAbsorbentColorTransition(PreplacedGhostInstallItem item)
        {
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            var targets = new List<ColorTarget>();

            for (int rendererIndex = 0; rendererIndex < renderers.Length; ++rendererIndex)
            {
                Renderer renderer = renderers[rendererIndex];
                Material[] materials = renderer.sharedMaterials;

                for (int materialIndex = 0; materialIndex < materials.Length; ++materialIndex)
                {
                    Material sourceMaterial = materials[materialIndex];
                    bool hasSourceColor = TryGetColorProperty(
                        sourceMaterial,
                        out int sourceColorPropertyId);

                    if (!hasSourceColor && absorbedMaterial == null)
                    {
                        string materialName = sourceMaterial != null
                            ? sourceMaterial.name
                            : "<missing>";
                        Debug.LogWarning(
                            $"[HCl Absorption] Renderer '{renderer.name}' material '{materialName}' has no supported Base Color property and was skipped.",
                            renderer);
                        continue;
                    }

                    Color originalColor = Color.white;
                    if (hasSourceColor)
                    {
                        var propertyBlock = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(propertyBlock, materialIndex);
                        originalColor = propertyBlock.HasColor(sourceColorPropertyId)
                            ? propertyBlock.GetColor(sourceColorPropertyId)
                            : sourceMaterial.GetColor(sourceColorPropertyId);
                    }

                    Material runtimeAbsorbedMaterial = CreateRuntimeAbsorbedMaterial(
                        renderer,
                        materialIndex,
                        out int absorbedColorPropertyId,
                        out Color transitionColor);

                    targets.Add(new ColorTarget(
                        renderer,
                        materialIndex,
                        hasSourceColor ? sourceColorPropertyId : 0,
                        originalColor,
                        transitionColor,
                        runtimeAbsorbedMaterial,
                        absorbedColorPropertyId));
                }
            }

            if (targets.Count > 0)
                colorTransitions.Add(new ColorTransition(targets));
        }

        void UpdateAbsorbentColors()
        {
            float duration = Mathf.Max(0.01f, absorbColorDuration);

            for (int transitionIndex = colorTransitions.Count - 1;
                 transitionIndex >= 0;
                 --transitionIndex)
            {
                ColorTransition transition = colorTransitions[transitionIndex];
                transition.Elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(transition.Elapsed / duration);
                float easedT = Mathf.SmoothStep(0f, 1f, t);

                for (int targetIndex = 0; targetIndex < transition.Targets.Count; ++targetIndex)
                {
                    ColorTarget target = transition.Targets[targetIndex];
                    if (target.Renderer == null)
                        continue;

                    if (target.SourceColorPropertyId != 0)
                    {
                        target.Renderer.GetPropertyBlock(
                            target.PropertyBlock,
                            target.MaterialIndex);
                        target.PropertyBlock.SetColor(
                            target.SourceColorPropertyId,
                            Color.LerpUnclamped(
                                target.OriginalColor,
                                target.TransitionColor,
                                easedT));
                        target.Renderer.SetPropertyBlock(
                            target.PropertyBlock,
                            target.MaterialIndex);
                    }

                    if (t >= 1f && target.RuntimeAbsorbedMaterial != null)
                        ApplyRuntimeAbsorbedMaterial(target);
                }

                if (t >= 1f)
                    colorTransitions.RemoveAt(transitionIndex);
            }
        }

        Material CreateRuntimeAbsorbedMaterial(
            Renderer renderer,
            int materialIndex,
            out int colorPropertyId,
            out Color transitionColor)
        {
            colorPropertyId = 0;
            transitionColor = absorbedFinalColor;

            if (absorbedMaterial == null)
                return null;

            var runtimeMaterial = new Material(absorbedMaterial)
            {
                name = $"{absorbedMaterial.name} (HCl Absorbed Runtime)",
                hideFlags = HideFlags.DontSave
            };

            if (TryGetColorProperty(runtimeMaterial, out colorPropertyId))
            {
                runtimeMaterial.SetColor(colorPropertyId, absorbedFinalColor);
                transitionColor = absorbedFinalColor;
            }
            else
            {
                Debug.LogWarning(
                    $"[HCl Absorption] Absorbed Material '{absorbedMaterial.name}' has no supported Base Color property. Its authored appearance will still be used for Renderer '{renderer.name}' slot {materialIndex}.",
                    renderer);
            }

            return runtimeMaterial;
        }

        static void ApplyRuntimeAbsorbedMaterial(ColorTarget target)
        {
            Material[] currentMaterials = target.Renderer.sharedMaterials;
            if (target.MaterialIndex < 0 || target.MaterialIndex >= currentMaterials.Length)
                return;

            currentMaterials[target.MaterialIndex] = target.RuntimeAbsorbedMaterial;
            target.Renderer.sharedMaterials = currentMaterials;

            if (target.AbsorbedColorPropertyId == 0)
                return;

            target.Renderer.GetPropertyBlock(target.PropertyBlock, target.MaterialIndex);
            target.PropertyBlock.SetColor(
                target.AbsorbedColorPropertyId,
                target.RuntimeAbsorbedMaterial.GetColor(target.AbsorbedColorPropertyId));
            target.Renderer.SetPropertyBlock(target.PropertyBlock, target.MaterialIndex);
        }

        static bool TryGetColorProperty(Material material, out int propertyId)
        {
            if (material != null && material.HasProperty(s_BaseColorPropertyId))
            {
                propertyId = s_BaseColorPropertyId;
                return true;
            }

            if (material != null && material.HasProperty(s_LegacyColorPropertyId))
            {
                propertyId = s_LegacyColorPropertyId;
                return true;
            }

            propertyId = 0;
            return false;
        }
    }
}
