using UnityEngine;

/// <summary>
/// Visual-only control for the wearable response bag under PlayerBodyAnchor.
/// Shown/hidden by <see cref="ResponseEquipmentManager"/> when the world bag is prepared/reset.
/// Does not force-activate closed/open child mode roots — that belongs to
/// <see cref="WearableResponseBagVisualController"/>.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagWearableVisual : MonoBehaviour
{
    [Header("Visual Root")]
    [Tooltip("Defaults to this GameObject.")]
    [SerializeField] GameObject wearableRoot;

    [Header("Local Pose (manual fine-tune)")]
    [SerializeField] Vector3 wearableLocalPosition = Vector3.zero;
    [SerializeField] Vector3 wearableLocalEuler = new Vector3(0f, 90f, 8f);
    [SerializeField] Vector3 wearableLocalScale = new Vector3(0.85f, 0.85f, 0.85f);

    [Header("Start")]
    [SerializeField] bool startHidden = true;

    [Header("Open / closed visual")]
    [SerializeField] WearableResponseBagVisualController visualController;

    bool appliedPose;
    /// <summary>
    /// When true, Awake must not auto-hide — ShowWearable activated us for the first time.
    /// Inactive objects defer Awake until SetActive(true); without this flag Awake's
    /// startHidden HideWearable immediately undoes the show.
    /// </summary>
    bool suppressStartHidden;

    public GameObject WearableRoot => wearableRoot != null ? wearableRoot : gameObject;
    public bool IsVisible => WearableRoot.activeSelf;
    public WearableResponseBagVisualController VisualController => visualController;

    void Awake()
    {
        if (wearableRoot == null)
            wearableRoot = gameObject;

        if (visualController == null)
            visualController = GetComponent<WearableResponseBagVisualController>();
        if (visualController == null)
            visualController = GetComponentInChildren<WearableResponseBagVisualController>(true);

        ApplyLocalPose();

        // Scene starts with wearable inactive — Awake runs on first ShowWearable.
        // Do not hide when ShowWearable intentionally just activated us.
        if (startHidden && !suppressStartHidden)
            HideWearable();
    }

    void OnValidate()
    {
        if (wearableRoot == null)
            wearableRoot = gameObject;

        if (Application.isPlaying || appliedPose)
            ApplyLocalPose();
    }

    public void SetBagMeshRenderersEnabled(bool enabled)
    {
        EnsureRoot();
        if (WearableRoot == null)
            return;

        var renderers = WearableRoot.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;
            if (IsUnderOpenContents(renderers[i].transform))
                continue;
            renderers[i].enabled = enabled;
        }
    }

    public void ShowWearable()
    {
        // Legacy API retained for serialized references. The course now uses one
        // local bag per Stage, so a body-following bag must never be re-enabled.
        HideWearable();
    }

    void EnsureBaseMeshHierarchyActive()
    {
        ActivateChildIfPresent("BagSharedFrame");
        ActivateChildIfPresent("BagVisual");
        ActivateChildIfPresent("BagClosedVisual");

        var bagVisual = WearableRoot.transform.Find("BagVisual");
        if (bagVisual == null)
            bagVisual = WearableRoot.transform.Find("BagSharedFrame/BagVisual");
        if (bagVisual != null)
        {
            if (!bagVisual.gameObject.activeSelf)
                bagVisual.gameObject.SetActive(true);

            var bagRoot = bagVisual.Find("Bag_Root");
            if (bagRoot != null && !bagRoot.gameObject.activeSelf)
                bagRoot.gameObject.SetActive(true);
        }
    }

    void ActivateChildIfPresent(string childName)
    {
        var child = WearableRoot.transform.Find(childName);
        if (child != null && !child.gameObject.activeSelf)
            child.gameObject.SetActive(true);
    }

    static bool IsUnderOpenContents(Transform t)
    {
        while (t != null)
        {
            if (t.name == "BagContentsRoot" || t.name == "BagOpenVisual")
                return true;
            t = t.parent;
        }

        return false;
    }

    public void HideWearable()
    {
        EnsureRoot();
        if (visualController != null)
            visualController.ResetBagVisual();
        WearableRoot.SetActive(false);
    }

    public void ToggleWearable()
    {
        HideWearable();
    }

    public void ResetWearable()
    {
        ApplyLocalPose();
        if (visualController != null)
            visualController.ResetBagVisual();
        HideWearable();
    }

    public void EditorWire(GameObject root)
    {
        wearableRoot = root != null ? root : gameObject;
        ApplyLocalPose();
        if (startHidden)
            wearableRoot.SetActive(false);
    }

    public void EditorSetVisualController(WearableResponseBagVisualController controller)
    {
        visualController = controller;
    }

    void EnsureRoot()
    {
        if (wearableRoot == null)
            wearableRoot = gameObject;
    }

    void ApplyLocalPose()
    {
        EnsureRoot();
        var t = WearableRoot.transform;
        t.localPosition = wearableLocalPosition;
        t.localRotation = Quaternion.Euler(wearableLocalEuler);
        t.localScale = wearableLocalScale;
        appliedPose = true;
    }

#if UNITY_EDITOR
    [ContextMenu("Test Show Wearable")]
    void ContextShow() => ShowWearable();

    [ContextMenu("Test Hide Wearable")]
    void ContextHide() => HideWearable();

    [ContextMenu("Test Toggle Wearable")]
    void ContextToggle() => ToggleWearable();
#endif
}
