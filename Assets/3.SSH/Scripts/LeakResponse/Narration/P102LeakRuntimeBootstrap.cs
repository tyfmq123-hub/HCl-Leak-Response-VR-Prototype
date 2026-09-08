using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using VRProject.LeakResponse;
using VRProject.LeakResponse.Logic;
using VRProject.ObjectInstallation;
using VRProject.Valves;

namespace VRProject.LeakResponse.Narration
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class P102LeakRuntimeBootstrap : MonoBehaviour
    {
        const string BootstrapName = "P102LeakRuntimeBootstrap";
        static readonly string[] SockGhostNames =
        {
            "INT_AbsorbentSock_Straight_01_ObjectGhost1",
            "INT_AbsorbentSock_Straight_01_ObjectGhost2",
            "INT_AbsorbentSock_Curved_01_ObjectGhost1",
            "INT_AbsorbentSock_Curved_01_ObjectGhost2",
        };

        static readonly string[] PadGhostNames =
        {
            "INT_AbsorbentPad_01_ObjectGhost1",
            "INT_AbsorbentPad_01_ObjectGhost2",
            "INT_AbsorbentPad_01_ObjectGhost3",
        };

        const float AbsorbentMagnetRadius = 1f;

        static P102LeakRuntimeBootstrap activeInstance;
        readonly SingleExecutionGate configureGate = new SingleExecutionGate();
        P102LeakNarrationController gatedNarration;
        PreplacedGhostInstallGroup gatedSockGroup;
        PreplacedGhostInstallGroup gatedPadGroup;
        XRGrabInteractable sockSourceGrab;
        XRGrabInteractable padSourceGrab;
        Coroutine stageFlowStartRoutine;
        bool stageActivated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetRuntimeState()
        {
            activeInstance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureRuntimeBootstrap()
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i)
                EnsureRuntimeBootstrap(SceneManager.GetSceneAt(i));
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureRuntimeBootstrap(scene);
        }

        static void EnsureRuntimeBootstrap(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            P102LeakRuntimeBootstrap existing = FindSceneComponent<P102LeakRuntimeBootstrap>(scene);
            if (existing != null && existing.gameObject.activeInHierarchy)
            {
                existing.enabled = true;
                return;
            }

            if (existing != null)
            {
                Debug.LogError(
                    $"[P102 Flow] Existing bootstrap '{existing.name}' is inactive in the hierarchy. " +
                    "An active runtime bootstrap will repair the Scene instead.", existing);
            }

            if (FindSceneObject(scene, "ENV_LeakPipeGateValveWheel_suc") == null &&
                FindSceneObject(scene, "ENV_LeakPipeGateValveWheel_dis") == null)
                return;

            var root = new GameObject(BootstrapName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<P102LeakRuntimeBootstrap>();
        }

        static T FindSceneComponent<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }

        static GameObject FindSceneObject(Scene scene, string exactName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; ++i)
                {
                    if (string.Equals(transforms[i].name, exactName, StringComparison.Ordinal))
                        return transforms[i].gameObject;
                }
            }
            return null;
        }

        void Awake()
        {
            if (activeInstance != null &&
                (!activeInstance.gameObject.activeInHierarchy || !activeInstance.enabled))
            {
                activeInstance = null;
            }

            if (activeInstance != null && activeInstance != this)
            {
                enabled = false;
                Debug.LogWarning("[P102 Flow] Duplicate runtime bootstrap was disabled.", this);
                return;
            }

            activeInstance = this;
            stageActivated = P102LeakStageActivation.ShouldActivate(gameObject.scene);
            P102WasteContainerPhysics.PrepareBeforeSimulation(
                FindSceneObject(gameObject.scene, "INT_WasteContainer_01"));
            HideAuthoredGrabPoseGhosts();
            ConfigureEnvironmentAudio();
        }

        void HideAuthoredGrabPoseGhosts()
        {
            P102RuntimeUtility.HideHandPoseGhostRenderers(
                FindSceneObject(gameObject.scene, "INT_RecoveryTongs_01"));
            P102RuntimeUtility.HideHandPoseGhostRenderers(
                FindSceneObject(gameObject.scene, "HazWasteContainer_Lid"));
        }

        void ConfigureEnvironmentAudio(
            ValveWheelClosureController leakStateProvider = null)
        {
            Scene scene = gameObject.scene;
            GameObject mistAnchor = FindSceneObject(scene, "LeakPoint");
            GameObject dropletsAnchor = FindSceneObject(scene, "FallingDrops");
            GameObject pumpAnchor = FindSceneObject(scene, "Pump_102");
            GameObject fenceAnchor = FindSceneObject(
                scene, "ENV_SiteFence_Set_01 (9)");
            GameObject alarm01Anchor = FindSceneObject(
                scene, "ENV_SiteFence_Set_01 (6)");
            GameObject alarm02Anchor = FindSceneObject(
                scene, "ENV_SiteFence_Set_01 (13)");

            if (leakStateProvider == null)
            {
                GameObject dischargeValve = FindSceneObject(
                    scene, "ENV_LeakPipeGateValveWheel_dis");
                if (dischargeValve != null)
                {
                    leakStateProvider =
                        dischargeValve.GetComponent<ValveWheelClosureController>();
                }
            }

            LeakResponseSpatialLeakAudio spatialAudio =
                ResolveOrCreateHelper<LeakResponseSpatialLeakAudio>();
            spatialAudio.Configure(
                mistAnchor != null ? mistAnchor.transform : null,
                dropletsAnchor != null ? dropletsAnchor.transform : null,
                pumpAnchor != null ? pumpAnchor.transform : null,
                fenceAnchor != null ? fenceAnchor.transform : null,
                alarm01Anchor != null ? alarm01Anchor.transform : null,
                alarm02Anchor != null ? alarm02Anchor.transform : null,
                leakStateProvider);
            spatialAudio.SetLeakStageActive(stageActivated);
        }

        void OnDestroy()
        {
            StopScheduledStageFlowStart();
            UnsubscribeInteractionGates();
            if (activeInstance == this)
                activeInstance = null;
        }

        IEnumerator Start()
        {
            // Existing Scene components finish Awake/Start first; this layer only fills missing wiring.
            yield return null;
            while (!stageActivated)
                yield return null;
            ConfigureOnce();
        }

        public bool IsStageActivated => stageActivated;

        public void SetStageActivated(bool active)
        {
            stageActivated = active;
            ConfigureEnvironmentAudio();

            if (!active)
            {
                StopScheduledStageFlowStart();
                gatedNarration?.SetStageActivated(false);
                P102RuntimeUtility.FindSceneComponent<P102LeakEnvironmentSetup>()
                    ?.SetStageActive(false);
                P102RuntimeUtility.FindSceneComponent<P102AbsorbentPlayerBoundaryRuntime>()
                    ?.SetStageActive(false);
                var bagHandler = GetComponent<P102SharedBagItemHandler>();
                if (bagHandler != null)
                    bagHandler.enabled = false;
                return;
            }

            ConfigureOnce();
            P102RuntimeUtility.FindSceneComponent<P102LeakEnvironmentSetup>()
                ?.SetStageActive(true);
            P102RuntimeUtility.FindSceneComponent<P102AbsorbentPlayerBoundaryRuntime>()
                ?.SetStageActive(true);
            gatedNarration?.SetStageActivated(true);
            ScheduleStageFlowStart(gatedNarration);
            var activeBagHandler = GetComponent<P102SharedBagItemHandler>();
            if (activeBagHandler != null)
                activeBagHandler.enabled = true;
        }

        public void ConfigureOnce()
        {
            if (!stageActivated)
                return;
            if (!configureGate.TryEnter())
                return;

            P102LeakEnvironmentSetup environmentSetup =
                ResolveOrCreateHelper<P102LeakEnvironmentSetup>();
            environmentSetup.Configure(gameObject.scene);
            DisableLeakSceneHandVisuals();

            ValveWheelClosureController suctionValve = FindValve("ENV_LeakPipeGateValveWheel_suc");
            ValveWheelClosureController dischargeValve = FindValve("ENV_LeakPipeGateValveWheel_dis");
            GameObject leakPointObject = P102RuntimeUtility.FindSceneObject("LeakPoint");
            ConfigureEnvironmentAudio(dischargeValve);

            PreplacedGhostInstallGroup sockGroup = ResolveOrBuildGroup(
                "Sock", SockGhostNames, "INT_AbsorbentSock_Straight_01", 0.25f);
            PreplacedGhostInstallGroup padGroup = ResolveOrBuildGroup(
                "Pad", PadGhostNames, "INT_AbsorbentPad_01", 0.75f);
            sockSourceGrab = ResolveSourceGrab("Sock", "INT_AbsorbentSock_Straight_01", sockGroup);
            padSourceGrab = ResolveSourceGrab("Pad", "INT_AbsorbentPad_01", padGroup);

            GameObject puddleObject = P102RuntimeUtility.FindSceneObject("Puddle");
            GameObject fallingDropsObject = P102RuntimeUtility.FindSceneObject("FallingDrops");
            ParticleSystem fallingDrops = fallingDropsObject != null
                ? fallingDropsObject.GetComponent<ParticleSystem>()
                : null;
            WaterSplashBurst splashBurst = P102RuntimeUtility.FindSceneComponent<WaterSplashBurst>();
            if (puddleObject == null)
                Debug.LogError("[P102 Flow] Required leak visual 'Puddle' is missing.", this);
            if (fallingDropsObject == null)
                Debug.LogError("[P102 Flow] Required leak source 'FallingDrops' is missing.", this);
            else if (fallingDrops == null)
                Debug.LogError("[P102 Flow] 'FallingDrops' has no ParticleSystem.", fallingDropsObject);

            LeakResponseDiagonalMistRuntime diagonalMist =
                ResolveOrCreateHelper<LeakResponseDiagonalMistRuntime>();
            diagonalMist.Configure(
                fallingDropsObject != null ? fallingDropsObject.transform : null,
                dischargeValve);

            HClAbsorptionVisualController absorption =
                P102RuntimeUtility.FindSceneComponent<HClAbsorptionVisualController>() ??
                gameObject.AddComponent<HClAbsorptionVisualController>();
            absorption.gameObject.SetActive(true);
            absorption.enabled = true;
            absorption.ConfigureRuntime(
                puddleObject != null ? puddleObject.transform : null,
                dischargeValve,
                fallingDrops,
                splashBurst,
                transform,
                CompactGroups(sockGroup, padGroup),
                sockGroup != null ? new[] { sockGroup } : Array.Empty<PreplacedGhostInstallGroup>());

            Transform cameraTransform = ResolveMainCamera();
            if (cameraTransform == null)
                Debug.LogError("[P102 Flow] Required XR/Main Camera could not be resolved.", this);
            P102LeakNarrationController narration =
                P102RuntimeUtility.FindSceneComponent<P102LeakNarrationController>() ??
                gameObject.AddComponent<P102LeakNarrationController>();
            narration.gameObject.SetActive(true);
            narration.enabled = true;
            narration.ConfigureRuntime(
                suctionValve,
                dischargeValve,
                dischargeValve,
                sockGroup,
                padGroup,
                absorption);
            LeakResponseTelemetryReporter telemetry =
                ResolveOrCreateHelper<LeakResponseTelemetryReporter>();
            telemetry.Configure(narration);
            narration.SetStageActivated(true);
            ConfigureInteractionGates(narration, sockGroup, padGroup);
            P102SharedBagItemHandler bagHandler =
                P102RuntimeUtility.GetOrAdd<P102SharedBagItemHandler>(gameObject);
            bagHandler.Configure(narration, sockGroup, padGroup, sockSourceGrab, padSourceGrab);

            Transform suctionUiAnchor = ResolveRequiredFixedAnchor(
                "ENV_LeakPipeGateValve_suc", "SUC Valve Root");
            Transform dischargeUiAnchor = ResolveRequiredFixedAnchor(
                "ENV_LeakPipeGateValve_dis", "DIS Valve Root");
            P102ValveGuidanceRuntime guidance = ResolveOrCreateHelper<P102ValveGuidanceRuntime>();
            guidance.Configure(suctionValve, dischargeValve, narration, cameraTransform,
                suctionUiAnchor, dischargeUiAnchor);

            if (leakPointObject == null)
                Debug.LogError("[P102 Flow] Required flange gaze target 'LeakPoint' is missing.", this);
            Transform flangeRoot = ResolveRequiredFixedAnchor(
                "ENV_LeakPipeFlange2_01_dis", "Flange Root");
            P102FlangeGazeRuntime gaze = ResolveOrCreateHelper<P102FlangeGazeRuntime>();
            gaze.Configure(narration, flangeRoot,
                leakPointObject != null ? leakPointObject.transform : null, cameraTransform,
                dischargeValve != null ? dischargeValve.transform : null);

            P102RecoveryRuntime recovery = ResolveOrCreateHelper<P102RecoveryRuntime>();
            recovery.Configure(sockGroup, padGroup, absorption, narration);

            P102WasteContainerRuntime waste = ResolveOrCreateHelper<P102WasteContainerRuntime>();
            waste.Configure(
                FindSceneObject(gameObject.scene, "INT_WasteContainer_01"), narration, absorption);

            P102RecoveryTongsRuntime tongs = ResolveOrCreateHelper<P102RecoveryTongsRuntime>();
            GameObject tongsGhostObject = FindSceneObject(
                gameObject.scene, "INT_RecoveryTongs_01_ObjectGhost");
            tongs.Configure(
                FindSceneObject(gameObject.scene, "INT_RecoveryTongs_01"),
                tongsGhostObject != null ? tongsGhostObject.transform : null,
                recovery,
                waste,
                narration);

            P102AbsorbentPlayerBoundaryRuntime playerBoundary =
                ResolveOrCreateHelper<P102AbsorbentPlayerBoundaryRuntime>();
            playerBoundary.Configure(
                gameObject.scene, ResolveGroupItemTransforms(sockGroup));

            ScheduleStageFlowStart(narration);
            Debug.Log(
                $"[P102 Flow] Bootstrap configured once. Media=disabled, Sock={sockGroup?.Items?.Length ?? 0}/4, " +
                $"Pad={padGroup?.Items?.Length ?? 0}/3, Facility=P102.", this);
        }

        void ScheduleStageFlowStart(P102LeakNarrationController narration)
        {
            if (narration == null ||
                narration.HasDispatched(P102LeakNarrationCue.LeakSceneArrival))
                return;

            StopScheduledStageFlowStart();
            stageFlowStartRoutine = StartCoroutine(
                BeginStageFlowAfterLifecycleBind(narration));
        }

        IEnumerator BeginStageFlowAfterLifecycleBind(
            P102LeakNarrationController narration)
        {
            // StageLifecycleAdapter intentionally waits two frames before binding.
            // Waiting one additional frame ensures it receives the immediate
            // Arrival start/completion pair and releases its interaction lock.
            for (int frame = 0; frame < 3; ++frame)
                yield return null;

            stageFlowStartRoutine = null;
            if (stageActivated && narration != null && narration.isActiveAndEnabled)
                narration.BeginStageFlow();
        }

        void StopScheduledStageFlowStart()
        {
            if (stageFlowStartRoutine == null)
                return;

            StopCoroutine(stageFlowStartRoutine);
            stageFlowStartRoutine = null;
        }

        PreplacedGhostInstallGroup ResolveOrBuildGroup(string typeId, string[] ghostNames,
            string sourceObjectName, float absorptionWeight)
        {
            PreplacedGhostInstallGroup existing = FindExistingGroup(typeId);
            if (HasExpectedItems(existing, ghostNames.Length, typeId))
            {
                existing.gameObject.SetActive(true);
                existing.enabled = true;
                existing.ConfigureRuntime(
                    typeId,
                    existing.ActivationCenter,
                    Mathf.Max(existing.InstallRadius, AbsorbentMagnetRadius),
                    absorptionWeight,
                    existing.Items);
                return existing;
            }

            GameObject sourceObject = P102RuntimeUtility.FindSceneObject(sourceObjectName);
            Renderer sourceRenderer = sourceObject != null
                ? sourceObject.GetComponentInChildren<Renderer>(true)
                : null;
            var items = new List<PreplacedGhostInstallItem>(ghostNames.Length);
            Vector3 center = Vector3.zero;

            for (int i = 0; i < ghostNames.Length; ++i)
            {
                GameObject ghost = P102RuntimeUtility.FindSceneObject(ghostNames[i]);
                if (ghost == null)
                {
                    Debug.LogError($"[P102 Flow] Required absorbent Ghost '{ghostNames[i]}' is missing.", this);
                    continue;
                }

                PreplacedGhostInstallItem item = ConfigureRuntimeGhostItem(ghost, typeId, sourceRenderer);
                if (item != null)
                {
                    items.Add(item);
                    center += ghost.transform.position;
                }
            }

            if (items.Count != ghostNames.Length)
            {
                Debug.LogError($"[P102 Flow] {typeId} installation group is incomplete ({items.Count}/{ghostNames.Length}). It will not auto-complete.", this);
                return null;
            }

            var groupObject = new GameObject($"P102_{typeId}_InstallGroup_Runtime");
            PreplacedGhostInstallGroup group;
            if (existing != null)
            {
                Destroy(groupObject);
                group = existing;
                group.gameObject.SetActive(true);
                group.enabled = true;
            }
            else
            {
                groupObject.transform.SetParent(transform, true);
                groupObject.transform.position = center / items.Count;
                group = groupObject.AddComponent<PreplacedGhostInstallGroup>();
            }
            group.ConfigureRuntime(typeId, group.transform, AbsorbentMagnetRadius,
                absorptionWeight, items.ToArray());
            return group;
        }

        static bool HasExpectedItems(PreplacedGhostInstallGroup group, int expectedCount, string typeId)
        {
            if (group == null || group.Items == null || group.Items.Length != expectedCount)
                return false;

            for (int i = 0; i < group.Items.Length; ++i)
            {
                PreplacedGhostInstallItem item = group.Items[i];
                if (item == null ||
                    !PreplacedGhostInstallGroup.ItemTypeIdsMatch(item.InstallationTypeId, typeId) ||
                    !item.TryValidateConfiguration(out _))
                {
                    return false;
                }

                item.gameObject.SetActive(true);
            }

            return true;
        }

        XRGrabInteractable ResolveSourceGrab(string typeId, string exactObjectName,
            PreplacedGhostInstallGroup group)
        {
            // Full Sequence keeps App/PPE/PreEntry objects loaded additively. Resolve
            // the authored supply only inside this LeakResponse Scene so an inactive
            // shared inventory record with the same name cannot win the global search.
            GameObject sourceObject = FindSceneObject(gameObject.scene, exactObjectName);
            if (sourceObject == null)
            {
                Debug.LogError(
                    $"[P102 Flow] Required clean {typeId} source '{exactObjectName}' is missing. Installation remains blocked.",
                    this);
                return null;
            }

            // This authored object is the deployable item stored in the response bag,
            // not a loose supply that should be visible in the LeakResponse Scene.
            // The LeakResponse Stage bag handler activates it only when the trainee retrieves it.
            sourceObject.SetActive(false);
            XRGrabInteractable sourceGrab = sourceObject.GetComponent<XRGrabInteractable>();
            if (sourceGrab == null)
            {
                Debug.LogError(
                    $"[P102 Flow] Required clean {typeId} source '{exactObjectName}' has no XRGrabInteractable. " +
                    "Installation remains blocked.", sourceObject);
                return null;
            }

            sourceGrab.useDynamicAttach = true;
            sourceGrab.matchAttachPosition = true;
            sourceGrab.matchAttachRotation = false;
            sourceGrab.snapToColliderVolume = true;
            sourceGrab.selectMode = InteractableSelectMode.Single;
            sourceGrab.throwOnDetach = false;
            sourceGrab.movementType = XRGrabInteractable.MovementType.Kinematic;
            sourceGrab.trackPosition = true;
            sourceGrab.trackRotation = true;
            sourceGrab.smoothPosition = false;
            sourceGrab.smoothRotation = false;
            P102RuntimeUtility.ConfigureLocomotionStableGrab(sourceGrab);

            PreplacedGhostInstallSource source =
                P102RuntimeUtility.GetOrAdd<PreplacedGhostInstallSource>(sourceObject);
            source.enabled = true;
            source.Configure(typeId, sourceGrab, true, 0.22f);
            sourceGrab.enabled = false;

            if (group == null)
                Debug.LogError($"[P102 Flow] {typeId} source was found, but its install group is unavailable.", sourceObject);
            return sourceGrab;
        }

        void ConfigureInteractionGates(P102LeakNarrationController narration,
            PreplacedGhostInstallGroup sockGroup, PreplacedGhostInstallGroup padGroup)
        {
            UnsubscribeInteractionGates();
            gatedNarration = narration;
            gatedSockGroup = sockGroup;
            gatedPadGroup = padGroup;

            if (sockSourceGrab != null)
                sockSourceGrab.enabled = false;
            if (padSourceGrab != null)
                padSourceGrab.enabled = false;
            if (gatedNarration != null)
                gatedNarration.CueCompleted += OnCueCompletedForInteractionGate;
            if (gatedSockGroup != null)
                gatedSockGroup.Installed += OnInstallGroupCompletedForInteractionGate;
            if (gatedPadGroup != null)
                gatedPadGroup.Installed += OnInstallGroupCompletedForInteractionGate;
        }

        void OnCueCompletedForInteractionGate(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.AbsorbentSockPlacementActivated)
            {
                Debug.Log("[P102 Flow] AbsorbentSockPlacementActivated received.", this);
                if (sockSourceGrab != null && gatedSockGroup != null && !gatedSockGroup.IsInstalled)
                    sockSourceGrab.enabled = true;
            }
            else if (cue == P102LeakNarrationCue.AbsorbentPadPlacementActivated)
            {
                bool socksComplete = gatedSockGroup != null && gatedSockGroup.IsInstalled;
                if (padSourceGrab != null && gatedPadGroup != null &&
                    !gatedPadGroup.IsInstalled && socksComplete)
                {
                    padSourceGrab.enabled = true;
                }
                else if (!socksComplete)
                {
                    Debug.LogError("[P102 Flow] Pad interaction remained locked because the Sock group is incomplete.", this);
                }
            }
        }

        void OnInstallGroupCompletedForInteractionGate(PreplacedGhostInstallGroup group)
        {
            if (group == gatedSockGroup && sockSourceGrab != null)
                sockSourceGrab.enabled = false;
            if (group == gatedPadGroup && padSourceGrab != null)
                padSourceGrab.enabled = false;
        }

        void UnsubscribeInteractionGates()
        {
            if (gatedNarration != null)
                gatedNarration.CueCompleted -= OnCueCompletedForInteractionGate;
            if (gatedSockGroup != null)
                gatedSockGroup.Installed -= OnInstallGroupCompletedForInteractionGate;
            if (gatedPadGroup != null)
                gatedPadGroup.Installed -= OnInstallGroupCompletedForInteractionGate;
            gatedNarration = null;
            gatedSockGroup = null;
            gatedPadGroup = null;
        }

        static PreplacedGhostInstallItem ConfigureRuntimeGhostItem(GameObject ghost, string typeId,
            Renderer sourceRenderer)
        {
            GhostObjectSnapSlot incompleteSlot = ghost.GetComponent<GhostObjectSnapSlot>();
            if (incompleteSlot != null)
                incompleteSlot.enabled = false;

            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return null;

            Rigidbody body = P102RuntimeUtility.GetOrAdd<Rigidbody>(ghost);
            body.useGravity = false;
            body.isKinematic = true;
            Collider primaryCollider = P102RuntimeUtility.EnsureBoundsCollider(ghost, false);
            Collider[] colliders = ghost.GetComponentsInChildren<Collider>(true);
            XRGrabInteractable grab = P102RuntimeUtility.GetOrAdd<XRGrabInteractable>(ghost);
            grab.colliders.Clear();
            for (int i = 0; i < colliders.Length; ++i)
                grab.colliders.Add(colliders[i]);
            if (colliders.Length == 0 && primaryCollider != null)
                grab.colliders.Add(primaryCollider);

            var rendererBindings = new PreplacedGhostInstallItem.RendererMaterialBinding[renderers.Length];
            Material[] sourceMaterials = sourceRenderer != null ? sourceRenderer.sharedMaterials : Array.Empty<Material>();
            for (int i = 0; i < renderers.Length; ++i)
            {
                Material[] ghostMaterials = renderers[i].sharedMaterials;
                Material[] installedMaterials = MatchMaterialCount(sourceMaterials, ghostMaterials);
                rendererBindings[i] = new PreplacedGhostInstallItem.RendererMaterialBinding(
                    renderers[i], ghostMaterials, installedMaterials);
            }

            var colliderBindings = new PreplacedGhostInstallItem.ColliderBinding[colliders.Length];
            for (int i = 0; i < colliders.Length; ++i)
                colliderBindings[i] = new PreplacedGhostInstallItem.ColliderBinding(colliders[i], true);

            PreplacedGhostInstallItem item = P102RuntimeUtility.GetOrAdd<PreplacedGhostInstallItem>(ghost);
            item.Configure(typeId, rendererBindings, colliderBindings, body, grab, true, false);
            return item.ApplyInitialGhostState() ? item : null;
        }

        static Material[] MatchMaterialCount(Material[] source, Material[] fallback)
        {
            int count = fallback != null ? fallback.Length : 0;
            var result = new Material[count];
            for (int i = 0; i < count; ++i)
            {
                if (source != null && source.Length > 0)
                    result[i] = source[Mathf.Min(i, source.Length - 1)];
                else
                    result[i] = fallback[i];
            }
            return result;
        }

        T ResolveOrCreateHelper<T>() where T : Component
        {
            T existing = P102RuntimeUtility.FindSceneComponent<T>();
            T resolved = existing != null ? existing : gameObject.AddComponent<T>();
            resolved.gameObject.SetActive(true);
            if (resolved is Behaviour behaviour)
                behaviour.enabled = true;
            return resolved;
        }

        void DisableLeakSceneHandVisuals()
        {
            string[] objectNames = { "Hand Visualizer", "VR Hand Pose Tool Tracking" };
            for (int i = 0; i < objectNames.Length; ++i)
            {
                GameObject handVisual = FindSceneObject(gameObject.scene, objectNames[i]);
                if (handVisual == null)
                    continue;

                handVisual.SetActive(false);
                Debug.Log(
                    $"[P102 Flow] LeakResponse hand visual disabled: " +
                    P102RuntimeUtility.GetHierarchyPath(handVisual.transform), handVisual);
            }

            // The standalone Leak Scene owns an embedded XR rig. Keep its Near/Far
            // interactors active for close-range grabbing, but suppress their resting
            // pointer curves so a ray is not drawn from either hand. In the integrated
            // App the whole embedded rig is already disabled by the shared XR profile.
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                XRRayInteractor[] rays = root.GetComponentsInChildren<XRRayInteractor>(true);
                for (int rayIndex = 0; rayIndex < rays.Length; ++rayIndex)
                    DisablePointerVisuals(rays[rayIndex].transform);

                NearFarInteractor[] nearFarInteractors =
                    root.GetComponentsInChildren<NearFarInteractor>(true);
                for (int interactorIndex = 0;
                     interactorIndex < nearFarInteractors.Length;
                     ++interactorIndex)
                {
                    DisablePointerVisuals(nearFarInteractors[interactorIndex].transform);
                }
            }
        }

        static void DisablePointerVisuals(Transform interactorRoot)
        {
            if (interactorRoot == null)
                return;

            CurveVisualController[] curveVisuals =
                interactorRoot.GetComponentsInChildren<CurveVisualController>(true);
            for (int i = 0; i < curveVisuals.Length; ++i)
                curveVisuals[i].enabled = false;

            XRInteractorLineVisual[] rayVisuals =
                interactorRoot.GetComponentsInChildren<XRInteractorLineVisual>(true);
            for (int i = 0; i < rayVisuals.Length; ++i)
                rayVisuals[i].enabled = false;

            LineRenderer[] lineRenderers =
                interactorRoot.GetComponentsInChildren<LineRenderer>(true);
            for (int i = 0; i < lineRenderers.Length; ++i)
                lineRenderers[i].enabled = false;

            Transform[] descendants = interactorRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; ++i)
            {
                Transform descendant = descendants[i];
                if (descendant != interactorRoot &&
                    string.Equals(descendant.name, "LineVisual", StringComparison.Ordinal))
                {
                    descendant.gameObject.SetActive(false);
                }
            }
        }

        static ValveWheelClosureController FindValve(string exactName)
        {
            GameObject valveObject = P102RuntimeUtility.FindSceneObject(exactName);
            if (valveObject == null)
            {
                Debug.LogError($"[P102 Flow] Required valve object '{exactName}' is missing.");
                return null;
            }

            valveObject.SetActive(true);
            ValveWheelClosureController valve = valveObject.GetComponent<ValveWheelClosureController>();
            if (valve == null)
            {
                Debug.LogError($"[P102 Flow] Required valve '{exactName}' has no ValveWheelClosureController.", valveObject);
                return null;
            }

            valve.enabled = true;
            XRGrabInteractable grab = valveObject.GetComponent<XRGrabInteractable>();
            ValveWheelGrabTransformer transformer =
                valveObject.GetComponent<ValveWheelGrabTransformer>();
            if (grab == null || transformer == null)
            {
                Debug.LogError(
                    $"[P102 Flow] Required valve '{exactName}' has incomplete Grab components.",
                    valveObject);
                return valve;
            }

            transformer.enabled = true;
            grab.enabled = true;
            grab.selectMode = InteractableSelectMode.Multiple;
            return valve;
        }

        static PreplacedGhostInstallGroup FindExistingGroup(string typeId)
        {
            PreplacedGhostInstallGroup[] groups = Resources.FindObjectsOfTypeAll<PreplacedGhostInstallGroup>();
            for (int i = 0; i < groups.Length; ++i)
            {
                PreplacedGhostInstallGroup group = groups[i];
                if (group != null && group.gameObject.scene.IsValid() &&
                    PreplacedGhostInstallGroup.ItemTypeIdsMatch(group.AcceptedItemTypeId, typeId))
                    return group;
            }
            return null;
        }

        static PreplacedGhostInstallGroup[] CompactGroups(params PreplacedGhostInstallGroup[] candidates)
        {
            var groups = new List<PreplacedGhostInstallGroup>();
            for (int i = 0; i < candidates.Length; ++i)
            {
                if (candidates[i] != null && !groups.Contains(candidates[i]))
                    groups.Add(candidates[i]);
            }
            return groups.ToArray();
        }

        static Transform[] ResolveGroupItemTransforms(PreplacedGhostInstallGroup group)
        {
            if (group == null || group.Items == null)
                return Array.Empty<Transform>();

            var transforms = new List<Transform>(group.Items.Length);
            for (int i = 0; i < group.Items.Length; ++i)
            {
                if (group.Items[i] != null)
                    transforms.Add(group.Items[i].transform);
            }
            return transforms.ToArray();
        }

        static Transform ResolveMainCamera()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.enabled && mainCamera.gameObject.activeInHierarchy)
                return mainCamera.transform;

            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < cameras.Length; ++i)
            {
                Camera candidate = cameras[i];
                if (candidate != null && candidate.gameObject.scene.IsValid() && candidate.enabled &&
                    candidate.gameObject.activeInHierarchy && candidate.stereoTargetEye != StereoTargetEyeMask.None)
                {
                    return candidate.transform;
                }
            }

            GameObject cameraObject = P102RuntimeUtility.FindSceneObject("Main Camera");
            if (cameraObject != null)
            {
                Camera camera = cameraObject.GetComponent<Camera>();
                if (camera != null && camera.enabled && cameraObject.activeInHierarchy)
                    return cameraObject.transform;
            }
            return null;
        }

        Transform ResolveRequiredFixedAnchor(string exactName, string role)
        {
            GameObject anchorObject = P102RuntimeUtility.FindSceneObject(exactName);
            if (anchorObject != null)
            {
                Debug.Log(
                    $"[P102 UI] {role} anchor={P102RuntimeUtility.GetHierarchyPath(anchorObject.transform)}",
                    anchorObject);
                return anchorObject.transform;
            }

            List<GameObject> candidates = P102RuntimeUtility.FindSceneObjects(
                candidateName => candidateName.IndexOf("Valve", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 candidateName.IndexOf("Flange", StringComparison.OrdinalIgnoreCase) >= 0);
            var candidatePaths = new List<string>(candidates.Count);
            for (int i = 0; i < candidates.Count; ++i)
                candidatePaths.Add(P102RuntimeUtility.GetHierarchyPath(candidates[i].transform));
            Debug.LogError(
                $"[P102 UI] Required {role} '{exactName}' was not found. Searched candidates: " +
                string.Join(", ", candidatePaths), this);
            return null;
        }

    }

    /// <summary>
    /// LeakResponse-owned activation contract. Loading the Scene can provide its
    /// environment without starting narration, task progression, or bag input.
    /// </summary>
    public static class P102LeakStageActivation
    {
        static readonly Dictionary<string, bool> sceneActivation =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetActivationState()
        {
            sceneActivation.Clear();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.IsValid() &&
                sceneActivation.TryGetValue(Normalize(scene.path), out var activate) &&
                !activate)
            {
                // sceneLoaded runs after Awake/OnEnable but before Start. Freeze the
                // leak simulation here so an additive environment never advances one
                // frame before SceneFlow can apply the dormant contract.
                Apply(scene, false);
            }
        }

        public static void PrepareSceneLoad(string scenePath, bool activate)
        {
            if (!string.IsNullOrWhiteSpace(scenePath))
                sceneActivation[Normalize(scenePath)] = activate;
        }

        public static bool ShouldActivate(Scene scene)
        {
            return !scene.IsValid() ||
                   !sceneActivation.TryGetValue(Normalize(scene.path), out var activate) ||
                   activate;
        }

        public static void Apply(Scene scene, bool activate)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            sceneActivation[Normalize(scene.path)] = activate;
            if (!activate)
            {
                SetProgressionBehaviours(scene, false);
                HideAuthoredAbsorbentSupplies(scene);
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                var bootstraps = root.GetComponentsInChildren<P102LeakRuntimeBootstrap>(true);
                for (var i = 0; i < bootstraps.Length; i++)
                    bootstraps[i].SetStageActivated(activate);

                var narrations = root.GetComponentsInChildren<P102LeakNarrationController>(true);
                for (var i = 0; i < narrations.Length; i++)
                    narrations[i].SetStageActivated(activate);

                var bagHandlers = root.GetComponentsInChildren<P102SharedBagItemHandler>(true);
                for (var i = 0; i < bagHandlers.Length; i++)
                    bagHandlers[i].enabled = activate;
            }

            if (activate)
                SetProgressionBehaviours(scene, true);
        }

        static void SetProgressionBehaviours(Scene scene, bool active)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var absorptions = root.GetComponentsInChildren<HClAbsorptionVisualController>(true);
                for (var i = 0; i < absorptions.Length; i++)
                    absorptions[i].enabled = active;

                var diagonalMists =
                    root.GetComponentsInChildren<LeakResponseDiagonalMistRuntime>(true);
                for (var i = 0; i < diagonalMists.Length; i++)
                    diagonalMists[i].enabled = active;

                var valves = root.GetComponentsInChildren<ValveWheelClosureController>(true);
                for (var i = 0; i < valves.Length; i++)
                {
                    valves[i].enabled = active;
                    XRGrabInteractable grab =
                        valves[i].GetComponent<XRGrabInteractable>();
                    ValveWheelGrabTransformer transformer =
                        valves[i].GetComponent<ValveWheelGrabTransformer>();
                    if (grab != null)
                        grab.enabled = active;
                    if (transformer != null)
                        transformer.enabled = active;
                }

                var splashBursts = root.GetComponentsInChildren<WaterSplashBurst>(true);
                for (var i = 0; i < splashBursts.Length; i++)
                {
                    if (!active)
                        splashBursts[i].StopLeak();
                    splashBursts[i].enabled = active;
                }
            }
        }

        static void HideAuthoredAbsorbentSupplies(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < transforms.Length; i++)
                {
                    var candidate = transforms[i];
                    if (candidate == null)
                        continue;

                    if (string.Equals(candidate.name, "INT_AbsorbentSock_Straight_01", StringComparison.Ordinal) ||
                        string.Equals(candidate.name, "INT_AbsorbentPad_01", StringComparison.Ordinal))
                    {
                        candidate.gameObject.SetActive(false);
                    }
                }
            }
        }

        static string Normalize(string path) =>
            string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').Trim();
    }
}
