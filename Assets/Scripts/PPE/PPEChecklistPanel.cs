using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// World-space preparation checklist. Displays manager state only -
/// never stores its own completion flags.
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

    public bool IsContentVisible => contentVisible;

    void Awake()
    {
        EnsureUiRefs();
        EnsureRowsWired();
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

    /// <summary>UI / Y: flip checklist panel visibility.</summary>
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
    /// Rebuild row bindings from hierarchy when scene serialization is missing/corrupt.
    /// </summary>
    void EnsureRowsWired()
    {
        if (rows != null && rows.Length > 0 && HasAnyRowVisual(rows))
            return;

        var root = contentRoot != null ? contentRoot.transform : transform;
        var built = new System.Collections.Generic.List<ChecklistRow>(8);
        TryAddRow(built, root, "FaceShieldRow", PPEEquipmentManager.IdFaceShield, ChecklistSource.Equipped);
        TryAddRow(built, root, "FaceRespiratorRow", PPEEquipmentManager.IdFaceRespirator, ChecklistSource.Equipped);
        TryAddRow(built, root, "ChemicalSuitRow", PPEEquipmentManager.IdChemicalSuit, ChecklistSource.Equipped);
        TryAddRow(built, root, "ChemicalGlovesRow", PPEEquipmentManager.IdChemicalGloves, ChecklistSource.Equipped);
        TryAddRow(built, root, "ChemicalBootsRow", PPEEquipmentManager.IdChemicalBoots, ChecklistSource.Equipped);
        TryAddRow(built, root, "ResponseBagRow", PPEEquipmentManager.IdResponseBag, ChecklistSource.ResponseBagPrepared);
        TryAddRow(built, root, "WasteContainerRow", PPEEquipmentManager.IdWasteContainer, ChecklistSource.Prepared);

        if (built.Count > 0)
            rows = built.ToArray();

        if (statusText == null)
        {
            var status = FindDeep(root, "StatusText");
            if (status != null)
                statusText = status.GetComponent<TextMeshProUGUI>();
        }
    }

    static bool HasAnyRowVisual(ChecklistRow[] list)
    {
        for (var i = 0; i < list.Length; i++)
        {
            var row = list[i];
            if (row != null && (row.indicator != null || row.indicatorMark != null || row.labelText != null))
                return true;
        }

        return false;
    }

    static void TryAddRow(
        System.Collections.Generic.List<ChecklistRow> list,
        Transform root,
        string rowObjectName,
        string itemId,
        ChecklistSource source)
    {
        var rowRoot = FindDeep(root, rowObjectName);
        if (rowRoot == null)
            return;

        var label = rowRoot.Find("Label")?.GetComponent<TextMeshProUGUI>();
        var indicator = rowRoot.Find("Indicator")?.GetComponent<Image>();
        var mark = rowRoot.Find("Indicator/Mark")?.GetComponent<TextMeshProUGUI>();
        list.Add(new ChecklistRow
        {
            itemId = itemId,
            source = source,
            labelText = label,
            indicator = indicator,
            indicatorMark = mark,
        });
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

        var font = Resources.Load<TMP_FontAsset>("PPE/PPE_HangulFont");
        if (font != null && font.material != null)
        {
            toggleButtonLabel.font = font;
            toggleButtonLabel.fontSharedMaterial = font.material;
        }

        // Keep button above other panel content in hierarchy draw/layout order.
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

        // While inspecting the world bag (pre-equip), Left Y reveals bag contents instead of the checklist.
        var response = ResponseEquipmentManager.Instance != null
            ? ResponseEquipmentManager.Instance
            : FindAnyObjectByType<ResponseEquipmentManager>();
        if (response != null)
        {
            if (response.IsWorldBagCurrentlySelected() && !response.ResponseBagPrepared)
                return;
        }

        ToggleContentVisibility();
    }

    void TrySubscribe()
    {
        ResolveManagers();

        if (equipmentManager != null && subscribedEquipment != equipmentManager)
        {
            if (subscribedEquipment != null)
            {
                subscribedEquipment.EquipmentSucceeded -= OnEquipmentSucceeded;
                subscribedEquipment.PreparationChanged -= OnPreparationChanged;
                subscribedEquipment.StateChanged -= OnStateChanged;
            }

            equipmentManager.EquipmentSucceeded += OnEquipmentSucceeded;
            equipmentManager.PreparationChanged += OnPreparationChanged;
            equipmentManager.StateChanged += OnStateChanged;
            subscribedEquipment = equipmentManager;
        }

        if (responseEquipmentManager != null && subscribedResponse != responseEquipmentManager)
        {
            if (subscribedResponse != null)
            {
                subscribedResponse.StateChanged -= OnResponseStateChanged;
                subscribedResponse.OnResponseBagPreparedChanged -= OnResponseBagPreparedChanged;
            }

            responseEquipmentManager.StateChanged += OnResponseStateChanged;
            responseEquipmentManager.OnResponseBagPreparedChanged += OnResponseBagPreparedChanged;
            subscribedResponse = responseEquipmentManager;
        }
    }

    void Unsubscribe()
    {
        if (subscribedEquipment != null)
        {
            subscribedEquipment.EquipmentSucceeded -= OnEquipmentSucceeded;
            subscribedEquipment.PreparationChanged -= OnPreparationChanged;
            subscribedEquipment.StateChanged -= OnStateChanged;
            subscribedEquipment = null;
        }

        if (subscribedResponse != null)
        {
            subscribedResponse.StateChanged -= OnResponseStateChanged;
            subscribedResponse.OnResponseBagPreparedChanged -= OnResponseBagPreparedChanged;
            subscribedResponse = null;
        }
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

    void OnEquipmentSucceeded(PPEEquipable _) => RefreshFromManager();
    void OnPreparationChanged(string _, bool __) => RefreshFromManager();
    void OnStateChanged() => RefreshFromManager();
    void OnResponseStateChanged() => RefreshFromManager();
    void OnResponseBagPreparedChanged(bool _) => RefreshFromManager();

    public void RefreshFromManager()
    {
        ResolveManagers();

        var ppeDone = 0;
        var ppeTotal = 0;
        var equipDone = 0;
        var equipTotal = 0;

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

                if (row.source == ChecklistSource.Equipped)
                {
                    ppeTotal++;
                    if (complete)
                        ppeDone++;
                }
                else
                {
                    equipTotal++;
                    if (complete)
                        equipDone++;
                }
            }
        }

        if (statusText != null)
        {
            statusText.text = string.Format(
                "PPE {0}/{1}  \u00B7  \uB300\uC751\uC7A5\uBE44 {2}/{3}",
                ppeDone, ppeTotal, equipDone, equipTotal);
        }
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
            default:
                return equipmentManager != null
                    && equipmentManager.IsEquipped(row.itemId);
        }
    }

    void ApplyRowVisual(ChecklistRow row, bool complete)
    {
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
        if (font == null || font.material == null)
            return;

        var texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (var i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null)
                continue;
            texts[i].font = font;
            texts[i].fontSharedMaterial = font.material;
        }
    }
}
