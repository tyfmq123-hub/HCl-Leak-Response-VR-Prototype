using System;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        bool configured;

        public Transform FunctionalRig { get; private set; }
        public Transform P102PumpRoot { get; private set; }
        public Transform StageBagAnchor { get; private set; }
        public int AddedColliderCount { get; private set; }

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

            StageBagAnchor = EnsureStageBagAnchor(scene, FunctionalRig);
            AddedColliderCount = EnsureP102Colliders(P102PumpRoot);
            Debug.Log(
                $"[P102 Environment] Fixed rig ready. addedColliders={AddedColliderCount}, " +
                $"bagAnchor={StageBagAnchor != null}.", this);
            return StageBagAnchor != null;
        }

        Transform EnsureStageBagAnchor(Scene scene, Transform functionalRig)
        {
            Transform existing = FindSceneTransform(scene, SharedPlayerEquipment.StageBagAnchorName);
            if (existing != null)
                return existing;

            var anchorObject = new GameObject(SharedPlayerEquipment.StageBagAnchorName);
            SceneManager.MoveGameObjectToScene(anchorObject, scene);
            Transform anchor = anchorObject.transform;
            anchor.SetParent(functionalRig, false);
            anchor.SetLocalPositionAndRotation(
                new Vector3(-0.45f, 0.05f, -4.8f),
                Quaternion.Euler(0f, 180f, 0f));
            return anchor;
        }

        static int EnsureP102Colliders(Transform pumpRoot)
        {
            int count = 0;
            for (int childIndex = 0; childIndex < pumpRoot.childCount; ++childIndex)
            {
                Transform child = pumpRoot.GetChild(childIndex);
                if (!CanAddCompoundCollider(child) ||
                    !TryGetRendererBoundsInLocalSpace(child, out Bounds bounds))
                {
                    continue;
                }

                BoxCollider collider = child.gameObject.AddComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = new Vector3(
                    Mathf.Max(0.02f, bounds.size.x),
                    Mathf.Max(0.02f, bounds.size.y),
                    Mathf.Max(0.02f, bounds.size.z));
                collider.isTrigger = false;
                ++count;
            }
            return count;
        }

        static bool CanAddCompoundCollider(Transform candidate)
        {
            if (candidate == null || candidate.GetComponentInChildren<Collider>(true) != null ||
                candidate.GetComponentInChildren<Rigidbody>(true) != null)
            {
                return false;
            }

            string candidateName = candidate.name;
            return candidateName.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) < 0 &&
                   candidateName.IndexOf("Valve", StringComparison.OrdinalIgnoreCase) < 0 &&
                   candidateName.IndexOf("Ghost", StringComparison.OrdinalIgnoreCase) < 0 &&
                   candidateName.IndexOf("Leak", StringComparison.OrdinalIgnoreCase) < 0;
        }

        static bool TryGetRendererBoundsInLocalSpace(Transform root, out Bounds localBounds)
        {
            localBounds = default;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasPoint = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; ++rendererIndex)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null || renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer || renderer is LineRenderer)
                {
                    continue;
                }

                Bounds world = renderer.bounds;
                Vector3 min = world.min;
                Vector3 max = world.max;
                for (int corner = 0; corner < 8; ++corner)
                {
                    Vector3 worldPoint = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 localPoint = root.InverseTransformPoint(worldPoint);
                    if (!hasPoint)
                    {
                        localBounds = new Bounds(localPoint, Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPoint);
                    }
                }
            }

            return hasPoint && localBounds.size.sqrMagnitude > 0.000001f;
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
}
