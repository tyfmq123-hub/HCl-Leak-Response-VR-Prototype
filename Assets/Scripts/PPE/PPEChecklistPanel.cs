using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// World-space preparation checklist. Displays manager state only -
/// never stores its own completion flags.
/// Rows always follow <see cref="PPESequenceManager"/> wear order (same as TTS).
/// Toggle visibility via on-panel button or left-controller Y (SecondaryButton).
/// </summary>
[DisallowMultipleComponent]
public class PPEChecklistPanel : MonoBehaviour
{
    public enum ChecklistSource
    {
        Equipped = 0,
        Prepared = 1,
        ResponseBagPrepared = 2,
    }

    [Serializable]
    public class ChecklistRow
    {
        [Tooltip("Item id for Equipped/Prepared rows. ResponseBag row uses ResponseEquipmentManager.")]
        public string itemId;

        public ChecklistSource source = ChecklistSource.Equipped;

        public TextMeshProUGUI labelText;
        public Image indicator;
        public TextMeshProUGUI indicatorMark;
    }

    [Header("Managers")]
    [SerializeField] PPEEquipmentManager equipmentManager;
    [SerializeField] ResponseEquipmentManager responseEquipmentManager;

    [Header("Visibility")]
    [Tooltip("Panel body that is shown/hidden. Canvas stays active so Y can reopen.")]
    [SerializeField] GameObject contentRoot;
    [SerializeField] Button toggleVisibilityButton;
    [SerializeField] TextMeshProUGUI toggleButtonLabel;
    [SerializeField] bool startVisible = true;

    [Header("Toggle Input (Left Y)")]
    [Tooltip("Defaults to Left-hand SecondaryButton (Quest Y).")]
    [SerializeField] InputActionProperty toggleVisibilityAction;

    [Header("Rows")]
    [SerializeField] ChecklistRow[] rows;

    [Header("Status")]
    [SerializeField] TextMeshProUGUI statusText;

    [Header("Colors")]
    [SerializeField] Color incompleteIndicatorColor = new Color(0.45f, 0.48f, 0.52f, 1f);
    [SerializeField] Color completeIndicatorColor = new Color(0.22f, 0.72f, 0.38f, 1f);
    [SerializeField] Color incompleteMarkColor = new Color(0.75f, 0.78f, 0.82f, 0.85f);
    [SerializeField] Color completeMarkColor = Color.white;

    [Header("Marks")]
    [SerializeField] string incompleteMark = "\u25A1";
    [SerializeField] string completeMark = "\u2713";

    PPEEquipmentManager subscribedEquipment;
    ResponseEquipmentManager subscribedResponse;
    InputAction ownedToggleAction;
    bool toggleSubscribed;
    bool contentVisible = true;

    static readonly string LabelHide = "\uB2EB\uAE30";
    static readonly string LabelShow = "\uC5F4\uAE30";

    struct WearOrderSpec
    {
        public string ObjectName;
        public string Label;
        public string ItemId;
        public ChecklistSource Source;
    }

    /// <summary>
    /// Exact wear order used by PPESequenceManager / TTS.
    /// Labels use Hangul already present in the room font atlas where possible.
    /// </summary>
    static readonly WearOrderSpec[] WearOrderSpecs =
    {
        new WearOrderSpec
        {
            ObjectName = "InnerGlovesRow",
            Label = "1. \uC18D\uC7A5\uAC11",
            ItemId = PPESequenceManager.IdInnerGloves,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "ChemicalSuitRow",
            Label = "2. \uB0B4\uC0B0\uC131 \uD654\uD559\uBCF4\uD638\uBCF5",
            ItemId = PPEEquipmentManager.IdChemicalSuit,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "ChemicalBootsRow",
            Label = "3. \uB0B4\uC0B0\uC131 \uD654\uD559\uBCF4\uD638 \uC7A5\uD654",
            ItemId = PPEEquipmentManager.IdChemicalBoots,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "FaceRespiratorRow",
            Label = "4. \uC804\uBA74\uD615 \uD638\uD761\uBCF4\uD638\uAD6C",
            ItemId = PPEEquipmentManager.IdFaceRespirator,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "ResponseBagRow",
            Label = "5. \uD654\uD559\uBB3C\uC9C8 \uB204\uCD9C \uB300\uC751\uAC00\uBC29",
            ItemId = PPEEquipmentManager.IdResponseBag,
            Source = ChecklistSource.ResponseBagPrepared,
        },
        new WearOrderSpec
        {
            ObjectName = "FaceShieldRow",
            Label = "6. \uC548\uBA74\uBCF4\uD638\uAD6C",
            ItemId = PPEEquipmentManager.IdFaceShield,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "ChemicalGlovesRow",
            Label = "7. \uB0B4\uD654\uD559 \uBCF4\uD638\uC7A5\uAC11",
            ItemId = PPEEquipmentManager.IdChemicalGloves,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "WasteContainerRow",
            Label = "8. \uC720\uD574\uD3D0\uAE30\uBB3C \uC218\uAC70\uC6A9\uAE30",
            ItemId = PPEEquipmentManager.IdWasteContainer,
            Source = ChecklistSource.Prepared,
        },
    };

    public bool IsContentVisible => contentVisible;

    void Awake()
    {
        EnsureUiRefs();
        ApplyWearSequenceOrder();
        EnsureUiInteraction();
        EnsureToggleButton();
        EnsureToggleAction();
        ApplyHangulFontIfAvailable();
        ResolveManagers();
        SetContentVisible(startVisible, refreshLabel: true);
        RefreshFromManager();
    }

    void OnEnable()
    {
        TrySubscribe();
        WireToggleButton(true);
        WireToggleAction(true);
        RefreshFromManager();
    }

    void Start()
    {
        // Second pass after other UI scripts finish Awake.
        ApplyWearSequenceOrder();
        ApplyHangulFontIfAvailable();
        TrySubscribe();
        RefreshFromManager();
    }

    void OnDisable()
    {
        Unsubscribe();
        WireToggleButton(false);
        WireToggleAction(false);
    }

    void OnDestroy()
    {
        Unsubscribe();
        WireToggleButton(false);
        WireToggleAction(false);

        if (ownedToggleAction != null)
        {
            ownedToggleAction.Dispose();
            ownedToggleAction = null;
        }
    }

    public void ToggleContentVisibility()
    {
        SetContentVisible(!contentVisible);
    }

    public void SetContentVisible(bool visible, bool refreshLabel = true)
    {
        contentVisible = visible;

        if (contentRoot != null)
            contentRoot.SetActive(visible);

        if (refreshLabel)
            RefreshToggleLabel();

        if (visible)
        {
            TrySubscribe();
            RefreshFromManager();
        }
    }

    void RefreshToggleLabel()
    {
        if (toggleButtonLabel == null)
            return;

        toggleButtonLabel.text = contentVisible ? LabelHide : LabelShow;
    }

    void EnsureUiRefs()
    {
        if (contentRoot == null)
        {
            var existing = transform.Find("ChecklistPanel");
            if (existing != null)
                contentRoot = existing.gameObject;
        }
    }

    /// <summary>
    /// Force hierarchy + labels into wear-order 1..8.
    /// Scene rows use absolute anchored positions (no VerticalLayoutGroup), so sibling
    /// reordering alone does nothing — we rewrite Y positions and numbered labels.
    /// </summary>
    void ApplyWearSequenceOrder()
    {
        var root = contentRoot != null ? contentRoot.transform : transform;
        var listParent = FindDeep(root, "PPESection");
        if (listParent == null)
            listParent = root;

        var sectionLabelTf = FindDeep(listParent, "PPESectionLabel");
        var sectionLabel = sectionLabelTf != null
            ? sectionLabelTf.GetComponent<TextMeshProUGUI>()
            : null;
        if (sectionLabel != null)
            sectionLabel.text = "\uCC29\uC6A9 \uC21C\uC11C";

        // Old "\uB300\uC751\uC7A5\uBE44" block — hide so only wear-order 1..8 shows.
        var equipmentSection = FindDeep(root, "EquipmentSection");
        if (equipmentSection != null)
            equipmentSection.gameObject.SetActive(false);
        var equipmentLabel = FindDeep(root, "EquipmentSectionLabel");
        if (equipmentLabel != null)
            equipmentLabel.gameObject.SetActive(false);

        RemoveDuplicateRows(root);

        var built = new List<ChecklistRow>(WearOrderSpecs.Length);
        var ordered = new List<Transform>(WearOrderSpecs.Length);

        for (var i = 0; i < WearOrderSpecs.Length; i++)
        {
            var spec = WearOrderSpecs[i];
            var rowRoot = FindDeep(root, spec.ObjectName);
            if (rowRoot == null)
                rowRoot = CreateRuntimeRow(listParent, spec);
            if (rowRoot == null)
                continue;

            ordered.Add(rowRoot);

            var label = rowRoot.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (label != null)
                label.text = spec.Label;

            built.Add(new ChecklistRow
            {
                itemId = spec.ItemId,
                source = spec.Source,
                labelText = label,
                indicator = rowRoot.Find("Indicator")?.GetComponent<Image>(),
                indicatorMark = rowRoot.Find("Indicator/Mark")?.GetComponent<TextMeshProUGUI>(),
            });
        }

        // Title stays at top; section label then rows 1..8 stacked by Y.
        const float x = 36f;
        const float width = 708f;
        const float rowHeight = 72f;
        const float rowGap = 6f;
        var y = -100f;

        if (sectionLabelTf != null)
        {
            if (sectionLabelTf.parent != listParent)
                sectionLabelTf.SetParent(listParent, false);
            PlaceRect(sectionLabelTf as RectTransform, x, y, width, 40f);
            sectionLabelTf.SetSiblingIndex(1);
            y -= 48f;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var row = ordered[i];
            if (row.parent != listParent)
                row.SetParent(listParent, false);
            PlaceRect(row as RectTransform, x, y, width, rowHeight);
            row.SetSiblingIndex(sectionLabelTf != null ? i + 2 : i + 1);
            y -= rowHeight + rowGap;
        }

        if (built.Count > 0)
            rows = built.ToArray();

        if (statusText == null)
        {
            var status = FindDeep(root, "StatusText");
            if (status != null)
                statusText = status.GetComponent<TextMeshProUGUI>();
        }

        if (statusText != null)
        {
            var statusRt = statusText.rectTransform;
            if (statusRt.parent != listParent)
                statusRt.SetParent(listParent, false);
            PlaceRect(statusRt, x, y - 8f, width, 48f);
            statusRt.SetAsLastSibling();
        }

        var layout = listParent.GetComponent<RectTransform>();
        if (layout != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(layout);
    }

    static void PlaceRect(RectTransform rt, float x, float y, float width, float height)
    {
        if (rt == null)
            return;

        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(width, height);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    void RemoveDuplicateRows(Transform root)
    {
        for (var s = 0; s < WearOrderSpecs.Length; s++)
        {
            var name = WearOrderSpecs[s].ObjectName;
            var matches = new List<Transform>();
            CollectByName(root, name, matches);
            // Keep the first, destroy extras (usually runtime Instantiates).
            for (var i = 1; i < matches.Count; i++)
            {
                if (matches[i] != null)
                    Destroy(matches[i].gameObject);
            }
        }
    }

    static void CollectByName(Transform root, string objectName, List<Transform> results)
    {
        if (root == null)
            return;
        if (root.name == objectName)
            results.Add(root);
        for (var i = 0; i < root.childCount; i++)
            CollectByName(root.GetChild(i), objectName, results);
    }

    Transform CreateRuntimeRow(Transform parent, WearOrderSpec spec)
    {
        if (parent == null)
            return null;

        var searchRoot = contentRoot != null ? contentRoot.transform : transform;
        Transform template = FindDeep(searchRoot, "ChemicalSuitRow");
        if (template == null)
            template = FindDeep(searchRoot, "FaceShieldRow");

        GameObject go;
        if (template != null)
        {
            go = Instantiate(template.gameObject, parent, false);
            go.name = spec.ObjectName;
            go.SetActive(true);
        }
        else
        {
            go = new GameObject(spec.ObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var indicator = new GameObject("Indicator", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            indicator.transform.SetParent(go.transform, false);
            var mark = new GameObject("Mark", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            mark.transform.SetParent(indicator.transform, false);
            var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            label.transform.SetParent(go.transform, false);
        }

        var labelTmp = go.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
        if (labelTmp != null)
            labelTmp.text = spec.Label;

        return go.transform;
    }

    static Transform FindDeep(Transform root, string objectName)
    {
        if (root == null)
            return null;
        if (root.name == objectName)
            return root;

        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }

    void EnsureUiInteraction()
    {
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        if (GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
    }

    void EnsureToggleButton()
    {
        if (contentRoot == null)
            return;

        if (toggleVisibilityButton != null)
            return;

        var existing = contentRoot.transform.Find("ToggleVisibilityButton");
        if (existing != null)
        {
            toggleVisibilityButton = existing.GetComponent<Button>();
            if (toggleButtonLabel == null)
            {
                var label = existing.Find("Label");
                if (label != null)
                    toggleButtonLabel = label.GetComponent<TextMeshProUGUI>();
            }

            return;
        }

        var btnGo = new GameObject("ToggleVisibilityButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(contentRoot.transform, false);

        var rect = btnGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-28f, -24f);
        rect.sizeDelta = new Vector2(150f, 56f);

        var image = btnGo.GetComponent<Image>();
        image.color = new Color(0.18f, 0.42f, 0.62f, 1f);
        image.raycastTarget = true;

        toggleVisibilityButton = btnGo.GetComponent<Button>();
        toggleVisibilityButton.targetGraphic = image;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(btnGo.transform, false);
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        toggleButtonLabel = labelGo.GetComponent<TextMeshProUGUI>();
        toggleButtonLabel.text = LabelHide;
        toggleButtonLabel.fontSize = 28f;
        toggleButtonLabel.alignment = TextAlignmentOptions.Center;
        toggleButtonLabel.color = Color.white;
        toggleButtonLabel.raycastTarget = false;

        btnGo.transform.SetAsLastSibling();
    }

    void EnsureToggleAction()
    {
        if (GetToggleAction() != null)
            return;

        ownedToggleAction = new InputAction(
            name: "PPE Checklist Toggle",
            type: InputActionType.Button);
        ownedToggleAction.AddBinding("<XRController>{LeftHand}/{SecondaryButton}");
        ownedToggleAction.AddBinding("<OculusTouchController>{LeftHand}/secondaryButton");
    }

    InputAction GetToggleAction()
    {
        if (toggleVisibilityAction.reference != null)
            return toggleVisibilityAction.action;
        if (toggleVisibilityAction.action != null && toggleVisibilityAction.action.bindings.Count > 0)
            return toggleVisibilityAction.action;
        return ownedToggleAction;
    }

    void WireToggleButton(bool subscribe)
    {
        if (toggleVisibilityButton == null)
            return;

        toggleVisibilityButton.onClick.RemoveListener(ToggleContentVisibility);
        if (subscribe)
            toggleVisibilityButton.onClick.AddListener(ToggleContentVisibility);
    }

    void WireToggleAction(bool subscribe)
    {
        var action = GetToggleAction();
        if (action == null)
            return;

        if (toggleSubscribed)
        {
            action.performed -= OnTogglePerformed;
            if (ownedToggleAction != null)
                action.Disable();
            toggleSubscribed = false;
        }

        if (!subscribe)
            return;

        action.performed += OnTogglePerformed;
        action.Enable();
        toggleSubscribed = true;
    }

    void OnTogglePerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;
        ToggleContentVisibility();
    }

    void ResolveManagers()
    {
        if (equipmentManager == null)
        {
            equipmentManager = PPEEquipmentManager.Instance != null
                ? PPEEquipmentManager.Instance
                : FindAnyObjectByType<PPEEquipmentManager>();
        }

        if (responseEquipmentManager == null)
        {
            responseEquipmentManager = ResponseEquipmentManager.Instance != null
                ? ResponseEquipmentManager.Instance
                : FindAnyObjectByType<ResponseEquipmentManager>();
        }
    }

    void TrySubscribe()
    {
        ResolveManagers();

        if (equipmentManager != null && subscribedEquipment != equipmentManager)
        {
            UnsubscribeEquipment();
            equipmentManager.EquipmentSucceeded += OnEquipmentSucceeded;
            equipmentManager.PreparationChanged += OnPreparationChanged;
            equipmentManager.StateChanged += OnStateChanged;
            subscribedEquipment = equipmentManager;
        }

        if (responseEquipmentManager != null && subscribedResponse != responseEquipmentManager)
        {
            UnsubscribeResponse();
            responseEquipmentManager.StateChanged += OnResponseStateChanged;
            responseEquipmentManager.OnResponseBagPreparedChanged += OnResponseBagPreparedChanged;
            subscribedResponse = responseEquipmentManager;
        }
    }

    void Unsubscribe()
    {
        UnsubscribeEquipment();
        UnsubscribeResponse();
    }

    void UnsubscribeEquipment()
    {
        if (subscribedEquipment == null)
            return;
        subscribedEquipment.EquipmentSucceeded -= OnEquipmentSucceeded;
        subscribedEquipment.PreparationChanged -= OnPreparationChanged;
        subscribedEquipment.StateChanged -= OnStateChanged;
        subscribedEquipment = null;
    }

    void UnsubscribeResponse()
    {
        if (subscribedResponse == null)
            return;
        subscribedResponse.StateChanged -= OnResponseStateChanged;
        subscribedResponse.OnResponseBagPreparedChanged -= OnResponseBagPreparedChanged;
        subscribedResponse = null;
    }

    void OnEquipmentSucceeded(PPEEquipable _) => RefreshFromManager();
    void OnPreparationChanged(string _, bool __) => RefreshFromManager();
    void OnStateChanged() => RefreshFromManager();
    void OnResponseStateChanged() => RefreshFromManager();
    void OnResponseBagPreparedChanged(bool _) => RefreshFromManager();

    public void RefreshFromManager()
    {
        ResolveManagers();

        var done = 0;
        var total = 0;

        if (rows != null)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                if (row == null)
                    continue;

                if (row.source != ChecklistSource.ResponseBagPrepared
                    && string.IsNullOrWhiteSpace(row.itemId))
                    continue;

                var complete = IsRowComplete(row);
                ApplyRowVisual(row, complete);

                total++;
                if (complete)
                    done++;
            }
        }

        if (statusText != null)
            statusText.text = string.Format("\uCC29\uC6A9 \uC21C\uC11C {0}/{1}", done, total);
    }

    bool IsRowComplete(ChecklistRow row)
    {
        if (row == null)
            return false;

        switch (row.source)
        {
            case ChecklistSource.ResponseBagPrepared:
                return responseEquipmentManager != null
                    && responseEquipmentManager.ResponseBagPrepared;

            case ChecklistSource.Prepared:
                return equipmentManager != null
                    && equipmentManager.IsPrepared(row.itemId);

            case ChecklistSource.Equipped:
                return equipmentManager != null
                    && equipmentManager.IsEquipped(row.itemId);

            default:
                return false;
        }
    }

    void ApplyRowVisual(ChecklistRow row, bool complete)
    {
        if (row == null)
            return;

        if (row.indicator != null)
            row.indicator.color = complete ? completeIndicatorColor : incompleteIndicatorColor;

        if (row.indicatorMark != null)
        {
            row.indicatorMark.text = complete ? completeMark : incompleteMark;
            row.indicatorMark.color = complete ? completeMarkColor : incompleteMarkColor;
        }
    }

    void ApplyHangulFontIfAvailable()
    {
        var font = Resources.Load<TMP_FontAsset>("PPE/PPE_HangulFont");
        if (font == null)
            return;

        // Ensure digits / new hangul glyphs exist (dynamic atlas).
        var needed = new StringBuilder(256);
        needed.Append("0123456789. ");
        needed.Append(LabelHide).Append(LabelShow);
        needed.Append("\uCC29\uC6A9 \uC21C\uC11C");
        for (var i = 0; i < WearOrderSpecs.Length; i++)
            needed.Append(WearOrderSpecs[i].Label);
        needed.Append(incompleteMark).Append(completeMark);
        font.TryAddCharacters(needed.ToString());

        var fallback = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (fallback != null
            && (font.fallbackFontAssetTable == null || !font.fallbackFontAssetTable.Contains(fallback)))
        {
            if (font.fallbackFontAssetTable == null)
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            font.fallbackFontAssetTable.Add(fallback);
        }

        var texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (var i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null)
                continue;
            texts[i].font = font;
            if (font.material != null)
                texts[i].fontSharedMaterial = font.material;
            texts[i].SetAllDirty();
        }
    }
}
