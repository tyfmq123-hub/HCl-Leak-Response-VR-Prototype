using System;
using UnityEngine;

namespace VRProject.ObjectInstallation
{
    [DisallowMultipleComponent]
    public sealed class GhostObjectSnapSlot : MonoBehaviour
    {
        [Header("Compatibility")]
        [SerializeField] string compatibilityId;

        [Header("Snap Target")]
        [SerializeField] Transform snapPose;
        [SerializeField, Min(0f)] float snapDistance = 0.25f;
        [SerializeField, Min(0f)] float snapDuration = 0.2f;

        [Header("Ghost Visual")]
        [SerializeField] Renderer[] ghostRenderers = Array.Empty<Renderer>();
        [SerializeField] bool hideGhostWhenOccupied = true;

        [Header("Runtime State (Read Only)")]
        [SerializeField] GhostSnappableObject currentObject;
        [SerializeField] bool occupied;

        bool[] rendererEnabledBeforeOccupancy;

        public string CompatibilityId => compatibilityId;
        public Transform SnapPose => snapPose != null ? snapPose : transform;
        public float SnapDistance => snapDistance;
        public float SnapDuration => snapDuration;
        public bool HideGhostWhenOccupied => hideGhostWhenOccupied;
        public GhostSnappableObject CurrentObject => currentObject;
        public bool IsOccupied => occupied;
        public bool IsReserved => currentObject != null && !occupied;
        public bool IsAvailable => currentObject == null;
        public Vector3 TargetWorldScale => SnapPose.lossyScale;

        void Reset()
        {
            snapPose = transform;
            ghostRenderers = GetComponentsInChildren<Renderer>(true);
        }

        void Awake()
        {
            CacheRendererStates();
            RefreshGhostVisibility();
        }

        void OnEnable()
        {
            if (rendererEnabledBeforeOccupancy == null || rendererEnabledBeforeOccupancy.Length != ghostRenderers.Length)
                CacheRendererStates();

            GhostObjectSnapRegistry.Register(this);
            RefreshGhostVisibility();
        }

        void OnDisable()
        {
            GhostObjectSnapRegistry.Unregister(this);
        }

        void OnValidate()
        {
            snapDistance = Mathf.Max(0f, snapDistance);
            snapDuration = Mathf.Max(0f, snapDuration);
        }

        public bool IsCompatible(string candidateCompatibilityId)
        {
            return !string.IsNullOrWhiteSpace(candidateCompatibilityId) &&
                   string.Equals(compatibilityId, candidateCompatibilityId, StringComparison.Ordinal);
        }

        public bool IsWithinSnapDistance(Vector3 worldPosition)
        {
            float maximumDistance = Mathf.Max(0f, snapDistance);
            return (SnapPose.position - worldPosition).sqrMagnitude <= maximumDistance * maximumDistance;
        }

        internal bool TryReserve(GhostSnappableObject candidate)
        {
            if (candidate == null || currentObject != null || !IsCompatible(candidate.CompatibilityId))
                return false;

            currentObject = candidate;
            occupied = false;
            return true;
        }

        internal bool CommitOccupancy(GhostSnappableObject candidate)
        {
            if (candidate == null || currentObject != candidate)
                return false;

            occupied = true;
            RefreshGhostVisibility();
            return true;
        }

        internal bool Release(GhostSnappableObject candidate)
        {
            if (candidate == null || currentObject != candidate)
                return false;

            currentObject = null;
            occupied = false;
            RefreshGhostVisibility();
            return true;
        }

        public void Configure(
            string newCompatibilityId,
            Transform newSnapPose,
            float newSnapDistance,
            float newSnapDuration,
            Renderer[] newGhostRenderers,
            bool newHideGhostWhenOccupied = true)
        {
            compatibilityId = newCompatibilityId;
            snapPose = newSnapPose != null ? newSnapPose : transform;
            snapDistance = Mathf.Max(0f, newSnapDistance);
            snapDuration = Mathf.Max(0f, newSnapDuration);
            ghostRenderers = newGhostRenderers ?? Array.Empty<Renderer>();
            hideGhostWhenOccupied = newHideGhostWhenOccupied;
            CacheRendererStates();
            RefreshGhostVisibility();
        }

        void CacheRendererStates()
        {
            rendererEnabledBeforeOccupancy = new bool[ghostRenderers.Length];
            for (int i = 0; i < ghostRenderers.Length; ++i)
            {
                Renderer ghostRenderer = ghostRenderers[i];
                rendererEnabledBeforeOccupancy[i] = ghostRenderer != null && ghostRenderer.enabled;
            }
        }

        void RefreshGhostVisibility()
        {
            bool shouldHide = hideGhostWhenOccupied && occupied;
            for (int i = 0; i < ghostRenderers.Length; ++i)
            {
                Renderer ghostRenderer = ghostRenderers[i];
                if (ghostRenderer == null)
                    continue;

                bool originalState = rendererEnabledBeforeOccupancy != null && i < rendererEnabledBeforeOccupancy.Length
                    ? rendererEnabledBeforeOccupancy[i]
                    : true;
                ghostRenderer.enabled = !shouldHide && originalState;
            }
        }
    }
}
