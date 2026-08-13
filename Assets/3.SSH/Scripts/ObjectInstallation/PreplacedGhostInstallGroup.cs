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

        [Tooltip("Maximum distance from the released supply to the nearest preplaced item. This prevents a large group activation sphere from accepting supplies far from every actual install location.")]
        [SerializeField, Min(0f)] float installRadius = 0.5f;

        [Tooltip("Optional exact release zone. When assigned, the supply must be inside this Collider instead of using nearest-item distance.")]
        [SerializeField] Collider installZone;

        [Header("Absorption")]
        [Tooltip("This completed group's share of total puddle absorption. Set Sock to 0.25 and Pad to 0.75.")]
        [SerializeField, Range(0f, 1f)] float absorptionWeight = 0.5f;

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
        public float InstallRadius => Mathf.Max(0f, installRadius);
        public Collider InstallZone => installZone;
        public float AbsorptionWeight => Mathf.Clamp01(absorptionWeight);
        public PreplacedGhostInstallItem[] Items => items;
        public bool IsInstalled => isInstalled;
        public bool IsProcessing => processing;

        /// <summary>
        /// Raised after every item in this group has entered its installed state successfully.
        /// Observer failures are isolated from the installation transaction.
        /// </summary>
        public event Action<PreplacedGhostInstallGroup> Installed;

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
            installRadius = Mathf.Max(0f, installRadius);
            absorptionWeight = Mathf.Clamp01(absorptionWeight);
            lastFailure = null;
        }

        void OnDrawGizmosSelected()
        {
            Transform center = ActivationCenter;
            if (center == null)
                return;

            Gizmos.color = new Color(0.15f, 0.85f, 1f, 0.85f);
            Gizmos.DrawWireSphere(center.position, ActivationRadius);

            if (installZone != null || items == null)
                return;

            Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.85f);
            for (int i = 0; i < items.Length; ++i)
            {
                if (items[i] != null)
                    Gizmos.DrawWireSphere(items[i].transform.position, InstallRadius);
            }
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

        public void ConfigureRuntime(
            string newAcceptedItemTypeId,
            Transform newActivationCenter,
            float newInstallRadius,
            float newAbsorptionWeight,
            PreplacedGhostInstallItem[] newItems)
        {
            Configure(newAcceptedItemTypeId, newActivationCenter, newInstallRadius, newItems);
            installRadius = Mathf.Max(0f, newInstallRadius);
            installZone = null;
            absorptionWeight = Mathf.Clamp01(newAbsorptionWeight);
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

                if (!group.TryGetInstallDistanceSqr(releaseWorldPosition, out float installSqrDistance) ||
                    installSqrDistance >= nearestSqrDistance)
                    continue;

                nearest = group;
                nearestSqrDistance = installSqrDistance;
            }

            return nearest;
        }

        public bool TryGetNearestInstallPose(Vector3 worldPosition, out Transform nearestPose)
        {
            nearestPose = null;
            float nearestSqrDistance = float.PositiveInfinity;
            if (!IsFinite(worldPosition) || items == null)
                return false;

            for (int i = 0; i < items.Length; ++i)
            {
                PreplacedGhostInstallItem item = items[i];
                if (item == null)
                    continue;

                float candidate = (item.transform.position - worldPosition).sqrMagnitude;
                if (candidate < nearestSqrDistance)
                {
                    nearestSqrDistance = candidate;
                    nearestPose = item.transform;
                }
            }

            float radius = InstallRadius;
            return nearestPose != null && nearestSqrDistance <= radius * radius;
        }

        bool TryGetInstallDistanceSqr(Vector3 releaseWorldPosition, out float sqrDistance)
        {
            sqrDistance = float.PositiveInfinity;
            if (!IsFinite(releaseWorldPosition))
                return false;

            if (installZone != null)
            {
                if (!installZone.enabled || !installZone.gameObject.activeInHierarchy)
                    return false;

                Vector3 closestPoint = installZone.ClosestPoint(releaseWorldPosition);
                if (!IsFinite(closestPoint))
                    return false;

                sqrDistance = (closestPoint - releaseWorldPosition).sqrMagnitude;
                return sqrDistance <= 0.000001f;
            }

            if (items == null || items.Length == 0)
                return false;

            for (int i = 0; i < items.Length; ++i)
            {
                PreplacedGhostInstallItem item = items[i];
                if (item == null)
                    continue;

                float candidate = (item.transform.position - releaseWorldPosition).sqrMagnitude;
                if (candidate < sqrDistance)
                    sqrDistance = candidate;
            }

            float radius = InstallRadius;
            return float.IsFinite(sqrDistance) && sqrDistance <= radius * radius;
        }

        static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

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
            bool succeeded = false;

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
                succeeded = isInstalled;
            }
            catch (Exception exception)
            {
                for (int i = changedItems.Count - 1; i >= 0; --i)
                    changedItems[i].ApplyInitialGhostState();

                isInstalled = false;
                installedCount = 0;
                LogFailureOnce($"Installation was rolled back: {exception.Message}");
            }
            finally
            {
                processing = false;
            }

            if (succeeded)
                NotifyInstalledObservers();

            return succeeded;
        }

        void NotifyInstalledObservers()
        {
            Delegate[] observers = Installed?.GetInvocationList();
            if (observers == null)
                return;

            for (int i = 0; i < observers.Length; ++i)
            {
                try
                {
                    ((Action<PreplacedGhostInstallGroup>)observers[i]).Invoke(this);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
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
