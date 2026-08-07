using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRProject.ObjectInstallation
{
    /// <summary>
    /// Installs every preplaced Ghost item in one Pad or Sock area exactly once.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PreplacedGhostInstallGroup : MonoBehaviour
    {
        static readonly List<PreplacedGhostInstallGroup> s_ActiveGroups =
            new List<PreplacedGhostInstallGroup>();

        [Header("Installation Type")]
        [Tooltip("Supply type accepted by this group, for example Pad or Sock.")]
        [SerializeField] string acceptedItemTypeId;

        [Header("Activation Area")]
        [Tooltip("Center of the supply release area. This Transform is used when omitted.")]
        [SerializeField] Transform activationCenter;

        [Tooltip("Maximum world-space release distance accepted by this group.")]
        [SerializeField, Min(0f)] float activationRadius = 0.5f;

        [Header("Preplaced Items")]
        [Tooltip("Every Scene-preplaced Ghost converted together by one matching supply.")]
        [SerializeField] PreplacedGhostInstallItem[] items =
            Array.Empty<PreplacedGhostInstallItem>();

        [Header("Runtime State (Read Only)")]
        [SerializeField] bool isInstalled;
        [SerializeField] bool processing;

        string lastFailure;

        public string AcceptedItemTypeId => acceptedItemTypeId;
        public Transform ActivationCenter => activationCenter != null ? activationCenter : transform;
        public float ActivationRadius => Mathf.Max(0f, activationRadius);
        public PreplacedGhostInstallItem[] Items => items;
        public bool IsInstalled => isInstalled;
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
            activationRadius = Mathf.Max(0f, activationRadius);
            lastFailure = null;
        }

        void OnDrawGizmosSelected()
        {
            Transform center = ActivationCenter;
            if (center == null)
                return;

            Gizmos.color = new Color(0.15f, 0.85f, 1f, 0.85f);
            Gizmos.DrawWireSphere(center.position, ActivationRadius);
        }

        public void Configure(
            string newAcceptedItemTypeId,
            Transform newActivationCenter,
            float newActivationRadius,
            PreplacedGhostInstallItem[] newItems)
        {
            acceptedItemTypeId = newAcceptedItemTypeId;
            activationCenter = newActivationCenter;
            activationRadius = Mathf.Max(0f, newActivationRadius);
            items = newItems ?? Array.Empty<PreplacedGhostInstallItem>();
            isInstalled = false;
            processing = false;
            lastFailure = null;
        }

        public static bool ItemTypeIdsMatch(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
                return false;

            return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static PreplacedGhostInstallGroup FindNearestCompatible(
            string itemTypeId,
            Vector3 releaseWorldPosition)
        {
            PreplacedGhostInstallGroup nearest = null;
            float nearestSqrDistance = float.PositiveInfinity;

            for (int i = s_ActiveGroups.Count - 1; i >= 0; --i)
            {
                PreplacedGhostInstallGroup group = s_ActiveGroups[i];
                if (group == null)
                {
                    s_ActiveGroups.RemoveAt(i);
                    continue;
                }

                if (!group.isActiveAndEnabled || group.isInstalled || group.processing ||
                    !ItemTypeIdsMatch(group.acceptedItemTypeId, itemTypeId))
                {
                    continue;
                }

                Transform center = group.ActivationCenter;
                if (center == null)
                    continue;

                float radius = group.ActivationRadius;
                float sqrDistance = (center.position - releaseWorldPosition).sqrMagnitude;
                if (sqrDistance > radius * radius || sqrDistance >= nearestSqrDistance)
                    continue;

                nearest = group;
                nearestSqrDistance = sqrDistance;
            }

            return nearest;
        }

        public bool TryInstallAll(string itemTypeId, out int installedCount)
        {
            installedCount = 0;
            if (processing || isInstalled || !isActiveAndEnabled ||
                !ItemTypeIdsMatch(acceptedItemTypeId, itemTypeId))
            {
                return false;
            }

            if (!TryValidateAllItems(out string issue))
            {
                LogFailureOnce(issue);
                return false;
            }

            processing = true;
            var changedItems = new List<PreplacedGhostInstallItem>(items.Length);

            try
            {
                for (int i = 0; i < items.Length; ++i)
                {
                    PreplacedGhostInstallItem item = items[i];
                    // Add before applying so even an unexpected mid-application exception rolls this item back.
                    changedItems.Add(item);
                    if (!item.TryInstall())
                        throw new InvalidOperationException($"Item '{item.name}' could not enter its installed state.");
                }

                installedCount = changedItems.Count;
                isInstalled = installedCount == items.Length;
                lastFailure = null;
                return isInstalled;
            }
            catch (Exception exception)
            {
                for (int i = changedItems.Count - 1; i >= 0; --i)
                    changedItems[i].ApplyInitialGhostState();

                isInstalled = false;
                installedCount = 0;
                LogFailureOnce($"Installation was rolled back: {exception.Message}");
                return false;
            }
            finally
            {
                processing = false;
            }
        }

        bool TryValidateAllItems(out string issue)
        {
            if (string.IsNullOrWhiteSpace(acceptedItemTypeId))
                return Fail("Accepted Item Type Id is empty.", out issue);

            if (items == null || items.Length == 0)
                return Fail("At least one preplaced Item is required.", out issue);

            var uniqueItems = new HashSet<PreplacedGhostInstallItem>();
            for (int i = 0; i < items.Length; ++i)
            {
                PreplacedGhostInstallItem item = items[i];
                if (item == null)
                    return Fail($"Item entry {i} is missing.", out issue);

                if (!uniqueItems.Add(item))
                    return Fail($"Item '{item.name}' is registered more than once.", out issue);

                if (!item.gameObject.activeInHierarchy)
                    return Fail($"Item '{item.name}' is not active in the Scene.", out issue);

                if (!ItemTypeIdsMatch(acceptedItemTypeId, item.InstallationTypeId))
                    return Fail($"Item '{item.name}' has type '{item.InstallationTypeId}', expected '{acceptedItemTypeId}'.", out issue);

                if (item.IsInstalled)
                    return Fail($"Item '{item.name}' is already installed while its group is not.", out issue);

                if (!item.TryValidateConfiguration(out string itemIssue))
                    return Fail($"Item '{item.name}' is invalid: {itemIssue}", out issue);
            }

            issue = null;
            return true;
        }

        void LogFailureOnce(string issue)
        {
            if (string.IsNullOrEmpty(issue) || string.Equals(lastFailure, issue, StringComparison.Ordinal))
                return;

            lastFailure = issue;
            Debug.LogError($"[Preplaced Ghost Install] {name}: {issue} No item was kept installed and the supply was not consumed.", this);
        }

        static bool Fail(string message, out string issue)
        {
            issue = message;
            return false;
        }
    }
}
