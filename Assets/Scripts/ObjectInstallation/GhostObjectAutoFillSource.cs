using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRProject.ObjectInstallation
{
    /// <summary>
    /// Marks a grabbed object as a supply bundle that can activate the nearest compatible auto-fill group.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostObjectAutoFillSource : MonoBehaviour
    {
        [Header("Supply Type")]
        [Tooltip("Installation type supplied by this object, for example Pad or Sock. Comparison ignores case and outer spaces.")]
        [SerializeField] string itemTypeId;

        [Header("Existing Object Reference")]
        [Tooltip("Existing XR Grab Interactable whose final Select Exit triggers one auto-fill attempt.")]
        [SerializeField] XRGrabInteractable grabInteractable;

        [Header("Consumption")]
        [Tooltip("When enabled, this supply GameObject is set inactive only after every planned slot was installed successfully.")]
        [SerializeField] bool consumeSourceOnSuccess = true;

        XRGrabInteractable subscribedGrabInteractable;
        Coroutine pendingReleaseRoutine;
        bool processingRelease;
        bool warnedAboutSnappableConflict;

        public string ItemTypeId => itemTypeId;
        public XRGrabInteractable GrabInteractable => grabInteractable;
        public bool ConsumeSourceOnSuccess => consumeSourceOnSuccess;
        public bool IsProcessingRelease => processingRelease;

        void Reset()
        {
            TryGetComponent(out grabInteractable);
        }

        void Awake()
        {
            ResolveGrabInteractable();
        }

        void OnEnable()
        {
            ResolveGrabInteractable();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            if (pendingReleaseRoutine != null)
            {
                StopCoroutine(pendingReleaseRoutine);
                pendingReleaseRoutine = null;
            }

            processingRelease = false;
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(itemTypeId))
                Debug.LogWarning($"[Ghost Auto Fill] {name}: Item Type Id is empty.", this);

            if (grabInteractable == null)
                Debug.LogWarning($"[Ghost Auto Fill] {name}: XR Grab Interactable is not assigned.", this);

            GhostSnappableObject snappable = FindIndividualSnapComponent();
            if (snappable != null && snappable.enabled)
            {
                Debug.LogWarning(
                    $"[Ghost Auto Fill] {name}: a supply bundle must not also use an enabled GhostSnappableObject. " +
                    "Keep GhostObjectAutoFillSource on the supply and GhostSnappableObject on installed templates.",
                    this);
            }
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (args.isCanceled || processingRelease || pendingReleaseRoutine != null)
                return;

            processingRelease = true;
            pendingReleaseRoutine = StartCoroutine(ProcessReleaseAfterSelectionSettles());
        }

        IEnumerator ProcessReleaseAfterSelectionSettles()
        {
            // Let same-frame multi-hand transitions and the released Rigidbody pose settle first.
            yield return null;
            pendingReleaseRoutine = null;

            if (grabInteractable == null || grabInteractable.interactorsSelecting.Count != 0 ||
                string.IsNullOrWhiteSpace(itemTypeId) || HasActiveIndividualSnapComponent())
            {
                processingRelease = false;
                yield break;
            }

            Vector3 releasePosition = grabInteractable.transform.position;
            GhostObjectAutoFillGroup group =
                GhostObjectAutoFillGroup.FindNearestCompatible(itemTypeId, releasePosition);

            bool succeeded = group != null && group.TryAutoFill(itemTypeId, out int installedCount) &&
                             installedCount > 0;
            processingRelease = false;

            if (succeeded && consumeSourceOnSuccess)
                gameObject.SetActive(false);
        }

        void ResolveGrabInteractable()
        {
            if (grabInteractable == null)
                TryGetComponent(out grabInteractable);
        }

        void Subscribe()
        {
            if (subscribedGrabInteractable == grabInteractable || grabInteractable == null)
                return;

            Unsubscribe();
            subscribedGrabInteractable = grabInteractable;
            subscribedGrabInteractable.selectExited.AddListener(OnSelectExited);
        }

        void Unsubscribe()
        {
            if (subscribedGrabInteractable == null)
                return;

            subscribedGrabInteractable.selectExited.RemoveListener(OnSelectExited);
            subscribedGrabInteractable = null;
        }

        bool HasActiveIndividualSnapComponent()
        {
            GhostSnappableObject snappable = FindIndividualSnapComponent();
            if (snappable == null || !snappable.isActiveAndEnabled)
                return false;

            if (!warnedAboutSnappableConflict)
            {
                warnedAboutSnappableConflict = true;
                Debug.LogWarning(
                    $"[Ghost Auto Fill] {name}: auto-fill was skipped because this supply also has an active " +
                    "GhostSnappableObject. Remove or disable the individual snap component on the supply bundle.",
                    this);
            }

            return true;
        }

        GhostSnappableObject FindIndividualSnapComponent()
        {
            GhostSnappableObject snappable = GetComponent<GhostSnappableObject>();
            if (snappable == null && grabInteractable != null)
                snappable = grabInteractable.GetComponent<GhostSnappableObject>();

            return snappable;
        }
    }
}
