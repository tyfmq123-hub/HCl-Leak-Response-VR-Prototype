using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Closed/open visual states for the equipped wearable response bag under PlayerBodyAnchor.
/// Stable local poses only — no camera-follow, no head-tilt correction.
/// </summary>
[DisallowMultipleComponent]
public class WearableResponseBagVisualController : MonoBehaviour
{
    static readonly string SubtitleOpen = "Y\uB85C \uAC00\uBC29 UI \uC5F4\uAE30";
    static readonly string SubtitleClose = "Y\uB85C \uAC00\uBC29 UI \uB2EB\uAE30";

    [Header("Roots")]
    [SerializeField] GameObject wearableBagRoot;
    [SerializeField] GameObject bagClosedVisual;
    [SerializeField] GameObject bagOpenVisual;
    [SerializeField] Transform bagFrontCover;
    [SerializeField] GameObject bagContentsRoot;
    [SerializeField] XRSimpleInteractable bagOpenInteractable;
    [SerializeField] Collider bagOpenCollider;
    [SerializeField] PPEInteractableOutline bagOpenOutline;

    [Header("Shared UI")]
    [SerializeField] ResponseBagItemNameLabel sharedSubtitle;
    [SerializeField] ResponseBagContentHoverItem[] contentHoverItems;

    [Header("State")]
    [SerializeField] bool startClosed = true;
    [SerializeField] [Min(0.05f)] float transitionDuration = 0.25f;

    [Header("Front cover local poses")]
    [SerializeField] Vector3 closedCoverLocalPosition = new Vector3(0.001f, -0.149f, 0.209f);
    [SerializeField] Vector3 closedCoverLocalEuler = Vector3.zero;
    [SerializeField] Vector3 openCoverLocalPosition = new Vector3(0.001f, -0.26f, 0.08f);
    [SerializeField] Vector3 openCoverLocalEuler = new Vector3(62f, 0f, 0f);

    [Header("Contents pose when open (wearable local)")]
    [Tooltip("Lifts items toward the bag mouth so looking down from the HMD can see them.")]
    [SerializeField] Vector3 openContentsLocalPosition = new Vector3(0f, 0.16f, 0.12f);
    [SerializeField] Vector3 closedContentsLocalPosition = Vector3.zero;
    [SerializeField] Vector3 openContentsLocalEuler = new Vector3(-28f, 0f, 0f);
    [SerializeField] Vector3 closedContentsLocalEuler = Vector3.zero;

    ResponseEquipmentManager subscribedManager;
    Coroutine coverRoutine;
    bool isOpen;
    bool isRayHoveringBag;

    public bool IsBagOpen => isOpen;
    public bool IsRayHoveringBag => isRayHoveringBag;
    public PPEInteractableOutline BagOpenOutline => bagOpenOutline;
    public GameObject WearableBagRoot =>
        wearableBagRoot != null ? wearableBagRoot : gameObject;

    /// <summary>Post-equip wearable ray/Y interaction removed.</summary>
    public bool IsLookingAtWearableBag() => false;

    void Awake()
    {
        ResolveDefaults();
        // Do not wire BagOpenInteractable — post-equip gear UI removed.
        if (startClosed)
            ApplyClosedImmediate();
    }

    void OnEnable()
    {
        WireBagInteractable(false);
        DisableBagOpenInteractable();
        TrySubscribeManager(true);
    }

    void OnDisable()
    {
        WireBagInteractable(false);
        TrySubscribeManager(false);
        StopCoverRoutine();
    }

    void OnDestroy()
    {
        TrySubscribeManager(false);
        WireBagInteractable(false);
    }

    public void EditorWire(
        GameObject root,
        GameObject closedVisual,
        GameObject openVisual,
        Transform frontCover,
        GameObject contentsRoot,
        XRSimpleInteractable openInteractable,
        Collider openCollider,
        PPEInteractableOutline openOutline,
        ResponseBagItemNameLabel subtitle,
        ResponseBagContentHoverItem[] hoverItems,
        Vector3 closedCoverPos,
        Vector3 closedCoverEuler,
        Vector3 openCoverPos,
        Vector3 openCoverEuler)
    {
        wearableBagRoot = root != null ? root : gameObject;
        bagClosedVisual = closedVisual;
        bagOpenVisual = openVisual;
        bagFrontCover = frontCover;
        bagContentsRoot = contentsRoot;
        bagOpenInteractable = openInteractable;
        bagOpenCollider = openCollider;
        bagOpenOutline = openOutline;
        sharedSubtitle = subtitle;
        contentHoverItems = hoverItems;
        closedCoverLocalPosition = closedCoverPos;
        closedCoverLocalEuler = closedCoverEuler;
        openCoverLocalPosition = openCoverPos;
        openCoverLocalEuler = openCoverEuler;
        startClosed = true;
    }

    /// <summary>
    /// Post-equip wearable gear UI removed.
    /// </summary>
    public void ToggleGearUiFromY()
    {
        // no-op
    }

    /// <summary>Called when gear UI closes from close button / prepare / reset — bag mesh stays worn.</summary>
    public void NotifyGearUiClosed()
    {
        isOpen = false;
        KeepInteriorMeshesHidden();
        EnsureSharedMeshVisible();
        ApplyClosedVisualsActive();
        HideSubtitleImmediate();
    }

    public void OpenBag()
    {
        // Post-equip open UI removed.
    }

    public void CloseBag()
    {
        isOpen = false;
        SetContentInteractionsEnabled(false);
        ClearAllContentVisuals();
        KeepInteriorMeshesHidden();
        EnsureSharedMeshVisible();
        ApplyClosedVisualsActive();
        ResolveManager()?.HideGearPanel();
        HideSubtitleImmediate();
    }

    public void ToggleBag()
    {
        // Post-equip open UI removed.
    }

    public void ResetBagVisual()
    {
        StopCoverRoutine();
        isOpen = false;
        ApplyCoverPose(open: false);
        ApplyClosedVisualsActive();
        SetContentInteractionsEnabled(false);
        ClearAllContentVisuals();
        HideSubtitleImmediate();
        ResolveManager()?.HideGearPanel();
    }

    /// <summary>Called after wearable root is shown — starts closed without camera follow.</summary>
    public void InitializeClosedAfterEquip()
    {
        ResetBagVisual();
        DisableBagOpenInteractable();
    }

    bool CanOpen()
    {
        return false;
    }

    void DisableBagOpenInteractable()
    {
        WireBagInteractable(false);
        isRayHoveringBag = false;

        if (bagOpenCollider != null)
            bagOpenCollider.enabled = false;

        if (bagOpenOutline != null)
            bagOpenOutline.enabled = false;

        if (bagOpenInteractable != null)
        {
            bagOpenInteractable.enabled = false;
            if (bagOpenInteractable.gameObject.activeSelf)
                bagOpenInteractable.gameObject.SetActive(false);
        }

        if (bagOpenVisual != null && bagOpenVisual.activeSelf)
            bagOpenVisual.SetActive(false);

        KeepInteriorMeshesHidden();
        ApplyClosedVisualsActive();
        HideSubtitleImmediate();
    }

    void ApplyOpenVisualsActive()
    {
        if (bagClosedVisual != null)
            bagClosedVisual.SetActive(false);
        if (bagOpenVisual != null)
            bagOpenVisual.SetActive(true);
        KeepInteriorMeshesHidden();
        ApplyCoverPose(open: true);
    }

    void ApplyClosedVisualsActive()
    {
        KeepInteriorMeshesHidden();
        if (bagOpenVisual != null)
            bagOpenVisual.SetActive(false);
        if (bagClosedVisual != null)
            bagClosedVisual.SetActive(true);

        // BagSharedFrame / BagVisual hold the actual mesh — never leave them off when closed.
        EnsureSharedMeshVisible();
    }

    void KeepInteriorMeshesHidden()
    {
        if (bagContentsRoot != null)
            bagContentsRoot.SetActive(false);
    }

    void ApplyContentsOpenPose()
    {
        // Kept for Inspector tuning / future use — interior meshes stay hidden while UI is used.
    }

    void ApplyContentsClosedPose()
    {
        KeepInteriorMeshesHidden();
    }

    void EnsureSharedMeshVisible()
    {
        var root = WearableBagRoot != null ? WearableBagRoot.transform : transform;
        ActivateNamed(root, "BagSharedFrame");
        ActivateNamed(root, "BagVisual");

        var bagVisual = root.Find("BagVisual");
        if (bagVisual == null)
            bagVisual = root.Find("BagSharedFrame/BagVisual");
        if (bagVisual != null)
        {
            if (!bagVisual.gameObject.activeSelf)
                bagVisual.gameObject.SetActive(true);
            ActivateNamed(bagVisual, "Bag_Root");
        }
    }

    static void ActivateNamed(Transform parent, string childName)
    {
        if (parent == null)
            return;
        var child = parent.Find(childName);
        if (child != null && !child.gameObject.activeSelf)
            child.gameObject.SetActive(true);
    }

    void ApplyClosedImmediate()
    {
        isOpen = false;
        StopCoverRoutine();
        ApplyCoverPose(open: false);
        ApplyClosedVisualsActive();
        SetContentInteractionsEnabled(false);
        ClearAllContentVisuals();
        HideSubtitleImmediate();
        ResolveManager()?.HideGearPanel();
    }

    void ApplyCoverPose(bool open)
    {
        if (bagFrontCover == null)
            return;

        if (open)
        {
            bagFrontCover.localPosition = openCoverLocalPosition;
            bagFrontCover.localRotation = Quaternion.Euler(openCoverLocalEuler);
        }
        else
        {
            bagFrontCover.localPosition = closedCoverLocalPosition;
            bagFrontCover.localRotation = Quaternion.Euler(closedCoverLocalEuler);
        }
    }

    IEnumerator AnimateCover(
        Vector3 fromPos,
        Quaternion fromRot,
        Vector3 toPos,
        Quaternion toRot)
    {
        if (bagFrontCover == null)
            yield break;

        var duration = Mathf.Max(0.05f, transitionDuration);
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            bagFrontCover.localPosition = Vector3.Lerp(fromPos, toPos, u);
            bagFrontCover.localRotation = Quaternion.Slerp(fromRot, toRot, u);
            yield return null;
        }

        bagFrontCover.localPosition = toPos;
        bagFrontCover.localRotation = toRot;
        coverRoutine = null;
    }

    IEnumerator AnimateCoverThenClosedVisuals(
        Vector3 fromPos,
        Quaternion fromRot,
        Vector3 toPos,
        Quaternion toRot)
    {
        yield return AnimateCover(fromPos, fromRot, toPos, toRot);
        ApplyClosedVisualsActive();
        coverRoutine = null;
    }

    void StopCoverRoutine()
    {
        if (coverRoutine == null)
            return;
        StopCoroutine(coverRoutine);
        coverRoutine = null;
    }

    void SetContentInteractionsEnabled(bool enabled)
    {
        EnsureHoverItems();
        if (contentHoverItems == null)
            return;

        for (var i = 0; i < contentHoverItems.Length; i++)
        {
            var hover = contentHoverItems[i];
            if (hover == null)
                continue;

            hover.enabled = enabled;
            var col = hover.GetComponent<Collider>();
            if (col != null)
                col.enabled = enabled;

            var xr = hover.GetComponent<XRSimpleInteractable>();
            if (xr != null)
                xr.enabled = enabled;

            if (!enabled)
                hover.ClearVisuals();
        }
    }

    void ClearAllContentVisuals()
    {
        EnsureHoverItems();
        if (contentHoverItems == null)
            return;

        for (var i = 0; i < contentHoverItems.Length; i++)
            contentHoverItems[i]?.ClearVisuals();

        if (bagOpenOutline != null)
            bagOpenOutline.ForceClearOutline();
    }

    void HideSubtitleImmediate()
    {
        if (sharedSubtitle != null)
            sharedSubtitle.HideImmediate();
    }

    void WireBagInteractable(bool subscribe)
    {
        if (bagOpenInteractable == null)
            return;

        bagOpenInteractable.hoverEntered.RemoveListener(OnBagHoverEntered);
        bagOpenInteractable.hoverExited.RemoveListener(OnBagHoverExited);
        bagOpenInteractable.selectEntered.RemoveListener(OnBagSelectEntered);
        isRayHoveringBag = false;

        if (!subscribe)
            return;

        bagOpenInteractable.hoverEntered.AddListener(OnBagHoverEntered);
        bagOpenInteractable.hoverExited.AddListener(OnBagHoverExited);
        // Cancel accidental select/grip retention — open/close is Left Y while hovering.
        bagOpenInteractable.selectEntered.AddListener(OnBagSelectEntered);
    }

    void OnBagHoverEntered(HoverEnterEventArgs args)
    {
        if (!(args.interactorObject is XRRayInteractor))
            return;

        isRayHoveringBag = true;
        if (sharedSubtitle != null)
            sharedSubtitle.ShowSubtitle(isOpen ? SubtitleClose : SubtitleOpen);
    }

    void OnBagHoverExited(HoverExitEventArgs args)
    {
        if (!(args.interactorObject is XRRayInteractor))
            return;

        isRayHoveringBag = false;
        if (sharedSubtitle != null)
            sharedSubtitle.Hide();
    }

    void OnBagSelectEntered(SelectEnterEventArgs args)
    {
        if (!(args.interactorObject is XRRayInteractor))
            return;

        // Never grab / never toggle on Grip — Y while hovering owns open/close.
        var manager = bagOpenInteractable != null ? bagOpenInteractable.interactionManager : null;
        if (manager != null && bagOpenInteractable is IXRSelectInteractable selectable && selectable.isSelected)
            manager.CancelInteractableSelection(selectable);
    }

    void TrySubscribeManager(bool subscribe)
    {
        var manager = ResolveManager();
        if (manager == null)
            return;

        if (subscribedManager != null)
            subscribedManager.OnResponseBagPreparedChanged -= OnPreparedChanged;

        subscribedManager = null;
        if (!subscribe)
            return;

        subscribedManager = manager;
        subscribedManager.OnResponseBagPreparedChanged += OnPreparedChanged;
    }

    void OnPreparedChanged(bool prepared)
    {
        if (prepared)
        {
            // ShowWearable is owned by ResponseBagWearableVisual / manager;
            // once visible, lock to closed.
            InitializeClosedAfterEquip();
        }
        else
        {
            ResetBagVisual();
        }
    }

    ResponseEquipmentManager ResolveManager()
    {
        if (ResponseEquipmentManager.Instance != null)
            return ResponseEquipmentManager.Instance;
        return FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
    }

    void ResolveDefaults()
    {
        if (wearableBagRoot == null)
            wearableBagRoot = gameObject;

        if (bagOpenInteractable == null)
            bagOpenInteractable = GetComponentInChildren<XRSimpleInteractable>(true);

        if (bagOpenCollider == null && bagOpenInteractable != null)
            bagOpenCollider = bagOpenInteractable.GetComponent<Collider>();

        if (bagOpenOutline == null && bagOpenInteractable != null)
            bagOpenOutline = bagOpenInteractable.GetComponent<PPEInteractableOutline>();

        if (sharedSubtitle == null)
            sharedSubtitle = FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);

        EnsureHoverItems();
    }

    void EnsureHoverItems()
    {
        if (contentHoverItems != null && contentHoverItems.Length > 0)
            return;

        if (bagContentsRoot != null)
            contentHoverItems = bagContentsRoot.GetComponentsInChildren<ResponseBagContentHoverItem>(true);
        else
            contentHoverItems = GetComponentsInChildren<ResponseBagContentHoverItem>(true);
    }
}
