using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRProject.ObjectInstallation.Editor
{
    /// <summary>
    /// Configures the selected, already-loaded absorbent Ghost hierarchy only when the user invokes the menu.
    /// It never opens, reloads, or saves a Scene and never changes a Prefab or Material asset.
    /// </summary>
    internal static class AbsorbentPreplacedGhostConfigurator
    {
        const string UndoName = "Configure Absorbent Preplaced Ghost Installation";
        const string RequiredRootName = "VRHandPoseTool_ObjectGhosts";
        const string PadType = "Pad";
        const string SockType = "Sock";
        const string PadGroupName = "Pad_AutoFillGroup";
        const string SockGroupName = "Sock_AutoFillGroup";
        const string PadSupplyName = "INT_AbsorbentPad_01";
        const string SockSupplyName = "INT_AbsorbentSock_Straight_01";
        const float MinimumActivationPadding = 0.15f;
        const float MaximumActivationPadding = 0.5f;
        const float LegacyThrowSmoothingDuration = 0.25f;
        const float LegacyThrowVelocityScale = 1.5f;
        const float LegacyThrowAngularVelocityScale = 1f;

        static readonly Regex s_GhostSuffix =
            new Regex("_ObjectGhost[0-9]+$", RegexOptions.CultureInvariant);

        static readonly string[] s_PadObjectNames =
        {
            "INT_AbsorbentPad_01_ObjectGhost1",
            "INT_AbsorbentPad_01_ObjectGhost2",
            "INT_AbsorbentPad_01_ObjectGhost3",
        };

        static readonly string[] s_SockObjectNames =
        {
            "INT_AbsorbentSock_Straight_01_ObjectGhost1",
            "INT_AbsorbentSock_Straight_01_ObjectGhost2",
            "INT_AbsorbentSock_Curved_01_ObjectGhost1",
            "INT_AbsorbentSock_Curved_01_ObjectGhost2",
        };

        sealed class RendererDescriptor
        {
            public Renderer renderer;
            public string key;
            public string displayPath;
            public int materialSlotCount;
        }

        sealed class MaterialSource
        {
            public string originalName;
            public string description;
            public readonly Dictionary<string, Material[]> materialsByRendererKey =
                new Dictionary<string, Material[]>(StringComparer.Ordinal);
        }

        sealed class ColliderCreationPlan
        {
            public MeshFilter meshFilter;
            public GameObject gameObject;
            public string displayPath;
        }

        sealed class TargetPlan
        {
            public GameObject gameObject;
            public string installationTypeId;
            public string originalName;
            public string materialSourceDescription;
            public PreplacedGhostInstallItem existingItem;
            public PreplacedGhostInstallItem.RendererMaterialBinding[] rendererBindings;
            public PreplacedGhostInstallItem.ColliderBinding[] colliderBindings;
            public ColliderCreationPlan colliderCreation;
            public bool grabUsesExplicitColliders;
            public Rigidbody rigidbody;
            public bool createRigidbody;
            public XRGrabInteractable grabInteractable;
            public bool createGrabInteractable;
            public bool migrateLegacyGrabInteractable;
            public string existingGrabPreservationReason;
            public bool installedIsKinematic;
            public bool installedUseGravity;
            public PreplacedGhostInstallItem configuredItem;
        }

        sealed class GroupPlan
        {
            public string groupName;
            public string itemTypeId;
            public GameObject existingObject;
            public PreplacedGhostInstallGroup existingGroup;
            public TargetPlan[] targets;
            public Vector3 calculatedCenter;
            public float calculatedRadius;
            public PreplacedGhostInstallGroup configuredGroup;
        }

        sealed class SourcePlan
        {
            public GameObject gameObject;
            public string itemTypeId;
            public XRGrabInteractable grabInteractable;
            public PreplacedGhostInstallSource existingSource;
        }

        sealed class ConfigurationPlan
        {
            public GameObject root;
            public TargetPlan[] targets;
            public GroupPlan padGroup;
            public GroupPlan sockGroup;
            public readonly List<SourcePlan> sources = new List<SourcePlan>();
            public readonly List<string> notices = new List<string>();
        }

        static void ConfigureSelectedAbsorbentGhostInstallation()
        {
            if (!TryBuildPlan(out ConfigurationPlan plan, out string error))
            {
                ShowError(error);
                return;
            }

            if (!TryApply(plan, out error))
            {
                ShowError(error);
                return;
            }

            string noticeText = plan.notices.Count == 0
                ? "All Pad and Sock supply objects were connected."
                : string.Join("\n", plan.notices.Select(notice => "- " + notice));
            string materialText = string.Join(
                "\n",
                plan.targets
                    .GroupBy(target => target.originalName, StringComparer.Ordinal)
                    .Select(group => $"- {group.Key}: {group.First().materialSourceDescription}"));
            string preservedGrabText = string.Join(
                "\n",
                plan.targets
                    .Where(target => !string.IsNullOrEmpty(target.existingGrabPreservationReason))
                    .Select(target =>
                        $"- {GetHierarchyPath(target.gameObject.transform)}: {target.existingGrabPreservationReason}"));

            string message =
                "The seven preplaced Ghost objects and both install groups were configured.\n" +
                $"Created {plan.targets.Count(target => target.colliderCreation != null)} convex MeshCollider(s) from target MeshFilters.\n" +
                $"Created {plan.targets.Count(target => target.createRigidbody)} Rigidbody component(s).\n" +
                (plan.targets.Any(target => !target.createRigidbody)
                    ? $"Reused {plan.targets.Count(target => !target.createRigidbody)} existing Rigidbody component(s).\n"
                    : string.Empty) +
                $"Created {plan.targets.Count(target => target.createGrabInteractable)} XRGrabInteractable component(s).\n" +
                (plan.targets.Any(target => !target.createGrabInteractable)
                    ? $"Reused {plan.targets.Count(target => !target.createGrabInteractable)} existing XRGrabInteractable component(s).\n"
                    : string.Empty) +
                $"Migrated legacy XRGrabInteractable(s): {plan.targets.Count(target => target.migrateLegacyGrabInteractable)}\n" +
                $"Preserved customized existing XRGrabInteractable(s): " +
                $"{plan.targets.Count(target => !target.createGrabInteractable && !target.migrateLegacyGrabInteractable)}\n" +
                (string.IsNullOrEmpty(preservedGrabText)
                    ? string.Empty
                    : "Preserved existing XRGrabInteractable detail(s):\n" + preservedGrabText + "\n") +
                "The Scene is dirty but was not saved. One Undo reverts this entire run.\n\n" +
                "Opaque material sources:\n" + materialText + "\n\n" +
                "Supply configuration:\n" + noticeText + "\n\n" +
                "Review the generated components and activation radii, then save the Scene manually.";

            Debug.Log($"[Absorbent Preplaced Configurator] {message}", plan.root);
            EditorUtility.DisplayDialog("Absorbent Ghost Installation", message, "OK");
        }

        static bool ValidateConfigureSelectedAbsorbentGhostInstallation()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        static bool TryBuildPlan(out ConfigurationPlan plan, out string error)
        {
            plan = null;
            error = null;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return Fail("Exit Play Mode before running the configurator.", out error);

            if (Selection.objects == null || Selection.objects.Length != 1 || Selection.activeGameObject == null)
                return Fail($"Select exactly one '{RequiredRootName}' Scene GameObject in the Hierarchy.", out error);

            GameObject root = Selection.activeGameObject;
            if (root.name != RequiredRootName)
                return Fail($"Selected object must be named exactly '{RequiredRootName}'.", out error);

            if (EditorUtility.IsPersistent(root) || PrefabUtility.IsPartOfPrefabAsset(root))
                return Fail("The selection is a Project asset. Select the Scene instance in the Hierarchy.", out error);

            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.IsPartOfPrefabContents(root))
                return Fail("Prefab Mode is not supported. Select the object in a normal loaded Scene.", out error);

            if (!root.scene.IsValid() || !root.scene.isLoaded)
                return Fail("The selected object is not in a loaded Scene.", out error);

            var result = new ConfigurationPlan { root = root };
            var targets = new List<TargetPlan>(7);
            var errors = new List<string>();

            CollectTargets(root, s_PadObjectNames, PadType, targets, errors);
            CollectTargets(root, s_SockObjectNames, SockType, targets, errors);
            if (errors.Count != 0)
                return Fail(JoinErrors("Target hierarchy validation failed", errors), out error);

            var materialSources = new Dictionary<string, MaterialSource>(StringComparer.Ordinal);
            foreach (IGrouping<string, TargetPlan> originalGroup in
                     targets.GroupBy(target => target.originalName, StringComparer.Ordinal))
            {
                TargetPlan referenceTarget = originalGroup.First();
                if (!TryResolveMaterialSource(root.scene, referenceTarget.gameObject, originalGroup.Key,
                        out MaterialSource source, out string sourceError))
                {
                    errors.Add(sourceError);
                    continue;
                }

                materialSources.Add(originalGroup.Key, source);
            }

            if (errors.Count != 0)
                return Fail(JoinErrors("Opaque material matching failed", errors), out error);

            for (int i = 0; i < targets.Count; ++i)
            {
                TargetPlan target = targets[i];
                MaterialSource source = materialSources[target.originalName];
                if (!TryCompleteTargetPlan(target, source, out string targetError))
                    errors.Add(targetError);
            }

            if (errors.Count != 0)
                return Fail(JoinErrors("Preplaced item validation failed", errors), out error);

            result.targets = targets.ToArray();
            TargetPlan[] padTargets = targets.Where(target => target.installationTypeId == PadType).ToArray();
            TargetPlan[] sockTargets = targets.Where(target => target.installationTypeId == SockType).ToArray();
            if (padTargets.Length != 3 || sockTargets.Length != 4)
                return Fail($"Expected exactly 3 Pad and 4 Sock targets, found {padTargets.Length} Pad and {sockTargets.Length} Sock.", out error);

            if (!TryBuildGroupPlan(root, PadGroupName, PadType, padTargets, out result.padGroup, out error) ||
                !TryBuildGroupPlan(root, SockGroupName, SockType, sockTargets, out result.sockGroup, out error))
            {
                return false;
            }

            if (!TryBuildSourcePlan(root.scene, PadSupplyName, PadType, result, out error) ||
                !TryBuildSourcePlan(root.scene, SockSupplyName, SockType, result, out error))
            {
                return false;
            }

            plan = result;
            return true;
        }

        static void CollectTargets(
            GameObject root,
            IEnumerable<string> requiredNames,
            string itemTypeId,
            ICollection<TargetPlan> targets,
            ICollection<string> errors)
        {
            foreach (string requiredName in requiredNames)
            {
                GameObject[] matches = FindObjectsByExactName(root.transform, requiredName);
                if (matches.Length == 0)
                {
                    errors.Add($"Missing child: {requiredName}");
                    continue;
                }

                if (matches.Length > 1)
                {
                    errors.Add($"Duplicate child name '{requiredName}' ({matches.Length} matches).");
                    continue;
                }

                string originalName = s_GhostSuffix.Replace(requiredName, string.Empty);
                if (originalName == requiredName)
                {
                    errors.Add($"Target '{requiredName}' does not end with _ObjectGhost plus a number.");
                    continue;
                }

                GameObject target = matches[0];
                targets.Add(new TargetPlan
                {
                    gameObject = target,
                    installationTypeId = itemTypeId,
                    originalName = originalName,
                    existingItem = target.GetComponent<PreplacedGhostInstallItem>(),
                });
            }
        }

        static bool TryCompleteTargetPlan(TargetPlan plan, MaterialSource materialSource, out string error)
        {
            error = null;
            RendererDescriptor[] renderers = GetRendererDescriptors(plan.gameObject);
            if (renderers.Length == 0)
                return Fail($"'{GetHierarchyPath(plan.gameObject.transform)}' has no Renderer.", out error);

            var rendererBindings = new List<PreplacedGhostInstallItem.RendererMaterialBinding>(renderers.Length);
            for (int i = 0; i < renderers.Length; ++i)
            {
                RendererDescriptor renderer = renderers[i];
                if (!materialSource.materialsByRendererKey.TryGetValue(renderer.key, out Material[] installedMaterials))
                {
                    return Fail(
                        $"'{plan.gameObject.name}' has no exact material match for Renderer '{renderer.displayPath}'.",
                        out error);
                }

                Material[] ghostMaterials;
                if (plan.existingItem != null &&
                    plan.existingItem.TryGetGhostMaterials(renderer.renderer, out Material[] savedGhostMaterials))
                {
                    ghostMaterials = savedGhostMaterials;
                }
                else
                {
                    ghostMaterials = CloneMaterials(renderer.renderer.sharedMaterials);
                }

                if (!HasCompleteMaterials(ghostMaterials, renderer.materialSlotCount))
                    return Fail($"'{plan.gameObject.name}/{renderer.displayPath}' has an incomplete Ghost material array.", out error);

                for (int materialIndex = 0; materialIndex < ghostMaterials.Length; ++materialIndex)
                {
                    if (!IsGhostMaterial(ghostMaterials[materialIndex]))
                    {
                        return Fail(
                            $"'{plan.gameObject.name}/{renderer.displayPath}' material slot {materialIndex} is not a recognized Ghost/Fresnel material: " +
                            $"'{ghostMaterials[materialIndex].name}'.",
                            out error);
                    }
                }

                rendererBindings.Add(new PreplacedGhostInstallItem.RendererMaterialBinding(
                    renderer.renderer,
                    ghostMaterials,
                    installedMaterials));
            }

            XRGrabInteractable[] grabs = plan.gameObject.GetComponentsInChildren<XRGrabInteractable>(true);
            if (grabs.Length > 1)
            {
                string locations = string.Join(", ", grabs.Select(candidate => GetHierarchyPath(candidate.transform)));
                return Fail(
                    $"'{GetHierarchyPath(plan.gameObject.transform)}' has multiple XRGrabInteractable components " +
                    $"({grabs.Length}: {locations}). The configurator will not guess which one to use.",
                    out error);
            }

            XRGrabInteractable grab = grabs.Length == 1 ? grabs[0] : null;
            if (grab != null && grab.gameObject != plan.gameObject)
            {
                return Fail(
                    $"'{GetHierarchyPath(plan.gameObject.transform)}' has its only XRGrabInteractable below the target root at " +
                    $"'{GetHierarchyPath(grab.transform)}'. Move it to the target root or remove it so the configurator can create one there.",
                    out error);
            }

            Rigidbody[] rootBodies = plan.gameObject.GetComponents<Rigidbody>();
            if (rootBodies.Length > 1)
            {
                return Fail(
                    $"'{GetHierarchyPath(plan.gameObject.transform)}' has multiple Rigidbody components on the target root.",
                    out error);
            }

            Rigidbody body = rootBodies.Length == 1 ? rootBodies[0] : null;
            if (body == null)
            {
                Rigidbody[] misplacedBodies = plan.gameObject.GetComponentsInChildren<Rigidbody>(true);
                if (misplacedBodies.Length != 0)
                {
                    return Fail(
                        $"'{GetHierarchyPath(plan.gameObject.transform)}' has no Rigidbody on the XRGrabInteractable root, " +
                        "but has Rigidbody component(s) below it. The configurator will not guess which body to use:\n" +
                        string.Join("\n", misplacedBodies.Select(candidate => $"  - {GetHierarchyPath(candidate.transform)}")),
                        out error);
                }

                if (plan.existingItem != null && plan.existingItem.TargetRigidbody != null)
                {
                    return Fail(
                        $"'{GetHierarchyPath(plan.gameObject.transform)}' has an existing PreplacedGhostInstallItem " +
                        $"that references a Rigidbody on '{GetHierarchyPath(plan.existingItem.TargetRigidbody.transform)}' instead of its root.",
                        out error);
                }
            }
            else if (plan.existingItem != null &&
                     plan.existingItem.TargetRigidbody != null &&
                     plan.existingItem.TargetRigidbody != body)
            {
                return Fail(
                    $"'{GetHierarchyPath(plan.gameObject.transform)}' has an existing PreplacedGhostInstallItem " +
                    $"that references a different Rigidbody on '{GetHierarchyPath(plan.existingItem.TargetRigidbody.transform)}'.",
                    out error);
            }

            if (!TryBuildColliderPlan(plan, grab, out string colliderError))
                return Fail(colliderError, out error);

            bool installedIsKinematic;
            bool installedUseGravity;
            if (body != null &&
                plan.existingItem != null &&
                plan.existingItem.TryGetInstalledRigidbodyState(out bool savedKinematic, out bool savedGravity))
            {
                installedIsKinematic = savedKinematic;
                installedUseGravity = savedGravity;
            }
            else
            {
                installedIsKinematic = body != null && body.isKinematic;
                installedUseGravity = body == null || body.useGravity;
            }

            plan.materialSourceDescription = materialSource.description;
            plan.rendererBindings = rendererBindings.ToArray();
            plan.rigidbody = body;
            plan.createRigidbody = body == null;
            plan.grabInteractable = grab;
            plan.createGrabInteractable = grab == null;
            plan.migrateLegacyGrabInteractable = grab != null && HasLegacyGeneratedGrabSettings(grab);
            plan.existingGrabPreservationReason = grab != null && !plan.migrateLegacyGrabInteractable
                ? DescribeLegacyGrabMismatch(grab)
                : null;
            plan.installedIsKinematic = installedIsKinematic;
            plan.installedUseGravity = installedUseGravity;
            return true;
        }

        static bool HasLegacyGeneratedGrabSettings(XRGrabInteractable grab)
        {
            return grab.movementType == XRBaseInteractable.MovementType.Instantaneous &&
                   grab.attachTransform == null &&
                   !grab.useDynamicAttach &&
                   grab.matchAttachPosition &&
                   grab.matchAttachRotation &&
                   grab.throwOnDetach &&
                   Mathf.Approximately(grab.throwSmoothingDuration, LegacyThrowSmoothingDuration) &&
                   Mathf.Approximately(grab.throwVelocityScale, LegacyThrowVelocityScale) &&
                   Mathf.Approximately(grab.throwAngularVelocityScale, LegacyThrowAngularVelocityScale);
        }

        static string DescribeLegacyGrabMismatch(XRGrabInteractable grab)
        {
            var mismatches = new List<string>();
            if (grab.movementType != XRBaseInteractable.MovementType.Instantaneous)
                mismatches.Add($"Movement Type is {grab.movementType}");
            if (grab.attachTransform != null)
                mismatches.Add($"Attach Transform is '{GetHierarchyPath(grab.attachTransform)}'");
            if (grab.useDynamicAttach)
                mismatches.Add("Dynamic Attach is enabled");
            if (!grab.matchAttachPosition)
                mismatches.Add("Match Attach Position is disabled");
            if (!grab.matchAttachRotation)
                mismatches.Add("Match Attach Rotation is disabled");
            if (!grab.throwOnDetach)
                mismatches.Add("Throw On Detach is disabled");
            if (!Mathf.Approximately(grab.throwSmoothingDuration, LegacyThrowSmoothingDuration))
                mismatches.Add($"Throw Smoothing Duration is {grab.throwSmoothingDuration:R}");
            if (!Mathf.Approximately(grab.throwVelocityScale, LegacyThrowVelocityScale))
                mismatches.Add($"Throw Velocity Scale is {grab.throwVelocityScale:R}");
            if (!Mathf.Approximately(grab.throwAngularVelocityScale, LegacyThrowAngularVelocityScale))
                mismatches.Add($"Throw Angular Velocity Scale is {grab.throwAngularVelocityScale:R}");

            return string.Join(", ", mismatches);
        }

        static void ApplyRecoverySafeGrabSettings(XRGrabInteractable grab)
        {
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true;
            grab.matchAttachRotation = true;
            grab.throwOnDetach = false;

            if (grab.movementType != XRBaseInteractable.MovementType.Kinematic ||
                !grab.useDynamicAttach ||
                !grab.matchAttachPosition ||
                !grab.matchAttachRotation ||
                grab.throwOnDetach)
            {
                throw new InvalidOperationException(
                    $"The XRGrabInteractable on '{grab.gameObject.name}' " +
                    "did not retain its recovery-safe movement, attach, and throw settings.");
            }
        }

        static bool TryBuildColliderPlan(
            TargetPlan plan,
            XRGrabInteractable grabInteractable,
            out string error)
        {
            error = null;
            Collider[] colliders = plan.gameObject.GetComponentsInChildren<Collider>(true);
            if (colliders.Length != 0)
            {
                var bindings = new PreplacedGhostInstallItem.ColliderBinding[colliders.Length];
                for (int i = 0; i < colliders.Length; ++i)
                {
                    bool installedEnabled = plan.existingItem != null &&
                                            plan.existingItem.TryGetInstalledColliderState(colliders[i], out bool savedEnabled)
                        ? savedEnabled
                        : colliders[i].enabled;
                    bindings[i] = new PreplacedGhostInstallItem.ColliderBinding(colliders[i], installedEnabled);
                }

                plan.colliderBindings = bindings;
                plan.colliderCreation = null;
                plan.grabUsesExplicitColliders =
                    grabInteractable != null && grabInteractable.colliders.Count != 0;
                return true;
            }

            MeshFilter[] meshFilters = plan.gameObject.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter[] candidates = meshFilters
                .Where(meshFilter =>
                    meshFilter.sharedMesh != null &&
                    meshFilter.GetComponent<MeshRenderer>() != null &&
                    UsesTargetRootRigidbodyAfterApply(plan.gameObject.transform, meshFilter.transform))
                .ToArray();

            string meshFilterState = DescribeMeshFilters(plan.gameObject, meshFilters);
            if (candidates.Length == 0)
            {
                return Fail(
                    $"'{GetHierarchyPath(plan.gameObject.transform)}' has no Collider and no unambiguous MeshFilter host " +
                    "with a non-null sharedMesh, a MeshRenderer on the same GameObject, and the target root Rigidbody.\n" +
                    meshFilterState,
                    out error);
            }

            if (candidates.Length > 1)
            {
                string candidatePaths = string.Join(
                    "\n",
                    candidates.Select(candidate =>
                        $"  - {GetRelativePath(plan.gameObject.transform, candidate.transform)} " +
                        $"(sharedMesh: '{candidate.sharedMesh.name}')"));
                return Fail(
                    $"'{GetHierarchyPath(plan.gameObject.transform)}' has no Collider and has multiple valid MeshCollider hosts. " +
                    "The configurator will not guess which GameObject should receive it.\n" +
                    candidatePaths + "\n" + meshFilterState,
                    out error);
            }

            MeshFilter selected = candidates[0];
            plan.colliderBindings = Array.Empty<PreplacedGhostInstallItem.ColliderBinding>();
            plan.colliderCreation = new ColliderCreationPlan
            {
                meshFilter = selected,
                gameObject = selected.gameObject,
                displayPath = GetRelativePath(plan.gameObject.transform, selected.transform),
            };
            plan.grabUsesExplicitColliders =
                grabInteractable != null && grabInteractable.colliders.Count != 0;
            return true;
        }

        static bool UsesTargetRootRigidbodyAfterApply(Transform targetRoot, Transform candidate)
        {
            Transform current = candidate;
            while (current != null && current != targetRoot)
            {
                if (current.GetComponent<Rigidbody>() != null)
                    return false;

                current = current.parent;
            }

            return current == targetRoot;
        }

        static string DescribeMeshFilters(GameObject target, MeshFilter[] meshFilters)
        {
            if (meshFilters == null || meshFilters.Length == 0)
                return "MeshFilters: none.";

            return "MeshFilters:\n" + string.Join(
                "\n",
                meshFilters.Select(meshFilter =>
                {
                    string path = GetRelativePath(target.transform, meshFilter.transform);
                    string mesh = meshFilter.sharedMesh != null
                        ? $"'{meshFilter.sharedMesh.name}'"
                        : "null";
                    bool hasMeshRenderer = meshFilter.GetComponent<MeshRenderer>() != null;
                    bool usesRootRigidbody = UsesTargetRootRigidbodyAfterApply(target.transform, meshFilter.transform);
                    return $"  - {path} (sharedMesh: {mesh}, MeshRenderer: {hasMeshRenderer}, target root Rigidbody after apply: {usesRootRigidbody})";
                }));
        }

        static bool TryResolveMaterialSource(
            Scene scene,
            GameObject referenceTarget,
            string originalName,
            out MaterialSource source,
            out string error)
        {
            source = null;
            error = null;
            var candidateFailures = new List<string>();

            GameObject[] sceneCandidates = FindSceneObjectsByExactName(scene, originalName);
            if (sceneCandidates.Length > 1)
            {
                return Fail(
                    $"Original '{originalName}' is ambiguous in the loaded Scene ({sceneCandidates.Length} exact-name instances).",
                    out error);
            }

            if (sceneCandidates.Length == 1)
            {
                if (TryCreateMaterialSource(referenceTarget, sceneCandidates[0],
                        $"Scene: {GetHierarchyPath(sceneCandidates[0].transform)}", out source, out string issue))
                {
                    return true;
                }

                candidateFailures.Add($"Scene candidate: {issue}");
            }

            var assetCandidates = FindExactGameObjectAssets(originalName);
            string[][] extensionTiers =
            {
                new[] { ".prefab" },
                new[] { ".fbx", ".obj", ".dae", ".blend" },
            };

            for (int tierIndex = 0; tierIndex < extensionTiers.Length; ++tierIndex)
            {
                string[] extensions = extensionTiers[tierIndex];
                var validSources = new List<MaterialSource>();
                foreach ((string path, GameObject asset) in assetCandidates)
                {
                    if (!extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (TryCreateMaterialSource(referenceTarget, asset, $"Asset: {path}",
                            out MaterialSource candidateSource, out string issue))
                    {
                        validSources.Add(candidateSource);
                    }
                    else
                    {
                        candidateFailures.Add($"{path}: {issue}");
                    }
                }

                if (validSources.Count == 1)
                {
                    source = validSources[0];
                    return true;
                }

                if (validSources.Count > 1)
                {
                    return Fail(
                        $"Original '{originalName}' has multiple exact, renderer-compatible sources in the same priority tier:\n" +
                        string.Join("\n", validSources.Select(candidate => "  " + candidate.description)),
                        out error);
                }
            }

            string detail = candidateFailures.Count == 0
                ? "No exact-name Scene object, Prefab, or model asset was found."
                : string.Join("\n", candidateFailures.Select(failure => "  " + failure));
            return Fail(
                $"Could not find an exact opaque material source for '{originalName}'.\n{detail}",
                out error);
        }

        static bool TryCreateMaterialSource(
            GameObject target,
            GameObject candidate,
            string description,
            out MaterialSource source,
            out string error)
        {
            source = null;
            error = null;
            RendererDescriptor[] targetRenderers = GetRendererDescriptors(target);
            RendererDescriptor[] candidateRenderers = GetRendererDescriptors(candidate);

            if (targetRenderers.Length != candidateRenderers.Length)
            {
                return Fail(
                    $"Renderer count differs (Ghost {targetRenderers.Length}, source {candidateRenderers.Length}).",
                    out error);
            }

            var candidateByKey = candidateRenderers.ToDictionary(renderer => renderer.key, StringComparer.Ordinal);
            var result = new MaterialSource
            {
                originalName = candidate.name,
                description = description,
            };

            for (int i = 0; i < targetRenderers.Length; ++i)
            {
                RendererDescriptor targetRenderer = targetRenderers[i];
                if (!candidateByKey.TryGetValue(targetRenderer.key, out RendererDescriptor candidateRenderer))
                {
                    return Fail($"Missing Renderer path/type '{targetRenderer.displayPath}'.", out error);
                }

                if (candidateRenderer.materialSlotCount != targetRenderer.materialSlotCount)
                {
                    return Fail(
                        $"Material slot count differs at '{targetRenderer.displayPath}' " +
                        $"(Ghost {targetRenderer.materialSlotCount}, source {candidateRenderer.materialSlotCount}).",
                        out error);
                }

                Material[] installedMaterials = CloneMaterials(candidateRenderer.renderer.sharedMaterials);
                if (!HasCompleteMaterials(installedMaterials, targetRenderer.materialSlotCount))
                    return Fail($"Source Renderer '{candidateRenderer.displayPath}' has a missing Material.", out error);

                for (int materialIndex = 0; materialIndex < installedMaterials.Length; ++materialIndex)
                {
                    Material material = installedMaterials[materialIndex];
                    if (IsGhostMaterial(material))
                    {
                        return Fail(
                            $"Source Renderer '{candidateRenderer.displayPath}' slot {materialIndex} uses Ghost/Fresnel Material '{material.name}'.",
                            out error);
                    }
                }

                result.materialsByRendererKey.Add(targetRenderer.key, installedMaterials);
            }

            source = result;
            return true;
        }

        static bool TryBuildGroupPlan(
            GameObject root,
            string groupName,
            string itemTypeId,
            TargetPlan[] targets,
            out GroupPlan plan,
            out string error)
        {
            plan = null;
            error = null;
            GameObject[] matches = FindObjectsByExactName(root.transform, groupName);
            if (matches.Length > 1)
                return Fail($"More than one '{groupName}' exists below the selected root.", out error);

            if (!TryCalculateActivationArea(targets, out Vector3 center, out float radius, out error))
                return false;

            GameObject existingObject = matches.Length == 1 ? matches[0] : null;
            plan = new GroupPlan
            {
                groupName = groupName,
                itemTypeId = itemTypeId,
                existingObject = existingObject,
                existingGroup = existingObject != null
                    ? existingObject.GetComponent<PreplacedGhostInstallGroup>()
                    : null,
                targets = targets,
                calculatedCenter = center,
                calculatedRadius = radius,
            };
            return true;
        }

        static bool TryCalculateActivationArea(
            TargetPlan[] targets,
            out Vector3 center,
            out float radius,
            out string error)
        {
            center = Vector3.zero;
            radius = 0f;
            error = null;
            bool hasBounds = false;
            Bounds combined = default;

            for (int i = 0; i < targets.Length; ++i)
            {
                Renderer[] renderers = targets[i].gameObject.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; ++rendererIndex)
                {
                    if (!hasBounds)
                    {
                        combined = renderers[rendererIndex].bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(renderers[rendererIndex].bounds);
                    }
                }
            }

            if (!hasBounds)
                return Fail("Cannot calculate an activation area because the group has no Renderer bounds.", out error);

            center = combined.center;
            float extentRadius = combined.extents.magnitude;
            float padding = Mathf.Clamp(extentRadius * 0.25f, MinimumActivationPadding, MaximumActivationPadding);
            radius = extentRadius + padding;
            return true;
        }

        static bool TryBuildSourcePlan(
            Scene scene,
            string requiredName,
            string itemTypeId,
            ConfigurationPlan result,
            out string error)
        {
            error = null;
            GameObject[] matches = FindSceneObjectsByExactName(scene, requiredName);
            if (matches.Length > 1)
            {
                return Fail(
                    $"Supply '{requiredName}' is ambiguous in the loaded Scene ({matches.Length} exact-name instances).",
                    out error);
            }

            if (matches.Length == 0)
            {
                result.notices.Add($"Supply '{requiredName}' was not found. Ghosts/groups were configured; add the Scene supply and run this menu again.");
                return true;
            }

            GameObject supply = matches[0];
            Rigidbody body = supply.GetComponent<Rigidbody>();
            XRGrabInteractable grab = supply.GetComponent<XRGrabInteractable>();
            if (body == null || grab == null)
            {
                result.notices.Add(
                    $"Supply '{GetHierarchyPath(supply.transform)}' was not connected because it needs an existing " +
                    $"Rigidbody and XRGrabInteractable (Rigidbody: {(body != null ? "OK" : "missing")}, XR Grab: {(grab != null ? "OK" : "missing")}).");
                return true;
            }

            if (supply.GetComponent<GhostObjectSnapSlot>() != null ||
                supply.GetComponent<GhostSnappableObject>() != null)
            {
                result.notices.Add(
                    $"Supply '{GetHierarchyPath(supply.transform)}' was not connected because it already participates in the generic Ghost Snap system. " +
                    "No generic component was removed from a supply object.");
                return true;
            }

            result.sources.Add(new SourcePlan
            {
                gameObject = supply,
                itemTypeId = itemTypeId,
                grabInteractable = grab,
                existingSource = supply.GetComponent<PreplacedGhostInstallSource>(),
            });
            result.notices.Add($"Supply '{GetHierarchyPath(supply.transform)}' connected as {itemTypeId}.");
            return true;
        }

        static bool TryApply(ConfigurationPlan plan, out string error)
        {
            error = null;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);

            try
            {
                for (int i = 0; i < plan.targets.Length; ++i)
                    ApplyTarget(plan.targets[i]);

                ApplyGroup(plan.root, plan.padGroup);
                ApplyGroup(plan.root, plan.sockGroup);

                for (int i = 0; i < plan.sources.Count; ++i)
                    ApplySource(plan.sources[i]);

                EditorSceneManager.MarkSceneDirty(plan.root.scene);
                Undo.CollapseUndoOperations(undoGroup);
                return true;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                return Fail(
                    $"Configuration failed and its Undo group was reverted.\n{exception.GetType().Name}: {exception.Message}",
                    out error);
            }
        }

        static void ApplyTarget(TargetPlan plan)
        {
            Rigidbody body = plan.rigidbody;
            if (plan.createRigidbody)
            {
                body = Undo.AddComponent<Rigidbody>(plan.gameObject);
                if (body == null)
                    throw new InvalidOperationException($"Could not add a Rigidbody to '{plan.gameObject.name}'.");

                body.isKinematic = false;
                body.useGravity = true;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                if (body.isKinematic || !body.useGravity)
                {
                    throw new InvalidOperationException(
                        $"The generated Rigidbody on '{plan.gameObject.name}' did not retain its installed-state defaults.");
                }

                plan.rigidbody = body;
                EditorUtility.SetDirty(body);
            }
            else
            {
                Undo.RecordObject(body, UndoName);
            }

            DestroyComponentsWithUndo<GhostObjectSnapSlot>(plan.gameObject);
            DestroyComponentsWithUndo<GhostSnappableObject>(plan.gameObject);
            DestroyComponentsWithUndo<GhostObjectAutoFillSource>(plan.gameObject);
            DestroyComponentsWithUndo<PreplacedGhostInstallSource>(plan.gameObject);
            Undo.RecordObjects(plan.rendererBindings.Select(binding => binding.TargetRenderer).ToArray(), UndoName);
            if (plan.colliderBindings.Length != 0)
                Undo.RecordObjects(plan.colliderBindings.Select(binding => binding.TargetCollider).ToArray(), UndoName);

            PreplacedGhostInstallItem.ColliderBinding[] colliderBindings = plan.colliderBindings;
            if (plan.colliderCreation != null)
            {
                MeshCollider meshCollider = Undo.AddComponent<MeshCollider>(plan.colliderCreation.gameObject);
                if (meshCollider == null)
                {
                    throw new InvalidOperationException(
                        $"Could not add a MeshCollider to '{plan.gameObject.name}/{plan.colliderCreation.displayPath}'.");
                }

                Undo.RecordObject(meshCollider, UndoName);
                meshCollider.sharedMesh = plan.colliderCreation.meshFilter.sharedMesh;
                meshCollider.convex = true;
                meshCollider.enabled = true;

                if (meshCollider.sharedMesh != plan.colliderCreation.meshFilter.sharedMesh ||
                    !meshCollider.convex ||
                    !meshCollider.enabled)
                {
                    throw new InvalidOperationException(
                        $"The generated MeshCollider on '{plan.gameObject.name}/{plan.colliderCreation.displayPath}' " +
                        "did not retain its required sharedMesh, convex, and installed-enabled state.");
                }

                colliderBindings = new[]
                {
                    new PreplacedGhostInstallItem.ColliderBinding(meshCollider, true),
                };

                EditorUtility.SetDirty(meshCollider);
            }

            XRGrabInteractable grab = plan.grabInteractable;
            if (plan.createGrabInteractable)
            {
                grab = Undo.AddComponent<XRGrabInteractable>(plan.gameObject);
                if (grab == null)
                {
                    throw new InvalidOperationException(
                        $"Could not add an XRGrabInteractable to '{plan.gameObject.name}'.");
                }

                // Recovery items should keep their grabbed point under the hand, follow through
                // Rigidbody motion, and never inherit a large detach velocity.
                ApplyRecoverySafeGrabSettings(grab);

                plan.grabInteractable = grab;
                EditorUtility.SetDirty(grab);
            }
            else
            {
                Undo.RecordObject(grab, UndoName);

                if (plan.migrateLegacyGrabInteractable)
                {
                    ApplyRecoverySafeGrabSettings(grab);
                    EditorUtility.SetDirty(grab);
                }
            }

            if (plan.colliderCreation != null &&
                plan.grabUsesExplicitColliders &&
                !grab.colliders.Contains(colliderBindings[0].TargetCollider))
            {
                grab.colliders.Add(colliderBindings[0].TargetCollider);
                EditorUtility.SetDirty(grab);
            }

            PreplacedGhostInstallItem item = plan.existingItem != null
                ? plan.existingItem
                : Undo.AddComponent<PreplacedGhostInstallItem>(plan.gameObject);
            Undo.RecordObject(item, UndoName);

            item.Configure(
                plan.installationTypeId,
                plan.rendererBindings,
                colliderBindings,
                body,
                grab,
                plan.installedIsKinematic,
                plan.installedUseGravity);

            if (!item.ApplyInitialGhostState())
                throw new InvalidOperationException($"Could not apply the initial Ghost state to '{plan.gameObject.name}'.");

            EditorUtility.SetDirty(item);
            plan.configuredItem = item;
        }

        static void ApplyGroup(GameObject root, GroupPlan plan)
        {
            GameObject groupObject = plan.existingObject;
            bool hasExistingPreplacedGroup = plan.existingGroup != null;
            if (groupObject == null)
            {
                groupObject = new GameObject(plan.groupName);
                Undo.RegisterCreatedObjectUndo(groupObject, UndoName);
                Undo.SetTransformParent(groupObject.transform, root.transform, UndoName);
                groupObject.transform.position = plan.calculatedCenter;
            }
            else if (!hasExistingPreplacedGroup)
            {
                Undo.RecordObject(groupObject.transform, UndoName);
                groupObject.transform.position = plan.calculatedCenter;
            }

            DestroyComponentsWithUndo<GhostObjectAutoFillGroup>(groupObject);

            PreplacedGhostInstallGroup group = plan.existingGroup != null
                ? plan.existingGroup
                : Undo.AddComponent<PreplacedGhostInstallGroup>(groupObject);
            Undo.RecordObject(group, UndoName);

            Transform center = hasExistingPreplacedGroup
                ? plan.existingGroup.ActivationCenter
                : groupObject.transform;
            float radius = hasExistingPreplacedGroup
                ? plan.existingGroup.ActivationRadius
                : plan.calculatedRadius;
            var configuredItems = plan.targets.Select(target => target.configuredItem).ToArray();
            group.Configure(plan.itemTypeId, center, radius, configuredItems);
            EditorUtility.SetDirty(group);
            plan.configuredGroup = group;
        }

        static void ApplySource(SourcePlan plan)
        {
            DestroyComponentsWithUndo<GhostObjectAutoFillSource>(plan.gameObject);

            PreplacedGhostInstallSource source = plan.existingSource != null
                ? plan.existingSource
                : Undo.AddComponent<PreplacedGhostInstallSource>(plan.gameObject);
            Undo.RecordObject(source, UndoName);
            source.Configure(plan.itemTypeId, plan.grabInteractable, true);
            EditorUtility.SetDirty(source);
        }

        static void DestroyComponentsWithUndo<T>(GameObject gameObject) where T : Component
        {
            T[] components = gameObject.GetComponents<T>();
            for (int i = components.Length - 1; i >= 0; --i)
                Undo.DestroyObjectImmediate(components[i]);
        }

        static RendererDescriptor[] GetRendererDescriptors(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            return renderers
                .Select(renderer =>
                {
                    string path = AnimationUtility.CalculateTransformPath(renderer.transform, root.transform);
                    if (string.IsNullOrEmpty(path))
                        path = ".";

                    int componentIndex = GetSameTypeRendererIndex(renderer);
                    string key = $"{path}|{renderer.GetType().FullName}|{componentIndex}";
                    return new RendererDescriptor
                    {
                        renderer = renderer,
                        key = key,
                        displayPath = $"{path} [{renderer.GetType().Name} #{componentIndex}]",
                        materialSlotCount = renderer.sharedMaterials.Length,
                    };
                })
                .OrderBy(renderer => renderer.key, StringComparer.Ordinal)
                .ToArray();
        }

        static int GetSameTypeRendererIndex(Renderer renderer)
        {
            Renderer[] components = renderer.gameObject.GetComponents<Renderer>();
            int index = 0;
            for (int i = 0; i < components.Length; ++i)
            {
                if (components[i].GetType() != renderer.GetType())
                    continue;

                if (components[i] == renderer)
                    return index;

                ++index;
            }

            return -1;
        }

        static List<(string path, GameObject asset)> FindExactGameObjectAssets(string exactName)
        {
            var results = new List<(string path, GameObject asset)>();
            string[] guids = AssetDatabase.FindAssets($"{exactName} t:GameObject");
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < guids.Length; ++i)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) || !seenPaths.Add(path))
                    continue;

                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null && string.Equals(asset.name, exactName, StringComparison.Ordinal))
                    results.Add((path, asset));
            }

            return results;
        }

        static GameObject[] FindObjectsByExactName(Transform root, string exactName)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Where(candidate => candidate != root &&
                                    string.Equals(candidate.name, exactName, StringComparison.Ordinal))
                .Select(candidate => candidate.gameObject)
                .ToArray();
        }

        static GameObject[] FindSceneObjectsByExactName(Scene scene, string exactName)
        {
            var matches = new List<GameObject>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
                for (int transformIndex = 0; transformIndex < transforms.Length; ++transformIndex)
                {
                    if (string.Equals(transforms[transformIndex].name, exactName, StringComparison.Ordinal))
                        matches.Add(transforms[transformIndex].gameObject);
                }
            }

            return matches.ToArray();
        }

        static bool HasCompleteMaterials(Material[] materials, int expectedCount)
        {
            if (materials == null || materials.Length != expectedCount)
                return false;

            for (int i = 0; i < materials.Length; ++i)
            {
                if (materials[i] == null)
                    return false;
            }

            return true;
        }

        static bool IsGhostMaterial(Material material)
        {
            if (material == null)
                return false;

            string name = material.name ?? string.Empty;
            string path = AssetDatabase.GetAssetPath(material) ?? string.Empty;
            return name.IndexOf("ghost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("fresnel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("ghost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("fresnel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static Material[] CloneMaterials(Material[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<Material>();

            var clone = new Material[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        static string GetHierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        static string GetRelativePath(Transform root, Transform target)
        {
            string path = AnimationUtility.CalculateTransformPath(target, root);
            return string.IsNullOrEmpty(path) ? "." : path;
        }

        static string JoinErrors(string heading, IEnumerable<string> errors)
        {
            var builder = new StringBuilder(heading);
            foreach (string item in errors)
                builder.Append("\n- ").Append(item);
            return builder.ToString();
        }

        static void ShowError(string error)
        {
            string message = error + "\n\nNo Scene object was changed.";
            Debug.LogError($"[Absorbent Preplaced Configurator] {message}");
            EditorUtility.DisplayDialog("Absorbent Ghost Installation", message, "OK");
        }

        static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
