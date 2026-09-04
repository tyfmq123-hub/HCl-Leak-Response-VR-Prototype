using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRProject.Valves.Editor
{
    public static class ValveWheelSceneConfigurator
    {
        const string AnchorName = "ValveWheel_KinematicAnchor";
        const string RimRootName = "RimColliders";
        const string CenterShaftColliderName = "CenterShaftGrabCollider";
        const string CenterShaftAttachName = "CenterShaftAttach";
        const int RimColliderCount = 10;
        const float AxisSeparationRatio = 0.45f;
        const float MinimumInPlaneAspect = 0.65f;

        sealed class Geometry
        {
            public Bounds Bounds;
            public Vector3 Axis;
            public Vector3 PlaneU;
            public Vector3 PlaneV;
            public float OuterRadius;
            public float CapsuleRadius;
            public float CapsuleHeight;
            public float ColliderCenterRadius;
        }

        sealed class ConfigurationPlan
        {
            public GameObject Wheel;
            public Geometry Geometry;
            public Rigidbody ExistingExternalAnchor;
            public Transform ExistingAnchorChild;
            public Transform ExistingRimRoot;
            public List<CapsuleCollider> LegacyRimColliders;
        }

        public static void ConfigureSelectedValveWheel()
        {
            if (!TryBuildPlan(out ConfigurationPlan plan, out string reason))
            {
                Debug.LogError($"[Valve Setup] No changes were made. {reason}");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Configure selected valve wheel");

            try
            {
                Configure(plan);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogException(exception);
                Debug.LogError("[Valve Setup] Configuration failed and its partial changes were reverted.");
            }
        }

        static bool ValidateConfigureSelectedValveWheel() =>
            !EditorApplication.isPlayingOrWillChangePlaymode && Selection.gameObjects.Length == 1;

        static bool TryBuildPlan(out ConfigurationPlan plan, out string reason)
        {
            plan = null;
            reason = string.Empty;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                reason = "Exit Play Mode before configuring a valve wheel.";
                return false;
            }

            GameObject[] selected = Selection.gameObjects;
            if (selected.Length != 1 || Selection.activeGameObject == null || selected[0] != Selection.activeGameObject)
            {
                reason = "Select exactly one GameObject in a loaded Scene.";
                return false;
            }

            GameObject wheel = Selection.activeGameObject;
            if (EditorUtility.IsPersistent(wheel) || PrefabUtility.IsPartOfPrefabAsset(wheel) ||
                !wheel.scene.IsValid() || !wheel.scene.isLoaded)
            {
                reason = "The selection is a Project asset rather than a loaded Scene instance.";
                return false;
            }

            if (wheel.GetComponentInParent<XRBaseInteractor>() != null ||
                wheel.GetComponentInChildren<XRBaseInteractor>(true) != null ||
                wheel.GetComponentInChildren<Camera>(true) != null)
            {
                reason = "The selection belongs to an interaction rig, not a standalone wheel.";
                return false;
            }

            if (!HasAtMostOne<Rigidbody>(wheel, out reason) ||
                !HasAtMostOne<HingeJoint>(wheel, out reason) ||
                !HasAtMostOne<XRGrabInteractable>(wheel, out reason) ||
                !HasAtMostOne<ValveWheelGrabTransformer>(wheel, out reason) ||
                !HasAtMostOne<ValveWheelClosureController>(wheel, out reason) ||
                !HasAtMostOne<ValveLeakOutput>(wheel, out reason))
                return false;

            if (!TryAnalyzeGeometry(wheel, out Geometry geometry, out reason))
                return false;

            if (!TryInspectAnchor(wheel, out Rigidbody externalAnchor, out Transform anchorChild, out reason))
                return false;

            if (!TryInspectRimStructure(wheel, out Transform rimRoot,
                    out List<CapsuleCollider> legacyColliders, out reason))
                return false;

            plan = new ConfigurationPlan
            {
                Wheel = wheel,
                Geometry = geometry,
                ExistingExternalAnchor = externalAnchor,
                ExistingAnchorChild = anchorChild,
                ExistingRimRoot = rimRoot,
                LegacyRimColliders = legacyColliders,
            };
            return true;
        }

        static bool HasAtMostOne<T>(GameObject wheel, out string reason) where T : Component
        {
            int count = wheel.GetComponents<T>().Length;
            reason = count <= 1
                ? string.Empty
                : $"'{wheel.name}' contains {count} {typeof(T).Name} components; resolve the duplicates first.";
            return count <= 1;
        }

        static bool TryAnalyzeGeometry(GameObject wheel, out Geometry geometry, out string reason)
        {
            geometry = null;
            reason = string.Empty;
            List<Vector3> corners = new List<Vector3>();

            foreach (MeshFilter filter in wheel.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || IsGeneratedRimObject(filter.transform, wheel.transform))
                    continue;
                AppendBoundsCorners(filter.sharedMesh.bounds, filter.transform, wheel.transform, corners);
            }

            foreach (SkinnedMeshRenderer renderer in wheel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null || IsGeneratedRimObject(renderer.transform, wheel.transform))
                    continue;
                AppendBoundsCorners(renderer.localBounds, renderer.transform, wheel.transform, corners);
            }

            if (corners.Count == 0)
            {
                foreach (Renderer renderer in wheel.GetComponentsInChildren<Renderer>(true))
                {
                    if (IsGeneratedRimObject(renderer.transform, wheel.transform))
                        continue;
                    AppendWorldBoundsCorners(renderer.bounds, wheel.transform, corners);
                }
            }

            if (corners.Count == 0)
            {
                reason = "No usable MeshFilter, SkinnedMeshRenderer, or Renderer bounds were found below the selection.";
                return false;
            }

            Bounds bounds = new Bounds(corners[0], Vector3.zero);
            for (int index = 1; index < corners.Count; ++index)
                bounds.Encapsulate(corners[index]);

            Vector3 size = bounds.size;
            if (!IsFinite(size) || size.x <= 0.0001f || size.y <= 0.0001f || size.z <= 0.0001f)
            {
                reason = $"Geometry bounds are degenerate or non-finite: {bounds}.";
                return false;
            }

            int[] axes = { 0, 1, 2 };
            Array.Sort(axes, (left, right) => size[left].CompareTo(size[right]));
            float thickness = size[axes[0]];
            float firstDiameter = size[axes[1]];
            float secondDiameter = size[axes[2]];
            if (thickness > firstDiameter * AxisSeparationRatio)
            {
                reason = $"The thinnest bounds axis is not distinct enough to infer a safe rotation axis ({size}).";
                return false;
            }

            if (firstDiameter / secondDiameter < MinimumInPlaneAspect)
            {
                reason = $"The two in-plane bounds are too dissimilar for a wheel ({size}).";
                return false;
            }

            float outerRadius = (firstDiameter + secondDiameter) * 0.25f;
            float centerTolerance = Mathf.Max(0.0005f, outerRadius * 0.01f);
            if (!IsFinite(bounds.center) || bounds.center.magnitude > centerTolerance)
            {
                reason = $"The geometry center {bounds.center} does not coincide with the selected Transform pivot. " +
                         "Recenter the wheel pivot before configuring it.";
                return false;
            }

            Vector3 axis;
            Vector3 planeU;
            Vector3 planeV;
            switch (axes[0])
            {
                case 0:
                    axis = Vector3.right;
                    planeU = Vector3.up;
                    planeV = Vector3.forward;
                    break;
                case 1:
                    axis = Vector3.up;
                    planeU = Vector3.forward;
                    planeV = Vector3.right;
                    break;
                default:
                    axis = Vector3.forward;
                    planeU = Vector3.right;
                    planeV = Vector3.up;
                    break;
            }

            float thicknessExtent = thickness * 0.5f;
            float capsuleRadius = Mathf.Clamp(
                Mathf.Min(thicknessExtent, outerRadius * 0.125f),
                outerRadius * 0.03f,
                outerRadius * 0.18f);
            float colliderCenterRadius = outerRadius - capsuleRadius * 1.5f;
            if (colliderCenterRadius <= capsuleRadius)
            {
                reason = $"The inferred rim is too small to create stable capsule colliders ({bounds}).";
                return false;
            }

            float capsuleHeight = 2f * colliderCenterRadius * Mathf.Sin(Mathf.PI / RimColliderCount) +
                                  2.1f * capsuleRadius;
            geometry = new Geometry
            {
                Bounds = bounds,
                Axis = axis,
                PlaneU = planeU,
                PlaneV = planeV,
                OuterRadius = outerRadius,
                CapsuleRadius = capsuleRadius,
                CapsuleHeight = Mathf.Max(capsuleHeight, capsuleRadius * 2f),
                ColliderCenterRadius = colliderCenterRadius,
            };
            return true;
        }

        static bool IsGeneratedRimObject(Transform candidate, Transform wheel)
        {
            for (Transform current = candidate; current != null && current != wheel; current = current.parent)
                if (current.name == RimRootName)
                    return true;
            return false;
        }

        static void AppendBoundsCorners(Bounds bounds, Transform source, Transform wheel, ICollection<Vector3> result)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 sourceLocal = center + Vector3.Scale(extents, new Vector3(x, y, z));
                result.Add(wheel.InverseTransformPoint(source.TransformPoint(sourceLocal)));
            }
        }

        static void AppendWorldBoundsCorners(Bounds bounds, Transform wheel, ICollection<Vector3> result)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                result.Add(wheel.InverseTransformPoint(center + Vector3.Scale(extents, new Vector3(x, y, z))));
        }

        static bool TryInspectAnchor(GameObject wheel, out Rigidbody externalAnchor,
            out Transform anchorChild, out string reason)
        {
            externalAnchor = null;
            List<Transform> anchorChildren = DirectChildrenNamed(wheel.transform, AnchorName);
            anchorChild = null;
            reason = string.Empty;

            if (anchorChildren.Count > 1)
            {
                reason = $"Multiple direct children named '{AnchorName}' exist.";
                return false;
            }
            if (anchorChildren.Count == 1)
                anchorChild = anchorChildren[0];

            HingeJoint hinge = wheel.GetComponent<HingeJoint>();
            if (hinge != null && hinge.connectedBody != null &&
                !hinge.connectedBody.transform.IsChildOf(wheel.transform))
            {
                externalAnchor = hinge.connectedBody;
                if (!externalAnchor.isKinematic || externalAnchor.GetComponents<Collider>().Length != 0 ||
                    Vector3.Distance(externalAnchor.position, wheel.transform.position) > 0.0001f)
                {
                    reason = "The existing external Hinge anchor is not a centered, collider-free kinematic body.";
                    return false;
                }
            }

            if (anchorChild == null)
                return true;

            if (!IsIdentityLocalTransform(anchorChild) || anchorChild.GetComponents<Collider>().Length != 0)
            {
                reason = $"Existing '{AnchorName}' is not a centered identity child or contains a Collider.";
                return false;
            }

            if (anchorChild.GetComponents<Rigidbody>().Length > 1)
            {
                reason = $"Existing '{AnchorName}' contains multiple Rigidbody components.";
                return false;
            }

            return true;
        }

        static bool TryInspectRimStructure(GameObject wheel, out Transform rimRoot,
            out List<CapsuleCollider> legacyColliders, out string reason)
        {
            rimRoot = null;
            legacyColliders = null;
            reason = string.Empty;
            List<Transform> roots = DirectChildrenNamed(wheel.transform, RimRootName);
            if (roots.Count > 1)
            {
                reason = $"Multiple direct children named '{RimRootName}' exist.";
                return false;
            }

            if (roots.Count == 1)
            {
                rimRoot = roots[0];
                if (!IsIdentityLocalTransform(rimRoot))
                {
                    reason = $"Existing '{RimRootName}' must have an identity local Transform.";
                    return false;
                }
                return ValidateNamedRimChildren(rimRoot, allowMissing: true, out _, out reason);
            }

            List<CapsuleCollider> directLegacy = new List<CapsuleCollider>();
            bool hasAnyLegacyName = false;
            for (int index = 1; index <= RimColliderCount; ++index)
            {
                string expectedName = RimName(index);
                List<Transform> matches = DirectChildrenNamed(wheel.transform, expectedName);
                hasAnyLegacyName |= matches.Count != 0;
                CapsuleCollider capsule = null;
                if (matches.Count > 1 || matches.Count == 1 &&
                    !TryGetOnlyCapsule(matches[0], out capsule))
                {
                    reason = $"Legacy rim child '{expectedName}' is duplicated or is not a single CapsuleCollider object.";
                    return false;
                }
                if (matches.Count == 1)
                    directLegacy.Add(capsule);
            }

            if (hasAnyLegacyName && directLegacy.Count != RimColliderCount)
            {
                reason = "A partial legacy RimCollider set exists. Complete or remove it manually before configuration.";
                return false;
            }

            if (directLegacy.Count == RimColliderCount)
                legacyColliders = directLegacy;
            return true;
        }

        static bool ValidateNamedRimChildren(Transform root, bool allowMissing,
            out List<CapsuleCollider> colliders, out string reason)
        {
            colliders = new List<CapsuleCollider>(RimColliderCount);
            reason = string.Empty;
            for (int index = 1; index <= RimColliderCount; ++index)
            {
                string expectedName = RimName(index);
                List<Transform> matches = DirectChildrenNamed(root, expectedName);
                CapsuleCollider capsule = null;
                if (matches.Count > 1 || matches.Count == 1 &&
                    !TryGetOnlyCapsule(matches[0], out capsule))
                {
                    reason = $"'{expectedName}' is duplicated or is not a single CapsuleCollider object.";
                    return false;
                }
                if (matches.Count == 1)
                    colliders.Add(capsule);
                else if (!allowMissing)
                {
                    reason = $"'{expectedName}' is missing.";
                    return false;
                }
            }
            return true;
        }

        static bool TryGetOnlyCapsule(Transform transform, out CapsuleCollider capsule)
        {
            CapsuleCollider[] capsules = transform.GetComponents<CapsuleCollider>();
            capsule = capsules.Length == 1 ? capsules[0] : null;
            return capsules.Length == 1 && transform.GetComponent<Rigidbody>() == null;
        }

        static List<Transform> DirectChildrenNamed(Transform parent, string exactName)
        {
            List<Transform> result = new List<Transform>();
            for (int index = 0; index < parent.childCount; ++index)
                if (parent.GetChild(index).name == exactName)
                    result.Add(parent.GetChild(index));
            return result;
        }

        static bool IsIdentityLocalTransform(Transform transform) =>
            transform.localPosition.sqrMagnitude <= 0.00000001f &&
            Quaternion.Angle(transform.localRotation, Quaternion.identity) <= 0.001f &&
            (transform.localScale - Vector3.one).sqrMagnitude <= 0.00000001f;

        static void Configure(ConfigurationPlan plan)
        {
            GameObject wheel = plan.Wheel;
            Rigidbody body = EnsureSingleComponent<Rigidbody>(wheel);
            ConfigureWheelBody(body, plan.Geometry.Axis);

            Rigidbody anchorBody = plan.ExistingExternalAnchor != null
                ? plan.ExistingExternalAnchor
                : ConfigureChildAnchor(wheel, plan.ExistingAnchorChild);

            HingeJoint hinge = EnsureSingleComponent<HingeJoint>(wheel);
            ConfigureHinge(hinge, anchorBody, plan.Geometry.Axis);

            CapsuleCollider centerShaftCollider = ConfigureCenterShaftCollider(plan);
            List<CapsuleCollider> rimColliders = ConfigureRimColliders(plan);
            ValveWheelGrabTransformer transformer = EnsureSingleComponent<ValveWheelGrabTransformer>(wheel);
            ConfigureTransformer(transformer, hinge);

            XRGrabInteractable grab = EnsureSingleComponent<XRGrabInteractable>(wheel);
            ConfigureGrab(grab, transformer, centerShaftCollider, rimColliders);

            ValveLeakOutput leakOutput = EnsureSingleComponent<ValveLeakOutput>(wheel);
            ValveWheelClosureController controller = EnsureSingleComponent<ValveWheelClosureController>(wheel);
            ConfigureController(controller, body, hinge, grab, transformer, leakOutput, wheel.transform);

            foreach (UnityEngine.Object changed in new UnityEngine.Object[]
                     { wheel, body, hinge, transformer, grab, leakOutput, controller })
                EditorUtility.SetDirty(changed);
            if (plan.ExistingExternalAnchor == null)
                EditorUtility.SetDirty(anchorBody);
            EditorSceneManager.MarkSceneDirty(wheel.scene);

            Debug.Log(
                $"[Valve Setup] Configured selected Scene instance '{GetHierarchyPath(wheel)}'. " +
                $"Local axis={plan.Geometry.Axis}, center={plan.Geometry.Bounds.center}, " +
                $"outer radius={plan.Geometry.OuterRadius:0.####}, Grab=wheel rim. " +
                "The positive inferred local axis is the valve front convention.", wheel);
        }

        static CapsuleCollider ConfigureCenterShaftCollider(ConfigurationPlan plan)
        {
            List<Transform> matches = DirectChildrenNamed(plan.Wheel.transform, CenterShaftColliderName);
            if (matches.Count > 1)
                throw new InvalidOperationException($"Multiple '{CenterShaftColliderName}' children exist.");

            Transform shaft;
            if (matches.Count == 0)
            {
                GameObject shaftObject = new GameObject(CenterShaftColliderName);
                Undo.RegisterCreatedObjectUndo(shaftObject, "Create valve center shaft collider");
                shaft = shaftObject.transform;
                Undo.SetTransformParent(shaft, plan.Wheel.transform, "Parent valve center shaft collider");
            }
            else
            {
                shaft = matches[0];
            }

            Undo.RecordObject(shaft, "Configure valve center shaft pose");
            shaft.localPosition = Vector3.zero;
            shaft.localRotation = Quaternion.identity;
            shaft.localScale = Vector3.one;

            CapsuleCollider capsule = shaft.GetComponent<CapsuleCollider>();
            if (capsule == null)
                capsule = Undo.AddComponent<CapsuleCollider>(shaft.gameObject);

            float radius = Mathf.Clamp(plan.Geometry.OuterRadius * 0.30f, 0.035f, 0.06f);
            float height = Mathf.Max(radius * 2f, plan.Geometry.OuterRadius * 1.35f);
            Undo.RecordObject(capsule, "Configure valve center shaft collider");
            capsule.enabled = true;
            capsule.isTrigger = true;
            capsule.direction = DominantAxis(plan.Geometry.Axis);
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = plan.Geometry.Axis.normalized * (height * 0.32f);
            EditorUtility.SetDirty(capsule);
            return capsule;
        }

        static int DominantAxis(Vector3 axis)
        {
            axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            if (axis.x >= axis.y && axis.x >= axis.z) return 0;
            return axis.y >= axis.z ? 1 : 2;
        }

        static T EnsureSingleComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(gameObject);
        }

        static void ConfigureWheelBody(Rigidbody body, Vector3 axis)
        {
            Undo.RecordObject(body, "Configure valve Rigidbody");
            body.useGravity = false;
            body.isKinematic = false;
            body.mass = 1f;
            body.linearDamping = 0f;
            body.angularDamping = 0.25f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            RigidbodyConstraints rotationLocks = axis == Vector3.right
                ? RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ
                : axis == Vector3.up
                    ? RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ
                    : RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            body.constraints = RigidbodyConstraints.FreezePosition | rotationLocks;
        }

        static Rigidbody ConfigureChildAnchor(GameObject wheel, Transform existing)
        {
            Transform anchor = existing;
            if (anchor == null)
            {
                GameObject anchorObject = new GameObject(AnchorName);
                Undo.RegisterCreatedObjectUndo(anchorObject, "Create valve kinematic anchor");
                anchor = anchorObject.transform;
                Undo.SetTransformParent(anchor, wheel.transform, "Parent valve kinematic anchor");
                anchor.localPosition = Vector3.zero;
                anchor.localRotation = Quaternion.identity;
                anchor.localScale = Vector3.one;
            }

            Rigidbody body = anchor.GetComponent<Rigidbody>();
            if (body == null)
                body = Undo.AddComponent<Rigidbody>(anchor.gameObject);
            Undo.RecordObject(body, "Configure valve kinematic anchor");
            body.useGravity = false;
            body.isKinematic = true;
            body.constraints = RigidbodyConstraints.None;
            return body;
        }

        static void ConfigureHinge(HingeJoint hinge, Rigidbody anchorBody, Vector3 axis)
        {
            Undo.RecordObject(hinge, "Configure valve Hinge Joint");
            hinge.connectedBody = anchorBody;
            hinge.autoConfigureConnectedAnchor = false;
            hinge.anchor = Vector3.zero;
            hinge.connectedAnchor = Vector3.zero;
            hinge.axis = axis;
            hinge.useSpring = false;
            hinge.useMotor = false;
            hinge.enableCollision = false;
            hinge.extendedLimits = true;
            hinge.useLimits = true;
        }

        static List<CapsuleCollider> ConfigureRimColliders(ConfigurationPlan plan)
        {
            List<CapsuleCollider> colliders;
            Transform parent;
            if (plan.LegacyRimColliders != null)
            {
                colliders = plan.LegacyRimColliders;
                parent = plan.Wheel.transform;
            }
            else
            {
                Transform root = plan.ExistingRimRoot;
                if (root == null)
                {
                    GameObject rootObject = new GameObject(RimRootName);
                    Undo.RegisterCreatedObjectUndo(rootObject, "Create valve rim collider root");
                    root = rootObject.transform;
                    Undo.SetTransformParent(root, plan.Wheel.transform, "Parent valve rim collider root");
                    root.localPosition = Vector3.zero;
                    root.localRotation = Quaternion.identity;
                    root.localScale = Vector3.one;
                }

                ValidateNamedRimChildren(root, allowMissing: true, out colliders, out _);
                Dictionary<string, CapsuleCollider> byName = colliders.ToDictionary(collider => collider.name);
                colliders = new List<CapsuleCollider>(RimColliderCount);
                for (int index = 1; index <= RimColliderCount; ++index)
                {
                    string name = RimName(index);
                    if (!byName.TryGetValue(name, out CapsuleCollider capsule))
                    {
                        GameObject colliderObject = new GameObject(name);
                        Undo.RegisterCreatedObjectUndo(colliderObject, $"Create {name}");
                        Undo.SetTransformParent(colliderObject.transform, root, $"Parent {name}");
                        capsule = Undo.AddComponent<CapsuleCollider>(colliderObject);
                    }
                    colliders.Add(capsule);
                }
                parent = root;
            }

            for (int index = 0; index < colliders.Count; ++index)
            {
                float angle = index * 360f / RimColliderCount;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 radial = Mathf.Cos(radians) * plan.Geometry.PlaneU +
                                 Mathf.Sin(radians) * plan.Geometry.PlaneV;
                Vector3 tangent = -Mathf.Sin(radians) * plan.Geometry.PlaneU +
                                  Mathf.Cos(radians) * plan.Geometry.PlaneV;
                CapsuleCollider capsule = colliders[index];
                Transform transform = capsule.transform;
                if (transform.parent != parent)
                    throw new InvalidOperationException($"'{capsule.name}' changed parent during configuration.");

                Undo.RecordObject(transform, $"Position {capsule.name}");
                transform.localPosition = radial * plan.Geometry.ColliderCenterRadius;
                transform.localRotation = Quaternion.LookRotation(plan.Geometry.Axis, tangent);
                transform.localScale = Vector3.one;

                Undo.RecordObject(capsule, $"Configure {capsule.name}");
                capsule.enabled = true;
                capsule.isTrigger = true;
                capsule.center = Vector3.zero;
                capsule.direction = 1;
                capsule.radius = plan.Geometry.CapsuleRadius;
                capsule.height = plan.Geometry.CapsuleHeight;
                EditorUtility.SetDirty(capsule);
            }

            return colliders;
        }

        static void ConfigureTransformer(ValveWheelGrabTransformer transformer, HingeJoint hinge)
        {
            Undo.RecordObject(transformer, "Configure valve grab transformer");
            SerializedObject serialized = new SerializedObject(transformer);
            serialized.FindProperty("hingeJoint").objectReferenceValue = hinge;
            serialized.FindProperty("oneHandRotationGain").floatValue = 0f;
            serialized.FindProperty("twoHandRotationGain").floatValue = 1f;
            serialized.FindProperty("twoHandMinimumDeltaDegrees").floatValue = 0.02f;
            serialized.FindProperty("maxOpenRotationDegrees").floatValue = 108f;
            serialized.FindProperty("maxCloseRotationDegrees").floatValue = 252f;
            serialized.FindProperty("maximumHands").intValue = 2;
            serialized.FindProperty("maxGrabSurfaceDistance").floatValue = 0.15f;
            serialized.ApplyModifiedProperties();
            transformer.ApplyConfiguredJointLimits();
        }

        static void ConfigureGrab(XRGrabInteractable grab, ValveWheelGrabTransformer transformer,
            CapsuleCollider centerShaftCollider, IReadOnlyList<CapsuleCollider> rimColliders)
        {
            Undo.RecordObject(grab, "Configure valve XR Grab Interactable");
            grab.enabled = true;
            grab.selectMode = InteractableSelectMode.Multiple;
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.trackPosition = false;
            grab.trackRotation = true;
            grab.trackScale = false;
            grab.useDynamicAttach = false;
            grab.matchAttachPosition = false;
            grab.matchAttachRotation = false;
            grab.throwOnDetach = false;
            grab.retainTransformParent = true;
            grab.addDefaultGrabTransformers = false;
            grab.attachTransform = EnsureCenterShaftAttach(grab.transform, centerShaftCollider);

            grab.colliders.Clear();
            for (int index = 0; index < rimColliders.Count; ++index)
                grab.colliders.Add(rimColliders[index]);

            grab.startingSingleGrabTransformers.Clear();
            grab.startingMultipleGrabTransformers.Clear();
            grab.startingMultipleGrabTransformers.Add(transformer);

            for (int index = grab.startingSelectFilters.Count - 1; index >= 0; --index)
                if (grab.startingSelectFilters[index] == transformer)
                    grab.startingSelectFilters.RemoveAt(index);
            grab.startingSelectFilters.Add(transformer);
        }

        static Transform EnsureCenterShaftAttach(Transform wheel, CapsuleCollider shaftCollider)
        {
            List<Transform> matches = DirectChildrenNamed(wheel, CenterShaftAttachName);
            if (matches.Count > 1)
                throw new InvalidOperationException($"Multiple '{CenterShaftAttachName}' children exist.");

            Transform attach;
            if (matches.Count == 0)
            {
                GameObject attachObject = new GameObject(CenterShaftAttachName);
                Undo.RegisterCreatedObjectUndo(attachObject, "Create valve center shaft attach");
                attach = attachObject.transform;
                Undo.SetTransformParent(attach, wheel, "Parent valve center shaft attach");
            }
            else
            {
                attach = matches[0];
            }

            Undo.RecordObject(attach, "Configure valve center shaft attach");
            attach.localPosition = shaftCollider.center;
            attach.localRotation = Quaternion.identity;
            attach.localScale = Vector3.one;
            return attach;
        }

        static void ConfigureController(ValveWheelClosureController controller, Rigidbody body,
            HingeJoint hinge, XRGrabInteractable grab, ValveWheelGrabTransformer transformer,
            ValveLeakOutput leakOutput, Transform frontReference)
        {
            Undo.RecordObject(controller, "Configure valve Closure controller");
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("hingeJoint").objectReferenceValue = hinge;
            serialized.FindProperty("wheelRigidbody").objectReferenceValue = body;
            serialized.FindProperty("grabInteractable").objectReferenceValue = grab;
            serialized.FindProperty("grabTransformer").objectReferenceValue = transformer;
            serialized.FindProperty("valveFrontReference").objectReferenceValue = frontReference;
            serialized.FindProperty("initialClosurePercent").floatValue = 30f;
            serialized.FindProperty("closeDirection").enumValueIndex =
                (int)ValveWheelClosureController.CloseDirection.ClockwiseFromFront;
            serialized.FindProperty("invertRotationDirection").boolValue = true;
            serialized.FindProperty("leakOutput").objectReferenceValue = leakOutput;
            serialized.ApplyModifiedProperties();
        }

        static string RimName(int index) => $"RimCollider_{index:00}";

        static string GetHierarchyPath(GameObject gameObject)
        {
            string path = gameObject.name;
            for (Transform parent = gameObject.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return gameObject.scene.name + ":" + path;
        }

        static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
