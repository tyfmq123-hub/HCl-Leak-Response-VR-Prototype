using System.Collections;
using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRProject.LeakResponse.Logic;

namespace VRProject.LeakResponse.Narration
{
    [DisallowMultipleComponent]
    public sealed class P102WasteContainerRuntime : MonoBehaviour
    {
        const int RequiredDepositCount = 7;

        readonly UniqueDepositGate depositGate = new UniqueDepositGate(RequiredDepositCount);

        GameObject containerRoot;
        GameObject bodyObject;
        GameObject lidObject;
        Transform sideGhost;
        Transform closedGhost;
        XRGrabInteractable lidGrab;
        Rigidbody lidRigidbody;
        P102LeakNarrationController narration;
        HClAbsorptionVisualController absorptionController;
        Vector3 closedPosition;
        Quaternion closedRotation;
        Vector3 designatedContainerPosition;
        Quaternion designatedContainerRotation;
        Transform originalLidParent;
        Bounds depositBounds;
        bool hasDepositBounds;
        bool lidStowEnabled;
        bool finalCloseEnabled;
        bool lidStowed;
        bool snappingLid;
        int depositedPads;
        int visibleDepositedCount;

        public event Action<P102RecoveryItemState> ItemDeposited;

        public int DepositedCount => depositGate.DepositedCount;
        public int VisibleDepositedCount => visibleDepositedCount;
        public bool IsAtDesignatedPosition =>
            containerRoot != null &&
            containerRoot.activeInHierarchy &&
            Vector3.Distance(containerRoot.transform.position, designatedContainerPosition) <= 0.02f &&
            Quaternion.Angle(containerRoot.transform.rotation, designatedContainerRotation) <= 2f;
        public bool CanFinalLock =>
            depositGate.CanFinalLock &&
            visibleDepositedCount >= RequiredDepositCount &&
            IsAtDesignatedPosition &&
            absorptionController != null &&
            absorptionController.IsFinalCleanupComplete;
        public bool IsLockedClosed => depositGate.IsLocked;
        public bool IsConfigured => containerRoot != null && bodyObject != null && lidObject != null;

        public bool Configure(GameObject newContainerRoot, P102LeakNarrationController narrationController,
            HClAbsorptionVisualController absorption)
        {
            Unsubscribe();
            containerRoot = newContainerRoot;
            narration = narrationController;
            absorptionController = absorption;
            if (containerRoot == null)
            {
                Debug.LogError("[P102 Flow] Waste Container is missing. Deposit and 7/7 completion remain blocked.", this);
                return false;
            }

            containerRoot.SetActive(true);
            designatedContainerPosition = containerRoot.transform.position;
            designatedContainerRotation = containerRoot.transform.rotation;
            depositedPads = 0;
            visibleDepositedCount = 0;

            bodyObject = FindDescendant(containerRoot, "HazWasteContainer_Body");
            lidObject = FindDescendant(containerRoot, "HazWasteContainer_Lid");
            GameObject sideGhostObject = P102RuntimeUtility.FindSceneObject("HazWasteContainer_Lid_ObjectGhost");
            sideGhost = sideGhostObject != null ? sideGhostObject.transform : null;

            if (bodyObject == null || lidObject == null)
            {
                Debug.LogError("[P102 Flow] Waste Container body or separable lid is missing. The step remains incomplete.", this);
                return false;
            }

            originalLidParent = lidObject.transform.parent;
            closedPosition = lidObject.transform.position;
            closedRotation = lidObject.transform.rotation;

            if (sideGhost == null)
            {
                GameObject existingSideGhost =
                    P102RuntimeUtility.FindSceneObject("HazWasteContainer_Lid_ObjectGhost_Runtime");
                sideGhost = existingSideGhost != null ? existingSideGhost.transform : CreateSideGhost();
            }
            GameObject existingClosedGhost =
                P102RuntimeUtility.FindSceneObject("HazWasteContainer_Lid_ClosedGhost_Runtime");
            closedGhost = existingClosedGhost != null
                ? existingClosedGhost.transform
                : P102RuntimeUtility.CreateRuntimeGhost(
                    lidObject, "HazWasteContainer_Lid_ClosedGhost_Runtime", closedPosition, closedRotation);
            SetGhostVisible(sideGhost, false);
            SetGhostVisible(closedGhost, false);

            lidRigidbody = P102RuntimeUtility.GetOrAdd<Rigidbody>(lidObject);
            P102RuntimeUtility.EnsureBoundsCollider(lidObject, false);
            lidGrab = P102RuntimeUtility.GetOrAdd<XRGrabInteractable>(lidObject);
            lidGrab.colliders.Clear();
            Collider[] lidColliders = lidObject.GetComponents<Collider>();
            for (int i = 0; i < lidColliders.Length; ++i)
                lidGrab.colliders.Add(lidColliders[i]);
            lidGrab.selectExited.AddListener(OnLidSelectExited);
            lidGrab.enabled = false;

            hasDepositBounds = P102RuntimeUtility.TryGetRendererBounds(bodyObject, out depositBounds);
            if (!hasDepositBounds)
                Debug.LogError("[P102 Flow] Waste Container body bounds are unavailable. Deposits remain blocked.", this);
            if (sideGhost == null)
                Debug.LogError("[P102 Flow] A lid stow ghost could not be resolved or created. Lid stow remains blocked.", this);
            if (absorptionController == null)
                Debug.LogError("[P102 Flow] HClAbsorptionVisualController is missing. Final container lock remains blocked.", this);

            if (narration != null)
            {
                narration.CueStarted += OnCueStarted;
                narration.CueCompleted += OnCueCompleted;
            }
            return IsConfigured;
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        public bool IsWithinDepositZone(Vector3 worldPosition)
        {
            if (!hasDepositBounds)
                return false;

            Bounds expanded = depositBounds;
            expanded.Expand(new Vector3(0.08f, 0.15f, 0.08f));
            return expanded.Contains(worldPosition);
        }

        public bool TryDeposit(P102RecoveryItemState item)
        {
            if (item == null || !item.CanBeDeposited ||
                !IsWithinDepositZone(item.transform.position) || !HasVisibleRenderer(item))
                return false;

            int uniqueId = item.GetInstanceID();
            if (!depositGate.TryDeposit(uniqueId) || !item.MarkDeposited())
                return false;

            if (item.Kind == P102AbsorbentKind.Pad)
                ++depositedPads;
            PlaceInStack(item, depositGate.DepositedCount - 1);
            ItemDeposited?.Invoke(item);
            Debug.Log($"[P102 Flow] Waste deposited {depositGate.DepositedCount}/{RequiredDepositCount}", item);

            if (depositedPads == 3)
                narration?.EnqueueOnce(P102LeakNarrationCue.AllPadsRecovered);
            if (CanFinalLock)
            {
                narration?.EnqueueOnce(P102LeakNarrationCue.AllSocksRecovered);
                Debug.Log("[P102 Flow] Waste deposited 7/7", this);
            }

            return true;
        }

        void OnCueStarted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.ContainerAndLidGhostActivated)
            {
                lidStowEnabled = true;
                SetGhostVisible(sideGhost, true);
                if (lidGrab != null)
                    lidGrab.enabled = true;
            }
            else if (cue == P102LeakNarrationCue.ContainerClosureGhostActivated)
            {
                finalCloseEnabled = CanFinalLock;
                SetGhostVisible(closedGhost, finalCloseEnabled);
                if (lidGrab != null)
                    lidGrab.enabled = finalCloseEnabled;
                if (!finalCloseEnabled)
                {
                    Debug.LogError(
                        $"[P102 Flow] Container closure remains blocked: deposited={DepositedCount}/7, " +
                        $"visible={VisibleDepositedCount}/7, designatedPosition={IsAtDesignatedPosition}, " +
                        $"leakCleanup={(absorptionController != null && absorptionController.IsFinalCleanupComplete)}.",
                        this);
                }
            }
        }

        void OnCueCompleted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.AllPadsRecovered)
                narration.EnqueueOnce(P102LeakNarrationCue.SockRecoveryStarted);
        }

        void OnLidSelectExited(SelectExitEventArgs args)
        {
            if (args.isCanceled || snappingLid || lidObject == null || depositGate.IsLocked)
                return;

            if (!lidStowed && lidStowEnabled && sideGhost != null &&
                Vector3.Distance(lidObject.transform.position, sideGhost.position) <= 0.35f)
            {
                StartCoroutine(SnapLid(sideGhost.position, sideGhost.rotation, false));
                return;
            }

            if (lidStowed && finalCloseEnabled && CanFinalLock &&
                Vector3.Distance(lidObject.transform.position, closedPosition) <= 0.35f)
            {
                StartCoroutine(SnapLid(closedPosition, closedRotation, true));
            }
        }

        IEnumerator SnapLid(Vector3 targetPosition, Quaternion targetRotation, bool finalLock)
        {
            snappingLid = true;
            lidRigidbody.linearVelocity = Vector3.zero;
            lidRigidbody.angularVelocity = Vector3.zero;
            lidRigidbody.useGravity = false;
            lidRigidbody.isKinematic = true;
            Vector3 startPosition = lidObject.transform.position;
            Quaternion startRotation = lidObject.transform.rotation;
            const float duration = 0.22f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                lidObject.transform.SetPositionAndRotation(
                    Vector3.LerpUnclamped(startPosition, targetPosition, eased),
                    Quaternion.SlerpUnclamped(startRotation, targetRotation, eased));
                yield return null;
            }

            lidObject.transform.SetPositionAndRotation(targetPosition, targetRotation);
            if (finalLock)
            {
                if (depositGate.TryLock())
                {
                    SetGhostVisible(closedGhost, false);
                    lidObject.transform.SetParent(originalLidParent, true);
                    lidGrab.enabled = false;
                    narration?.EnqueueOnce(P102LeakNarrationCue.ContainerClosedTaskCompleted);
                    Debug.Log("[P102 Flow] Waste lid locked", this);
                }
            }
            else
            {
                lidStowed = true;
                SetGhostVisible(sideGhost, false);
                lidGrab.enabled = false;
                narration?.EnqueueOnce(P102LeakNarrationCue.ContainerLidStowed);
            }

            snappingLid = false;
        }

        void PlaceInStack(P102RecoveryItemState item, int index)
        {
            Transform itemTransform = item.transform;
            Rigidbody body = item.GetComponent<Rigidbody>();
            XRGrabInteractable grab = item.GetComponent<XRGrabInteractable>();
            if (grab != null)
                grab.enabled = false;
            Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
                colliders[i].enabled = false;
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }

            int column = index % 3;
            int level = index / 3;
            float x = Mathf.Lerp(depositBounds.min.x + depositBounds.size.x * 0.25f,
                depositBounds.max.x - depositBounds.size.x * 0.25f, column / 2f);
            float y = depositBounds.min.y + depositBounds.size.y * (0.2f + level * 0.23f);
            float z = depositBounds.center.z;
            itemTransform.SetParent(bodyObject.transform, true);
            itemTransform.position = new Vector3(x, y, z);
            itemTransform.rotation = Quaternion.Euler(0f, index * 37f, 0f);
            item.gameObject.SetActive(true);
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
                renderers[i].enabled = true;
            ++visibleDepositedCount;
        }

        static bool HasVisibleRenderer(P102RecoveryItemState item)
        {
            if (item == null)
                return false;

            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i] != null)
                    return true;
            }

            Debug.LogError($"[P102 Flow] Contaminated item '{item.name}' has no Renderer and cannot enter the visible 1-7 stack.", item);
            return false;
        }

        void Unsubscribe()
        {
            if (lidGrab != null)
                lidGrab.selectExited.RemoveListener(OnLidSelectExited);
            if (narration != null)
            {
                narration.CueStarted -= OnCueStarted;
                narration.CueCompleted -= OnCueCompleted;
            }
        }

        Transform CreateSideGhost()
        {
            if (!P102RuntimeUtility.TryGetRendererBounds(bodyObject, out Bounds bodyBounds) ||
                !P102RuntimeUtility.TryGetRendererBounds(lidObject, out Bounds lidBounds))
                return null;

            Vector3 sideDirection = containerRoot.transform.right;
            float offset = Mathf.Max(bodyBounds.extents.x, bodyBounds.extents.z) +
                           Mathf.Max(lidBounds.extents.x, lidBounds.extents.z) + 0.18f;
            Vector3 position = bodyBounds.center + sideDirection.normalized * offset;
            position.y = closedPosition.y;
            return P102RuntimeUtility.CreateRuntimeGhost(
                lidObject, "HazWasteContainer_Lid_ObjectGhost_Runtime", position, closedRotation);
        }

        static void SetGhostVisible(Transform ghost, bool visible)
        {
            if (ghost != null)
                ghost.gameObject.SetActive(visible);
        }

        static GameObject FindDescendant(GameObject root, string exactName)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; ++i)
            {
                if (string.Equals(children[i].name, exactName, System.StringComparison.Ordinal))
                    return children[i].gameObject;
            }
            return null;
        }
    }
}
