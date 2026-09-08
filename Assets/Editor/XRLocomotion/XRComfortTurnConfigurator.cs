using System;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace VRProject.XRLocomotion.Editor
{
    /// <summary>
    /// Configures comfort turning only when the user explicitly invokes the menu command.
    /// This class never opens, saves, or reloads a Scene and never edits an Input Action Asset.
    /// </summary>
    internal static class XRComfortTurnConfigurator
    {
        const string UndoName = "Configure XR Origin Comfort Turn";
        const string PreferredVignettePrefabPath =
            "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/TunnelingVignette/TunnelingVignette.prefab";
        const string PreferredRightLocomotionMapName = "XRI Right Locomotion";
        const string PreferredSnapTurnActionName = "Snap Turn";
        const string RightPrimaryAxisBindingPath = "<XRController>{RightHand}/primary2DAxis";

        const float TurnAmount = 30f;
        const float DebounceTime = 0.25f;
        const float ApertureSize = 0.82f;
        const float FeatheringEffect = 0.25f;
        const float EaseInTime = 0.04f;
        const float EaseOutTime = 0.10f;
        const float EaseOutDelayTime = 0.02f;

        sealed class ConfigurationPlan
        {
            public XROrigin xrOrigin;
            public Camera mainCamera;
            public GameObject providerHost;
            public InputActionReference rightSnapTurnReference;
            public InputAction rightSnapTurnEmbeddedAction;
            public XRInputValueReader.InputSourceMode rightSnapTurnInputSourceMode;
            public string rightSnapTurnInputSourceDescription;
            public string rightSnapTurnPath;
            public string rightSnapTurnAssetPath;
            public TunnelingVignetteController existingVignette;
            public GameObject vignettePrefab;
            public string vignettePrefabPath;
            public string vignetteMaterialPath;
            public ContinuousTurnProvider[] continuousTurnProviders;
            public ContinuousMoveProvider[] continuousMoveProviders;
        }

        sealed class ActionCandidate
        {
            public InputAction action;
            public InputActionReference reference;
            public InputAction embeddedAction;
            public XRInputValueReader.InputSourceMode inputSourceMode;
            public string inputSourceDescription;
            public string assetPath;
            public string actionPath;
        }

        static void ConfigureSelectedXROrigin()
        {
            if (!TryBuildPlanFromSelection(out var plan, out var error))
            {
                ShowError(error);
                return;
            }

            if (!TryApply(plan, out var changed, out error))
            {
                ShowError(error);
                return;
            }

            var state = changed
                ? "Configuration applied. The Scene is dirty but was not saved."
                : "The selected XR Origin was already fully configured; no Scene values changed.";

            var message =
                $"{state}\n\n" +
                $"XR Origin: {GetHierarchyPath(plan.xrOrigin.transform)}\n" +
                $"Right Turn Action: {plan.rightSnapTurnPath}\n" +
                $"Input Action Asset: {plan.rightSnapTurnAssetPath}\n" +
                $"Right Turn Input Source: {plan.rightSnapTurnInputSourceDescription}\n" +
                $"Vignette Prefab: {plan.vignettePrefabPath}\n" +
                $"Vignette Material: {plan.vignetteMaterialPath}\n\n" +
                "Final Input Layout:\n" +
                "- Left Thumb Stick: Move\n" +
                "- Right Thumb Stick Left/Right: 30° Snap Turn\n" +
                "- Right Thumb Stick Up/Down: Unused\n" +
                "- Continuous Turn: Disabled\n" +
                "- Comfort Vignette: Enabled\n\n" +
                "Review the Inspector, then save the Scene manually if the result is correct. " +
                "One Ctrl+Z reverts this configurator run.";

            Debug.Log($"[XR Comfort Turn] {message}", plan.xrOrigin);
            EditorUtility.DisplayDialog("XR Comfort Turn", message, "OK");
        }

        static bool ValidateConfigureSelectedXROrigin()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        static bool TryBuildPlanFromSelection(out ConfigurationPlan plan, out string error)
        {
            plan = null;
            error = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Fail("Exit Play Mode before running the configurator.", out error);

            if (Selection.objects == null || Selection.objects.Length != 1 || Selection.activeGameObject == null)
                return Fail("Select exactly one XR Origin Scene GameObject in the Hierarchy.", out error);

            var selected = Selection.activeGameObject;
            if (EditorUtility.IsPersistent(selected) || PrefabUtility.IsPartOfPrefabAsset(selected))
                return Fail("The selection is a Prefab Asset. Select an XR Origin Scene instance in the Hierarchy.", out error);

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.IsPartOfPrefabContents(selected))
                return Fail("Prefab Mode content is not supported. Select an instance in a normal loaded Scene.", out error);

            if (!selected.scene.IsValid() || !selected.scene.isLoaded)
                return Fail("The selected object is not an instance in a loaded Scene.", out error);

            if (!TryResolveXROrigin(selected, out var xrOrigin, out error))
                return false;

            var camera = xrOrigin.Camera;
            if (camera == null)
                return Fail("The selected XROrigin does not have its Camera reference assigned.", out error);

            if (camera.gameObject.scene != xrOrigin.gameObject.scene ||
                (camera.transform != xrOrigin.transform && !camera.transform.IsChildOf(xrOrigin.transform)))
            {
                return Fail("The XROrigin Camera must be part of the selected XR Origin hierarchy.", out error);
            }

            if (!TryResolveProviderHost(xrOrigin, out var providerHost, out error))
                return false;

            if (!TryFindRightSnapTurnInput(xrOrigin, out var actionCandidate, out error))
                return false;

            var allVignettes = xrOrigin.GetComponentsInChildren<TunnelingVignetteController>(true);
            if (allVignettes.Length > 1)
                return Fail("More than one TunnelingVignetteController exists under the selected XR Origin. Resolve the ambiguity manually first.", out error);

            TunnelingVignetteController existingVignette = null;
            GameObject vignettePrefab = null;
            string vignettePrefabPath;
            string vignetteMaterialPath;

            if (allVignettes.Length == 1)
            {
                existingVignette = allVignettes[0];
                if (existingVignette.transform != camera.transform && !existingVignette.transform.IsChildOf(camera.transform))
                {
                    return Fail("An existing TunnelingVignetteController was found outside the XROrigin Main Camera hierarchy. " +
                        "No duplicate was created.", out error);
                }

                vignettePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(existingVignette.gameObject);
                if (string.IsNullOrEmpty(vignettePrefabPath))
                    vignettePrefabPath = "Existing Scene TunnelingVignetteController";

                vignetteMaterialPath = GetVignetteMaterialPath(existingVignette.gameObject);
            }
            else
            {
                if (!TryFindOfficialVignettePrefab(out vignettePrefab, out vignettePrefabPath, out vignetteMaterialPath))
                {
                    return Fail("The official XRI Starter Assets TunnelingVignette Prefab was not found. " +
                        "Import the XR Interaction Toolkit Starter Assets sample, then run this menu again.", out error);
                }
            }

            var continuousMoveProviders = xrOrigin.GetComponentsInChildren<ContinuousMoveProvider>(true);
            if (continuousMoveProviders.Length > 1)
            {
                return Fail("More than one ContinuousMoveProvider exists under the selected XR Origin. " +
                    "Resolve the ambiguity manually before running the configurator.", out error);
            }

            plan = new ConfigurationPlan
            {
                xrOrigin = xrOrigin,
                mainCamera = camera,
                providerHost = providerHost,
                rightSnapTurnReference = actionCandidate.reference,
                rightSnapTurnEmbeddedAction = actionCandidate.embeddedAction,
                rightSnapTurnInputSourceMode = actionCandidate.inputSourceMode,
                rightSnapTurnInputSourceDescription = actionCandidate.inputSourceDescription,
                rightSnapTurnPath = actionCandidate.actionPath,
                rightSnapTurnAssetPath = actionCandidate.assetPath,
                existingVignette = existingVignette,
                vignettePrefab = vignettePrefab,
                vignettePrefabPath = vignettePrefabPath,
                vignetteMaterialPath = vignetteMaterialPath,
                continuousTurnProviders = xrOrigin.GetComponentsInChildren<ContinuousTurnProvider>(true),
                continuousMoveProviders = continuousMoveProviders,
            };
            return true;
        }

        static bool TryResolveXROrigin(GameObject selected, out XROrigin xrOrigin, out string error)
        {
            var matches = new HashSet<XROrigin>();
            var onSelection = selected.GetComponent<XROrigin>();
            if (onSelection != null)
                matches.Add(onSelection);

            var inParent = selected.GetComponentInParent<XROrigin>(true);
            if (inParent != null)
                matches.Add(inParent);

            foreach (var inChild in selected.GetComponentsInChildren<XROrigin>(true))
                matches.Add(inChild);

            if (matches.Count != 1)
            {
                xrOrigin = null;
                var reason = matches.Count == 0
                    ? "No XROrigin exists on the selected object or its parent/child hierarchy."
                    : "The selection resolves to multiple XROrigin components. Select one exact XR Origin root.";
                return Fail(reason, out error);
            }

            xrOrigin = matches.First();
            error = null;
            return true;
        }

        static bool TryResolveProviderHost(XROrigin xrOrigin, out GameObject providerHost, out string error)
        {
            var snapProviders = xrOrigin.GetComponentsInChildren<SnapTurnProvider>(true);
            if (snapProviders.Length > 1)
            {
                providerHost = null;
                return Fail("Multiple SnapTurnProvider components already exist under the selected XR Origin. " +
                    "No additional provider was created.", out error);
            }

            if (snapProviders.Length == 1)
            {
                providerHost = snapProviders[0].gameObject;
                error = null;
                return true;
            }

            var providerHosts = xrOrigin.GetComponentsInChildren<LocomotionProvider>(true)
                .Select(component => component.gameObject)
                .Distinct()
                .ToArray();

            if (providerHosts.Length == 1)
            {
                providerHost = providerHosts[0];
                error = null;
                return true;
            }

            if (providerHosts.Length > 1)
            {
                providerHost = null;
                return Fail("Existing Locomotion Providers are spread across multiple GameObjects. " +
                    "Select a single-provider-host layout before configuring comfort turn.", out error);
            }

            var mediators = xrOrigin.GetComponentsInChildren<LocomotionMediator>(true);
            if (mediators.Length > 1)
            {
                providerHost = null;
                return Fail("Multiple LocomotionMediator components exist under the selected XR Origin.", out error);
            }

            if (mediators.Length == 1)
            {
                providerHost = mediators[0].gameObject;
                error = null;
                return true;
            }

            var bodyTransformers = xrOrigin.GetComponentsInChildren<XRBodyTransformer>(true);
            if (bodyTransformers.Length > 1)
            {
                providerHost = null;
                return Fail("Multiple XRBodyTransformer components exist under the selected XR Origin.", out error);
            }

            providerHost = bodyTransformers.Length == 1 ? bodyTransformers[0].gameObject : xrOrigin.gameObject;
            error = null;
            return true;
        }

        static bool TryFindRightSnapTurnInput(XROrigin xrOrigin, out ActionCandidate selectedCandidate, out string error)
        {
            selectedCandidate = null;
            error = null;

            var managers = xrOrigin.GetComponentsInChildren<InputActionManager>(true);
            var actionAssets = managers
                .Where(manager => manager.actionAssets != null)
                .SelectMany(manager => manager.actionAssets)
                .Where(asset => asset != null)
                .Distinct()
                .ToArray();

            if (actionAssets.Length == 0)
                return Fail("No Input Action Asset is assigned to an InputActionManager under the selected XR Origin.", out error);

            var candidates = new List<ActionCandidate>();
            foreach (var asset in actionAssets)
            {
                var assetPath = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                foreach (var map in asset.actionMaps)
                {
                    foreach (var action in map.actions)
                    {
                        if (!IsValidRightPrimaryAxisAction(action))
                            continue;

                        candidates.Add(new ActionCandidate
                        {
                            action = action,
                            assetPath = assetPath,
                            actionPath = $"{map.name}/{action.name}",
                        });
                    }
                }
            }

            var preferredPath = $"{PreferredRightLocomotionMapName}/{PreferredSnapTurnActionName}";
            var preferredCandidates = candidates
                .Where(candidate => string.Equals(candidate.actionPath, preferredPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (preferredCandidates.Length == 0)
            {
                return Fail($"The required {preferredPath} Value/Vector2 action with an active RightHand Primary 2D Axis binding " +
                    "was not found in the selected XR Origin's InputActionManager assets.", out error);
            }

            if (preferredCandidates.Length != 1)
            {
                var assets = string.Join("\n", preferredCandidates.Select(candidate => $"- {candidate.assetPath}"));
                return Fail($"Multiple valid {preferredPath} actions were found. No Scene changes were made.\n" + assets, out error);
            }

            selectedCandidate = preferredCandidates[0];
            selectedCandidate.reference = FindExistingActionReference(
                selectedCandidate.action,
                selectedCandidate.assetPath,
                out var referenceAssetPath);

            if (selectedCandidate.reference != null)
            {
                selectedCandidate.inputSourceMode = XRInputValueReader.InputSourceMode.InputActionReference;
                selectedCandidate.inputSourceDescription = $"Existing InputActionReference ({referenceAssetPath})";
                return true;
            }

            selectedCandidate.embeddedAction = CreateEmbeddedRightSnapTurnAction();
            if (!IsValidEmbeddedRightSnapTurnAction(selectedCandidate.embeddedAction))
                return Fail("XRI 3.4.1 could not prepare a serializable embedded Value/Vector2 right-hand Snap Turn action.", out error);

            selectedCandidate.inputSourceMode = XRInputValueReader.InputSourceMode.InputAction;
            selectedCandidate.inputSourceDescription = "Embedded InputAction (no Input Action Asset changes)";
            return true;
        }

        static InputActionReference FindExistingActionReference(
            InputAction targetAction,
            string targetAssetPath,
            out string referenceAssetPath)
        {
            var reference = FindActionReferenceAtPath(targetAction.id, targetAssetPath);
            if (reference != null)
            {
                referenceAssetPath = targetAssetPath;
                return reference;
            }

            var projectPaths = AssetDatabase.FindAssets("t:InputActionReference")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path) &&
                    !string.Equals(path, targetAssetPath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var path in projectPaths)
            {
                reference = FindActionReferenceAtPath(targetAction.id, path);
                if (reference == null)
                    continue;

                referenceAssetPath = path;
                return reference;
            }

            referenceAssetPath = null;
            return null;
        }

        static InputActionReference FindActionReferenceAtPath(Guid targetActionId, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<InputActionReference>()
                .Where(candidate => candidate != null && candidate.action != null && candidate.action.id == targetActionId)
                .OrderBy(candidate => candidate.name, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        static InputAction CreateEmbeddedRightSnapTurnAction()
        {
            var action = new InputAction(
                "Right Hand Snap Turn",
                InputActionType.Value,
                expectedControlType: "Vector2");
            action.AddBinding(RightPrimaryAxisBindingPath);
            return action;
        }

        static bool IsValidRightPrimaryAxisAction(InputAction action)
        {
            if (action == null || action.type != InputActionType.Value ||
                !string.Equals(action.expectedControlType, "Vector2", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return action.bindings.Any(binding => IsRightPrimaryAxisPath(binding.effectivePath));
        }

        static bool IsValidEmbeddedRightSnapTurnAction(InputAction action)
        {
            return action != null &&
                action.type == InputActionType.Value &&
                string.Equals(action.expectedControlType, "Vector2", StringComparison.OrdinalIgnoreCase) &&
                action.bindings.Count == 1 &&
                string.Equals(action.bindings[0].path, RightPrimaryAxisBindingPath, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsRightPrimaryAxisPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return path.IndexOf("<XRController>{RightHand}", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (path.IndexOf("primary2DAxis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 path.IndexOf("{Primary2DAxis}", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static bool TryFindOfficialVignettePrefab(
            out GameObject prefab,
            out string prefabPath,
            out string materialPath)
        {
            prefab = null;
            prefabPath = null;
            materialPath = null;

            var candidatePaths = new List<string> { PreferredVignettePrefabPath };
            candidatePaths.AddRange(AssetDatabase.FindAssets("TunnelingVignette t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path) && !candidatePaths.Contains(path)));

            foreach (var path in candidatePaths)
            {
                if (path.IndexOf("/Samples/XR Interaction Toolkit/", StringComparison.OrdinalIgnoreCase) < 0 ||
                    path.IndexOf("/Starter Assets/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (candidate == null || candidate.GetComponent<TunnelingVignetteController>() == null)
                    continue;

                var renderer = candidate.GetComponent<MeshRenderer>();
                var officialMaterial = renderer == null
                    ? null
                    : renderer.sharedMaterials.FirstOrDefault(sharedMaterial =>
                        sharedMaterial != null && sharedMaterial.shader != null &&
                        string.Equals(sharedMaterial.shader.name, "VR/TunnelingVignette", StringComparison.Ordinal));

                if (officialMaterial == null)
                    continue;

                var foundMaterialPath = AssetDatabase.GetAssetPath(officialMaterial);
                if (string.IsNullOrEmpty(foundMaterialPath) ||
                    foundMaterialPath.IndexOf("/Starter Assets/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                prefab = candidate;
                prefabPath = path;
                materialPath = foundMaterialPath;
                return true;
            }

            return false;
        }

        static bool TryApply(ConfigurationPlan plan, out bool changed, out string error)
        {
            changed = false;
            error = null;

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);

            try
            {
                if (plan.continuousMoveProviders.Length == 1)
                {
                    var continuousMove = plan.continuousMoveProviders[0];
                    if (NeedsRightHandMoveInputDisabled(continuousMove))
                    {
                        Undo.RecordObject(continuousMove, UndoName);
                        DisableRightHandMoveInput(continuousMove);
                        changed = true;
                    }
                }

                var bodyTransformer = plan.providerHost.GetComponent<XRBodyTransformer>();
                if (bodyTransformer == null)
                {
                    bodyTransformer = Undo.AddComponent<XRBodyTransformer>(plan.providerHost);
                    changed = true;
                }

                if (bodyTransformer.xrOrigin != plan.xrOrigin)
                {
                    Undo.RecordObject(bodyTransformer, UndoName);
                    bodyTransformer.xrOrigin = plan.xrOrigin;
                    changed = true;
                }

                var mediator = plan.providerHost.GetComponent<LocomotionMediator>();
                if (mediator == null)
                {
                    mediator = Undo.AddComponent<LocomotionMediator>(plan.providerHost);
                    changed = true;
                }

                var snapTurn = plan.providerHost.GetComponent<SnapTurnProvider>();
                if (snapTurn == null)
                {
                    snapTurn = Undo.AddComponent<SnapTurnProvider>(plan.providerHost);
                    changed = true;
                }

                if (NeedsSnapTurnChanges(
                    snapTurn,
                    mediator,
                    plan.rightSnapTurnInputSourceMode,
                    plan.rightSnapTurnReference,
                    plan.rightSnapTurnEmbeddedAction))
                {
                    Undo.RecordObject(snapTurn, UndoName);
                    ConfigureSnapTurn(
                        snapTurn,
                        mediator,
                        plan.rightSnapTurnInputSourceMode,
                        plan.rightSnapTurnReference,
                        plan.rightSnapTurnEmbeddedAction);
                    changed = true;
                }

                foreach (var continuousTurn in plan.continuousTurnProviders)
                {
                    if (continuousTurn == null || !continuousTurn.enabled)
                        continue;

                    Undo.RecordObject(continuousTurn, UndoName);
                    continuousTurn.enabled = false;
                    changed = true;
                }

                var vignette = plan.existingVignette;
                if (vignette == null)
                {
                    var instance = PrefabUtility.InstantiatePrefab(plan.vignettePrefab, plan.mainCamera.gameObject.scene) as GameObject;
                    if (instance == null)
                        throw new InvalidOperationException("Unity could not instantiate the official TunnelingVignette Prefab.");

                    Undo.RegisterCreatedObjectUndo(instance, UndoName);
                    var prefabLocalPosition = plan.vignettePrefab.transform.localPosition;
                    var prefabLocalRotation = plan.vignettePrefab.transform.localRotation;
                    var prefabLocalScale = plan.vignettePrefab.transform.localScale;

                    Undo.SetTransformParent(instance.transform, plan.mainCamera.transform, UndoName);
                    Undo.RecordObject(instance.transform, UndoName);
                    instance.transform.localPosition = prefabLocalPosition;
                    instance.transform.localRotation = prefabLocalRotation;
                    instance.transform.localScale = prefabLocalScale;

                    vignette = instance.GetComponent<TunnelingVignetteController>();
                    if (vignette == null)
                        throw new InvalidOperationException("The instantiated Prefab does not contain TunnelingVignetteController.");

                    changed = true;
                }

                if (!vignette.gameObject.activeSelf)
                {
                    Undo.RecordObject(vignette.gameObject, UndoName);
                    vignette.gameObject.SetActive(true);
                    changed = true;
                }

                if (NeedsVignetteChanges(vignette, snapTurn, plan.continuousMoveProviders))
                {
                    Undo.RecordObject(vignette, UndoName);
                    ConfigureVignette(vignette, snapTurn, plan.continuousMoveProviders);
                    changed = true;
                }

                if (changed)
                    EditorSceneManager.MarkSceneDirty(plan.xrOrigin.gameObject.scene);

                Undo.CollapseUndoOperations(undoGroup);
                return true;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                error = "Configuration failed and its Undo group was reverted.\n" + exception.Message;
                Debug.LogException(exception);
                return false;
            }
        }

        static bool NeedsRightHandMoveInputDisabled(ContinuousMoveProvider continuousMove)
        {
            return continuousMove.rightHandMoveInput == null ||
                continuousMove.rightHandMoveInput.inputSourceMode != XRInputValueReader.InputSourceMode.Unused;
        }

        static void DisableRightHandMoveInput(ContinuousMoveProvider continuousMove)
        {
            var rightInput = continuousMove.rightHandMoveInput ??
                new XRInputValueReader<Vector2>("Right Hand Move", XRInputValueReader.InputSourceMode.Unused);
            rightInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            continuousMove.rightHandMoveInput = rightInput;
        }

        static bool NeedsSnapTurnChanges(
            SnapTurnProvider snapTurn,
            LocomotionMediator mediator,
            XRInputValueReader.InputSourceMode rightInputSourceMode,
            InputActionReference rightReference,
            InputAction rightEmbeddedAction)
        {
            var leftInput = snapTurn.leftHandTurnInput;
            var rightInput = snapTurn.rightHandTurnInput;
            return !snapTurn.enabled ||
                snapTurn.mediator != mediator ||
                !Mathf.Approximately(snapTurn.turnAmount, TurnAmount) ||
                !Mathf.Approximately(snapTurn.debounceTime, DebounceTime) ||
                !snapTurn.enableTurnLeftRight ||
                snapTurn.enableTurnAround ||
                leftInput == null ||
                leftInput.inputSourceMode != XRInputValueReader.InputSourceMode.Unused ||
                leftInput.inputActionReference != null ||
                !RightTurnInputMatches(rightInput, rightInputSourceMode, rightReference, rightEmbeddedAction);
        }

        static bool RightTurnInputMatches(
            XRInputValueReader<Vector2> rightInput,
            XRInputValueReader.InputSourceMode inputSourceMode,
            InputActionReference rightReference,
            InputAction rightEmbeddedAction)
        {
            if (rightInput == null || rightInput.inputSourceMode != inputSourceMode)
                return false;

            if (inputSourceMode == XRInputValueReader.InputSourceMode.InputActionReference)
                return rightReference != null && rightInput.inputActionReference == rightReference;

            return inputSourceMode == XRInputValueReader.InputSourceMode.InputAction &&
                rightEmbeddedAction != null &&
                IsValidEmbeddedRightSnapTurnAction(rightInput.inputAction);
        }

        static void ConfigureSnapTurn(
            SnapTurnProvider snapTurn,
            LocomotionMediator mediator,
            XRInputValueReader.InputSourceMode rightInputSourceMode,
            InputActionReference rightReference,
            InputAction rightEmbeddedAction)
        {
            snapTurn.enabled = true;
            snapTurn.mediator = mediator;
            snapTurn.turnAmount = TurnAmount;
            snapTurn.debounceTime = DebounceTime;
            snapTurn.enableTurnLeftRight = true;
            snapTurn.enableTurnAround = false;

            var leftInput = snapTurn.leftHandTurnInput ??
                new XRInputValueReader<Vector2>("Left Hand Snap Turn", XRInputValueReader.InputSourceMode.Unused);
            leftInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            leftInput.inputActionReference = null;
            snapTurn.leftHandTurnInput = leftInput;

            var rightInput = snapTurn.rightHandTurnInput ??
                new XRInputValueReader<Vector2>("Right Hand Snap Turn", rightInputSourceMode);
            rightInput.inputSourceMode = rightInputSourceMode;
            if (rightInputSourceMode == XRInputValueReader.InputSourceMode.InputActionReference)
            {
                rightInput.inputActionReference = rightReference;
            }
            else
            {
                rightInput.inputAction = rightEmbeddedAction;
                rightInput.inputActionReference = null;
            }
            snapTurn.rightHandTurnInput = rightInput;
        }

        static bool NeedsVignetteChanges(
            TunnelingVignetteController vignette,
            SnapTurnProvider snapTurn,
            IReadOnlyList<ContinuousMoveProvider> moveProviders)
        {
            if (!vignette.enabled || !HasDesiredParameters(vignette.defaultParameters))
                return true;

            var providers = vignette.locomotionVignetteProviders;
            if (providers == null || !HasExactlyOneEnabledDefaultProvider(providers, snapTurn))
                return true;

            return moveProviders.Any(moveProvider =>
                moveProvider != null && !HasExactlyOneEnabledDefaultProvider(providers, moveProvider));
        }

        static bool HasDesiredParameters(VignetteParameters parameters)
        {
            return parameters != null &&
                Mathf.Approximately(parameters.apertureSize, ApertureSize) &&
                Mathf.Approximately(parameters.featheringEffect, FeatheringEffect) &&
                Mathf.Approximately(parameters.easeInTime, EaseInTime) &&
                Mathf.Approximately(parameters.easeOutTime, EaseOutTime) &&
                parameters.easeInTimeLock &&
                Mathf.Approximately(parameters.easeOutDelayTime, EaseOutDelayTime) &&
                parameters.vignetteColor == Color.black &&
                parameters.vignetteColorBlend == Color.black;
        }

        static bool HasExactlyOneEnabledDefaultProvider(
            IEnumerable<LocomotionVignetteProvider> providers,
            LocomotionProvider target)
        {
            var matches = providers
                .Where(provider => provider != null && provider.locomotionProvider == target)
                .ToArray();
            return matches.Length == 1 && matches[0].enabled && !matches[0].overrideDefaultParameters;
        }

        static void ConfigureVignette(
            TunnelingVignetteController vignette,
            SnapTurnProvider snapTurn,
            IReadOnlyList<ContinuousMoveProvider> moveProviders)
        {
            vignette.enabled = true;
            var parameters = vignette.defaultParameters ?? new VignetteParameters();
            parameters.apertureSize = ApertureSize;
            parameters.featheringEffect = FeatheringEffect;
            parameters.easeInTime = EaseInTime;
            parameters.easeOutTime = EaseOutTime;
            parameters.easeInTimeLock = true;
            parameters.easeOutDelayTime = EaseOutDelayTime;
            parameters.vignetteColor = Color.black;
            parameters.vignetteColorBlend = Color.black;
            vignette.defaultParameters = parameters;

            var providers = vignette.locomotionVignetteProviders ?? new List<LocomotionVignetteProvider>();
            EnsureSingleProvider(providers, snapTurn);
            foreach (var moveProvider in moveProviders)
            {
                if (moveProvider != null)
                    EnsureSingleProvider(providers, moveProvider);
            }

            vignette.locomotionVignetteProviders = providers;
        }

        static void EnsureSingleProvider(List<LocomotionVignetteProvider> providers, LocomotionProvider target)
        {
            LocomotionVignetteProvider first = null;
            for (var index = providers.Count - 1; index >= 0; --index)
            {
                var provider = providers[index];
                if (provider == null || provider.locomotionProvider != target)
                    continue;

                if (first == null)
                    first = provider;
                else
                    providers.RemoveAt(index);
            }

            if (first == null)
            {
                first = new LocomotionVignetteProvider { locomotionProvider = target };
                providers.Add(first);
            }

            first.enabled = true;
            first.overrideDefaultParameters = false;
        }

        static string GetVignetteMaterialPath(GameObject vignetteObject)
        {
            var renderer = vignetteObject.GetComponent<MeshRenderer>();
            if (renderer == null)
                return string.Empty;

            var material = renderer.sharedMaterials.FirstOrDefault(sharedMaterial =>
                sharedMaterial != null && sharedMaterial.shader != null &&
                string.Equals(sharedMaterial.shader.name, "VR/TunnelingVignette", StringComparison.Ordinal));
            return material == null ? string.Empty : AssetDatabase.GetAssetPath(material);
        }

        static string GetHierarchyPath(Transform target)
        {
            var names = new Stack<string>();
            for (var current = target; current != null; current = current.parent)
                names.Push(current.name);

            return $"{target.gameObject.scene.name}/{string.Join("/", names)}";
        }

        static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        static void ShowError(string error)
        {
            Debug.LogError("[XR Comfort Turn] " + error);
            EditorUtility.DisplayDialog("XR Comfort Turn - Not Applied", error, "OK");
        }
    }
}
