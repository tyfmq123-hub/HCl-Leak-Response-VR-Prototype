using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Stable bag-interior content binder. Keeps the three content models as fixed children
/// of the world bag — no camera follow, no relocate, no hide/show on gaze.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagContentsRevealController : MonoBehaviour
{
    const string ExactGasName = "ENV_HClDetector_01";
    const string ExactSockName = "INT_AbsorbentSock_Straight_01";
    // Prefer the real pad mesh only — never fall back to cabinet meshes.
    const string ExactPadPreferred = "INT_AbsorbentPad_01";
    const float MaxHoverColliderAxis = 0.45f;
    const string HangulFontResource = "PPE/PPE_HangulFont";

    [Header("Bag")]
    [SerializeField] GameObject worldResponseBagRoot;

    [Header("Content roots (Inspector — exact refs)")]
    [SerializeField] GameObject gasDetectorRoot;
    [SerializeField] GameObject absorbentSockRoot;
    [SerializeField] GameObject absorbentPadRoot;

    [Header("References")]
    [SerializeField] Camera xrCamera;
    [SerializeField] ResponseEquipmentManager equipmentManager;
    [SerializeField] ResponseBagItemNameLabel sharedNameLabel;
    [SerializeField] ResponseBagContentHoverItem[] hoverItems;

    [Header("Visibility")]
    [SerializeField] bool keepContentsVisible = false;

    [Header("World-fixed inspection (placed from the current HMD pose)")]
    [Tooltip("Placement offset calculated once when Y is pressed. The stage does not follow later head movement.")]
    [SerializeField] Vector3 inspectStageLocalPosition = new Vector3(0f, -0.08f, 1.15f);
    [Tooltip("Horizontal spacing between the three item centers (meters).")]
    [SerializeField] float inspectItemSpacing = 0.4f;
    [Tooltip("Uniform max world extent for each item (meters).")]
    [SerializeField] float inspectItemMaxExtent = 0.28f;
    // Gas: product-front pose. Sock/pad: upright, no heavy tilt.
    [SerializeField] Vector3 gasInspectEuler = new Vector3(-90f, -90f, 0f);
    [SerializeField] Vector3 sockInspectEuler = new Vector3(0f, 90f, 90f);
    [SerializeField] Vector3 padInspectEuler = new Vector3(0f, 0f, 0f);

    [Header("World name labels (under each item)")]
    [SerializeField] float nameLabelBelowItem = 0.2f;
    [SerializeField] float nameLabelWorldScale = 0.0009f;
    [SerializeField] Vector2 nameLabelCanvasSize = new Vector2(480f, 72f);
    [SerializeField] float nameLabelFontSize = 36f;

    bool runtimeConfigured;
    readonly List<GameObject> worldNameLabels = new List<GameObject>(3);
    Transform inspectStage;
    bool presentingInFrontOfCamera;
    PoseCache gasHome;
    PoseCache sockHome;
    PoseCache padHome;

    struct PoseCache
    {
        public bool valid;
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public bool wasActive;
    }

    public float MaxGazeDistance => 3f;
    public bool ContentsVisible =>
        presentingInFrontOfCamera
        || (gasDetectorRoot != null && gasDetectorRoot.activeInHierarchy);
    public GameObject WorldBagRoot =>
        worldResponseBagRoot != null ? worldResponseBagRoot : gameObject;

    public GameObject GasDetectorRoot => gasDetectorRoot;
    public GameObject AbsorbentSockRoot => absorbentSockRoot;
    public GameObject AbsorbentPadRoot => absorbentPadRoot;

    /// <summary>
    /// Y inspect: present the three content meshes in front of the HMD like PPE preview
    /// (similar size, even spacing, name labels underneath) — not inside the bag.
    /// </summary>
    public void PrepareContentsForInspection()
    {
        EnsureRuntimeConfigured(equipmentManager);
        PresentContentsInFrontOfCamera();
        RefitContentHoverColliders();
        EnsureWorldItemNameLabels();
        ShowWorldItemNameLabels(true);
    }

    /// <summary>Show/hide the world-space name plates under each content object.</summary>
    public void ShowWorldItemNameLabels(bool visible)
    {
        if (visible)
            EnsureWorldItemNameLabels();

        for (var i = 0; i < worldNameLabels.Count; i++)
        {
            var label = worldNameLabels[i];
            if (label == null)
                continue;
            if (label.activeSelf != visible)
                label.SetActive(visible);
        }

        if (!visible)
            ClearWorldNameLabels();
    }

    void EnsureWorldItemNameLabels()
    {
        ClearWorldNameLabels();
        if (inspectStage == null)
            return;

        EnsureWorldNameLabel(
            -inspectItemSpacing,
            "\uD734\uB300\uC6A9 HCl \uAC00\uC2A4\uCE21\uC815\uAE30");
        EnsureWorldNameLabel(
            0f,
            "\uCF00\uBBF8\uCEEC \uD761\uCC29\uC0AD");
        EnsureWorldNameLabel(
            inspectItemSpacing,
            "\uD654\uD559\uBB3C\uC9C8\uC6A9 \uD761\uCC29\uD328\uB4DC");
    }

    void ClearWorldNameLabels()
    {
        for (var i = 0; i < worldNameLabels.Count; i++)
        {
            if (worldNameLabels[i] != null)
                DestroyImmediate(worldNameLabels[i]);
        }

        worldNameLabels.Clear();

        if (inspectStage == null)
            return;

        for (var i = inspectStage.childCount - 1; i >= 0; i--)
        {
            var child = inspectStage.GetChild(i);
            if (child != null && child.name.StartsWith("ItemNameLabel", System.StringComparison.Ordinal))
                DestroyImmediate(child.gameObject);
        }
    }

    void EnsureWorldNameLabel(float localX, string koreanName)
    {
        if (inspectStage == null)
            return;

        var labelGo = new GameObject("ItemNameLabel");
        labelGo.transform.SetParent(inspectStage, false);
        labelGo.transform.localPosition = new Vector3(localX, -nameLabelBelowItem, 0f);
        labelGo.transform.localRotation = Quaternion.identity;
        labelGo.transform.localScale = Vector3.one * nameLabelWorldScale;

        var canvas = labelGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = xrCamera;

        var rt = labelGo.GetComponent<RectTransform>();
        rt.sizeDelta = nameLabelCanvasSize;
        labelGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(labelGo.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 0.88f);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(bg.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(12f, 8f);
        textRt.offsetMax = new Vector2(-12f, -8f);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = koreanName;
        tmp.fontSize = nameLabelFontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        var font = Resources.Load<TMP_FontAsset>(HangulFontResource);
        if (font != null)
            tmp.font = font;

        labelGo.AddComponent<ResponseBagWorldItemNameBillboard>();
        worldNameLabels.Add(labelGo);
    }

    static Bounds? GetLocalRendererBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds? bounds = null;
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled)
                continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                continue;

            // Convert world bounds corners into root-local space and encapsulate.
            var wb = r.bounds;
            var corners = new[]
            {
                wb.center + new Vector3(-wb.extents.x, -wb.extents.y, -wb.extents.z),
                wb.center + new Vector3(-wb.extents.x, -wb.extents.y, wb.extents.z),
                wb.center + new Vector3(-wb.extents.x, wb.extents.y, -wb.extents.z),
                wb.center + new Vector3(-wb.extents.x, wb.extents.y, wb.extents.z),
                wb.center + new Vector3(wb.extents.x, -wb.extents.y, -wb.extents.z),
                wb.center + new Vector3(wb.extents.x, -wb.extents.y, wb.extents.z),
                wb.center + new Vector3(wb.extents.x, wb.extents.y, -wb.extents.z),
                wb.center + new Vector3(wb.extents.x, wb.extents.y, wb.extents.z),
            };

            for (var c = 0; c < corners.Length; c++)
            {
                var local = root.InverseTransformPoint(corners[c]);
                if (!bounds.HasValue)
                    bounds = new Bounds(local, Vector3.zero);
                else
                {
                    var b = bounds.Value;
                    b.Encapsulate(local);
                    bounds = b;
                }
            }
        }

        return bounds;
    }

    /// <summary>
    /// Lift the three bag items into a camera-front row (PPE-style glance layout).
    /// </summary>
    public void ArrangeContentsForInspection()
    {
        PresentContentsInFrontOfCamera();
    }

    void PresentContentsInFrontOfCamera()
    {
        ResolveRefs();
        CacheHomePosesIfNeeded();
        EnsureInspectStage();
        if (inspectStage == null)
            return;

        // Left → center → right: gas / sock / pad
        PresentItem(gasDetectorRoot, -inspectItemSpacing, gasInspectEuler);
        PresentItem(absorbentSockRoot, 0f, sockInspectEuler);
        PresentItem(absorbentPadRoot, inspectItemSpacing, padInspectEuler);
        presentingInFrontOfCamera = true;
    }

    void EnsureInspectStage()
    {
        ResolveRefs();
        if (xrCamera == null)
            return;

        if (inspectStage == null)
        {
            var existingObject = GameObject.Find("ResponseBag_ContentsInspectStage");
            if (existingObject != null)
                inspectStage = existingObject.transform;
            else
                inspectStage = new GameObject("ResponseBag_ContentsInspectStage").transform;
        }

        var system = GameObject.Find("PPE_System");
        if (system != null && inspectStage.parent != system.transform)
            inspectStage.SetParent(system.transform, true);
        else if (system == null && inspectStage.parent != null)
            inspectStage.SetParent(null, true);

        var cameraTransform = xrCamera.transform;
        var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;
        var right = Vector3.Cross(Vector3.up, forward).normalized;
        var offset = inspectStageLocalPosition;
        inspectStage.position = cameraTransform.position
                                + right * offset.x
                                + Vector3.up * offset.y
                                + forward * offset.z;
        inspectStage.rotation = Quaternion.LookRotation(forward, Vector3.up);
        inspectStage.localScale = Vector3.one;
        if (!inspectStage.gameObject.activeSelf)
            inspectStage.gameObject.SetActive(true);
    }

    void PresentItem(GameObject root, float localX, Vector3 localEuler)
    {
        if (root == null || inspectStage == null)
            return;

        // Remove legacy labels parented under the mesh from older inspect builds.
        var legacyLabel = root.transform.Find("ItemNameLabel");
        if (legacyLabel != null)
            DestroyImmediate(legacyLabel.gameObject);

        var t = root.transform;
        t.SetParent(inspectStage, false);
        t.localRotation = Quaternion.Euler(localEuler);
        t.localScale = Vector3.one;
        t.localPosition = new Vector3(localX, 0f, 0f);

        if (!root.activeSelf)
            root.SetActive(true);

        FitUniformWorldExtent(t, inspectItemMaxExtent);
        CenterBoundsOnLocalPoint(t, new Vector3(localX, 0f, 0f));
    }

    static void FitUniformWorldExtent(Transform t, float targetMaxExtent)
    {
        if (t == null || targetMaxExtent < 0.01f)
            return;

        if (!TryGetWorldRendererBounds(t, out var bounds))
            return;

        var maxExtent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (maxExtent < 0.0001f)
            return;

        var factor = targetMaxExtent / maxExtent;
        t.localScale *= factor;
    }

    static void CenterBoundsOnLocalPoint(Transform t, Vector3 desiredLocalCenter)
    {
        if (t == null || t.parent == null)
            return;

        if (!TryGetWorldRendererBounds(t, out var bounds))
        {
            t.localPosition = desiredLocalCenter;
            return;
        }

        var parent = t.parent;
        var currentLocalCenter = parent.InverseTransformPoint(bounds.center);
        var delta = desiredLocalCenter - currentLocalCenter;
        t.localPosition += delta;
    }

    static bool TryGetWorldRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var has = false;
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled)
                continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                continue;

            if (!has)
            {
                bounds = r.bounds;
                has = true;
            }
            else
                bounds.Encapsulate(r.bounds);
        }

        return has;
    }

    void CacheHomePosesIfNeeded()
    {
        CachePose(ref gasHome, gasDetectorRoot);
        CachePose(ref sockHome, absorbentSockRoot);
        CachePose(ref padHome, absorbentPadRoot);
    }

    static void CachePose(ref PoseCache cache, GameObject root)
    {
        if (cache.valid || root == null)
            return;

        var t = root.transform;
        cache.valid = true;
        cache.parent = t.parent;
        cache.localPosition = t.localPosition;
        cache.localRotation = t.localRotation;
        cache.localScale = t.localScale;
        cache.wasActive = root.activeSelf;
    }

    void RestoreHomePoses()
    {
        RestorePose(ref gasHome, gasDetectorRoot);
        RestorePose(ref sockHome, absorbentSockRoot);
        RestorePose(ref padHome, absorbentPadRoot);
        presentingInFrontOfCamera = false;

        if (inspectStage != null && inspectStage.gameObject.activeSelf)
            inspectStage.gameObject.SetActive(false);
    }

    static void RestorePose(ref PoseCache cache, GameObject root)
    {
        if (!cache.valid || root == null)
            return;

        var t = root.transform;
        t.SetParent(cache.parent, false);
        t.localPosition = cache.localPosition;
        t.localRotation = cache.localRotation;
        t.localScale = cache.localScale;
        if (root.activeSelf != cache.wasActive)
            root.SetActive(cache.wasActive);
    }

    /// <summary>Hide interior props until Y inspection — restore shelf poses first.</summary>
    public void HideContentsUntilSelected()
    {
        ClearAllItemVisuals();
        ClearWorldNameLabels();
        RestoreHomePoses();
        SetContentsActive(false);
    }

    void FixGasDetectorFacing()
    {
        // Kept for older call sites; camera-front layout uses gasInspectEuler instead.
    }

    void Awake()
    {
        ResolveRefs();
        if (keepContentsVisible)
            SetContentsActive(true);
    }

    public void EditorWire(
        GameObject bagRoot,
        GameObject gas,
        GameObject sock,
        GameObject pad,
        Camera camera,
        ResponseEquipmentManager manager,
        ResponseBagItemNameLabel nameLabel,
        ResponseBagContentHoverItem[] items,
        float dwell,
        float hide)
    {
        worldResponseBagRoot = bagRoot;
        gasDetectorRoot = gas;
        absorbentSockRoot = sock;
        absorbentPadRoot = pad;
        xrCamera = camera;
        equipmentManager = manager;
        sharedNameLabel = nameLabel;
        hoverItems = items;
        keepContentsVisible = false;
        runtimeConfigured = true;
    }

    /// <summary>Wire exact children + hover + shared subtitle if not already set up.</summary>
    public void EnsureRuntimeConfigured(ResponseEquipmentManager manager)
    {
        if (runtimeConfigured && gasDetectorRoot != null && absorbentSockRoot != null && absorbentPadRoot != null)
        {
            if (!presentingInFrontOfCamera)
            {
                if (keepContentsVisible)
                    SetContentsActive(true);
                else
                    SetContentsActive(false);
            }
            return;
        }

        equipmentManager = manager != null ? manager : equipmentManager;
        if (worldResponseBagRoot == null)
            worldResponseBagRoot = gameObject;

        var bag = worldResponseBagRoot.transform;
        if (gasDetectorRoot == null)
        {
            var t = FindExact(bag, ExactGasName);
            if (t != null)
                gasDetectorRoot = t.gameObject;
        }

        if (absorbentSockRoot == null)
        {
            var t = FindExact(bag, ExactSockName);
            if (t != null)
                absorbentSockRoot = t.gameObject;
        }

        if (absorbentPadRoot == null)
        {
            var t = FindExact(bag, ExactPadPreferred);
            if (t != null)
                absorbentPadRoot = t.gameObject;
        }

        if (gasDetectorRoot == null || absorbentSockRoot == null || absorbentPadRoot == null)
        {
            Debug.LogWarning(
                "[Response Bag Contents] Missing content models. Expected: " +
                ExactGasName + ", " + ExactSockName + ", " + ExactPadPreferred,
                this);
            return;
        }

        ResolveRefs();
        if (sharedNameLabel == null)
            sharedNameLabel = CreateNameLabel();

        RestrictBagColliders(worldResponseBagRoot);

        var gasHover = EnsureHoverItem(
            gasDetectorRoot,
            ResponseEquipmentManager.IdGasDetector,
            "\uD734\uB300\uC6A9 HCl \uAC00\uC2A4\uCE21\uC815\uAE30");
        var sockHover = EnsureHoverItem(
            absorbentSockRoot,
            ResponseEquipmentManager.IdAbsorbentSock,
            "\uCF00\uBBF8\uCEEC \uD761\uCC29\uC0AD");
        var padHover = EnsureHoverItem(
            absorbentPadRoot,
            ResponseEquipmentManager.IdAbsorbentPad,
            "\uD654\uD559\uBB3C\uC9C8\uC6A9 \uD761\uCC29\uD328\uB4DC");

        hoverItems = new[] { gasHover, sockHover, padHover };
        runtimeConfigured = true;
        SetContentsActive(keepContentsVisible);
        Debug.Log("[Response Bag Contents] Stable interior contents ready.", this);
    }

    void RefitContentHoverColliders()
    {
        EnsureHoverItems();
        if (hoverItems == null)
            return;

        for (var i = 0; i < hoverItems.Length; i++)
        {
            var hover = hoverItems[i];
            if (hover == null)
                continue;

            var box = hover.GetComponent<BoxCollider>();
            if (box == null)
                continue;

            FitBox(box, hover.ContentRoot != null ? hover.ContentRoot : hover.transform);
        }
    }

    /// <summary>Legacy gaze API — ignored (bag stays put).</summary>
    public void NotifyGazeHit(bool hitThisFrame)
    {
    }

    public void NotifyContentHoverChanged()
    {
    }

    public void RevealContentsForInspectionPrompt()
    {
        SetContentsActive(true);
    }

    public void HideContentsImmediate()
    {
        ClearAllItemVisuals();
        ShowWorldItemNameLabels(false);
        RestoreHomePoses();
        SetContentsActive(false);
    }

    public void ResetContentsDisplay()
    {
        ClearAllItemVisuals();
        ShowWorldItemNameLabels(false);
        RestoreHomePoses();
        SetContentsActive(keepContentsVisible);
    }

    public bool CanAcceptGaze() => false;

    void SetContentsActive(bool active)
    {
        SetRootActive(gasDetectorRoot, active);
        SetRootActive(absorbentSockRoot, active);
        SetRootActive(absorbentPadRoot, active);
    }

    static void SetRootActive(GameObject root, bool active)
    {
        if (root == null)
            return;
        if (root.activeSelf != active)
            root.SetActive(active);
    }

    void ClearAllItemVisuals()
    {
        if (sharedNameLabel != null)
            sharedNameLabel.HideImmediate();

        EnsureHoverItems();
        if (hoverItems == null)
            return;

        for (var i = 0; i < hoverItems.Length; i++)
            hoverItems[i]?.ClearVisuals();
    }

    void EnsureHoverItems()
    {
        if (hoverItems != null && hoverItems.Length > 0)
            return;

        hoverItems = GetComponentsInChildren<ResponseBagContentHoverItem>(true);
    }

    void ResolveRefs()
    {
        if (worldResponseBagRoot == null)
            worldResponseBagRoot = gameObject;

        if (equipmentManager == null)
        {
            equipmentManager = ResponseEquipmentManager.Instance != null
                ? ResponseEquipmentManager.Instance
                : FindAnyObjectByType<ResponseEquipmentManager>();
        }

        if (sharedNameLabel == null)
            sharedNameLabel = FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);

        if (xrCamera == null)
        {
            var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null && origin.Camera != null)
                xrCamera = origin.Camera;
            else
                xrCamera = Camera.main;
        }

        EnsureHoverItems();
    }

    ResponseBagContentHoverItem EnsureHoverItem(GameObject root, string itemId, string koreanName)
    {
        var interactable = root.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
            interactable = root.AddComponent<XRSimpleInteractable>();

        var col = root.GetComponent<Collider>();
        if (col == null)
        {
            var box = root.AddComponent<BoxCollider>();
            FitBox(box, root.transform);
            col = box;
        }
        else if (col is BoxCollider existingBox)
        {
            // Re-clamp oversized leftovers (cabinet-sized ghost pads).
            var maxAxis = Mathf.Max(existingBox.bounds.size.x, existingBox.bounds.size.y, existingBox.bounds.size.z);
            if (maxAxis > MaxHoverColliderAxis)
                FitBox(existingBox, root.transform);
        }

        if (interactable.colliders.Count == 0 || !interactable.colliders.Contains(col))
        {
            interactable.colliders.Clear();
            interactable.colliders.Add(col);
        }

        var outline = root.GetComponent<PPEInteractableOutline>();
        if (outline == null)
            outline = root.AddComponent<PPEInteractableOutline>();
        outline.EditorSetHoverYellowRayOnly();

        var hover = root.GetComponent<ResponseBagContentHoverItem>();
        if (hover == null)
            hover = root.AddComponent<ResponseBagContentHoverItem>();

        hover.EditorWire(
            itemId,
            koreanName,
            root.transform,
            interactable,
            col,
            outline,
            equipmentManager,
            sharedNameLabel,
            this);
        return hover;
    }

    ResponseBagItemNameLabel CreateNameLabel()
    {
        var system = GameObject.Find("PPE_System");
        if (system == null)
            system = new GameObject("PPE_System");

        var canvasGo = new GameObject("ResponseBagItemNameCanvas");
        canvasGo.transform.SetParent(system.transform, false);
        canvasGo.transform.localScale = Vector3.one * 0.00115f;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = xrCamera;

        var rt = canvasGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(520f, 72f);
        canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvasGo.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.11f, 0.82f);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(bg.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(12f, 8f);
        textRt.offsetMax = new Vector2(-12f, -8f);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 32f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        var font = Resources.Load<TMP_FontAsset>(HangulFontResource);
        if (font != null)
            tmp.font = font;

        var label = canvasGo.AddComponent<ResponseBagItemNameLabel>();
        label.EditorWire(canvas, bgRt, tmp, xrCamera);
        canvasGo.SetActive(false);
        return label;
    }

    static void RestrictBagColliders(GameObject bag)
    {
        var interactable = bag.GetComponent<XRSimpleInteractable>();
        var bagCol = bag.GetComponent<Collider>();
        if (interactable == null || bagCol == null)
            return;

        interactable.colliders.Clear();
        interactable.colliders.Add(bagCol);
    }

    static void FitBox(BoxCollider box, Transform model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one * 0.12f;
            return;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] is MeshRenderer || renderers[i] is SkinnedMeshRenderer)
                bounds.Encapsulate(renderers[i].bounds);
        }

        // Reject enormous cabinet bounds — clamp so rays cannot ghost-hover across the room.
        var maxAxis = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxAxis > MaxHoverColliderAxis)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one * 0.14f;
            return;
        }

        var lossy = box.transform.lossyScale;
        box.center = box.transform.InverseTransformPoint(bounds.center);
        box.size = new Vector3(
            AbsDiv(bounds.size.x, lossy.x),
            AbsDiv(bounds.size.y, lossy.y),
            AbsDiv(bounds.size.z, lossy.z));
        box.size = Vector3.Max(box.size, Vector3.one * 0.05f);

        var localMax = Mathf.Max(box.size.x, box.size.y, box.size.z);
        if (localMax > MaxHoverColliderAxis)
            box.size *= MaxHoverColliderAxis / localMax;
    }

    static float AbsDiv(float a, float b)
    {
        var d = Mathf.Abs(b);
        return d < 0.0001f ? a : a / d;
    }

    static Transform FindExact(Transform root, string exactName)
    {
        for (var i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i).name == exactName)
                return root.GetChild(i);
        }

        var all = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] != root && all[i].name == exactName)
                return all[i];
        }

        return null;
    }
}
