using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRProject.ObjectInstallation
{
    /// <summary>
    /// Represents one installation area that fills every currently empty slot from per-slot templates.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostObjectAutoFillGroup : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Ghost slot to occupy. The slot keeps ownership and Ghost visibility state.")]
            [SerializeField] GhostObjectSnapSlot slot;

            [Tooltip("Prefab or Scene template made for this exact slot shape. It must contain one GhostSnappableObject.")]
            [SerializeField] GameObject installedPrefab;

            public GhostObjectSnapSlot Slot => slot;
            public GameObject InstalledPrefab => installedPrefab;

            public Entry(GhostObjectSnapSlot newSlot, GameObject newInstalledPrefab)
            {
                slot = newSlot;
                installedPrefab = newInstalledPrefab;
            }
        }

        sealed class InstallPlan
        {
            public readonly GhostObjectSnapSlot Slot;
            public readonly GameObject Prefab;

            public InstallPlan(GhostObjectSnapSlot slot, GameObject prefab)
            {
                Slot = slot;
                Prefab = prefab;
            }
        }

        sealed class SpawnRecord
        {
            public GameObject Root;
            public GhostSnappableObject Snappable;
        }

        static readonly List<GhostObjectAutoFillGroup> s_ActiveGroups =
            new List<GhostObjectAutoFillGroup>();

        [Header("Installation Type")]
        [Tooltip("Supply type accepted by this area, for example Pad or Sock. Comparison ignores case and outer spaces.")]
        [SerializeField] string acceptedItemTypeId;

        [Header("Activation Area")]
        [Tooltip("Optional center of the release detection area. This Transform is used when omitted.")]
        [SerializeField] Transform activationCenter;

        [Tooltip("Maximum world-space distance from the center at which a matching supply can activate this group.")]
        [SerializeField, Min(0f)] float activationRadius = 0.5f;

        [Header("Per-Slot Installed Objects")]
        [Tooltip("Each slot has its own correctly shaped installation Prefab or template. Filled slots are left unchanged.")]
        [SerializeField] Entry[] entries = Array.Empty<Entry>();

        bool processing;
        string lastRuntimeFailure;

        public string AcceptedItemTypeId => acceptedItemTypeId;
        public Transform ActivationCenter => activationCenter != null ? activationCenter : transform;
        public float ActivationRadius => Mathf.Max(0f, activationRadius);
        public IReadOnlyList<Entry> Entries => entries;
        public bool IsProcessing => processing;

        void OnEnable()
        {
            if (!s_ActiveGroups.Contains(this))
                s_ActiveGroups.Add(this);
        }

        void OnDisable()
        {
            s_ActiveGroups.Remove(this);
        }

        void OnValidate()
        {
            lastRuntimeFailure = null;
            if (TryGetInspectorConfigurationIssue(out string issue))
                Debug.LogWarning($"[Ghost Auto Fill] {name}: {issue}", this);
        }

        void OnDrawGizmosSelected()
        {
            Transform center = ActivationCenter;
            if (center == null)
                return;

            Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(center.position, ActivationRadius);
        }

        public bool IsCompatibleItemType(string candidateItemTypeId)
        {
            return ItemTypeIdsMatch(acceptedItemTypeId, candidateItemTypeId);
        }

        public static bool ItemTypeIdsMatch(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
                return false;

            return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static GhostObjectAutoFillGroup FindNearestCompatible(
            string itemTypeId,
            Vector3 releaseWorldPosition)
        {
            if (string.IsNullOrWhiteSpace(itemTypeId))
                return null;

            GhostObjectAutoFillGroup nearest = null;
            float nearestSqrDistance = float.PositiveInfinity;

            for (int i = s_ActiveGroups.Count - 1; i >= 0; --i)
            {
                GhostObjectAutoFillGroup group = s_ActiveGroups[i];
                if (group == null)
                {
                    s_ActiveGroups.RemoveAt(i);
                    continue;
                }

                if (!group.isActiveAndEnabled || !group.IsCompatibleItemType(itemTypeId))
                    continue;

                float sqrDistance = (group.ActivationCenter.position - releaseWorldPosition).sqrMagnitude;
                float radius = group.ActivationRadius;
                if (sqrDistance > radius * radius || sqrDistance >= nearestSqrDistance)
                    continue;

                nearest = group;
                nearestSqrDistance = sqrDistance;
            }

            return nearest;
        }

        /// <summary>
        /// Fills all slots that were available at the start of this request.
        /// Returns true only when every planned slot was occupied successfully.
        /// </summary>
        public bool TryAutoFill(string itemTypeId, out int installedCount)
        {
            installedCount = 0;
            if (processing || !isActiveAndEnabled || !IsCompatibleItemType(itemTypeId))
                return false;

            processing = true;
            var spawned = new List<SpawnRecord>();

            try
            {
                if (!TryBuildInstallPlan(out List<InstallPlan> plans, out string issue))
                {
                    LogRuntimeFailureOnce(issue);
                    return false;
                }

                // A full group is a normal no-op: do not create anything, consume the source, or warn repeatedly.
                if (plans.Count == 0)
                    return false;

                for (int i = 0; i < plans.Count; ++i)
                {
                    InstallPlan plan = plans[i];
                    Transform pose = plan.Slot.SnapPose;
                    GameObject clone = Instantiate(plan.Prefab, pose.position, pose.rotation);
                    var record = new SpawnRecord { Root = clone };
                    spawned.Add(record);

                    clone.name = $"{plan.Prefab.name}_Installed_{plan.Slot.name}";
                    if (!clone.activeSelf)
                        clone.SetActive(true);

                    GhostSnappableObject[] snappables =
                        clone.GetComponentsInChildren<GhostSnappableObject>(true);
                    if (snappables.Length != 1)
                        return RollBackFailure(spawned,
                            $"Instantiated object '{plan.Prefab.name}' did not contain exactly one GhostSnappableObject.");

                    record.Snappable = snappables[0];
                    if (!record.Snappable.TryInstallToSlot(plan.Slot, false))
                        return RollBackFailure(spawned,
                            $"Could not occupy slot '{plan.Slot.name}' with '{plan.Prefab.name}'.");
                }

                installedCount = spawned.Count;
                lastRuntimeFailure = null;
                return installedCount > 0;
            }
            catch (Exception exception)
            {
                RollBackSpawned(spawned);
                LogRuntimeFailureOnce($"Installation threw {exception.GetType().Name}: {exception.Message}");
                return false;
            }
            finally
            {
                processing = false;
            }
        }

        public void Configure(
            string newAcceptedItemTypeId,
            Transform newActivationCenter,
            float newActivationRadius,
            Entry[] newEntries)
        {
            acceptedItemTypeId = newAcceptedItemTypeId;
            activationCenter = newActivationCenter;
            activationRadius = newActivationRadius;
            entries = newEntries ?? Array.Empty<Entry>();
            lastRuntimeFailure = null;
        }

        bool TryBuildInstallPlan(out List<InstallPlan> plans, out string issue)
        {
            plans = new List<InstallPlan>();
            issue = null;

            if (string.IsNullOrWhiteSpace(acceptedItemTypeId))
            {
                issue = "Accepted Item Type Id is empty.";
                return false;
            }

            if (entries == null || entries.Length == 0)
            {
                issue = "At least one Entry is required.";
                return false;
            }

            var uniqueSlots = new HashSet<GhostObjectSnapSlot>();
            for (int i = 0; i < entries.Length; ++i)
            {
                Entry entry = entries[i];
                if (entry == null)
                {
                    issue = $"Entry {i} is null.";
                    return false;
                }

                GhostObjectSnapSlot slot = entry.Slot;
                if (slot == null)
                {
                    issue = $"Entry {i} has no Ghost Object Snap Slot.";
                    return false;
                }

                if (!uniqueSlots.Add(slot))
                {
                    issue = $"Slot '{slot.name}' is assigned more than once.";
                    return false;
                }

                if (!slot.IsAvailable)
                    continue;

                if (!slot.isActiveAndEnabled)
                {
                    issue = $"Empty slot '{slot.name}' is not active and enabled.";
                    return false;
                }

                GameObject prefab = entry.InstalledPrefab;
                if (prefab == null)
                {
                    issue = $"Empty slot '{slot.name}' has no Installed Prefab or template.";
                    return false;
                }

                if (!ValidateTemplate(prefab, slot, out issue))
                    return false;

                plans.Add(new InstallPlan(slot, prefab));
            }

            return true;
        }

        static bool ValidateTemplate(
            GameObject prefab,
            GhostObjectSnapSlot slot,
            out string issue)
        {
            GhostSnappableObject[] snappables =
                prefab.GetComponentsInChildren<GhostSnappableObject>(true);
            if (snappables.Length != 1)
            {
                issue = $"'{prefab.name}' must contain exactly one GhostSnappableObject.";
                return false;
            }

            GhostSnappableObject snappable = snappables[0];
            if (!slot.IsCompatible(snappable.CompatibilityId))
            {
                issue = $"'{prefab.name}' compatibility ID does not match slot '{slot.name}'.";
                return false;
            }

            bool hasGrab = snappable.GrabInteractable != null ||
                           snappable.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>() != null;
            bool hasBody = snappable.TargetRigidbody != null || snappable.GetComponent<Rigidbody>() != null;
            if (!hasGrab || !hasBody)
            {
                issue = $"'{prefab.name}' needs the existing XRGrabInteractable and Rigidbody required by GhostSnappableObject.";
                return false;
            }

            issue = null;
            return true;
        }

        bool RollBackFailure(List<SpawnRecord> spawned, string issue)
        {
            RollBackSpawned(spawned);
            LogRuntimeFailureOnce(issue);
            return false;
        }

        static void RollBackSpawned(List<SpawnRecord> spawned)
        {
            for (int i = spawned.Count - 1; i >= 0; --i)
            {
                SpawnRecord record = spawned[i];
                if (record.Snappable != null && record.Snappable.CurrentSlot != null)
                    record.Snappable.ReleaseFromSlot();

                if (record.Root == null)
                    continue;

                record.Root.SetActive(false);
                Destroy(record.Root);
            }
        }

        void LogRuntimeFailureOnce(string issue)
        {
            if (string.IsNullOrEmpty(issue) || string.Equals(lastRuntimeFailure, issue, StringComparison.Ordinal))
                return;

            lastRuntimeFailure = issue;
            Debug.LogWarning($"[Ghost Auto Fill] {name}: {issue} No slots were kept from this attempt.", this);
        }

        bool TryGetInspectorConfigurationIssue(out string issue)
        {
            if (string.IsNullOrWhiteSpace(acceptedItemTypeId))
            {
                issue = "Accepted Item Type Id is empty.";
                return true;
            }

            if (entries == null || entries.Length == 0)
            {
                issue = "At least one Entry is required.";
                return true;
            }

            var uniqueSlots = new HashSet<GhostObjectSnapSlot>();
            for (int i = 0; i < entries.Length; ++i)
            {
                Entry entry = entries[i];
                if (entry == null)
                {
                    issue = $"Entry {i} is null.";
                    return true;
                }

                if (entry.Slot == null)
                {
                    issue = $"Entry {i} has no Ghost Object Snap Slot.";
                    return true;
                }

                if (!uniqueSlots.Add(entry.Slot))
                {
                    issue = $"Slot '{entry.Slot.name}' is assigned more than once.";
                    return true;
                }

                if (entry.InstalledPrefab == null)
                {
                    issue = $"Entry for '{entry.Slot.name}' has no Installed Prefab or template.";
                    return true;
                }
            }

            issue = null;
            return false;
        }
    }
}
