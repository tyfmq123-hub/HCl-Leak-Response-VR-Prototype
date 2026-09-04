using System;
using System.Collections.Generic;
using System.Text;
using HClTraining.SharedXR;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// World-space preparation checklist. Displays manager state only -
/// never stores its own completion flags.
/// Rows are grouped into PPE donning and response-equipment preparation.
/// Toggle visibility via on-panel button or left-controller X (PrimaryButton).
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
    [Tooltip("Panel body that is shown/hidden. Canvas stays active so X can reopen.")]
    [SerializeField] GameObject contentRoot;
    [SerializeField] Button toggleVisibilityButton;
    [SerializeField] TextMeshProUGUI toggleButtonLabel;
    [SerializeField] bool startVisible = true;

    [Header("Toggle Input (Left X)")]
    [Tooltip("Defaults to Left-hand PrimaryButton (Quest X).")]
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
    SharedXRInputContract sharedInput;
    bool toggleSubscribed;
    bool contentVisible = true;

    // Treat X as the controller operation hint, not a cancel marker.
    static readonly string LabelHide = "X\uB85C \uB2EB\uAE30";
    static readonly string LabelShow = "\uC5F4\uAE30";

    struct WearOrderSpec
    {
        public string ObjectName;
        public string Label;
        public string ItemId;
        public ChecklistSource Source;
        public bool RequireConfiguredSequenceStep;
        public PPESequenceStep SequenceStep;
        public bool EquipmentPreparation;
    }

    /// <summary>
    /// Checklist rows grouped by training phase. Labels stay in the room Hangul atlas.
    /// </summary>
    static readonly WearOrderSpec[] WearOrderSpecs =
    {
        new WearOrderSpec
        {
            ObjectName = "HelmetRow",
            Label = "안전모",
            ItemId = PPEEquipmentManager.IdHelmet,
            Source = ChecklistSource.Equipped,
            RequireConfiguredSequenceStep = true,
            SequenceStep = PPESequenceStep.Helmet,
        },
        new WearOrderSpec
        {
            ObjectName = "InnerGlovesRow",
            Label = "속장갑",
            ItemId = PPESequenceManager.IdInnerGloves,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "ChemicalSuitRow",
            Label = "화학보호복",
            ItemId = PPEEquipmentManager.IdChemicalSuit,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "ChemicalBootsRow",
            Label = "화학보호 장화",
            ItemId = PPEEquipmentManager.IdChemicalBoots,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "FaceRespiratorRow",
            Label = "전면형 호흡보호구",
            ItemId = PPEEquipmentManager.IdFaceRespirator,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "FaceShieldRow",
            Label = "안면보호구",
            ItemId = PPEEquipmentManager.IdFaceShield,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "ChemicalGlovesRow",
            Label = "내화학 보호장갑",
            ItemId = PPEEquipmentManager.IdChemicalGloves,
            Source = ChecklistSource.Equipped,
        },
        new WearOrderSpec
        {
            ObjectName = "ResponseBagRow",
            Label = "대응가방 확인",
            ItemId = PPEEquipmentManager.IdResponseBag,
            Source = ChecklistSource.ResponseBagPrepared,
            EquipmentPreparation = true,
        },
        new WearOrderSpec
        {
            ObjectName = "WasteContainerRow",
            Label = "유해폐기물 수거용기 준비",
            ItemId = PPEEquipmentManager.IdWasteContainer,
            Source = ChecklistSource.Prepared,
            EquipmentPreparation = true,
        },
    };

    static readonly string DonningSectionTitle = "PPE 착용";
    static readonly string EquipmentSectionTitle = "대응 장비 준비";

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
        sharedInput = SharedXRInputContract.Instance;
        if (sharedInput != null)
            sharedInput.Performed += OnSharedInputPerformed;
        else
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
        if (sharedInput != null)
            sharedInput.Performed -= OnSharedInputPerformed;
        sharedInput = null;
        WireToggleAction(false);
    }

    void OnDestroy()
    {
        Unsubscribe();
        WireToggleButton(false);
        if (sharedInput != null)
            sharedInput.Performed -= OnSharedInputPerformed;
        sharedInput = null;
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
    /// Force hierarchy + labels into two phase groups.
    /// Scene rows use absolute anchored positions (no VerticalLayoutGroup), so sibling
    /// reordering alone does nothing — we rewrite Y positions and section labels.
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
            sectionLabel.text = DonningSectionTitle;

        var equipmentSection = FindDeep(root, "EquipmentSection");
        if (equipmentSection != null)
            equipmentSection.gameObject.SetActive(true);

        var equipmentLabelTf = FindDeep(root, "EquipmentSectionLabel");
        if (equipmentLabelTf != null)
        {
            equipmentLabelTf.gameObject.SetActive(true);
            if (equipmentLabelTf.parent != listParent)
                equipmentLabelTf.SetParent(listParent, false);
            var equipmentLabel = equipmentLabelTf.GetComponent<TextMeshProUGUI>();
            if (equipmentLabel != null)
                equipmentLabel.text = EquipmentSectionTitle;
        }

        RemoveDuplicateRows(root);

        var built = new List<ChecklistRow>(WearOrderSpecs.Length);
        var donningRows = new List<Transform>();
        var equipmentRows = new List<Transform>();
        var sequence = PPESequenceManager.Resolve();

        for (var i = 0; i < WearOrderSpecs.Length; i++)
        {
            var spec = WearOrderSpecs[i];
            var mapsToSequence = PPESequenceManager.TryGetStep(spec.ItemId, out var mappedStep);
            var excludeOptional = spec.RequireConfiguredSequenceStep &&
                                  (sequence == null || !sequence.ContainsStep(spec.SequenceStep));
            var excludeFromConfiguredOrder = !spec.RequireConfiguredSequenceStep &&
                                             mapsToSequence && sequence != null &&
                                             !sequence.ContainsStep(mappedStep);
            if (excludeOptional || excludeFromConfiguredOrder)
            {
                var unusedRow = FindDeep(root, spec.ObjectName);
                if (unusedRow != null)
                    unusedRow.gameObject.SetActive(false);
                continue;
            }

            var rowRoot = FindDeep(root, spec.ObjectName);
            if (rowRoot == null)
                rowRoot = CreateRuntimeRow(listParent, spec);
            if (rowRoot == null)
                continue;

            rowRoot.gameObject.SetActive(true);
            if (spec.EquipmentPreparation)
                equipmentRows.Add(rowRoot);
            else
                donningRows.Add(rowRoot);

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

        const float x = 36f;
        const float width = 708f;
        const float rowHeight = 72f;
        const float rowGap = 6f;
        var y = -100f;
        var sibling = 1;

        if (sectionLabelTf != null)
        {
            if (sectionLabelTf.parent != listParent)
                sectionLabelTf.SetParent(listParent, false);
            PlaceRect(sectionLabelTf as RectTransform, x, y, width, 40f);
            sectionLabelTf.SetSiblingIndex(sibling++);
            y -= 48f;
        }

        for (var i = 0; i < donningRows.Count; i++)
        {
            var row = donningRows[i];
            if (row.parent != listParent)
                row.SetParent(listParent, false);
            PlaceRect(row as RectTransform, x, y, width, rowHeight);
            row.SetSiblingIndex(sibling++);
            y -= rowHeight + rowGap;
        }

        y -= 12f;
        if (equipmentLabelTf != null)
        {
            PlaceRect(equipmentLabelTf as RectTransform, x, y, width, 40f);
            equipmentLabelTf.SetSiblingIndex(sibling++);
            y -= 48f;
        }

        for (var i = 0; i < equipmentRows.Count; i++)
        {
            var row = equipmentRows[i];
            if (row.parent != listParent)
                row.SetParent(listParent, false);
            PlaceRect(row as RectTransform, x, y, width, rowHeight);
            row.SetSiblingIndex(sibling++);
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
        ownedToggleAction.AddBinding("<XRController>{LeftHand}/{PrimaryButton}");
        ownedToggleAction.AddBinding("<OculusTouchController>{LeftHand}/primaryButton");
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

    void OnSharedInputPerformed(SharedXRHand hand, SharedXRControl control)
    {
        if (hand == SharedXRHand.Left && control == SharedXRControl.PrimaryButton)
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

        var donningDone = 0;
        var donningTotal = 0;
        var equipmentDone = 0;
        var equipmentTotal = 0;

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

                if (IsEquipmentPreparationRow(row))
                {
                    equipmentTotal++;
                    if (complete)
                        equipmentDone++;
                }
                else
                {
                    donningTotal++;
                    if (complete)
                        donningDone++;
                }
            }
        }

        if (statusText != null)
        {
            statusText.text = string.Format(
                "{0} {1}/{2}  |  {3} {4}/{5}",
                DonningSectionTitle,
                donningDone,
                donningTotal,
                EquipmentSectionTitle,
                equipmentDone,
                equipmentTotal);
        }
    }

    static bool IsEquipmentPreparationRow(ChecklistRow row)
    {
        if (row == null)
            return false;
        return row.source == ChecklistSource.ResponseBagPrepared
               || row.source == ChecklistSource.Prepared;
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
        needed.Append(DonningSectionTitle).Append(EquipmentSectionTitle);
        needed.Append(" | ");
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
