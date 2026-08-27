using TMPro;
using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>
    /// Adds a solid BoxCollider on the P/T cabinet so shared locomotion cannot
    /// walk through the panel. Stop-button and gaze colliders stay triggers.
    /// </summary>
    internal static class EntrySafetyPtPanelLocomotionBlocker
    {
        private const string BlockerName = "PtPanelLocomotionBlocker";
        private const float MinDepthMeters = 0.28f;
        private const float FrontInsetMeters = 0.04f;
        private static readonly Vector3[] s_Corners = new Vector3[8];

        public static void Ensure(Transform panelRoot, Vector3 inwardWorld)
        {
            if (panelRoot == null)
                return;

            KeepInteractionCollidersAsTriggers(panelRoot);

            var blocker = panelRoot.Find(BlockerName);
            if (blocker == null)
            {
                blocker = new GameObject(BlockerName).transform;
                blocker.SetParent(panelRoot, false);
            }

            blocker.localPosition = Vector3.zero;
            blocker.localRotation = Quaternion.identity;
            blocker.localScale = Vector3.one;
            blocker.gameObject.layer = panelRoot.gameObject.layer;

            if (!TryBuildLocalBounds(panelRoot, out var center, out var size))
            {
                center = new Vector3(0f, 0.85f, 0f);
                size = new Vector3(1.6f, 1.7f, 0.2f);
            }

            var inward = panelRoot.InverseTransformDirection(inwardWorld);
            if (inward.sqrMagnitude < 0.0001f)
                inward = Vector3.forward;
            inward.Normalize();

            // Keep the player-facing plane, then thicken into the cabinet only.
            center += inward * FrontInsetMeters;
            var absAxis = new Vector3(Mathf.Abs(inward.x), Mathf.Abs(inward.y), Mathf.Abs(inward.z));
            var depth = Vector3.Dot(size, absAxis);
            var extra = MinDepthMeters - depth;
            if (extra > 0f)
            {
                center += inward * (extra * 0.5f);
                size += absAxis * extra;
            }

            var box = blocker.GetComponent<BoxCollider>();
            if (box == null)
                box = blocker.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.center = center;
            box.size = size;
        }

        private static void KeepInteractionCollidersAsTriggers(Transform panelRoot)
        {
            var colliders = panelRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || collider.gameObject.name == BlockerName)
                    continue;

                if (IsStopButtonHierarchy(collider.transform) ||
                    collider.GetComponent<EntrySafetyGazeTargetBehaviour>() != null)
                    collider.isTrigger = true;
            }
        }

        private static bool TryBuildLocalBounds(Transform panelRoot, out Vector3 center, out Vector3 size)
        {
            center = Vector3.zero;
            size = Vector3.zero;
            var hasBounds = false;
            Bounds localBounds = default;

            var searchRoot = panelRoot.Find("PT_Panel_CelOutline_v2");
            if (searchRoot == null)
                searchRoot = panelRoot;

            var renderers = searchRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !ShouldIncludeRenderer(renderer))
                    continue;

                EncapsulateWorldRenderer(panelRoot, renderer, ref localBounds, ref hasBounds);
            }

            if (!hasBounds)
                return false;

            center = localBounds.center;
            size = localBounds.size;
            size.x = Mathf.Max(0.2f, size.x);
            size.y = Mathf.Max(0.2f, size.y);
            size.z = Mathf.Max(0.05f, size.z);
            return true;
        }

        private static bool ShouldIncludeRenderer(Renderer renderer)
        {
            if (renderer.GetComponent<TMP_Text>() != null)
                return false;
            if (renderer.GetComponent<EntrySafetyGazeTargetBehaviour>() != null)
                return false;
            if (IsStopButtonHierarchy(renderer.transform))
                return false;

            var name = renderer.gameObject.name;
            if (name.IndexOf("Proximity", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (name.IndexOf("Highlight", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return renderer.enabled;
        }

        private static bool IsStopButtonHierarchy(Transform transform)
        {
            var current = transform;
            while (current != null)
            {
                if (current.name.IndexOf("StopButton", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                current = current.parent;
            }

            return false;
        }

        private static void EncapsulateWorldRenderer(
            Transform panelRoot,
            Renderer renderer,
            ref Bounds localBounds,
            ref bool hasBounds)
        {
            var world = renderer.bounds;
            var extents = world.extents;
            var c = world.center;
            s_Corners[0] = c + new Vector3(-extents.x, -extents.y, -extents.z);
            s_Corners[1] = c + new Vector3(-extents.x, -extents.y, extents.z);
            s_Corners[2] = c + new Vector3(-extents.x, extents.y, -extents.z);
            s_Corners[3] = c + new Vector3(-extents.x, extents.y, extents.z);
            s_Corners[4] = c + new Vector3(extents.x, -extents.y, -extents.z);
            s_Corners[5] = c + new Vector3(extents.x, -extents.y, extents.z);
            s_Corners[6] = c + new Vector3(extents.x, extents.y, -extents.z);
            s_Corners[7] = c + new Vector3(extents.x, extents.y, extents.z);

            for (var i = 0; i < s_Corners.Length; i++)
            {
                var local = panelRoot.InverseTransformPoint(s_Corners[i]);
                if (!hasBounds)
                {
                    localBounds = new Bounds(local, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(local);
                }
            }
        }
    }
}
