using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRProject.ObjectInstallation
{
    /// <summary>
    /// Tracks active installation slots and resolves the nearest compatible, unreserved slot.
    /// Compatibility is based only on the serialized compatibility ID, never on object names.
    /// </summary>
    public static class GhostObjectSnapRegistry
    {
        static readonly List<GhostObjectSnapSlot> s_ActiveSlots = new List<GhostObjectSnapSlot>();

        public static IReadOnlyList<GhostObjectSnapSlot> ActiveSlots => s_ActiveSlots;

        internal static void Register(GhostObjectSnapSlot slot)
        {
            if (slot != null && !s_ActiveSlots.Contains(slot))
                s_ActiveSlots.Add(slot);
        }

        internal static void Unregister(GhostObjectSnapSlot slot)
        {
            if (slot != null)
                s_ActiveSlots.Remove(slot);
        }

        public static GhostObjectSnapSlot FindNearestAvailable(string compatibilityId, Vector3 worldPosition)
        {
            return FindNearestAvailable(s_ActiveSlots, compatibilityId, worldPosition);
        }

        public static GhostObjectSnapSlot FindNearestAvailable(
            IReadOnlyList<GhostObjectSnapSlot> slots,
            string compatibilityId,
            Vector3 worldPosition)
        {
            if (slots == null || string.IsNullOrWhiteSpace(compatibilityId))
                return null;

            GhostObjectSnapSlot nearest = null;
            float nearestSqrDistance = float.PositiveInfinity;

            for (int i = 0; i < slots.Count; ++i)
            {
                GhostObjectSnapSlot slot = slots[i];
                if (slot == null || !slot.isActiveAndEnabled || !slot.IsAvailable ||
                    !string.Equals(slot.CompatibilityId, compatibilityId, StringComparison.Ordinal))
                {
                    continue;
                }

                float sqrDistance = (slot.SnapPose.position - worldPosition).sqrMagnitude;
                float maximumDistance = Mathf.Max(0f, slot.SnapDistance);
                if (sqrDistance > maximumDistance * maximumDistance || sqrDistance >= nearestSqrDistance)
                    continue;

                nearest = slot;
                nearestSqrDistance = sqrDistance;
            }

            return nearest;
        }
    }
}
