using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Scene-level state for response equipment preparation and contents inspection.
/// Independent from wearable PPE and from inactive bag GameObjects.
/// </summary>
[DisallowMultipleComponent]
public class ResponseEquipmentManager : MonoBehaviour
{
    public const string IdGasDetector = "GasDetector";
    public const string IdAbsorbentSock = "AbsorbentSock";
    public const string IdAbsorbentPad = "AbsorbentPad";

    // Legacy aliases (older scenes / scripts).
    public const string IdAbsorbPad = IdAbsorbentPad;
    public const string IdAbsorbSock = IdAbsorbentSock;
    public const string IdConcentrationMeter = IdGasDetector;

    public static readonly string[] RequiredBagContentIds =
    {
        IdGasDetector,
        IdAbsorbentSock,
        IdAbsorbentPad,
    };

    public static ResponseEquipmentManager Instance { get; private set; }

    [Header("Bag root")]
    [Tooltip("Only this GameObject is deactivated on prepare. Never a parent cabinet/shelf.")]
    [SerializeField] GameObject responseBagRoot;

    [Header("UI")]
    [SerializeField] ResponseBagContentsPanel contentsPanel;
    [SerializeField] ResponseBagContentsRevealController contentsReveal;
    [SerializeField] ResponseBagGearPanel gearPanel;

    [Header("Wearable (optional existing controller)")]
    [SerializeField] ResponseBagWearableVisual wearableVisual;

    [Header("Bag item take (equipped gear UI)")]
    [SerializeField] ResponseBagItemRetriever itemRetriever;

    [SerializeField] bool responseBagPrepared;

    InputAction contentsUiToggleAction;

    readonly System.Collections.Generic.HashSet<string> inspectedItemIds =
        new System.Collections.Generic.HashSet<string>();

    Transform bagOriginalParent;
    Vector3 bagOriginalLocalPosition;
    Quaternion bagOriginalLocalRotation;
    Vector3 bagOriginalLocalScale = Vector3.one;
    bool bagPoseCached;

    bool gearUiHidingBag;
    PPEOutlineHighlight suppressedBagOutline;
    PPEInteractableOutline suppressedWearableOutline;

    public bool ResponseBagPrepared => responseBagPrepared;
    public ResponseBagWearableVisual WearableVisual => wearableVisual;
    public bool HasCachedOriginalBagPose => bagPoseCached;

    public bool GasDetectorInspected => IsResponseBagItemInspected(IdGasDetector);
    public bool AbsorbentSockInspected => IsResponseBagItemInspected(IdAbsorbentSock);
    public bool AbsorbentPadInspected => IsResponseBagItemInspected(IdAbsorbentPad);
    public bool ResponseBagContentsVerified => AreAllResponseBagItemsInspected();

    public event Action StateChanged;
    public event Action OnResponseBagContentsChanged;
    public event Action<bool> OnResponseBagPreparedChanged;
    public event Action ResponseBagPreparedSucceeded;
    public event Action<string> BagItemTaken;
    public event Action<string> BagItemRestocked;
    public event Action BagItemAvailabilityReset;

    public GameObject ResponseBagRoot => responseBagRoot;
    public ResponseBagContentsPanel ContentsPanel => contentsPanel;
    public ResponseBagContentsRevealController ContentsReveal => contentsReveal;
    public ResponseBagGearPanel GearPanel => gearPanel;
    public ResponseBagItemRetriever ItemRetriever => itemRetriever;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple ResponseEquipmentManager instances found. Keeping the first one.", this);
            enabled = false;
            return;
        }

        Instance = this;
        EnsureItemRetriever();

        // Cache spawn pose before gameplay interaction if already assigned.
        if (responseBagRoot != null && !IsBagCurrentlySelected(responseBagRoot))
            CacheBagPoseIfNeeded(force: false);
    }

    void Start()
    {
        if (responseBagRoot != null && !bagPoseCached && !IsBagCurrentlySelected(responseBagRoot))
            CacheBagPoseIfNeeded(force: false);

        EnsureDirectContentsRevealRuntime();
    }

    /// <summary>
    /// If the Editor setup menu has not been run, wire the three bag child models at Play.
    /// </summary>
    void EnsureDirectContentsRevealRuntime()
    {
        if (responseBagRoot == null)
            return;

        if (responseBagRoot.GetComponentInParent<ResponseBagWearableVisual>(true) != null)
            return;

        // If a previous inspection mode left the bag under a camera root, snap home.
        var inspectionRoot = GameObject.Find("ResponseBagInspectionRoot");
        if (inspectionRoot != null
            && responseBagRoot.transform.parent == inspectionRoot.transform
            && bagPoseCached)
        {
            RestoreBagPose();
        }

        // Hide any leftover large contents UI.
        var panels = FindObjectsByType<ResponseBagContentsPanel>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < panels.Length; i++)
        {
            if (panels[i] != null && panels[i].gameObject.activeSelf)
                panels[i].gameObject.SetActive(false);
        }

        // Disable camera-gaze presentation (caused bag shake / relocate flows).
        var gazeDetectors = FindObjectsByType<ResponseBagCameraGazeDetector>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < gazeDetectors.Length; i++)
        {
            if (gazeDetectors[i] != null)
                gazeDetectors[i].enabled = false;
        }

        var inspections = FindObjectsByType<ResponseBagInspectionModeController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < inspections.Length; i++)
        {
            if (inspections[i] != null)
                inspections[i].enabled = false;
        }

        ResolveContentsReveal();
        if (contentsReveal == null)
        {
            contentsReveal = responseBagRoot.GetComponent<ResponseBagContentsRevealController>();
            if (contentsReveal == null)
                contentsReveal = responseBagRoot.AddComponent<ResponseBagContentsRevealController>();
        }

        contentsReveal.EnsureRuntimeConfigured(this);
        contentsReveal.HideContentsUntilSelected();

        if (gearPanel == null)
            gearPanel = ResponseBagGearPanel.EnsureCreated(this);

        EnsureItemRetriever();
        EnsureContentsUiToggleAction();
    }

    void EnsureItemRetriever()
    {
        if (itemRetriever == null)
            itemRetriever = GetComponent<ResponseBagItemRetriever>();
        if (itemRetriever == null)
            itemRetriever = gameObject.AddComponent<ResponseBagItemRetriever>();
    }

    /// <summary>
    /// Equipped bag: take a gameplay item into the selecting hand.
    /// Pre-equip: returns false — callers should use InspectResponseBagItem instead.
    /// </summary>
    public bool TryTakeBagItem(string itemId)
    {
        if (!responseBagPrepared)
            return false;

        EnsureItemRetriever();
        return itemRetriever != null && itemRetriever.TryTakeItem(itemId);
    }

    public bool IsBagItemAvailable(string itemId)
    {
        EnsureItemRetriever();
        return itemRetriever == null || itemRetriever.IsItemAvailableInBag(itemId);
    }

    public void NotifyBagItemTaken(string itemId)
    {
        BagItemTaken?.Invoke(itemId);
        StateChanged?.Invoke();
        if (gearPanel != null)
            gearPanel.RefreshTakenVisuals();
    }

    public void NotifyBagItemRestocked(string itemId)
    {
        BagItemRestocked?.Invoke(itemId);
        StateChanged?.Invoke();
        if (gearPanel != null)
            gearPanel.RefreshTakenVisuals();
    }

    public void NotifyBagItemAvailabilityReset()
    {
        BagItemAvailabilityReset?.Invoke();
        StateChanged?.Invoke();
        if (gearPanel != null)
            gearPanel.RefreshTakenVisuals();
    }

    public static bool IsKnownBagContentId(string itemId)
    {
        itemId = NormalizeContentId(itemId);
        for (var i = 0; i < RequiredBagContentIds.Length; i++)
        {
            if (RequiredBagContentIds[i] == itemId)
                return true;
        }

        return false;
    }

    /// <summary>Interior meshes stay hidden — contents are reviewed via GearPanel (Y key / Left Y).</summary>
    public void PrepareBagContentsForInspection()
    {
        HideBagContents();
    }

    void OnEnable()
    {
        EnsureContentsUiToggleAction();
        if (contentsUiToggleAction != null && !contentsUiToggleAction.enabled)
            contentsUiToggleAction.Enable();
    }

    void OnDisable()
    {
        if (contentsUiToggleAction == null)
            return;
        contentsUiToggleAction.Disable();
    }

    void EnsureContentsUiToggleAction()
    {
        if (contentsUiToggleAction != null)
            return;

        // Left-controller Y (SecondaryButton) + keyboard Y while bag is selected.
        contentsUiToggleAction = new InputAction(
            name: "ResponseBag Contents UI Toggle",
            type: InputActionType.Button);
        contentsUiToggleAction.AddBinding("<XRController>{LeftHand}/{SecondaryButton}");
        contentsUiToggleAction.AddBinding("<OculusTouchController>{LeftHand}/secondaryButton");
        contentsUiToggleAction.AddBinding("<Keyboard>/y");
        contentsUiToggleAction.performed += OnContentsUiTogglePerformed;
        contentsUiToggleAction.Enable();
    }

    void OnContentsUiTogglePerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        // Equipped wearable: Left Y only while ray-hovering the bag.
        if (responseBagPrepared)
        {
            ToggleGearPanelIfWearableBagHovered();
            return;
        }

        // Pre-equip world bag: Left Y while the world bag is sticky-selected.
        ToggleGearPanelIfBagSelected();
    }

    /// <summary>True when the sticky-selected PPE is the world response bag.</summary>
    public bool IsWorldBagCurrentlySelected()
    {
        var selection = PPESelectionManager.Instance != null
            ? PPESelectionManager.Instance
            : FindAnyObjectByType<PPESelectionManager>();
        if (selection == null || selection.CurrentSelectedPPE == null)
            return false;

        var highlight = selection.CurrentSelectedPPE;
        return highlight.GetComponent<ResponseBagPrepareInteractable>() != null
            || highlight.GetComponentInParent<ResponseBagPrepareInteractable>() != null;
    }

    /// <summary>True when a ray is aimed at the equipped wearable bag.</summary>
    public bool IsWearableBagCurrentlyHovered()
    {
        var controller = wearableVisual != null ? wearableVisual.VisualController : null;
        return controller != null && controller.IsLookingAtWearableBag();
    }

    void ToggleGearPanelIfBagSelected()
    {
        if (!IsWorldBagCurrentlySelected())
            return;

        if (gearPanel != null && gearPanel.IsOpen)
        {
            HideGearPanel();
            return;
        }

        ShowGearPanel();
    }

    void ToggleGearPanelIfWearableBagHovered()
    {
        var controller = wearableVisual != null ? wearableVisual.VisualController : null;
        if (controller == null || !controller.IsLookingAtWearableBag())
            return;

        controller.ToggleGearUiFromY();
    }

    void OnDestroy()
    {
        if (contentsUiToggleAction != null)
        {
            contentsUiToggleAction.performed -= OnContentsUiTogglePerformed;
            contentsUiToggleAction.Disable();
            contentsUiToggleAction.Dispose();
            contentsUiToggleAction = null;
        }

        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Assign world bag root. Prefer caching before interaction.
    /// Same root reassignment does not invalidate a valid pose cache.
    /// </summary>
    public void InitializeResponseBagRoot(GameObject root, bool cacheOriginalPose)
    {
        if (root == null)
        {
            Debug.LogWarning("[Response Equipment] InitializeResponseBagRoot — root is null.", this);
            return;
        }

        if (responseBagRoot == root)
        {
            if (cacheOriginalPose && !bagPoseCached && !IsBagCurrentlySelected(root))
                CacheBagPoseIfNeeded(force: false);
            return;
        }

        responseBagRoot = root;

        if (cacheOriginalPose && !IsBagCurrentlySelected(root))
        {
            bagPoseCached = false;
            CacheBagPoseIfNeeded(force: false);
        }
        else if (!bagPoseCached)
        {
            // Different root without immediate cache — wait until not selected.
            bagPoseCached = false;
        }
    }

    /// <summary>Legacy setter — caches only when bag is not selected.</summary>
    public void SetResponseBagRoot(GameObject root)
    {
        InitializeResponseBagRoot(root, cacheOriginalPose: true);
    }

    public void SetContentsPanel(ResponseBagContentsPanel panel)
    {
        contentsPanel = panel;
    }

    public void SetContentsReveal(ResponseBagContentsRevealController reveal)
    {
        contentsReveal = reveal;
    }

    public void SetGearPanel(ResponseBagGearPanel panel)
    {
        gearPanel = panel;
    }

    public void ShowGearPanel()
    {
        if (responseBagPrepared)
            return;

        ShowGearPanelInternal(showPrepareHint: true);
    }

    /// <summary>
    /// Equipped wearable open: same yellow-slot UI, no prepare-hint toast.
    /// </summary>
    public void ShowGearPanelForEquippedBag()
    {
        if (!responseBagPrepared)
            return;

        ShowGearPanelInternal(showPrepareHint: false);
    }

    void ShowGearPanelInternal(bool showPrepareHint)
    {
        ResolveContentsReveal();
        if (contentsReveal != null && !responseBagPrepared)
            contentsReveal.PrepareContentsForInspection();

        // Always go through EnsureCreated so outdated canvases are rebuilt
        // (previously kept an old gearPanel forever → stuck on result #2 poses).
        gearPanel = ResponseBagGearPanel.EnsureCreated(this);

        gearPanel.Show(showPrepareHint);
    }

    public void HideGearPanel()
    {
        if (gearPanel != null)
            gearPanel.Hide();

        // Keep wearable open-state in sync when UI is closed from the panel X / prepare flow.
        if (wearableVisual != null && wearableVisual.VisualController != null)
            wearableVisual.VisualController.NotifyGearUiClosed();
    }

    /// <summary>
    /// While gear cards are open during pre-equip inspection: hide world bag mesh + outlines
    /// so cards are readable. Equipped wearable stays visible (worn bag remains on the body).
    /// </summary>
    public void SetBagHiddenForGearUi(bool hidden)
    {
        // Equipped/worn bag should remain visible while its gear UI is open.
        if (responseBagPrepared)
        {
            if (gearUiHidingBag)
                RestoreBagVisibilityAfterGearUi();
            return;
        }

        if (gearUiHidingBag == hidden)
            return;
        gearUiHidingBag = hidden;

        if (hidden)
        {
            // World-bag sticky yellow outline (Y inspect / select).
            var selection = PPESelectionManager.Instance != null
                ? PPESelectionManager.Instance
                : FindAnyObjectByType<PPESelectionManager>();
            if (selection != null && selection.CurrentSelectedPPE != null)
            {
                suppressedBagOutline = selection.CurrentSelectedPPE;
                suppressedBagOutline.enabled = false;
            }
            else if (responseBagRoot != null)
            {
                suppressedBagOutline = responseBagRoot.GetComponentInChildren<PPEOutlineHighlight>(true);
                if (suppressedBagOutline != null)
                    suppressedBagOutline.enabled = false;
            }

            // Wearable open-handle outline.
            if (wearableVisual != null && wearableVisual.VisualController != null)
            {
                var openOutline = wearableVisual.VisualController.BagOpenOutline;
                if (openOutline != null)
                {
                    suppressedWearableOutline = openOutline;
                    openOutline.ForceClearOutline();
                    openOutline.enabled = false;
                }
            }
        }
        else
        {
            RestoreBagVisibilityAfterGearUi();
            return;
        }

        // Hide bag meshes (world preview bag only for pre-equip).
        if (responseBagRoot != null && responseBagRoot.activeInHierarchy)
            SetRenderersEnabled(responseBagRoot, !hidden);

        if (wearableVisual != null)
            wearableVisual.SetBagMeshRenderersEnabled(!hidden);
    }

    void RestoreBagVisibilityAfterGearUi()
    {
        gearUiHidingBag = false;

        if (suppressedBagOutline != null)
        {
            suppressedBagOutline.enabled = true;
            suppressedBagOutline = null;
        }

        if (suppressedWearableOutline != null)
        {
            suppressedWearableOutline.enabled = true;
            suppressedWearableOutline = null;
        }

        if (responseBagRoot != null && responseBagRoot.activeInHierarchy)
            SetRenderersEnabled(responseBagRoot, true);

        if (wearableVisual != null)
            wearableVisual.SetBagMeshRenderersEnabled(true);
    }

    static void SetRenderersEnabled(GameObject root, bool enabled)
    {
        if (root == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = enabled;
        }
    }

    public void HideBagContents()
    {
        if (contentsReveal != null)
            contentsReveal.HideContentsUntilSelected();
    }

    public void SetWearableVisual(ResponseBagWearableVisual visual)
    {
        wearableVisual = visual;
    }

    public bool IsResponseBagPrepared() => responseBagPrepared;

    public bool PrepareResponseBag(GameObject bagRoot) => PrepareResponseBag(bagRoot, deactivateRoot: true);

    public bool PrepareResponseBag(GameObject bagRoot, bool deactivateRoot)
    {
        if (responseBagPrepared)
            return false;

        if (bagRoot != null)
        {
            // Same root: do NOT invalidate pose cache.
            if (responseBagRoot != bagRoot)
                InitializeResponseBagRoot(bagRoot, cacheOriginalPose: !IsBagCurrentlySelected(bagRoot));
            else if (responseBagRoot == null)
                responseBagRoot = bagRoot;
        }

        if (responseBagRoot == null)
        {
            Debug.LogWarning("[Response Equipment] PrepareResponseBag failed — bag root missing.", this);
            return false;
        }

        // Never recache during prepare / while selected.
        if (!bagPoseCached && !IsBagCurrentlySelected(responseBagRoot))
            CacheBagPoseIfNeeded(force: false);

        ClearXrInteraction(responseBagRoot);
        ClearRigidbodyMotion(responseBagRoot);
        // Sync restore preview parent before deactivate (ClearSelection alone is async).
        PrepareSelectionForDeactivate(responseBagRoot);
        ClearContentsVisuals();
        HideGearPanel();

        responseBagPrepared = true;

        if (contentsPanel != null)
            contentsPanel.HidePanel();

        ShowPreparedFeedback();
        ShowWearableVisual();
        SyncPpeEquippedBookkeeping(responseBagRoot);

        if (deactivateRoot)
            responseBagRoot.SetActive(false);

        StateChanged?.Invoke();
        OnResponseBagPreparedChanged?.Invoke(true);
        ResponseBagPreparedSucceeded?.Invoke();
        Debug.Log("[Response Equipment] Response bag prepared.", this);
        return true;
    }

    /// <summary>Grip confirm prepares the bag after all three contents are hover-inspected.</summary>
    public bool TryCompleteResponseBagPreparation()
    {
        if (responseBagPrepared)
            return false;

        if (!AreAllResponseBagItemsInspected())
        {
            ShowIncompleteContentsMessage();
            return false;
        }

        return PrepareResponseBag(responseBagRoot);
    }

    static void ShowIncompleteContentsMessage()
    {
        var feedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        if (feedback == null)
            return;

        feedback.ShowMessage(
            "\uB300\uC751\uAC00\uBC29 \uB0B4\uBD80 \uC7A5\uBE44\uB97C \uBAA8\uB450 \uD655\uC778\uD558\uC2ED\uC2DC\uC624.");
    }

    public bool TryCompleteResponseBag() => TryCompleteResponseBagPreparation();

    /// <summary>Legacy — no longer blocks prepare or relocates the bag.</summary>
    public void RequestContentsInspectionBeforePrepare()
    {
        // Kept for API compatibility. Visual hover names are available on the stable bag.
    }

    public void EnterInspectionPresentation()
    {
        // Disabled — bag remains in world pose.
    }

    public void ExitInspectionPresentation(bool restoreWorldPose)
    {
        ClearContentsVisuals();
    }

    void ClearContentsVisuals()
    {
        if (contentsReveal != null)
            contentsReveal.HideContentsImmediate();

        var label = FindAnyObjectByType<ResponseBagItemNameLabel>(FindObjectsInactive.Include);
        if (label != null)
            label.HideImmediate();

        var items = FindObjectsByType<ResponseBagContentHoverItem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < items.Length; i++)
            items[i]?.ClearVisuals();
    }

    /// <summary>
    /// Called before <see cref="PPEEquipmentManager.Equip"/> so spawn pose is not lost while previewed/held.
    /// </summary>
    public void PreparePoseCacheForPpeEquip(GameObject bagRoot)
    {
        if (bagRoot == null)
            return;

        if (responseBagRoot != bagRoot)
        {
            InitializeResponseBagRoot(bagRoot, cacheOriginalPose: !IsBagCurrentlySelected(bagRoot));
            return;
        }

        if (!bagPoseCached && !IsBagCurrentlySelected(bagRoot))
            CacheBagPoseIfNeeded(force: false);
    }

    /// <summary>
    /// PPE Equip path: wearable already activated via PPEEquipable.equippedVisual.
    /// Sync checklist / prepared flag without double toast or second deactivate.
    /// </summary>
    public void NotifyPreparedFromPpeEquip(GameObject bagRoot)
    {
        if (responseBagPrepared)
            return;

        if (bagRoot != null && responseBagRoot != bagRoot)
            InitializeResponseBagRoot(bagRoot, cacheOriginalPose: false);
        else if (bagRoot != null && responseBagRoot == null)
            responseBagRoot = bagRoot;

        responseBagPrepared = true;

        ClearContentsVisuals();

        if (contentsPanel != null)
            contentsPanel.HidePanel();

        // Ensure wearable visual pose + active (equippedVisual may already be on).
        ShowWearableVisual();

        StateChanged?.Invoke();
        OnResponseBagPreparedChanged?.Invoke(true);
        ResponseBagPreparedSucceeded?.Invoke();
        Debug.Log("[Response Equipment] Response bag equipped via PPE pipeline.", this);
    }

    public void ResetResponseBag()
    {
        var wasPrepared = responseBagPrepared;
        responseBagPrepared = false;
        inspectedItemIds.Clear();
        OnResponseBagContentsChanged?.Invoke();

        ClearContentsVisuals();
        HideGearPanel();

        if (responseBagRoot != null)
        {
            ClearXrInteraction(responseBagRoot);

            if (!responseBagRoot.activeSelf)
                responseBagRoot.SetActive(true);

            RestoreBagPose();
            ClearRigidbodyMotion(responseBagRoot);
            ClearSelectionVisuals(responseBagRoot);

            var equipable = responseBagRoot.GetComponent<PPEEquipable>();
            if (equipable == null)
                equipable = responseBagRoot.GetComponentInChildren<PPEEquipable>(true);
            if (equipable != null)
                equipable.ResetEquipState();
        }

        ClearPpeResponseBagEquipId();
        HideWearableVisual();

        var wearableCtrl = FindAnyObjectByType<WearableResponseBagVisualController>(FindObjectsInactive.Include);
        if (wearableCtrl != null)
            wearableCtrl.ResetBagVisual();

        if (contentsReveal != null)
            contentsReveal.ResetContentsDisplay();
        else if (contentsPanel != null)
            contentsPanel.ResetPanelVisuals();

        EnsureItemRetriever();
        itemRetriever?.ResetAllItems();

        StateChanged?.Invoke();
        if (wasPrepared)
            OnResponseBagPreparedChanged?.Invoke(false);

        Debug.Log("[Response Equipment] ResetResponseBag()", this);
    }

    static void ClearPpeResponseBagEquipId()
    {
        var ppe = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : UnityEngine.Object.FindAnyObjectByType<PPEEquipmentManager>();
        if (ppe != null)
            ppe.ClearEquippedId(PPEEquipmentManager.IdResponseBag);
    }

    public void ResetResponseEquipment() => ResetResponseBag();

    public void ResetResponseBagContents()
    {
        inspectedItemIds.Clear();
        OnResponseBagContentsChanged?.Invoke();
        StateChanged?.Invoke();
        if (contentsPanel != null)
            contentsPanel.RefreshFromManager();
    }

    public bool IsResponseBagItemInspected(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;
        return inspectedItemIds.Contains(NormalizeContentId(itemId));
    }

    public bool IsItemInspected(string itemId) => IsResponseBagItemInspected(itemId);

    public bool AreAllResponseBagItemsInspected()
    {
        for (var i = 0; i < RequiredBagContentIds.Length; i++)
        {
            if (!inspectedItemIds.Contains(RequiredBagContentIds[i]))
                return false;
        }

        return true;
    }

    public bool AreAllBagContentsInspected() => AreAllResponseBagItemsInspected();

    public bool InspectResponseBagItem(string itemId)
    {
        if (responseBagPrepared)
            return false;

        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        var id = NormalizeContentId(itemId);
        if (!IsKnownBagContentId(id))
            return false;

        if (!inspectedItemIds.Add(id))
            return false;

        OnResponseBagContentsChanged?.Invoke();
        StateChanged?.Invoke();
        return true;
    }

    public bool MarkItemInspected(string itemId) => InspectResponseBagItem(itemId);

    static string NormalizeContentId(string itemId)
    {
        if (itemId == "AbsorbPad")
            return IdAbsorbentPad;
        if (itemId == "AbsorbSock")
            return IdAbsorbentSock;
        if (itemId == "ConcentrationMeter")
            return IdGasDetector;
        return itemId;
    }

    void ResolveContentsPanel()
    {
        if (contentsPanel != null)
            return;
        contentsPanel = FindAnyObjectByType<ResponseBagContentsPanel>(FindObjectsInactive.Include);
    }

    void ResolveContentsReveal()
    {
        if (contentsReveal != null)
            return;
        contentsReveal = FindAnyObjectByType<ResponseBagContentsRevealController>(FindObjectsInactive.Include);
    }

    public void RestoreBagToCachedPosePublic()
    {
        RestoreBagPose();
    }

    void CacheBagPoseIfNeeded(bool force)
    {
        if (responseBagRoot == null)
            return;

        if (bagPoseCached && !force)
            return;

        if (!force && IsBagCurrentlySelected(responseBagRoot))
            return;

        bagOriginalParent = responseBagRoot.transform.parent;
        bagOriginalLocalPosition = responseBagRoot.transform.localPosition;
        bagOriginalLocalRotation = responseBagRoot.transform.localRotation;
        bagOriginalLocalScale = responseBagRoot.transform.localScale;
        bagPoseCached = true;
    }

    void RestoreBagPose()
    {
        if (responseBagRoot == null || !bagPoseCached)
        {
            Debug.LogWarning(
                "[Response Equipment] Reset without cached original pose — leaving current transform.",
                this);
            return;
        }

        var t = responseBagRoot.transform;
        if (t.parent != bagOriginalParent)
            t.SetParent(bagOriginalParent, worldPositionStays: false);

        t.localPosition = bagOriginalLocalPosition;
        t.localRotation = bagOriginalLocalRotation;
        t.localScale = bagOriginalLocalScale;
    }

    static bool IsBagCurrentlySelected(GameObject bagRoot)
    {
        if (bagRoot == null)
            return false;

        // Sticky outline selection keeps bag under preview even after XR select release.
        var selection = PPESelectionManager.Instance != null
            ? PPESelectionManager.Instance
            : UnityEngine.Object.FindAnyObjectByType<PPESelectionManager>();
        if (selection != null && selection.CurrentSelectedPPE != null)
        {
            var highlight = selection.CurrentSelectedPPE;
            var highlightRoot = highlight.transform;
            if (highlightRoot == bagRoot.transform || highlightRoot.IsChildOf(bagRoot.transform)
                || bagRoot.transform.IsChildOf(highlightRoot))
                return true;

            if (highlight.GetComponent<ResponseBagPrepareInteractable>() != null
                || highlight.GetComponentInParent<ResponseBagPrepareInteractable>() != null)
            {
                var prepareRoot = highlight.GetComponentInParent<ResponseBagPrepareInteractable>(true);
                if (prepareRoot != null && prepareRoot.gameObject == bagRoot)
                    return true;
            }
        }

        var interactable = bagRoot.GetComponent<XRBaseInteractable>();
        if (interactable == null)
            interactable = bagRoot.GetComponentInChildren<XRBaseInteractable>(true);

        return interactable != null && interactable.isSelected;
    }

    /// <summary>
    /// Instantly restores preview parenting before SetActive(false).
    /// Prefer this over ClearSelection alone (async coroutine restore).
    /// </summary>
    static void PrepareSelectionForDeactivate(GameObject bagRoot)
    {
        if (bagRoot == null)
            return;

        var highlight = bagRoot.GetComponent<PPEOutlineHighlight>();
        if (highlight == null)
            highlight = bagRoot.GetComponentInChildren<PPEOutlineHighlight>(true);

        var selection = PPESelectionManager.Instance != null
            ? PPESelectionManager.Instance
            : UnityEngine.Object.FindAnyObjectByType<PPESelectionManager>();

        if (selection != null && highlight != null)
        {
            selection.NotifyEquipped(highlight);
            return;
        }

        if (highlight == null)
            return;

        var preview = UnityEngine.Object.FindAnyObjectByType<PPEPreviewController>(FindObjectsInactive.Include);
        if (preview != null)
            preview.PrepareForEquipDeactivate(highlight);

        highlight.SetSelected(false);
    }

    static void SyncPpeEquippedBookkeeping(GameObject bagRoot)
    {
        var ppe = PPEEquipmentManager.Instance != null
            ? PPEEquipmentManager.Instance
            : UnityEngine.Object.FindAnyObjectByType<PPEEquipmentManager>();
        if (ppe == null)
            return;

        ppe.MirrorResponseBagEquipped();

        if (bagRoot == null)
            return;

        var equipable = bagRoot.GetComponent<PPEEquipable>();
        if (equipable == null)
            equipable = bagRoot.GetComponentInChildren<PPEEquipable>(true);
        if (equipable != null && !equipable.IsEquipped)
            equipable.MarkAsEquipped();
    }

    static void ClearSelectionVisuals(GameObject bagRoot)
    {
        PrepareSelectionForDeactivate(bagRoot);
    }

    static void ClearXrInteraction(GameObject bagRoot)
    {
        if (bagRoot == null)
            return;

        var interactable = bagRoot.GetComponent<XRBaseInteractable>();
        if (interactable == null)
            interactable = bagRoot.GetComponentInChildren<XRBaseInteractable>(true);
        if (interactable == null)
            return;

        var manager = interactable.interactionManager;
        if (manager == null)
            manager = UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>();
        if (manager == null)
            return;

        if (interactable is IXRSelectInteractable selectable && selectable.isSelected)
            manager.CancelInteractableSelection(selectable);

        if (interactable is IXRHoverInteractable hoverable && hoverable.isHovered)
            manager.CancelInteractableHover(hoverable);
    }

    static void ClearRigidbodyMotion(GameObject bagRoot)
    {
        if (bagRoot == null)
            return;

        var bodies = bagRoot.GetComponentsInChildren<Rigidbody>(true);
        for (var i = 0; i < bodies.Length; i++)
        {
            var rb = bodies[i];
            if (rb == null)
                continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    static void ShowPreparedFeedback()
    {
        var feedback = UnityEngine.Object.FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        if (feedback == null)
        {
            Debug.LogWarning("[Response Equipment] PPEEquipFeedbackUI missing — prepared message skipped.");
            return;
        }

        feedback.ShowMessage(
            "\uD654\uD559\uBB3C\uC9C8 \uB204\uCD9C \uB300\uC751\uAC00\uBC29\uC744 \uC900\uBE44\uD588\uC2B5\uB2C8\uB2E4.");
    }

    static void ShowWearableVisual()
    {
        var manager = Instance;
        var wearable = manager != null ? manager.wearableVisual : null;
        if (wearable == null)
        {
            wearable = UnityEngine.Object.FindAnyObjectByType<ResponseBagWearableVisual>(
                FindObjectsInactive.Include);
            if (manager != null && wearable != null)
                manager.wearableVisual = wearable;
        }

        if (wearable == null)
        {
            Debug.LogWarning(
                "[Response Equipment] ResponseBagWearableVisual missing — body bag not shown. Assign wearable reference.");
            return;
        }

        wearable.ShowWearable();
    }

    static void HideWearableVisual()
    {
        var manager = Instance;
        var wearable = manager != null ? manager.wearableVisual : null;
        if (wearable == null)
        {
            wearable = UnityEngine.Object.FindAnyObjectByType<ResponseBagWearableVisual>(
                FindObjectsInactive.Include);
        }

        if (wearable == null)
            return;

        wearable.HideWearable();
    }

#if UNITY_EDITOR
    [ContextMenu("Test Prepare Response Bag")]
    void EditorTestPrepare() => PrepareResponseBag(responseBagRoot);

    [ContextMenu("Reset Response Bag")]
    void EditorReset() => ResetResponseBag();

    [ContextMenu("Log Cached Response Bag Pose")]
    void EditorLogCachedPose()
    {
        var selected = IsBagCurrentlySelected(responseBagRoot);
        var cur = responseBagRoot != null ? responseBagRoot.transform.position : Vector3.zero;
        Debug.Log(
            $"[Response Equipment] CachedPose={bagPoseCached} parent={(bagOriginalParent != null ? bagOriginalParent.name : "null")} " +
            $"localPos={bagOriginalLocalPosition} localRot={bagOriginalLocalRotation.eulerAngles} localScale={bagOriginalLocalScale} " +
            $"currentWorldPos={cur} isSelected={selected}",
            this);
    }
#endif
}
