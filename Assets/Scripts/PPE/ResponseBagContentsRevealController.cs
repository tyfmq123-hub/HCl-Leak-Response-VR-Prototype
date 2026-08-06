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

    [Header("Inspection layout (local under BagContentsRoot — tunable)")]
    [SerializeField] Vector3 gasLocalPosition = new Vector3(0.09f, 0.05f, 0.03f);
    [SerializeField] Vector3 gasLocalEuler = new Vector3(-18f, 200f, 0f);
    [SerializeField] float gasUniformScale = 0.13f;

    [SerializeField] Vector3 sockLocalPosition = new Vector3(0f, -0.01f, 0.05f);
    [SerializeField] Vector3 sockLocalEuler = new Vector3(5f, 90f, 0f);
    [SerializeField] float sockUniformScale = 0.26f;

    [SerializeField] Vector3 padLocalPosition = new Vector3(-0.09f, 0.03f, -0.01f);
    [SerializeField] Vector3 padLocalEuler = new Vector3(85f, 0f, 10f);
    [SerializeField] float padUniformScale = 0.18f;

    bool runtimeConfigured;

    public float MaxGazeDistance => 3f;
    public bool ContentsVisible =>
        gasDetectorRoot != null && gasDetectorRoot.activeInHierarchy;
    public GameObject WorldBagRoot =>
        worldResponseBagRoot != null ? worldResponseBagRoot : gameObject;

    public GameObject GasDetectorRoot => gasDetectorRoot;
    public GameObject AbsorbentSockRoot => absorbentSockRoot;
    public GameObject AbsorbentPadRoot => absorbentPadRoot;

    /// <summary>
    /// Called when the world bag is sticky-selected into camera preview:
    /// show contents, arrange inside the open bag, fix gas facing.
    /// </summary>
    public void PrepareContentsForInspection()
    {
        EnsureRuntimeConfigured(equipmentManager);
        ArrangeContentsForInspection();
        RefitContentHoverColliders();
        SetContentsActive(true);
        FixGasDetectorFacing();
    }

    /// <summary>
    /// Place the three content objects inside the bag opening for inspect:
    /// sock along the lower interior, pad left/back, gas detector on the right.
    /// </summary>
    public void ArrangeContentsForInspection()
    {
        var bag = WorldBagRoot != null ? WorldBagRoot.transform : transform;
        var contentsRoot = bag.Find("BagContentsRoot");
        if (contentsRoot == null)
        {
            var go = new GameObject("BagContentsRoot");
            contentsRoot = go.transform;
            contentsRoot.SetParent(bag, false);
            contentsRoot.localPosition = Vector3.zero;
            contentsRoot.localRotation = Quaternion.identity;
            contentsRoot.localScale = Vector3.one;
        }

        // Prefer existing slot roots; otherwise use assigned content roots.
        ApplyInspectionPose(
            ResolveSlotOrRoot(contentsRoot, "GasDetectorSlot", gasDetectorRoot),
            gasLocalPosition,
            gasLocalEuler,
            gasUniformScale,
            ExactGasName);

        ApplyInspectionPose(
            ResolveSlotOrRoot(contentsRoot, "AbsorbentSockSlot", absorbentSockRoot),
            sockLocalPosition,
            sockLocalEuler,
            sockUniformScale,
            ExactSockName);

        ApplyInspectionPose(
            ResolveSlotOrRoot(contentsRoot, "AbsorbentPadSlot", absorbentPadRoot),
            padLocalPosition,
            padLocalEuler,
            padUniformScale,
            ExactPadPreferred);
    }

    static Transform ResolveSlotOrRoot(Transform contentsRoot, string slotName, GameObject contentRoot)
    {
        var slot = contentsRoot.Find(slotName);
        if (slot != null)
            return slot;

        if (contentRoot == null)
            return null;

        if (contentRoot.transform.parent != contentsRoot)
            contentRoot.transform.SetParent(contentsRoot, true);

        return contentRoot.transform;
    }

    static void ApplyInspectionPose(
        Transform slot,
        Vector3 localPos,
        Vector3 localEuler,
        float uniformScale,
        string preferredChildName)
    {
        if (slot == null)
            return;

        slot.localPosition = localPos;
        slot.localRotation = Quaternion.Euler(localEuler);
        slot.localScale = Vector3.one * Mathf.Max(0.01f, uniformScale);

        // Zero the mesh under the slot so Inspector scale lives on the slot.
        var model = preferredChildName != null ? FindExact(slot, preferredChildName) : null;
        if (model == null && slot.childCount > 0)
            model = slot.GetChild(0);
        if (model == null)
            return;

        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one;
    }

    /// <summary>Hide interior props until the bag is selected for inspection.</summary>
    public void HideContentsUntilSelected()
    {
        ClearAllItemVisuals();
        SetContentsActive(false);
    }

    void FixGasDetectorFacing()
    {
        if (gasDetectorRoot == null)
            return;

        // Pose comes from ArrangeContentsForInspection (gasLocalEuler includes face-out yaw).
        // Keep mesh identity under the slot so slot euler is the single source of truth.
        var model = FindExact(gasDetectorRoot.transform, ExactGasName);
        if (model != null)
        {
            model.localRotation = Quaternion.identity;
            return;
        }
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
            if (keepContentsVisible)
                SetContentsActive(true);
            else
                SetContentsActive(false);
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
        SetContentsActive(false);
    }

    public void ResetContentsDisplay()
    {
        ClearAllItemVisuals();
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
        tmp.enableWordWrapping = false;
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
