using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>
    /// Verifies the fixed P-102 LeakResponse rig and supplies missing environment-only
    /// colliders without moving the authored rig, Ghosts, or entry spawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class P102LeakEnvironmentSetup : MonoBehaviour
    {
        const string FunctionalRigName = "INT";
        const string P102PumpRootName = "Pump_102";
        const string ResponseBagRootName = "LeakResponse_ResponseBag";
        const float MinimumEnvironmentDimension = 0.12f;

        static readonly string[] NonEnvironmentHierarchyTokens =
        {
            "Absorbent",
            "Ghost",
            "HandPose",
            "RecoveryTongs",
            "WasteContainer",
            "ResponseBag",
            "Retrieval",
            "LeakPoint",
            "FallingDrops",
            "Puddle",
            "Controller",
            "Hand Visualizer",
            "XR Origin",
            "Main Camera",
            "EventSystem",
            "Canvas",
            "Guidance",
            "Highlight",
            "Marker",
            "Spawn",
        };

        readonly List<Collider> addedColliders = new List<Collider>();
        bool configured;

        public Transform FunctionalRig { get; private set; }
        public Transform P102PumpRoot { get; private set; }
        public Transform ResponseBagRoot { get; private set; }
        public int AddedColliderCount => addedColliders.Count;

        public bool Configure(Scene scene)
        {
            if (configured)
                return FunctionalRig != null && P102PumpRoot != null;

            configured = true;
            FunctionalRig = FindSceneTransform(scene, FunctionalRigName);
            P102PumpRoot = FindSceneTransform(scene, P102PumpRootName);
            if (FunctionalRig == null || P102PumpRoot == null)
            {
                Debug.LogError(
                    $"[P102 Environment] Setup failed. functionalRig={FunctionalRig != null}, " +
                    $"pumpRoot={P102PumpRoot != null}.", this);
                return false;
            }

            ResponseBagRoot = FindSceneTransform(scene, ResponseBagRootName);
            if (ResponseBagRoot == null)
            {
                Debug.LogError(
                    $"[P102 Environment] Stage-owned bag root '{ResponseBagRootName}' is missing.",
                    this);
                return false;
            }
            EnsureSceneEnvironmentColliders(scene, addedColliders);
            SetStageActive(true);
            Debug.Log(
                $"[P102 Environment] Fixed rig ready. addedColliders={AddedColliderCount}, " +
                $"responseBag={ResponseBagRoot != null}.", this);
            return true;
        }

        public void SetStageActive(bool active)
        {
            for (int i = 0; i < addedColliders.Count; ++i)
            {
                if (addedColliders[i] == null)
                    continue;
                addedColliders[i].enabled = active;
            }
        }

        static void EnsureSceneEnvironmentColliders(Scene scene, List<Collider> results)
        {
            results.Clear();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; ++i)
                {
                    MeshFilter filter = filters[i];
                    if (!CanAddEnvironmentCollider(filter))
                        continue;

                    Mesh mesh = filter.sharedMesh;
                    BoxCollider collider = filter.gameObject.AddComponent<BoxCollider>();
                    collider.center = mesh.bounds.center;
                    collider.size = new Vector3(
                        Mathf.Max(0.01f, mesh.bounds.size.x),
                        Mathf.Max(0.01f, mesh.bounds.size.y),
                        Mathf.Max(0.01f, mesh.bounds.size.z));
                    collider.isTrigger = false;
                    results.Add(collider);
                }
            }
        }

        static bool CanAddEnvironmentCollider(MeshFilter filter)
        {
            if (filter == null || filter.sharedMesh == null ||
                !filter.gameObject.activeInHierarchy)
                return false;

            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled ||
                filter.GetComponent<Collider>() != null ||
                filter.GetComponentInParent<Collider>(true) != null ||
                filter.GetComponentInParent<Rigidbody>(true) != null ||
                filter.GetComponentInParent<XRGrabInteractable>(true) != null ||
                filter.GetComponentInParent<XRBaseInteractor>(true) != null ||
                HasNonEnvironmentName(filter.transform))
                return false;

            Vector3 scale = filter.transform.lossyScale;
            Vector3 worldSize = Vector3.Scale(
                filter.sharedMesh.bounds.size,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            return Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z)) >=
                   MinimumEnvironmentDimension;
        }

        static bool HasNonEnvironmentName(Transform candidate)
        {
            while (candidate != null)
            {
                string candidateName = candidate.name;
                for (int i = 0; i < NonEnvironmentHierarchyTokens.Length; ++i)
                {
                    if (candidateName.IndexOf(
                            NonEnvironmentHierarchyTokens[i],
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                candidate = candidate.parent;
            }
            return false;
        }

        static Transform FindSceneTransform(Scene scene, string exactName)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(exactName))
                return null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; ++i)
                {
                    if (string.Equals(transforms[i].name, exactName, StringComparison.Ordinal))
                        return transforms[i];
                }
            }
            return null;
        }
    }

    /// <summary>
    /// A solid containment volume that collides only with CharacterControllers.
    /// Tracked hands, tongs, and carried absorbents remain free to enter the cleanup area.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class P102AbsorbentPlayerBoundaryRuntime : MonoBehaviour
    {
        const string BoundaryName = "P102_SockContainment_PlayerBoundary_Runtime";
        const float BoundaryHeight = 2.4f;
        const float HorizontalMargin = 0.08f;
        const float CollisionRefreshInterval = 0.25f;

        readonly HashSet<Collider> ignoredDynamicColliders = new HashSet<Collider>();
        BoxCollider boundaryCollider;
        Scene containmentScene;
        float nextCollisionRefreshTime;

        public BoxCollider BoundaryCollider => boundaryCollider;

        public bool Configure(Scene scene, Transform[] containmentTargets)
        {
            if (!scene.IsValid() || !scene.isLoaded ||
                !TryGetCombinedRendererBounds(containmentTargets, out Bounds bounds))
            {
                Debug.LogError(
                    "[P102 Environment] Sock containment bounds are unavailable. Player boundary remains disabled.",
                    this);
                SetStageActive(false);
                return false;
            }
            containmentScene = scene;

            Transform boundary = transform.Find(BoundaryName);
            if (boundary == null)
            {
                var boundaryObject = new GameObject(BoundaryName);
                boundary = boundaryObject.transform;
                boundary.SetParent(transform, false);
            }

            boundary.SetPositionAndRotation(
                new Vector3(bounds.center.x, bounds.min.y + BoundaryHeight * 0.5f, bounds.center.z),
                Quaternion.identity);
            Vector3 parentScale = transform.lossyScale;
            boundary.localScale = new Vector3(
                ReciprocalScale(parentScale.x),
                ReciprocalScale(parentScale.y),
                ReciprocalScale(parentScale.z));

            boundaryCollider = boundary.GetComponent<BoxCollider>();
            if (boundaryCollider == null)
                boundaryCollider = boundary.gameObject.AddComponent<BoxCollider>();
            boundaryCollider.center = Vector3.zero;
            boundaryCollider.size = new Vector3(
                Mathf.Max(0.5f, bounds.size.x + HorizontalMargin * 2f),
                BoundaryHeight,
                Mathf.Max(0.5f, bounds.size.z + HorizontalMargin * 2f));
            boundaryCollider.isTrigger = false;
            SetStageActive(true);
            RefreshDynamicCollisionIgnores();
            return true;
        }

        public void SetStageActive(bool active)
        {
            if (boundaryCollider != null)
                boundaryCollider.enabled = active;
            enabled = active;
        }

        void LateUpdate()
        {
            if (boundaryCollider == null || !boundaryCollider.enabled ||
                Time.unscaledTime < nextCollisionRefreshTime)
                return;

            nextCollisionRefreshTime = Time.unscaledTime + CollisionRefreshInterval;
            RefreshDynamicCollisionIgnores();
        }

        void RefreshDynamicCollisionIgnores()
        {
            if (boundaryCollider == null)
                return;

            IgnoreDynamicCollidersInScene(containmentScene);
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; ++sceneIndex)
            {
                Scene loadedScene = SceneManager.GetSceneAt(sceneIndex);
                if (!loadedScene.IsValid() || !loadedScene.isLoaded ||
                    loadedScene.handle == containmentScene.handle)
                    continue;

                IgnoreDynamicCollidersInScene(loadedScene);
            }
        }

        void IgnoreDynamicCollidersInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; ++i)
                {
                    Collider candidate = colliders[i];
                    if (candidate == null || candidate == boundaryCollider ||
                        candidate is CharacterController ||
                        !IsDynamicInteractionCollider(candidate) ||
                        !ignoredDynamicColliders.Add(candidate))
                        continue;

                    Physics.IgnoreCollision(boundaryCollider, candidate, true);
                }
            }
        }

        static bool IsDynamicInteractionCollider(Collider candidate)
        {
            return candidate.attachedRigidbody != null ||
                   candidate.GetComponentInParent<XRGrabInteractable>(true) != null ||
                   candidate.GetComponentInParent<XRBaseInteractor>(true) != null;
        }

        static bool TryGetCombinedRendererBounds(Transform[] targets, out Bounds combined)
        {
            combined = default;
            bool hasBounds = false;
            if (targets == null)
                return false;

            for (int targetIndex = 0; targetIndex < targets.Length; ++targetIndex)
            {
                Transform target = targets[targetIndex];
                if (target == null)
                    continue;

                Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; ++rendererIndex)
                {
                    Renderer renderer = renderers[rendererIndex];
                    if (renderer == null || renderer is ParticleSystemRenderer ||
                        renderer is TrailRenderer || renderer is LineRenderer ||
                        P102RuntimeUtility.IsHandPoseGhostTransform(
                            renderer.transform, target))
                        continue;

                    if (!hasBounds)
                    {
                        combined = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(renderer.bounds);
                    }
                }
            }
            return hasBounds;
        }

        static float ReciprocalScale(float scale)
        {
            return 1f / Mathf.Max(0.0001f, Mathf.Abs(scale));
        }
    }
}
