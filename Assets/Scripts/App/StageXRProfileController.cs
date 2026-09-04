using System;
using System.Collections.Generic;
using System.Reflection;
using HClTraining.SharedXR;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace HClTraining.App
{
    [DisallowMultipleComponent]
    public sealed class StageXRProfileController : MonoBehaviour
    {
        const string HandVisualizerPath = "Camera Offset/Hand Visualizer";
        const string HandTrackingPath = "Camera Offset/VR Hand Pose Tool Tracking";
        const string LeftDefaultHandName = "PPE_DefaultHand_Left";
        const string RightDefaultHandName = "PPE_DefaultHand_Right";
        const string LeftWearableHandName = "PPE_VrHand_Left_Wearable";
        const string RightWearableHandName = "PPE_VrHand_Right_Wearable";
        [SerializeField] GameObject commonXrOrigin;
        [SerializeField] StageXRProfile[] profiles;

        StageXRRequirements[] requirementAssets;

        readonly Dictionary<Behaviour, bool> startBehaviourStates = new Dictionary<Behaviour, bool>();
        readonly Dictionary<Renderer, bool> startRendererStates = new Dictionary<Renderer, bool>();
        readonly Dictionary<GameObject, bool> startObjectStates = new Dictionary<GameObject, bool>();
        readonly Dictionary<XRRayInteractor, XRInputButtonReader> startUiPressStates =
            new Dictionary<XRRayInteractor, XRInputButtonReader>();
        readonly Dictionary<XRRayInteractor, bool> startUiInteractionStates =
            new Dictionary<XRRayInteractor, bool>();
        bool startProfileActive;
        TrainingStage activeStage;
        float nextRuntimeInteractorAuditAt;

        public GameObject CommonXrOrigin => commonXrOrigin;
        public int LastReboundReferenceCount { get; private set; }
        public string LastAppliedProfile { get; private set; }

        public void Initialize(GameObject xrOrigin)
        {
            commonXrOrigin = xrOrigin;
            if (profiles == null || profiles.Length == 0)
                profiles = Resources.LoadAll<StageXRProfile>("App/StageXRProfiles");
            requirementAssets = Resources.LoadAll<StageXRRequirements>("SharedXR");
        }

        void Update()
        {
            var transitionLock = GetComponent<TransitionInputLock>();
            if (activeStage == TrainingStage.None || activeStage == TrainingStage.Complete ||
                commonXrOrigin == null || (transitionLock != null && transitionLock.IsLocked) ||
                Time.unscaledTime < nextRuntimeInteractorAuditAt)
            {
                return;
            }

            nextRuntimeInteractorAuditAt = Time.unscaledTime + 0.5f;
            EnableCommonInput(commonXrOrigin.transform, false);
            DisableUnexpectedRuntimeInteractors();
        }

        public bool ApplyStartProfile(out string error)
        {
            error = null;
            if (commonXrOrigin == null)
            {
                error = "Common XR Origin is missing.";
                return false;
            }

            RestoreStartProfile();
            startProfileActive = true;
            activeStage = TrainingStage.None;

            var behaviours = commonXrOrigin.GetComponentsInChildren<Behaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour != null && IsSharedLocomotionBehaviour(behaviour.GetType()))
                    SetStartBehaviour(behaviour, false);
            }

            SetStartObject("Locomotion/Grab Move", false);
            SetStartObject("Locomotion/Teleportation", false);
            SetStartObject("Locomotion/Climb", false);
            SetStartObject("Camera Offset/Hand Visualizer", false);
            SetStartObject("Camera Offset/VR Hand Pose Tool Tracking", false);
            SetStartObject("Camera Offset/Main Camera/TunnelingVignette", false);
            SetStartObject("ChemicalGloves_WearableHost", false);
            SetStartObject("Camera Offset/Left Controller/Left_NearFarInteractor", false);
            SetStartObject("Camera Offset/Right Controller/Right_NearFarInteractor", false);
            ConfigureStartController("Left", "XR Controller Left");
            ConfigureStartController("Right", "XR Controller Right");

            var managers = commonXrOrigin.GetComponentsInChildren<InputActionManager>(true);
            for (var i = 0; i < managers.Length; i++)
                SetStartBehaviour(managers[i], true);

            if (!ConfigureStartRay("Camera Offset/Left Controller/Ray Interactor", "XRI Left Interaction", out error) ||
                !ConfigureStartRay("Camera Offset/Right Controller/Ray Interactor", "XRI Right Interaction", out error))
            {
                RestoreStartProfile();
                return false;
            }

            ConfigureAimPoseDriver(commonXrOrigin.transform, "Left", "Ray Interactor");
            ConfigureAimPoseDriver(commonXrOrigin.transform, "Right", "Ray Interactor");

            LastAppliedProfile = "StartMenu";
            Debug.Log("[Stage XR Profile] Applied StartMenu; locomotion disabled, controller visuals and UI rays enabled.", this);
            return true;
        }

        public bool ApplyCompleteProfile(out string error)
        {
            if (!ApplyStartProfile(out error))
                return false;

            // Completion is App-owned UI, so retain the standard UI rays configured
            // by ApplyStartProfile. Near/Far Stage pointers remain disabled there.
            EnableCommonInput(commonXrOrigin.transform, true);
            activeStage = TrainingStage.Complete;
            LastAppliedProfile = "Complete";
            Debug.Log("[Stage XR Profile] Applied Complete; locomotion disabled and standard UI rays enabled.", this);
            return true;
        }

        public bool ApplyStageProfile(Scene scene, TrainingStage stage, out string error)
        {
            error = null;
            RestoreStartProfile();
            if (commonXrOrigin == null)
            {
                error = "Common XR Origin is missing.";
                Debug.LogError("[Stage XR Profile] " + error, this);
                return false;
            }

            var sourceOrigin = FindSceneOrigin(scene);
            var profile = FindProfile(stage);
            var requirements = FindRequirements(scene.path);
            if (requirements == null)
            {
                error = $"Stage XR 요구사항 자산이 없습니다: {scene.path}";
                Debug.LogError("[Stage XR Profile] " + error, this);
                return false;
            }
            commonXrOrigin.SetActive(true);
            commonXrOrigin.transform.SetPositionAndRotation(
                sourceOrigin != null ? sourceOrigin.transform.position : requirements.SpawnPosition,
                sourceOrigin != null ? sourceOrigin.transform.rotation : requirements.SpawnRotation);

            if (!ApplySharedLocomotionPolicy(commonXrOrigin.transform, requirements, out var locomotionError))
            {
                error = locomotionError;
                Debug.LogError("[Stage XR Profile] " + error, this);
                return false;
            }
            ConfigureComfortVignette(commonXrOrigin.transform, requirements.ComfortVignette);
            LastReboundReferenceCount = 0;
            if (sourceOrigin != null)
            {
                LastReboundReferenceCount = RebindSceneReferences(
                    scene, sourceOrigin.transform, commonXrOrigin.transform);
                LastReboundReferenceCount += RebindDetachedControllers(
                    scene, sourceOrigin.transform, commonXrOrigin.transform);
                DisableLegacyXrObjects(scene, sourceOrigin.gameObject);
            }
            var disabledStageInteractors = DisableStageOwnedInteractors(scene);
            ApplyCommonControllerPolicy(commonXrOrigin.transform, requirements);
            ConfigureCommonInteractionInputs(commonXrOrigin.transform, requirements);
            ConfigureStageUiPress(commonXrOrigin.transform, requirements.UIInteraction);
            EnableCommonInput(commonXrOrigin.transform, true);
            activeStage = stage;
            nextRuntimeInteractorAuditAt = 0f;

            LastAppliedProfile = profile != null ? profile.name : stage.ToString();
            Debug.Log(
                $"[Stage XR Profile] Applied {LastAppliedProfile}; spawn={commonXrOrigin.transform.position}; " +
                $"reboundReferences={LastReboundReferenceCount}; " +
                $"disabledStageInteractors={disabledStageInteractors}; " +
                $"activeCommonRays={CountActiveCommonRays(commonXrOrigin.transform)}.", this);
            return true;
        }

        /// <summary>
        /// Removes player-system ownership from an additively loaded environment Scene
        /// without changing the active Stage profile or moving the shared XR Origin.
        /// </summary>
        public bool PrepareDependencyScene(Scene scene, out string error)
        {
            error = null;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                error = "Additive dependency Scene is not loaded.";
                return false;
            }

            if (commonXrOrigin == null)
            {
                error = "Common XR Origin is missing.";
                return false;
            }

            var sourceOrigin = FindSceneOrigin(scene);
            var rebound = 0;
            if (sourceOrigin != null)
            {
                rebound += RebindSceneReferences(scene, sourceOrigin.transform, commonXrOrigin.transform);
                rebound += RebindDetachedControllers(scene, sourceOrigin.transform, commonXrOrigin.transform);
                DisableLegacyXrObjects(scene, sourceOrigin.gameObject);
            }

            var disabledInteractors = DisableStageOwnedInteractors(scene);
            Debug.Log(
                $"[Stage XR Profile] Prepared additive dependency '{scene.path}'; " +
                $"reboundReferences={rebound}; disabledStageInteractors={disabledInteractors}.",
                this);
            return true;
        }

        void ConfigureStartController(string side, string modelName)
        {
            var controllerPath = $"Camera Offset/{side} Controller";
            var defaultHandName = side == "Left" ? LeftDefaultHandName : RightDefaultHandName;
            var wearableHandName = side == "Left" ? LeftWearableHandName : RightWearableHandName;
            SetStartObject(controllerPath, true);
            SetStartObject($"{controllerPath}/Visuals", true);
            SetStartObject($"{controllerPath}/Visuals/{modelName}", true);
            SetStartObject($"{controllerPath}/Visuals/{defaultHandName}", false);
            SetStartObject($"{controllerPath}/Visuals/{wearableHandName}", false);
            SetStartObject($"{controllerPath}/Visual", false);
        }

        static void ApplyCommonControllerPolicy(Transform xrRoot, StageXRRequirements requirements)
        {
            SetObjectActive(xrRoot, HandVisualizerPath, false);
            SetObjectActive(xrRoot, HandTrackingPath, false);
            var useStandardRay = requirements.UseStandardRayPointer;
            var useNearFar = requirements.UseNearFarPointer;
            var visualMode = requirements.HandVisualMode;
            var showControllers = visualMode == StageXRHandVisualMode.Controllers;
            var showLeftHand = visualMode == StageXRHandVisualMode.LeftHand ||
                               visualMode == StageXRHandVisualMode.BothHands;
            var showRightHand = visualMode == StageXRHandVisualMode.RightHand ||
                                visualMode == StageXRHandVisualMode.BothHands;
            var allowLeftInteraction = visualMode != StageXRHandVisualMode.RightHand;
            var allowRightInteraction = visualMode != StageXRHandVisualMode.LeftHand;
            ConfigureController(
                xrRoot, "Left", "XR Controller Left", LeftDefaultHandName, LeftWearableHandName,
                showControllers, showLeftHand, useStandardRay && allowLeftInteraction,
                requirements.UIInteraction, requirements.Haptics);
            ConfigureController(
                xrRoot, "Right", "XR Controller Right", RightDefaultHandName, RightWearableHandName,
                showControllers, showRightHand, useStandardRay && allowRightInteraction,
                requirements.UIInteraction, requirements.Haptics);
            ConfigureNearFarInteractor(
                xrRoot,
                "Camera Offset/Left Controller/Left_NearFarInteractor",
                useNearFar && allowLeftInteraction,
                requirements.NearInteraction,
                requirements.FarInteraction,
                requirements.Haptics);
            ConfigureNearFarInteractor(
                xrRoot,
                "Camera Offset/Right Controller/Right_NearFarInteractor",
                useNearFar && allowRightInteraction,
                requirements.NearInteraction,
                requirements.FarInteraction,
                requirements.Haptics);
        }

        static void ConfigureController(
            Transform xrRoot,
            string side,
            string modelName,
            string defaultHandName,
            string wearableHandName,
            bool controllerModelActive,
            bool defaultHandActive,
            bool standardRayActive,
            bool uiInteraction,
            bool haptics)
        {
            var controllerPath = $"Camera Offset/{side} Controller";
            SetObjectActive(xrRoot, controllerPath, true);
            SetObjectActive(xrRoot, $"{controllerPath}/Visuals", true);
            SetObjectActive(xrRoot, $"{controllerPath}/Visuals/{modelName}", controllerModelActive);
            SetObjectActive(xrRoot, $"{controllerPath}/Visuals/{defaultHandName}", defaultHandActive);
            SetObjectActive(xrRoot, $"{controllerPath}/Visuals/{wearableHandName}", false);
            SetObjectActive(xrRoot, $"{controllerPath}/Visual", false);
            SetInteractorActive(xrRoot, $"{controllerPath}/Ray Interactor", standardRayActive);
            var ray = xrRoot.Find($"{controllerPath}/Ray Interactor")?.GetComponent<XRRayInteractor>();
            if (ray != null)
            {
                ray.hitClosestOnly = true;
                ray.blendVisualLinePoints = false;
                ray.enableUIInteraction = uiInteraction;
                var lineVisual = ray.GetComponent<XRInteractorLineVisual>();
                if (lineVisual != null)
                {
                    lineVisual.lineBendRatio = 1f;
                    lineVisual.overrideInteractorLineOrigin = false;
                }
                SetHapticCapability(ray.gameObject, haptics);
            }

            ConfigureAimPoseDriver(xrRoot, side, "Ray Interactor");
        }

        static void ConfigureAimPoseDriver(Transform xrRoot, string side, string interactorName)
        {
            if (xrRoot == null)
                return;

            var controller = xrRoot.Find($"Camera Offset/{side} Controller");
            var interactor = controller != null ? controller.Find(interactorName) : null;
            if (controller == null || interactor == null || controller.parent == null)
                return;

            var driver = interactor.GetComponent<SharedXRAimPoseDriver>();
            if (driver == null)
                driver = interactor.gameObject.AddComponent<SharedXRAimPoseDriver>();
            driver.Initialize(
                side == "Left" ? SharedXRHand.Left : SharedXRHand.Right,
                controller.parent);
        }

        static void ConfigureNearFarInteractor(
            Transform xrRoot,
            string relativePath,
            bool active,
            bool nearCasting,
            bool farCasting,
            bool haptics)
        {
            var interactor = xrRoot != null ? xrRoot.Find(relativePath) : null;
            if (interactor == null)
                return;

            var lineVisual = interactor.Find("LineVisual");
            var showFarVisual = active && farCasting;
            var curveVisualReady = !showFarVisual ||
                                   PrepareNearFarCurveVisual(xrRoot, interactor, lineVisual);
            interactor.gameObject.SetActive(active);
            if (!active)
                return;

            var nearFar = interactor.GetComponent<NearFarInteractor>();
            if (nearFar != null)
            {
                nearFar.enableNearCasting = nearCasting;
                nearFar.enableFarCasting = farCasting;
            }

            ConfigureAimPoseDriver(
                xrRoot,
                relativePath.IndexOf("Left Controller", StringComparison.Ordinal) >= 0 ? "Left" : "Right",
                interactor.name);

            if (lineVisual != null)
                lineVisual.gameObject.SetActive(showFarVisual);

            var behaviours = interactor.GetComponentsInChildren<Behaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;
                behaviour.enabled = !(behaviour is CurveVisualController) ||
                                    (showFarVisual && curveVisualReady);
            }
            var renderers = interactor.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = showFarVisual;
            SetHapticCapability(interactor.gameObject, haptics);
        }

        static bool PrepareNearFarCurveVisual(
            Transform xrRoot,
            Transform interactor,
            Transform lineVisual)
        {
            if (lineVisual == null)
                return true;

            var curveVisual = lineVisual.GetComponent<CurveVisualController>();
            if (curveVisual == null)
                return true;

            var lineRenderer = curveVisual.lineRenderer ?? lineVisual.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                // The App rig was authored without a LineRenderer on the Near/Far
                // LineVisual. Add it while the visual is still inactive so the XRI
                // controller's Awake method can initialize all of its native state.
                lineRenderer = lineVisual.gameObject.AddComponent<LineRenderer>();
                var standardRayRenderer = interactor.parent
                    ?.Find("Ray Interactor")
                    ?.GetComponent<LineRenderer>();
                if (standardRayRenderer != null)
                {
                    lineRenderer.sharedMaterials = standardRayRenderer.sharedMaterials;
                    lineRenderer.widthMultiplier = standardRayRenderer.widthMultiplier;
                    lineRenderer.widthCurve = standardRayRenderer.widthCurve;
                    lineRenderer.colorGradient = standardRayRenderer.colorGradient;
                    lineRenderer.numCornerVertices = standardRayRenderer.numCornerVertices;
                    lineRenderer.numCapVertices = standardRayRenderer.numCapVertices;
                    lineRenderer.alignment = standardRayRenderer.alignment;
                    lineRenderer.textureMode = standardRayRenderer.textureMode;
                    lineRenderer.useWorldSpace = standardRayRenderer.useWorldSpace;
                }
                else
                {
                    lineRenderer.widthMultiplier = 0.003f;
                    lineRenderer.useWorldSpace = true;
                }

                curveVisual.lineRenderer = lineRenderer;
            }

            var nearFar = interactor.GetComponent<NearFarInteractor>();
            if (curveVisual.curveInteractionDataProvider == null && nearFar != null)
                curveVisual.curveInteractionDataProvider = nearFar;

            var ready = curveVisual.lineRenderer != null &&
                        curveVisual.curveInteractionDataProvider != null;
            if (!ready)
            {
                curveVisual.enabled = false;
                Debug.LogError(
                    $"[Stage XR Profile] Near/Far line visual references are incomplete: {interactor.name}.",
                    curveVisual);
            }

            return ready;
        }

        static void SetHapticCapability(GameObject owner, bool enabled)
        {
            if (owner == null)
                return;
            var behaviours = owner.GetComponents<Behaviour>();
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().Name == "SimpleHapticFeedback")
                    behaviour.enabled = enabled;
            }
        }

        static void SetInteractorActive(Transform xrRoot, string relativePath, bool active)
        {
            var interactor = xrRoot != null ? xrRoot.Find(relativePath) : null;
            if (interactor == null)
                return;

            interactor.gameObject.SetActive(active);
            var behaviours = interactor.GetComponents<Behaviour>();
            for (var i = 0; i < behaviours.Length; i++)
                if (behaviours[i] != null) behaviours[i].enabled = active;
            var lineRenderer = interactor.GetComponent<LineRenderer>();
            if (lineRenderer != null)
                lineRenderer.enabled = active;
        }

        static void EnableCommonInput(Transform xrRoot, bool restoreManager)
        {
            var managers = xrRoot.GetComponentsInChildren<InputActionManager>(true);
            for (var i = 0; i < managers.Length; i++)
            {
                var manager = managers[i];
                if (manager == null || !manager.gameObject.activeInHierarchy)
                    continue;

                // TransitionInputLock deliberately leaves input disabled for Stage profile
                // application to restore. Periodic audits must not defeat that lock while
                // an additive Scene transition is still in progress.
                if (restoreManager)
                    manager.enabled = true;
                if (manager.enabled)
                    manager.EnableInput();
            }

            // Stage-owned InputActionManagers can disable the shared XRI asset while their
            // legacy rig is being shut down. Re-enable common-rig move references explicitly.
            var moveProviders = xrRoot.GetComponentsInChildren<ContinuousMoveProvider>(true);
            for (var i = 0; i < moveProviders.Length; i++)
            {
                var provider = moveProviders[i];
                if (provider == null || !provider.enabled || !provider.gameObject.activeInHierarchy)
                    continue;

                EnableActionReference(provider.leftHandMoveInput);
                EnableActionReference(provider.rightHandMoveInput);
            }

            var turnProviders = xrRoot.GetComponentsInChildren<SnapTurnProvider>(true);
            for (var i = 0; i < turnProviders.Length; i++)
            {
                var provider = turnProviders[i];
                if (provider == null || !provider.enabled || !provider.gameObject.activeInHierarchy)
                    continue;

                EnableActionReference(provider.leftHandTurnInput);
                EnableActionReference(provider.rightHandTurnInput);
            }

            SharedXRInputContract.Instance?.EnsureActionsEnabled();
        }

        static void EnableActionReference(XRInputValueReader<Vector2> reader)
        {
            if (reader == null ||
                reader.inputSourceMode != XRInputValueReader.InputSourceMode.InputActionReference)
            {
                return;
            }

            reader.inputActionReference?.action?.Enable();
        }

        void ConfigureCommonInteractionInputs(Transform xrRoot, StageXRRequirements requirements)
        {
            if (xrRoot == null || requirements == null)
                return;

            ConfigureInteractionInput(
                xrRoot.Find("Camera Offset/Left Controller/Ray Interactor")
                    ?.GetComponent<XRBaseInputInteractor>(),
                "XRI Left Interaction");
            ConfigureInteractionInput(
                xrRoot.Find("Camera Offset/Right Controller/Ray Interactor")
                    ?.GetComponent<XRBaseInputInteractor>(),
                "XRI Right Interaction");
            ConfigureInteractionInput(
                xrRoot.Find("Camera Offset/Left Controller/Left_NearFarInteractor")
                    ?.GetComponent<XRBaseInputInteractor>(),
                "XRI Left Interaction");
            ConfigureInteractionInput(
                xrRoot.Find("Camera Offset/Right Controller/Right_NearFarInteractor")
                    ?.GetComponent<XRBaseInputInteractor>(),
                "XRI Right Interaction");
        }

        void ConfigureInteractionInput(XRBaseInputInteractor interactor, string actionMapName)
        {
            if (interactor == null)
                return;

            var select = FindInputAction(actionMapName, "Select");
            var selectValue = FindInputAction(actionMapName, "Select Value");
            var activate = FindInputAction(actionMapName, "Activate");
            var activateValue = FindInputAction(actionMapName, "Activate Value");
            if (select == null || selectValue == null || activate == null || activateValue == null)
            {
                Debug.LogError(
                    $"[Stage XR Profile] Select/Activate actions are incomplete: {actionMapName}.",
                    this);
                return;
            }

            if (!ReaderMatches(interactor.selectInput, select, selectValue))
                interactor.selectInput = CreateButtonReader("Select", "Select Value", select, selectValue);
            if (!ReaderMatches(interactor.activateInput, activate, activateValue))
                interactor.activateInput = CreateButtonReader("Activate", "Activate Value", activate, activateValue);

            select.Enable();
            selectValue.Enable();
            activate.Enable();
            activateValue.Enable();
        }

        static bool ReaderMatches(XRInputButtonReader reader, InputAction performed, InputAction value)
        {
            return reader != null &&
                   reader.inputSourceMode == XRInputButtonReader.InputSourceMode.InputActionReference &&
                   reader.inputActionReferencePerformed != null &&
                   reader.inputActionReferenceValue != null &&
                   reader.inputActionReferencePerformed.action == performed &&
                   reader.inputActionReferenceValue.action == value;
        }

        static XRInputButtonReader CreateButtonReader(
            string performedName,
            string valueName,
            InputAction performed,
            InputAction value)
        {
            return new XRInputButtonReader(performedName, valueName)
            {
                inputSourceMode = XRInputButtonReader.InputSourceMode.InputActionReference,
                inputActionReferencePerformed = InputActionReference.Create(performed),
                inputActionReferenceValue = InputActionReference.Create(value),
            };
        }

        void ConfigureStageUiPress(Transform xrRoot, bool enabled)
        {
            ConfigureUiPress(
                xrRoot.Find("Camera Offset/Left Controller/Ray Interactor")?.GetComponent<XRRayInteractor>(),
                "XRI Left Interaction",
                enabled);
            ConfigureUiPress(
                xrRoot.Find("Camera Offset/Right Controller/Ray Interactor")?.GetComponent<XRRayInteractor>(),
                "XRI Right Interaction",
                enabled);
            ConfigureUiPress(
                xrRoot.Find("Camera Offset/Left Controller/Left_NearFarInteractor")?.GetComponent<NearFarInteractor>(),
                "XRI Left Interaction",
                enabled);
            ConfigureUiPress(
                xrRoot.Find("Camera Offset/Right Controller/Right_NearFarInteractor")?.GetComponent<NearFarInteractor>(),
                "XRI Right Interaction",
                enabled);
        }

        void ConfigureUiPress(XRRayInteractor interactor, string actionMapName, bool enabled)
        {
            if (interactor == null)
                return;

            interactor.enableUIInteraction = enabled;
            if (!enabled)
                return;

            var press = FindInputAction(actionMapName, "UI Press");
            var value = FindInputAction(actionMapName, "UI Press Value");
            if (press == null || value == null)
            {
                Debug.LogWarning($"[Stage XR Profile] UI Press actions are missing: {actionMapName}.", this);
                return;
            }

            interactor.uiPressInput = CreateUiPressReader(press, value);
        }

        void ConfigureUiPress(NearFarInteractor interactor, string actionMapName, bool enabled)
        {
            if (interactor == null)
                return;

            interactor.enableUIInteraction = enabled;
            if (!enabled)
                return;

            var press = FindInputAction(actionMapName, "UI Press");
            var value = FindInputAction(actionMapName, "UI Press Value");
            if (press == null || value == null)
            {
                Debug.LogWarning($"[Stage XR Profile] UI Press actions are missing: {actionMapName}.", this);
                return;
            }

            interactor.uiPressInput = CreateUiPressReader(press, value);
        }

        static XRInputButtonReader CreateUiPressReader(InputAction press, InputAction value)
        {
            return CreateButtonReader("UI Press", "UI Press Value", press, value);
        }

        static void SetObjectActive(Transform root, string relativePath, bool active)
        {
            var target = root != null ? root.Find(relativePath) : null;
            if (target != null)
                target.gameObject.SetActive(active);
        }

        static void ConfigureComfortVignette(Transform xrRoot, bool active)
        {
            var vignette = xrRoot != null
                ? xrRoot.GetComponentInChildren<TunnelingVignetteController>(true)
                : null;
            if (vignette == null)
                return;

            vignette.gameObject.SetActive(active);
            vignette.enabled = active;
        }

        bool ConfigureStartRay(string relativePath, string actionMapName, out string error)
        {
            error = null;
            var rayTransform = commonXrOrigin.transform.Find(relativePath);
            if (rayTransform == null)
            {
                error = $"Start UI Ray was not found: {relativePath}";
                return false;
            }

            SetStartObject(relativePath, true);

            var ray = rayTransform.GetComponent<XRRayInteractor>();
            var lineVisual = rayTransform.GetComponent<XRInteractorLineVisual>();
            var lineRenderer = rayTransform.GetComponent<LineRenderer>();
            if (ray == null || lineVisual == null || lineRenderer == null)
            {
                error = $"Start UI Ray components are incomplete: {relativePath}";
                return false;
            }

            SetStartBehaviour(ray, true);
            ray.hitClosestOnly = true;
            ray.blendVisualLinePoints = false;
            SetStartBehaviour(lineVisual, true);
            lineVisual.lineBendRatio = 1f;
            lineVisual.overrideInteractorLineOrigin = false;
            if (!startRendererStates.ContainsKey(lineRenderer))
                startRendererStates.Add(lineRenderer, lineRenderer.enabled);
            lineRenderer.enabled = true;

            if (!startUiInteractionStates.ContainsKey(ray))
                startUiInteractionStates.Add(ray, ray.enableUIInteraction);
            ray.enableUIInteraction = true;

            if (!startUiPressStates.ContainsKey(ray))
                startUiPressStates.Add(ray, ray.uiPressInput);
            var uiPress = FindInputAction(actionMapName, "UI Press");
            var uiPressValue = FindInputAction(actionMapName, "UI Press Value");
            if (uiPress == null || uiPressValue == null)
            {
                error = $"Start UI actions are missing: {actionMapName}/UI Press.";
                return false;
            }

            ray.uiPressInput = new XRInputButtonReader("UI Press", "UI Press Value")
            {
                inputSourceMode = XRInputButtonReader.InputSourceMode.InputActionReference,
                inputActionReferencePerformed = InputActionReference.Create(uiPress),
                inputActionReferenceValue = InputActionReference.Create(uiPressValue),
            };
            return true;
        }

        InputAction FindInputAction(string mapName, string actionName)
        {
            var managers = commonXrOrigin.GetComponentsInChildren<InputActionManager>(true);
            for (var managerIndex = 0; managerIndex < managers.Length; managerIndex++)
            {
                var assets = managers[managerIndex].actionAssets;
                if (assets == null)
                    continue;
                for (var assetIndex = 0; assetIndex < assets.Count; assetIndex++)
                {
                    var action = assets[assetIndex]?.FindAction($"{mapName}/{actionName}", false);
                    if (action != null)
                        return action;
                }
            }
            return null;
        }

        void SetStartObject(string relativePath, bool active)
        {
            var target = commonXrOrigin.transform.Find(relativePath);
            if (target == null)
                return;
            if (!startObjectStates.ContainsKey(target.gameObject))
                startObjectStates.Add(target.gameObject, target.gameObject.activeSelf);
            target.gameObject.SetActive(active);
        }

        void SetStartBehaviour(Behaviour behaviour, bool enabled)
        {
            if (behaviour == null)
                return;
            if (!startBehaviourStates.ContainsKey(behaviour))
                startBehaviourStates.Add(behaviour, behaviour.enabled);
            behaviour.enabled = enabled;
        }

        void RestoreStartProfile()
        {
            if (!startProfileActive)
                return;

            foreach (var pair in startUiPressStates)
                if (pair.Key != null) pair.Key.uiPressInput = pair.Value;
            foreach (var pair in startUiInteractionStates)
                if (pair.Key != null) pair.Key.enableUIInteraction = pair.Value;
            foreach (var pair in startBehaviourStates)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            foreach (var pair in startRendererStates)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            foreach (var pair in startObjectStates)
                if (pair.Key != null) pair.Key.SetActive(pair.Value);

            startUiPressStates.Clear();
            startUiInteractionStates.Clear();
            startBehaviourStates.Clear();
            startRendererStates.Clear();
            startObjectStates.Clear();
            startProfileActive = false;
        }

        static bool IsStartLocomotionBehaviour(Type type)
        {
            var ns = type.Namespace ?? string.Empty;
            var name = type.Name;
            return ns.IndexOf(".Locomotion", StringComparison.Ordinal) >= 0 ||
                   name.IndexOf("MoveProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("TurnProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("TeleportationProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("ClimbProvider", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool ApplySharedLocomotionPolicy(
            Transform xrRoot,
            StageXRRequirements requirements,
            out string error)
        {
            error = null;
            if (xrRoot == null)
            {
                error = "Common XR root is missing while applying shared locomotion.";
                return false;
            }
            if (requirements == null)
            {
                error = "Stage XR requirements are missing while applying shared locomotion.";
                return false;
            }

            var bodyTransformers = xrRoot.GetComponents<XRBodyTransformer>();
            var mediators = xrRoot.GetComponents<LocomotionMediator>();
            var moveProviders = xrRoot.GetComponents<ContinuousMoveProvider>();
            var controllerDrivers = GetBehavioursNamed(xrRoot, "CharacterControllerDriver", false);
            var snapTurnProviders = xrRoot.GetComponents<SnapTurnProvider>();
            if (bodyTransformers.Length != 1 || mediators.Length != 1 || moveProviders.Length != 1 ||
                controllerDrivers.Length != 1 || snapTurnProviders.Length != 1)
            {
                error =
                    "Shared XR locomotion topology is invalid. Expected exactly one canonical " +
                    $"root BodyTransformer/Mediator/Move/CharacterDriver/SnapTurn, found " +
                    $"{bodyTransformers.Length}/{mediators.Length}/{moveProviders.Length}/" +
                    $"{controllerDrivers.Length}/{snapTurnProviders.Length}.";
                return false;
            }

            var behaviours = xrRoot.GetComponentsInChildren<Behaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour != null && IsSharedLocomotionBehaviour(behaviour.GetType()))
                    behaviour.enabled = false;
            }

            // LeakResponse's verified locomotion is the shared App baseline. Reuse the
            // already-authored components and settings for every Stage; never import a
            // Stage rig's component paths or create Stage-specific providers here.
            bodyTransformers[0].enabled = true;
            mediators[0].enabled = true;
            moveProviders[0].enabled = requirements.Movement;
            controllerDrivers[0].enabled = true;
            snapTurnProviders[0].enabled = requirements.Turning;
            return true;
        }

        static Behaviour[] GetBehavioursNamed(Transform root, string typeName, bool includeChildren)
        {
            var source = includeChildren
                ? root.GetComponentsInChildren<Behaviour>(true)
                : root.GetComponents<Behaviour>();
            var matches = new List<Behaviour>();
            for (var i = 0; i < source.Length; i++)
            {
                var behaviour = source[i];
                if (behaviour != null && string.Equals(behaviour.GetType().Name, typeName, StringComparison.Ordinal))
                    matches.Add(behaviour);
            }

            return matches.ToArray();
        }

        static bool IsSharedLocomotionBehaviour(Type type)
        {
            var name = type.Name;
            return IsStartLocomotionBehaviour(type) ||
                   name.IndexOf("LocomotionMediator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("XRBodyTransformer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("CharacterControllerDriver", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("GravityProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("JumpProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("ClimbTeleport", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        StageXRProfile FindProfile(TrainingStage stage)
        {
            if (profiles == null)
                return null;
            for (var i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] != null && profiles[i].Stage == stage)
                    return profiles[i];
            }
            return null;
        }

        StageXRRequirements FindRequirements(string scenePath)
        {
            if (requirementAssets == null || requirementAssets.Length == 0)
                requirementAssets = Resources.LoadAll<StageXRRequirements>("SharedXR");

            var normalizedScenePath = NormalizeAssetPath(scenePath);
            for (var i = 0; i < requirementAssets.Length; i++)
            {
                var requirements = requirementAssets[i];
                if (requirements != null && string.Equals(
                        NormalizeAssetPath(requirements.ScenePath),
                        normalizedScenePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return requirements;
                }
            }
            return null;
        }

        static string NormalizeAssetPath(string path) =>
            string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').Trim();

        static XROrigin FindSceneOrigin(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var origins = root.GetComponentsInChildren<XROrigin>(true);
                if (origins.Length > 0)
                    return origins[0];
            }
            return null;
        }

        static UnityEngine.Object RemapObject(UnityEngine.Object sourceObject, Transform sourceRoot, Transform targetRoot)
        {
            if (sourceObject == null)
                return null;

            Transform sourceTransform = null;
            if (sourceObject is GameObject gameObject)
                sourceTransform = gameObject.transform;
            else if (sourceObject is Component component)
                sourceTransform = component.transform;

            if (sourceTransform == null || (sourceTransform != sourceRoot && !sourceTransform.IsChildOf(sourceRoot)))
                return null;

            var relativePath = GetRelativePath(sourceRoot, sourceTransform);
            var targetTransform = string.IsNullOrEmpty(relativePath) ? targetRoot : targetRoot.Find(relativePath);
            if (targetTransform == null)
                return null;
            if (sourceObject is GameObject)
                return targetTransform.gameObject;
            if (sourceObject is Transform)
                return targetTransform;

            var sourceComponent = (Component)sourceObject;
            var sourceSiblings = sourceTransform.GetComponents(sourceComponent.GetType());
            var componentIndex = Array.IndexOf(sourceSiblings, sourceComponent);
            var targetSiblings = targetTransform.GetComponents(sourceComponent.GetType());
            return componentIndex >= 0 && componentIndex < targetSiblings.Length
                ? targetSiblings[componentIndex]
                : null;
        }

        static int RebindSceneReferences(Scene scene, Transform sourceRoot, Transform targetRoot)
        {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                var components = root.GetComponentsInChildren<MonoBehaviour>(true);
                for (var i = 0; i < components.Length; i++)
                {
                    var component = components[i];
                    if (component == null || component.transform == sourceRoot || component.transform.IsChildOf(sourceRoot))
                        continue;
                    count += RebindObjectFields(component, sourceRoot, targetRoot);
                }
            }
            return count;
        }

        static int RebindDetachedControllers(Scene scene, Transform sourceOrigin, Transform commonOrigin)
        {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < transforms.Length; i++)
                {
                    var candidate = transforms[i];
                    if (candidate == null || candidate == sourceOrigin || candidate.IsChildOf(sourceOrigin) ||
                        (candidate.name != "Left Controller" && candidate.name != "Right Controller") ||
                        !HasTrackedPoseDriver(candidate))
                    {
                        continue;
                    }

                    var target = commonOrigin.Find($"Camera Offset/{candidate.name}");
                    if (target != null)
                        count += RebindSceneReferences(scene, candidate, target);
                }
            }
            return count;
        }

        static bool HasTrackedPoseDriver(Transform candidate)
        {
            var components = candidate.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] != null && components[i].GetType().Name == "TrackedPoseDriver")
                    return true;
            }
            return false;
        }

        static int RebindObjectFields(MonoBehaviour component, Transform sourceRoot, Transform targetRoot)
        {
            var count = 0;
            for (var type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (var i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    if (field.IsStatic || field.IsInitOnly ||
                        (field.IsPrivate && field.GetCustomAttribute<SerializeField>() == null))
                        continue;
                    try
                    {
                        var value = field.GetValue(component);
                        if (value is UnityEngine.Object unityObject)
                        {
                            var replacement = RemapObject(unityObject, sourceRoot, targetRoot);
                            if (replacement != null && replacement != unityObject)
                            {
                                field.SetValue(component, replacement);
                                count++;
                            }
                        }
                        else if (value is Array array)
                        {
                            var changed = false;
                            for (var index = 0; index < array.Length; index++)
                            {
                                if (!(array.GetValue(index) is UnityEngine.Object item))
                                    continue;
                                var replacement = RemapObject(item, sourceRoot, targetRoot);
                                if (replacement == null || replacement == item)
                                    continue;
                                array.SetValue(replacement, index);
                                count++;
                                changed = true;
                            }
                            if (changed)
                                field.SetValue(component, array);
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[Stage XR Profile] Reference rebind skipped for {component.GetType().Name}.{field.Name}: {exception.Message}");
                    }
                }
            }
            return count;
        }

        static void DisableLegacyXrObjects(Scene scene, GameObject legacyOrigin)
        {
            legacyOrigin.SetActive(false);
            foreach (var root in scene.GetRootGameObjects())
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < transforms.Length; i++)
                {
                    var candidate = transforms[i];
                    if (candidate == null || candidate == legacyOrigin.transform ||
                        candidate.IsChildOf(legacyOrigin.transform) ||
                        (candidate.name != "Left Controller" && candidate.name != "Right Controller") ||
                        !HasTrackedPoseDriver(candidate))
                    {
                        continue;
                    }
                    candidate.gameObject.SetActive(false);
                }

                var eventSystems = root.GetComponentsInChildren<EventSystem>(true);
                for (var i = 0; i < eventSystems.Length; i++)
                    eventSystems[i].gameObject.SetActive(false);

                var components = root.GetComponentsInChildren<Behaviour>(true);
                for (var i = 0; i < components.Length; i++)
                {
                    var behaviour = components[i];
                    if (behaviour == null)
                        continue;
                    var name = behaviour.GetType().Name;
                    if (name == "XRInteractionManager" || name == "InputActionManager")
                        behaviour.gameObject.SetActive(false);
                }

                var cameras = root.GetComponentsInChildren<Camera>(true);
                for (var i = 0; i < cameras.Length; i++)
                {
                    if (cameras[i].CompareTag("MainCamera") || cameras[i].name == "Main Camera")
                        cameras[i].enabled = false;
                }
                var listeners = root.GetComponentsInChildren<AudioListener>(true);
                for (var i = 0; i < listeners.Length; i++)
                    listeners[i].enabled = false;
            }
        }

        // Integrated App play must never use an interactor owned by an additive Stage.
        // This also catches detached or unusually named Stage rays that are not children
        // of the authored XR Origin. It deliberately does not touch the App common rig.
        static int DisableStageOwnedInteractors(Scene scene)
        {
            var disabledObjects = new HashSet<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                var rays = root.GetComponentsInChildren<XRRayInteractor>(true);
                for (var i = 0; i < rays.Length; i++)
                    DisableStageInteractor(rays[i], disabledObjects);

                var nearFarInteractors = root.GetComponentsInChildren<NearFarInteractor>(true);
                for (var i = 0; i < nearFarInteractors.Length; i++)
                    DisableStageInteractor(nearFarInteractors[i], disabledObjects);
            }
            return disabledObjects.Count;
        }

        static void DisableStageInteractor(Behaviour interactor, HashSet<GameObject> disabledObjects)
        {
            if (interactor == null)
                return;

            interactor.enabled = false;
            var owner = interactor.gameObject;
            if (owner != null)
            {
                owner.SetActive(false);
                disabledObjects.Add(owner);
            }
        }

        static int CountActiveCommonRays(Transform commonOrigin)
        {
            if (commonOrigin == null)
                return 0;

            var count = 0;
            var rays = commonOrigin.GetComponentsInChildren<XRRayInteractor>(true);
            for (var i = 0; i < rays.Length; i++)
                if (rays[i] != null && rays[i].isActiveAndEnabled) count++;

            var nearFarInteractors = commonOrigin.GetComponentsInChildren<NearFarInteractor>(true);
            for (var i = 0; i < nearFarInteractors.Length; i++)
                if (nearFarInteractors[i] != null && nearFarInteractors[i].isActiveAndEnabled) count++;

            return count;
        }

        void DisableUnexpectedRuntimeInteractors()
        {
            var commonRoot = commonXrOrigin.transform;
            var rays = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Exclude);
            for (var i = 0; i < rays.Length; i++)
                DisableIfOutsideCommonRig(rays[i], commonRoot);

            var nearFarInteractors = FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Exclude);
            for (var i = 0; i < nearFarInteractors.Length; i++)
                DisableIfOutsideCommonRig(nearFarInteractors[i], commonRoot);
        }

        static void DisableIfOutsideCommonRig(Behaviour interactor, Transform commonRoot)
        {
            if (interactor == null || commonRoot == null || interactor.transform.IsChildOf(commonRoot))
                return;

            var owner = interactor.gameObject;
            Debug.LogWarning(
                $"[Stage XR Profile] Disabled runtime Stage interactor outside the common rig: " +
                $"scene='{owner.scene.name}', path='{GetHierarchyPath(owner.transform)}', " +
                $"type='{interactor.GetType().Name}'.",
                owner);
            interactor.enabled = false;
            owner.SetActive(false);
        }

        static string GetHierarchyPath(Transform target)
        {
            if (target == null)
                return string.Empty;

            var parts = new Stack<string>();
            for (var current = target; current != null; current = current.parent)
                parts.Push(current.name);
            return string.Join("/", parts);
        }

        static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
                return string.Empty;
            var parts = new Stack<string>();
            for (var current = target; current != null && current != root; current = current.parent)
                parts.Push(current.name);
            return string.Join("/", parts);
        }
    }
}
