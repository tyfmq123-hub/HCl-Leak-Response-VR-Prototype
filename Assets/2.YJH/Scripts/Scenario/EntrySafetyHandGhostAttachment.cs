using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JH.EntrySafety
{
    /// <summary>
    /// Attaches the baked detector hand Ghost to the runtime detector without depending
    /// on the authoring-only VR Hand Pose Tool package.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntrySafetyHandGhostAttachment : MonoBehaviour
    {
        public const string GhostContainerName = "VRHandPoseTool_Ghosts";
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");

        [SerializeField] private string m_TargetName;
        [SerializeField, HideInInspector] private string[] m_TargetRelativeNamePath = Array.Empty<string>();
        [SerializeField] private GameObject m_HandGhostPrefab;
        [SerializeField] private bool m_GhostVisible = true;
        [SerializeField, ColorUsage(false, true)] private Color m_GhostColor =
            new Color(0.35f, 0.85f, 1f, 1f);
        [SerializeField, HideInInspector, Min(0.05f)] private float m_RefreshInterval = 0.25f;

        private Transform m_AttachedTarget;
        private GameObject m_SpawnedGhost;
        private MaterialPropertyBlock m_PropertyBlock;
        private float m_NextRefreshTime;

        public GameObject SpawnedGhost => m_SpawnedGhost;

        public void RefreshAttachment()
        {
            if (m_HandGhostPrefab == null || string.IsNullOrWhiteSpace(m_TargetName))
            {
                DestroySpawnedGhost();
                m_AttachedTarget = null;
                return;
            }

            var target = ResolveTarget();
            if (target == null)
            {
                DestroySpawnedGhost();
                m_AttachedTarget = null;
                return;
            }

            if (m_AttachedTarget != target || m_SpawnedGhost == null)
            {
                DestroySpawnedGhost();
                m_AttachedTarget = target;
                m_SpawnedGhost = FindOrCreateGhost(target);
            }

            ApplyDisplaySettings();
        }

        private void OnEnable()
        {
            m_NextRefreshTime = 0f;
            if (Application.isPlaying)
                RefreshAttachment();
        }

        private void Update()
        {
            if (Time.unscaledTime < m_NextRefreshTime)
                return;

            m_NextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, m_RefreshInterval);
            RefreshAttachment();
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
                DestroySpawnedGhost();
            m_AttachedTarget = null;
        }

        private Transform ResolveTarget()
        {
            var pathTarget = ResolveByRelativePath();
            if (IsExpectedGrabTarget(pathTarget))
                return pathTarget;

            var grabs = GetComponentsInChildren<XRGrabInteractable>(true);
            Transform matched = null;
            for (var i = 0; i < grabs.Length; i++)
            {
                var grab = grabs[i];
                if (grab == null || grab.transform == transform ||
                    !string.Equals(
                        StableObjectName(grab.name),
                        StableObjectName(m_TargetName),
                        StringComparison.Ordinal))
                    continue;

                if (matched != null)
                    return null;
                matched = grab.transform;
            }

            return matched;
        }

        private Transform ResolveByRelativePath()
        {
            if (m_TargetRelativeNamePath == null || m_TargetRelativeNamePath.Length == 0)
                return null;

            var current = transform;
            for (var pathIndex = 0; pathIndex < m_TargetRelativeNamePath.Length; pathIndex++)
            {
                Transform next = null;
                for (var childIndex = 0; childIndex < current.childCount; childIndex++)
                {
                    var child = current.GetChild(childIndex);
                    if (!string.Equals(
                        StableObjectName(child.name),
                        StableObjectName(m_TargetRelativeNamePath[pathIndex]),
                        StringComparison.Ordinal))
                        continue;

                    if (next != null)
                        return null;
                    next = child;
                }

                if (next == null)
                    return null;
                current = next;
            }

            return current;
        }

        private bool IsExpectedGrabTarget(Transform candidate)
        {
            return candidate != null && candidate != transform &&
                   candidate.GetComponent<XRGrabInteractable>() != null &&
                   string.Equals(
                       StableObjectName(candidate.name),
                       StableObjectName(m_TargetName),
                       StringComparison.Ordinal);
        }

        private GameObject FindOrCreateGhost(Transform target)
        {
            var container = FindDirectChild(target, GhostContainerName);
            if (container == null)
            {
                container = new GameObject(GhostContainerName).transform;
                container.SetParent(target, false);
            }

            var existing = FindDirectChild(container, m_HandGhostPrefab.name);
            if (existing != null)
                return existing.gameObject;

            var instance = Instantiate(m_HandGhostPrefab, container, false);
            instance.name = m_HandGhostPrefab.name;
            instance.SetActive(true);
            return instance;
        }

        private void ApplyDisplaySettings()
        {
            if (m_SpawnedGhost == null)
                return;

            m_PropertyBlock ??= new MaterialPropertyBlock();
            var visible = m_GhostVisible && IsTargetVisualEnabled(m_AttachedTarget);
            var renderers = m_SpawnedGhost.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.enabled = visible;
                renderer.GetPropertyBlock(m_PropertyBlock);
                m_PropertyBlock.SetColor(RimColorId, m_GhostColor);
                renderer.SetPropertyBlock(m_PropertyBlock);
                m_PropertyBlock.Clear();
            }
        }

        private static bool IsTargetVisualEnabled(Transform target)
        {
            if (target == null)
                return false;
            if (!target.gameObject.activeInHierarchy)
                return true;

            var foundTargetRenderer = false;
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || IsInsideGhostContainer(renderer.transform, target))
                    continue;

                foundTargetRenderer = true;
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                    return true;
            }

            return !foundTargetRenderer;
        }

        private static bool IsInsideGhostContainer(Transform child, Transform target)
        {
            for (var current = child; current != null && current != target; current = current.parent)
            {
                if (string.Equals(current.name, GhostContainerName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void DestroySpawnedGhost()
        {
            if (m_SpawnedGhost == null)
                return;

            if (Application.isPlaying)
                Destroy(m_SpawnedGhost);
            else
                DestroyImmediate(m_SpawnedGhost);
            m_SpawnedGhost = null;
        }

        private static string StableObjectName(string objectName)
        {
            var value = string.IsNullOrWhiteSpace(objectName) ? "RuntimeObject" : objectName.Trim();
            const string cloneSuffix = "(Clone)";
            while (value.EndsWith(cloneSuffix, StringComparison.Ordinal))
                value = value.Substring(0, value.Length - cloneSuffix.Length).TrimEnd();
            return string.IsNullOrWhiteSpace(value) ? "RuntimeObject" : value;
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            if (parent == null)
                return null;

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                    return child;
            }
            return null;
        }
    }
}
