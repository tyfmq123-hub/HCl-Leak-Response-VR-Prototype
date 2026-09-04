using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;

/// <summary>
/// Polished yellow showcase-card panel for response-bag equipment.
/// Card previews prefer a live capture of the real 3D mesh (same poses as the PNG
/// thumbnails), displayed in a clipped UI Image. Falls back to Resources PNG sprites.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagGearPanel : MonoBehaviour
{
    public const string PrefabResourcePath = "PPE/ResponseBagGearPanel";
    public const string PrefabAssetPath = "Assets/1.JSR/Prefabs/ResponseBagGearPanel.prefab";

    const string HangulFontResource = "PPE/PPE_HangulFont";
    const string ThumbGasResource = "PPE/Thumbnails/ResponseBag_GasDetector";
    const string ThumbSockResource = "PPE/Thumbnails/ResponseBag_AbsorbentSock";
    const string ThumbPadResource = "PPE/Thumbnails/ResponseBag_AbsorbentPad";

    static readonly Color CardYellow = new Color(1f, 0.84f, 0.08f, 1f);
    static readonly Color CardYellowHover = new Color(1f, 0.9f, 0.22f, 1f);
    static readonly Color PreviewInset = new Color(0.16f, 0.16f, 0.17f, 1f);
    static readonly Color PanelBackdrop = new Color(0.12f, 0.12f, 0.13f, 0.72f);
    static readonly Color LabelWhite = Color.white;
    static readonly Color CloseRed = new Color(0.78f, 0.14f, 0.14f, 1f);
    static readonly Color PlaceholderGray = new Color(0.28f, 0.28f, 0.3f, 1f);
    static readonly Color TakenDim = new Color(0.45f, 0.45f, 0.45f, 0.55f);
    // 가져감
    static readonly string TakenLabel = "\uAC00\uC838\uAC10";

    static Sprite s_RoundedSprite;

    [SerializeField] GameObject panelRoot;
    [SerializeField] RectTransform panelRect;
    [SerializeField] Button closeButton;
    [SerializeField] ResponseEquipmentManager equipmentManager;
    [SerializeField] Camera xrCamera;

    [Header("Placement (stable card row)")]
    [SerializeField] float distance = 1.2f;
    [SerializeField] float heightOffset = -0.1f;
    [SerializeField] float horizontalOffset = 0f;
    [SerializeField] float worldScale = 0.0015f;
    [SerializeField] Vector2 panelSize = new Vector2(1050f, 430f);
    [SerializeField] int canvasSortOrder = 80;

    [Header("Slots")]
    [SerializeField] SlotView[] slots;
    [SerializeField] bool markAllInspectedOnShow = true;
    [Tooltip("Bake live 3D mesh into the card Image using thumbnail poses. Off by default — URP capture can fail; PNG thumbnails are used instead.")]
    [SerializeField] bool preferLive3DCapture = false;
    [SerializeField] int liveCaptureSize = 512;

    bool open;

    [System.Serializable]
    public class SlotView
    {
        public string itemId;
        public string koreanName;
        public RectTransform frame;
        public Image previewImage;
        public Sprite previewSprite;
        public TextMeshProUGUI label;
        public Button button;
        [System.NonSerialized] public Sprite runtimeOwnedSprite;
        [System.NonSerialized] public CanvasGroup canvasGroup;
        [System.NonSerialized] public TextMeshProUGUI takenOverlay;
        [System.NonSerialized] public Color originalCardColor = CardYellow;
        [System.NonSerialized] public Image cardBackground;
    }

    public bool IsOpen => open && panelRoot != null && panelRoot.activeInHierarchy;

    void Awake()
    {
        ResolveCamera();
        if (panelRoot == null)
            panelRoot = gameObject;
        WireButtons();
        ApplyThumbnails();
        Hide();
    }

    void OnEnable() => WireButtons();

    void OnDisable()
    {
        ClearRuntimeCaptures();
        open = false;
    }

    void LateUpdate()
    {
        if (!open)
            return;
        PlaceInFrontOfCamera();
    }

    public void Show() => Show(showPrepareHint: true);

    public void Show(bool showPrepareHint)
    {
        ResolveCamera();
        ApplyThumbnails();
        if (preferLive3DCapture)
        {
            // Legacy live 3D card capture disabled.
            preferLive3DCapture = false;
        }

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        if (panelRoot != null && !panelRoot.activeSelf)
            panelRoot.SetActive(true);

        PlaceInFrontOfCamera();
        BeginBagSuppressedPresentation();
        open = true;
        RefreshTakenVisuals();

        if (!showPrepareHint || !markAllInspectedOnShow)
            return;
        if (equipmentManager != null && equipmentManager.ResponseBagPrepared)
            return;

        MarkAllContentsInspected();
        var feedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        feedback?.ShowMessage(
            "\uB0B4\uBD80 \uC7A5\uBE44\uB97C \uD655\uC778\uD588\uC2B5\uB2C8\uB2E4. Grip\uC73C\uB85C \uC900\uBE44\uD554\uC138\uC694.");
    }

    public void Hide()
    {
        open = false;
        ClearRuntimeCaptures();
        EndBagSuppressedPresentation();
        if (panelRoot != null && panelRoot.activeSelf)
            panelRoot.SetActive(false);

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        if (equipmentManager != null &&
            equipmentManager.WearableVisual != null &&
            equipmentManager.WearableVisual.VisualController != null)
            equipmentManager.WearableVisual.VisualController.NotifyGearUiClosed();
    }

    public void EditorWire(
        GameObject root,
        Button close,
        ResponseEquipmentManager manager,
        Camera camera,
        SlotView[] gearSlots)
    {
        panelRoot = root;
        panelRect = root != null ? root.GetComponent<RectTransform>() : null;
        closeButton = close;
        equipmentManager = manager;
        xrCamera = camera;
        slots = gearSlots;
    }

    public void ApplyFrontOfBagPlacementDefaults()
    {
        distance = 1.2f;
        heightOffset = -0.1f;
        horizontalOffset = 0f;
        worldScale = 0.0015f;
        panelSize = new Vector2(1050f, 430f);
        canvasSortOrder = 80;
    }

    void MarkAllContentsInspected()
    {
        if (equipmentManager == null)
            return;
        equipmentManager.InspectResponseBagItem(ResponseEquipmentManager.IdGasDetector);
        equipmentManager.InspectResponseBagItem(ResponseEquipmentManager.IdAbsorbentSock);
        equipmentManager.InspectResponseBagItem(ResponseEquipmentManager.IdAbsorbentPad);
    }

    void BeginBagSuppressedPresentation()
    {
        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);

        // Keep the worn/equipped bag visible while its gear cards are open.
        if (equipmentManager != null && equipmentManager.ResponseBagPrepared)
            return;

        equipmentManager?.SetBagHiddenForGearUi(true);
    }

    void EndBagSuppressedPresentation()
    {
        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);
        equipmentManager?.SetBagHiddenForGearUi(false);
    }

    void WireButtons()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Hide);
            closeButton.onClick.AddListener(Hide);
        }

        if (slots == null)
            return;

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.button == null)
                continue;

            slot.button.onClick.RemoveAllListeners();
            var captured = slot;
            slot.button.onClick.AddListener(() => OnSlotPressed(captured));

            if (slot.button.GetComponent<XRUISelectHandCapture>() == null)
                slot.button.gameObject.AddComponent<XRUISelectHandCapture>();

            CacheSlotVisualRefs(slot);
        }

        RefreshTakenVisuals();
    }

    void OnSlotPressed(SlotView slot)
    {
        if (slot == null || equipmentManager == null || string.IsNullOrEmpty(slot.itemId))
            return;

        // Post-equip take-from-bag removed. Cards only mark inspect pre-equip.
        if (equipmentManager.ResponseBagPrepared)
            return;

        equipmentManager.InspectResponseBagItem(slot.itemId);
    }

    /// <summary>Dims unavailable cards and shows a taken label until restock (3s).</summary>
    public void RefreshTakenVisuals()
    {
        if (slots == null)
            return;

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null)
                continue;

            CacheSlotVisualRefs(slot);

            var available = equipmentManager == null ||
                            !equipmentManager.ResponseBagPrepared ||
                            equipmentManager.IsBagItemAvailable(slot.itemId);
            ApplyTakenVisual(slot, !available);
        }
    }

    void CacheSlotVisualRefs(SlotView slot)
    {
        if (slot == null || slot.frame == null)
            return;

        if (slot.canvasGroup == null)
        {
            slot.canvasGroup = slot.frame.GetComponent<CanvasGroup>();
            if (slot.canvasGroup == null)
                slot.canvasGroup = slot.frame.gameObject.AddComponent<CanvasGroup>();
        }

        if (slot.cardBackground == null)
        {
            var yellow = slot.frame.Find("YellowCardBackground");
            if (yellow != null)
                slot.cardBackground = yellow.GetComponent<Image>();
            if (slot.cardBackground == null)
                slot.cardBackground = slot.frame.GetComponent<Image>();
            if (slot.cardBackground != null)
                slot.originalCardColor = slot.cardBackground.color;
        }

        if (slot.takenOverlay == null)
        {
            var existing = slot.frame.Find("TakenOverlay");
            if (existing != null)
                slot.takenOverlay = existing.GetComponent<TextMeshProUGUI>();
            else
                slot.takenOverlay = CreateTakenOverlay(slot.frame);
        }
    }

    static TextMeshProUGUI CreateTakenOverlay(RectTransform frame)
    {
        var go = new GameObject("TakenOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(frame, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = TakenLabel;
        tmp.fontSize = 42f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 1f, 1f, 0.92f);
        tmp.raycastTarget = false;

        var font = Resources.Load<TMP_FontAsset>(HangulFontResource);
        if (font != null && font.material != null)
        {
            tmp.font = font;
            tmp.fontSharedMaterial = font.material;
        }

        go.SetActive(false);
        return tmp;
    }

    void ApplyTakenVisual(SlotView slot, bool taken)
    {
        if (slot.button != null)
            slot.button.interactable = !taken;

        if (slot.canvasGroup != null)
        {
            slot.canvasGroup.alpha = taken ? 0.55f : 1f;
            slot.canvasGroup.blocksRaycasts = !taken;
            slot.canvasGroup.interactable = !taken;
        }

        if (slot.cardBackground != null)
            slot.cardBackground.color = taken ? TakenDim : slot.originalCardColor;

        if (slot.takenOverlay != null)
            slot.takenOverlay.gameObject.SetActive(taken);
    }

    public void ApplyThumbnails()
    {
        if (slots == null)
            return;

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null)
                continue;

            if (slot.previewSprite == null)
                slot.previewSprite = LoadDefaultThumbnail(slot.itemId);

            if (slot.previewImage == null)
                continue;

            slot.previewImage.preserveAspect = true;
            slot.previewImage.raycastTarget = false;

            if (slot.previewSprite != null)
            {
                slot.previewImage.sprite = slot.previewSprite;
                slot.previewImage.color = Color.white;
            }
            else
            {
                slot.previewImage.sprite = null;
                slot.previewImage.color = PlaceholderGray;
                Debug.LogWarning(
                    $"[ResponseBagGearPanel] Missing thumbnail sprite for itemId='{slot.itemId}'. " +
                    "Run Tools > PPE > Render Response Bag Thumbnails.",
                    this);
            }
        }
    }

    void CaptureLive3DPreviews()
    {
        if (slots == null)
            return;

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<ResponseEquipmentManager>(FindObjectsInactive.Include);

        var reveal = equipmentManager != null ? equipmentManager.ContentsReveal : null;
        if (reveal == null)
            reveal = FindAnyObjectByType<ResponseBagContentsRevealController>(FindObjectsInactive.Include);

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.previewImage == null || string.IsNullOrEmpty(slot.itemId))
                continue;

            var source = ResolveSource(reveal, slot.itemId);
            if (source == null)
                continue;

            var sprite = ResponseBagPreviewCapture.CaptureSprite(
                source,
                slot.itemId,
                Mathf.Clamp(liveCaptureSize, 256, 1024));
            if (sprite == null)
            {
                // Keep PNG (already applied by ApplyThumbnails).
                continue;
            }

            ResponseBagPreviewCapture.ApplyCapturedSprite(
                slot.previewImage,
                sprite,
                ref slot.runtimeOwnedSprite);
        }
    }

    void ClearRuntimeCaptures()
    {
        if (slots == null)
            return;

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null)
                continue;

            if (slot.runtimeOwnedSprite != null)
            {
                var tex = slot.runtimeOwnedSprite.texture;
                if (slot.previewImage != null && slot.previewImage.sprite == slot.runtimeOwnedSprite)
                {
                    // Restore baked PNG fallback if present.
                    slot.previewImage.sprite = slot.previewSprite != null
                        ? slot.previewSprite
                        : LoadDefaultThumbnail(slot.itemId);
                }

                Object.Destroy(slot.runtimeOwnedSprite);
                if (tex != null)
                    Object.Destroy(tex);
                slot.runtimeOwnedSprite = null;
            }
        }
    }

    static GameObject ResolveSource(ResponseBagContentsRevealController reveal, string itemId)
    {
        if (reveal == null || string.IsNullOrEmpty(itemId))
            return null;
        if (itemId == ResponseEquipmentManager.IdGasDetector)
            return reveal.GasDetectorRoot;
        if (itemId == ResponseEquipmentManager.IdAbsorbentSock)
            return reveal.AbsorbentSockRoot;
        if (itemId == ResponseEquipmentManager.IdAbsorbentPad)
            return reveal.AbsorbentPadRoot;
        return null;
    }

    static Sprite LoadDefaultThumbnail(string itemId)
    {
        if (itemId == ResponseEquipmentManager.IdGasDetector)
            return Resources.Load<Sprite>(ThumbGasResource);
        if (itemId == ResponseEquipmentManager.IdAbsorbentSock)
            return Resources.Load<Sprite>(ThumbSockResource);
        if (itemId == ResponseEquipmentManager.IdAbsorbentPad)
            return Resources.Load<Sprite>(ThumbPadResource);
        return null;
    }

    void PlaceInFrontOfCamera()
    {
        ResolveCamera();
        if (xrCamera == null)
            return;

        var cam = xrCamera.transform;
        transform.position =
            cam.position
            + cam.forward * distance
            + cam.up * heightOffset
            + cam.right * horizontalOffset;
        transform.rotation = Quaternion.LookRotation(transform.position - cam.position, cam.up);
        transform.localScale = Vector3.one * worldScale;

        if (panelRect == null)
            panelRect = panelRoot != null ? panelRoot.GetComponent<RectTransform>() : GetComponent<RectTransform>();
        if (panelRect != null)
            panelRect.sizeDelta = panelSize;

        var canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = canvasSortOrder;
        }
    }

    void ResolveCamera()
    {
        if (xrCamera != null)
            return;

        var origin = FindAnyObjectByType<XROrigin>();
        if (origin != null && origin.Camera != null)
        {
            xrCamera = origin.Camera;
            return;
        }

        xrCamera = Camera.main;
    }

    static bool IsValidThumbnailPanel(ResponseBagGearPanel panel)
    {
        if (panel == null || panel.slots == null || panel.slots.Length == 0)
            return false;
        for (var i = 0; i < panel.slots.Length; i++)
        {
            var slot = panel.slots[i];
            if (slot == null || slot.previewImage == null || slot.button == null)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Reuses an existing panel, or Instantiates the persistent prefab.
    /// Never destroys a valid panel due to layout version churn.
    /// </summary>
    public static ResponseBagGearPanel EnsureCreated(ResponseEquipmentManager manager)
    {
        var existing = FindAnyObjectByType<ResponseBagGearPanel>(FindObjectsInactive.Include);
        if (existing != null && IsValidThumbnailPanel(existing))
        {
            if (manager != null)
                manager.SetGearPanel(existing);
            return existing;
        }

        if (existing != null)
        {
            // Legacy 3D-clone panel or incomplete instance — replace once.
            Object.DestroyImmediate(existing.gameObject);
        }

        var prefab = Resources.Load<GameObject>(PrefabResourcePath);
        GameObject root;
        if (prefab != null)
        {
            root = Object.Instantiate(prefab);
            root.name = "ResponseBagGearCanvas";
        }
        else
        {
            Debug.LogWarning(
                "[ResponseBagGearPanel] Prefab missing at Resources/" + PrefabResourcePath +
                ". Building a runtime fallback panel. Run Tools > PPE > Build Response Bag Gear Panel Prefab.");
            root = BuildRuntimePanel();
        }

        var system = GameObject.Find("PPE_System");
        if (system == null)
            system = new GameObject("PPE_System");
        root.transform.SetParent(system.transform, false);

        var panel = root.GetComponent<ResponseBagGearPanel>();
        if (panel == null)
            panel = root.AddComponent<ResponseBagGearPanel>();

        if (panel.equipmentManager == null)
            panel.equipmentManager = manager;
        if (panel.xrCamera == null)
            panel.xrCamera = FindXrCamera();

        panel.WireButtons();
        panel.ApplyThumbnails();
        if (manager != null)
            manager.SetGearPanel(panel);
        root.SetActive(false);
        return panel;
    }

    /// <summary>Runtime fallback if the prefab has not been built yet.</summary>
    public static GameObject BuildRuntimePanel()
    {
        var camera = FindXrCamera();
        var root = new GameObject("ResponseBagGearCanvas");
        root.transform.localScale = Vector3.one * 0.001f;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 80;
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<TrackedDeviceGraphicRaycaster>();

        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1050f, 430f);

        var bg = CreateRoundedImage(root.transform, "Background", PanelBackdrop);
        StretchFull(bg.rectTransform);
        bg.rectTransform.offsetMin = new Vector2(-18f, -18f);
        bg.rectTransform.offsetMax = new Vector2(18f, 18f);

        var cardRow = new GameObject("CardRow", typeof(RectTransform));
        cardRow.transform.SetParent(root.transform, false);
        StretchFull(cardRow.GetComponent<RectTransform>());

        var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        closeGo.transform.SetParent(root.transform, false);
        var closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 1f);
        closeRt.anchorMax = new Vector2(1f, 1f);
        closeRt.pivot = new Vector2(1f, 1f);
        closeRt.sizeDelta = new Vector2(46f, 46f);
        closeRt.anchoredPosition = new Vector2(-12f, -12f);
        var closeImg = closeGo.GetComponent<Image>();
        ApplyRoundedSprite(closeImg);
        closeImg.color = CloseRed;
        var closeBtn = closeGo.GetComponent<Button>();

        var xGo = new GameObject("X", typeof(RectTransform));
        xGo.transform.SetParent(closeGo.transform, false);
        StretchFull(xGo.GetComponent<RectTransform>());
        var xLabel = xGo.AddComponent<TextMeshProUGUI>();
        xLabel.text = "X";
        xLabel.fontSize = 24f;
        xLabel.fontStyle = FontStyles.Bold;
        xLabel.alignment = TextAlignmentOptions.Center;
        xLabel.color = Color.white;

        var defs = new[]
        {
            (ResponseEquipmentManager.IdGasDetector, "GasDetectorCard",
                "\uD734\uB300\uC6A9 HCl \uAC00\uC2A4\uCE21\uC815\uAE30"),
            (ResponseEquipmentManager.IdAbsorbentSock, "AbsorbentSockCard",
                "\uCF00\uBBF8\uCEEC \uD761\uCC29\uC0AD"),
            (ResponseEquipmentManager.IdAbsorbentPad, "AbsorbentPadCard",
                "\uD654\uD559\uBB3C\uC9C8\uC6A9 \uD761\uCC29\uD328\uB4DC"),
        };

        var slotViews = new SlotView[defs.Length];
        for (var i = 0; i < defs.Length; i++)
            slotViews[i] = CreateShowcaseCard(
                cardRow.transform, i, defs.Length, defs[i].Item1, defs[i].Item2, defs[i].Item3);

        var panel = root.AddComponent<ResponseBagGearPanel>();
        panel.EditorWire(root, closeBtn, null, camera, slotViews);
        panel.ApplyFrontOfBagPlacementDefaults();
        return root;
    }

    public static SlotView CreateShowcaseCard(
        Transform parent,
        int index,
        int count,
        string itemId,
        string cardName,
        string koreanName)
    {
        const float left = 0.03f;
        const float right = 0.97f;
        const float gap = 0.022f; // ~22px relative on 1050 panel
        var span = right - left;
        var cell = (span - gap * (count - 1)) / count;
        var x0 = left + index * (cell + gap);
        var x1 = x0 + cell;

        var frameGo = new GameObject(cardName, typeof(RectTransform), typeof(Image), typeof(Button));
        frameGo.transform.SetParent(parent, false);
        var frameRt = frameGo.GetComponent<RectTransform>();
        frameRt.anchorMin = new Vector2(x0, 0.05f);
        frameRt.anchorMax = new Vector2(x1, 0.95f);
        frameRt.offsetMin = Vector2.zero;
        frameRt.offsetMax = Vector2.zero;

        var yellow = frameGo.GetComponent<Image>();
        yellow.name = "YellowCardBackground";
        ApplyRoundedSprite(yellow);
        yellow.color = CardYellow;
        yellow.pixelsPerUnitMultiplier = 1.2f;

        var btn = frameGo.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = CardYellowHover;
        colors.pressedColor = CardYellowHover;
        colors.selectedColor = Color.white;
        btn.colors = colors;
        btn.targetGraphic = yellow;

        // ~72% dark preview inset
        var dark = CreateRoundedImage(frameGo.transform, "DarkPreviewPanel", PreviewInset);
        dark.rectTransform.anchorMin = new Vector2(0.07f, 0.30f);
        dark.rectTransform.anchorMax = new Vector2(0.93f, 0.94f);
        dark.rectTransform.offsetMin = Vector2.zero;
        dark.rectTransform.offsetMax = Vector2.zero;
        dark.pixelsPerUnitMultiplier = 1.5f;
        dark.raycastTarget = false;

        var previewGo = new GameObject("PreviewImage", typeof(RectTransform), typeof(Image));
        previewGo.transform.SetParent(dark.transform, false);
        var previewRt = previewGo.GetComponent<RectTransform>();
        previewRt.anchorMin = new Vector2(0.06f, 0.06f);
        previewRt.anchorMax = new Vector2(0.94f, 0.94f);
        previewRt.offsetMin = Vector2.zero;
        previewRt.offsetMax = Vector2.zero;
        var previewImage = previewGo.GetComponent<Image>();
        previewImage.color = Color.white;
        previewImage.preserveAspect = true;
        previewImage.raycastTarget = false;

        // ~28% label band
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(frameGo.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0.06f, 0.04f);
        labelRt.anchorMax = new Vector2(0.94f, 0.27f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = koreanName;
        label.fontSize = 27f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.color = LabelWhite;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        ApplyHangul(label);

        return new SlotView
        {
            itemId = itemId,
            koreanName = koreanName,
            frame = frameRt,
            previewImage = previewImage,
            previewSprite = LoadDefaultThumbnail(itemId),
            label = label,
            button = btn,
        };
    }

    static Image CreateRoundedImage(Transform parent, string name, Color color)
    {
        var img = CreateImage(parent, name, color);
        ApplyRoundedSprite(img);
        return img;
    }

    static void ApplyRoundedSprite(Image img)
    {
        if (img == null)
            return;
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1.35f;
    }

    static Sprite GetRoundedSprite()
    {
        if (s_RoundedSprite != null)
            return s_RoundedSprite;

        const int size = 64;
        const int radius = 20;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "PPE_GearCardRounded",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };

        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var fx = x + 0.5f;
                var fy = y + 0.5f;
                var cx = Mathf.Clamp(fx, radius, size - radius);
                var cy = Mathf.Clamp(fy, radius, size - radius);
                var dx = fx - cx;
                var dy = fy - cy;
                var inside = (dx * dx + dy * dy) <= (radius * radius);
                pixels[y * size + x] = inside
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(255, 255, 255, 0);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        var border = new Vector4(radius, radius, radius, radius);
        s_RoundedSprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            border);
        return s_RoundedSprite;
    }

    static Image CreateImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void ApplyHangul(TextMeshProUGUI tmp)
    {
        var font = Resources.Load<TMP_FontAsset>(HangulFontResource);
        if (font != null)
            tmp.font = font;
    }

    static Camera FindXrCamera()
    {
        var origin = FindAnyObjectByType<XROrigin>();
        if (origin != null && origin.Camera != null)
            return origin.Camera;
        return Camera.main;
    }
}
