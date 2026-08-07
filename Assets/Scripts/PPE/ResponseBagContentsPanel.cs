using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// World-space contents inspection panel for the chemical response bag.
/// Inspection truth lives in <see cref="ResponseEquipmentManager"/>.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagContentsPanel : MonoBehaviour
{
    [Serializable]
    public class ContentRowUi
    {
        public string itemId;
        public Button rowButton;
        public Image background;
        public TextMeshProUGUI labelText;
        public TextMeshProUGUI checkText;
        public Image highlight;
    }

    [Header("Manager")]
    [SerializeField] ResponseEquipmentManager equipmentManager;

    [Header("Roots")]
    [SerializeField] GameObject panelRoot;
    [SerializeField] RectTransform panelRect;
    [SerializeField] Transform faceTarget;

    [Header("Entries (preview prefabs assigned in Inspector)")]
    [SerializeField] ResponseBagContentEntry[] entries = CreateDefaultEntries();

    [Header("Rows")]
    [SerializeField] ContentRowUi[] rows;

    [Header("Detail")]
    [SerializeField] TextMeshProUGUI selectedNameText;
    [SerializeField] TextMeshProUGUI selectedDescriptionText;
    [SerializeField] TextMeshProUGUI progressText;
    [SerializeField] RawImage previewImage;

    [Header("Buttons")]
    [SerializeField] Button completeButton;
    [SerializeField] Button closeButton;
    [SerializeField] TextMeshProUGUI completeButtonLabel;

    [Header("Preview")]
    [SerializeField] ResponseBagItemPreviewController previewController;

    [Header("Colors")]
    [SerializeField] Color rowNormal = new Color(0.18f, 0.22f, 0.28f, 0.92f);
    [SerializeField] Color rowSelected = new Color(0.72f, 0.62f, 0.12f, 0.95f);
    [SerializeField] Color rowCompletedTint = new Color(0.16f, 0.38f, 0.24f, 0.92f);

    ResponseEquipmentManager subscribedManager;
    bool wiredButtons;
    int selectedIndex = -1;
    bool completing;
    bool pointerOverPanel;

    static readonly string IncompleteConfirmMessage =
        "\uB300\uC751\uAC00\uBC29 \uB0B4\uBD80 \uC7A5\uBE44\uB97C \uBA3C\uC800 \uD655\uC778\uD558\uC2ED\uC2DC\uC624.";

    public bool IsOpen =>
        panelRoot != null ? panelRoot.activeInHierarchy : gameObject.activeInHierarchy;

    /// <summary>True while a higher-priority modal blocks gaze open of this panel as "another modal".</summary>
    public bool IsModalBlockingOtherUi => false;

    public bool ShouldKeepOpenDespiteLookAway =>
        pointerOverPanel || selectedIndex >= 0 || IsPointerOverUi();

    public ResponseBagContentEntry[] Entries => entries;

    static ResponseBagContentEntry[] CreateDefaultEntries()
    {
        return new[]
        {
            new ResponseBagContentEntry
            {
                ItemId = ResponseEquipmentManager.IdGasDetector,
                KoreanDisplayName = "\uD734\uB300\uC6A9 HCl \uAC00\uC2A4\uCE21\uC815\uAE30",
                KoreanDescription =
                    "\uC8FC\uBCC0 HCl \uB18D\uB3C4\uB97C \uCE21\uC815\uD558\uC5EC \uD604\uC7A5 \uC811\uADFC \uAC00\uB2A5 \uC5EC\uBD80\uC640 \uC791\uC5C5 \uC911 \uB300\uAE30 \uC0C1\uD0DC\uB97C \uD655\uC778\uD569\uB2C8\uB2E4.",
            },
            new ResponseBagContentEntry
            {
                ItemId = ResponseEquipmentManager.IdAbsorbentSock,
                KoreanDisplayName = "\uCF00\uBBF8\uCEEC \uD761\uCC29\uC0AD",
                KoreanDescription =
                    "\uB204\uCD9C\uC561\uC758 \uD655\uC0B0 \uACBD\uACC4\uB97C \uB9CC\uB4E4\uACE0 \uBC30\uC218\uAD6C \uB610\uB294 \uBE44\uC624\uC5FC \uAD6C\uC5ED\uC73C\uB85C \uBC88\uC9C0\uB294 \uAC83\uC744 \uBC29\uC9C0\uD569\uB2C8\uB2E4.",
            },
            new ResponseBagContentEntry
            {
                ItemId = ResponseEquipmentManager.IdAbsorbentPad,
                KoreanDisplayName = "\uD654\uD559\uBB3C\uC9C8\uC6A9 \uD761\uCC29\uD328\uB4DC",
                KoreanDescription =
                    "\uD761\uCC29\uC0AD \uB0B4\uBD80\uC5D0 \uB0A8\uC544 \uC788\uB294 \uB204\uCD9C\uC561\uC744 \uD761\uC218\uD558\uACE0 \uC794\uB958 \uC624\uC5FC\uC744 \uC81C\uAC70\uD558\uB294 \uB370 \uC0AC\uC6A9\uD569\uB2C8\uB2E4.",
            },
        };
    }

    void Awake()
    {
        if (entries == null || entries.Length == 0)
            entries = CreateDefaultEntries();

        if (panelRoot == null)
            panelRoot = gameObject;

        ResolveManager();
        WireButtons();
        ApplyHangulFontIfAvailable();
        HidePanel();
        RefreshFromManager();
    }

    void OnEnable()
    {
        TrySubscribe();
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
        pointerOverPanel = false;
    }

    void OnDestroy()
    {
        Unsubscribe();
        UnwireButtons();
    }

    void LateUpdate()
    {
        if (!IsOpen)
            return;

        FaceCameraYawOnly();
        pointerOverPanel = IsPointerOverUi();
    }

    public void ShowPanel()
    {
        // Legacy contents panel removed — use ResponseBagGearPanel pre-equip inspect.
        return;
    }

    static void ClearNonBagPpeSelection()
    {
        var selection = PPESelectionManager.Instance != null
            ? PPESelectionManager.Instance
            : FindAnyObjectByType<PPESelectionManager>();
        if (selection == null || selection.CurrentSelectedPPE == null)
            return;

        var current = selection.CurrentSelectedPPE;
        var isBag = current.GetComponent<ResponseBagPrepareInteractable>() != null
            || current.GetComponentInParent<ResponseBagPrepareInteractable>() != null;
        if (!isBag)
            selection.ClearSelection();
    }

    public void HidePanel()
    {
        if (previewController != null)
            previewController.DeactivateStage();

        if (panelRoot != null && panelRoot != gameObject)
            panelRoot.SetActive(false);
        else
            gameObject.SetActive(false);

        pointerOverPanel = false;
    }

    public void FocusPanel()
    {
        if (!IsOpen)
            ShowPanel();
    }

    public void ShowIncompleteContentsPrompt()
    {
        FocusPanel();
        var feedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        if (feedback != null)
            feedback.ShowMessage(IncompleteConfirmMessage);
        else
            Debug.Log("[Response Bag] " + IncompleteConfirmMessage, this);
    }

    public void ResetPanelVisuals()
    {
        selectedIndex = -1;
        if (previewController != null)
            previewController.ClearPreview();
        HidePanel();
        RefreshFromManager();
    }

    public void EditorWire(
        ResponseEquipmentManager manager,
        GameObject root,
        ContentRowUi[] contentRows,
        Button complete,
        Button close,
        TextMeshProUGUI completeLabel,
        TextMeshProUGUI selectedName,
        TextMeshProUGUI selectedDescription,
        TextMeshProUGUI progress,
        RawImage preview,
        ResponseBagItemPreviewController previewCtrl,
        ResponseBagContentEntry[] contentEntries)
    {
        equipmentManager = manager;
        panelRoot = root;
        rows = contentRows;
        completeButton = complete;
        closeButton = close;
        completeButtonLabel = completeLabel;
        selectedNameText = selectedName;
        selectedDescriptionText = selectedDescription;
        progressText = progress;
        previewImage = preview;
        previewController = previewCtrl;
        if (contentEntries != null && contentEntries.Length > 0)
            entries = contentEntries;
    }

    void TrySubscribe()
    {
        ResolveManager();
        if (equipmentManager == null)
            return;

        if (subscribedManager == equipmentManager)
            return;

        Unsubscribe();
        equipmentManager.StateChanged += OnStateChanged;
        equipmentManager.OnResponseBagContentsChanged += OnStateChanged;
        subscribedManager = equipmentManager;
    }

    void Unsubscribe()
    {
        if (subscribedManager == null)
            return;

        subscribedManager.StateChanged -= OnStateChanged;
        subscribedManager.OnResponseBagContentsChanged -= OnStateChanged;
        subscribedManager = null;
    }

    void ResolveManager()
    {
        if (equipmentManager != null)
            return;

        equipmentManager = ResponseEquipmentManager.Instance != null
            ? ResponseEquipmentManager.Instance
            : FindAnyObjectByType<ResponseEquipmentManager>();
    }

    void OnStateChanged() => RefreshFromManager();

    void WireButtons()
    {
        if (wiredButtons)
            return;

        if (rows != null)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var index = i;
                var row = rows[i];
                if (row == null || row.rowButton == null)
                    continue;
                row.rowButton.onClick.AddListener(() => SelectEntry(index));
            }
        }

        if (completeButton != null)
            completeButton.onClick.AddListener(OnCompleteClicked);
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);

        wiredButtons = true;
    }

    void UnwireButtons()
    {
        if (!wiredButtons)
            return;

        if (rows != null)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i] != null && rows[i].rowButton != null)
                    rows[i].rowButton.onClick.RemoveAllListeners();
            }
        }

        if (completeButton != null)
            completeButton.onClick.RemoveAllListeners();
        if (closeButton != null)
            closeButton.onClick.RemoveAllListeners();

        wiredButtons = false;
    }

    void SelectEntry(int index)
    {
        ResolveManager();
        if (entries == null || index < 0 || index >= entries.Length)
            return;

        selectedIndex = index;
        var entry = entries[index];
        if (entry == null)
            return;

        if (equipmentManager != null)
            equipmentManager.InspectResponseBagItem(entry.ItemId);

        if (selectedNameText != null)
            selectedNameText.text = entry.KoreanDisplayName;
        if (selectedDescriptionText != null)
            selectedDescriptionText.text = entry.KoreanDescription;

        if (previewController != null)
        {
            if (entry.PreviewPrefab == null)
                Debug.LogWarning(
                    $"[Response Bag Contents] PreviewPrefab not assigned for '{entry.ItemId}'.",
                    this);
            previewController.ShowPrefab(entry.PreviewPrefab);
        }

        ApplySelectionVisuals();
        RefreshFromManager();
    }

    void OnCompleteClicked()
    {
        if (completing)
            return;

        ResolveManager();
        if (equipmentManager == null)
            return;

        completing = true;
        try
        {
            if (!equipmentManager.TryCompleteResponseBagPreparation())
                return;

            HidePanel();
            RefreshFromManager();
        }
        finally
        {
            completing = false;
        }
    }

    void OnCloseClicked()
    {
        HidePanel();
    }

    public void RefreshFromManager()
    {
        ResolveManager();
        SyncEntryInspectedFlags();
        ApplySelectionVisuals();

        var inspectedCount = 0;
        if (entries != null)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].IsInspected)
                    inspectedCount++;
            }
        }

        if (progressText != null)
            progressText.text = $"\uD655\uC778 \uC644\uB8CC {inspectedCount} / 3";

        if (completeButton != null)
        {
            var canComplete = equipmentManager != null
                && !equipmentManager.ResponseBagPrepared
                && equipmentManager.ResponseBagContentsVerified;
            completeButton.interactable = canComplete;
        }

        if (completeButtonLabel != null)
            completeButtonLabel.text = "\uC900\uBE44 \uC644\uB8CC";
    }

    void SyncEntryInspectedFlags()
    {
        if (entries == null)
            return;

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry == null)
                continue;
            entry.IsInspected = equipmentManager != null
                && equipmentManager.IsResponseBagItemInspected(entry.ItemId);
        }
    }

    void ApplySelectionVisuals()
    {
        if (rows == null || entries == null)
            return;

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row == null)
                continue;

            var entry = i < entries.Length ? entries[i] : null;
            var inspected = entry != null && entry.IsInspected;
            var selected = i == selectedIndex;

            if (row.labelText != null && entry != null)
                row.labelText.text = entry.KoreanDisplayName;

            if (row.checkText != null)
                row.checkText.text = inspected ? "\u2713" : "\u25A1";

            if (row.highlight != null)
                row.highlight.enabled = selected;

            if (row.background != null)
            {
                if (selected)
                    row.background.color = rowSelected;
                else if (inspected)
                    row.background.color = rowCompletedTint;
                else
                    row.background.color = rowNormal;
            }
        }
    }

    void FaceCameraYawOnly()
    {
        var cam = Camera.main;
        var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin != null && origin.Camera != null)
            cam = origin.Camera;
        if (cam == null)
            return;

        var t = faceTarget != null ? faceTarget : (panelRect != null ? (Transform)panelRect : transform);
        var toCam = cam.transform.position - t.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 0.0001f)
            return;
        t.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
    }

    static bool IsPointerOverUi()
    {
        if (EventSystem.current == null)
            return false;

        // XR UI modules update hovered objects; any UI hover keeps panel open.
        return EventSystem.current.IsPointerOverGameObject();
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

#if UNITY_EDITOR
    /// <summary>Legacy EditorWire signature used by older setup tools.</summary>
    public void EditorWire(
        ResponseEquipmentManager manager,
        GameObject root,
        ContentRow[] contentRows,
        Button complete,
        Button close,
        TextMeshProUGUI completeLabel,
        PPEEquipFeedbackUI feedback)
    {
        equipmentManager = manager;
        panelRoot = root;
        completeButton = complete;
        closeButton = close;
        completeButtonLabel = completeLabel;
    }

    [Serializable]
    public class ContentRow
    {
        public string itemId;
        public string displayName;
        public Button inspectButton;
        public TextMeshProUGUI labelText;
        public TextMeshProUGUI statusText;
        public Image indicator;
    }
#endif
}
