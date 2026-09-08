using System.Collections;
using System.Collections.Generic;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Table layout plus bag packing: Grab → bag-mouth trigger → Release → slot snap.
/// Reuses ResponseEquipmentManager inspect flags, PPE sequence, inventory Store, and feedback UI.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(80)]
public sealed class ResponseBagPackingController : MonoBehaviour
{
    public const string DeskName = "PPE_DeskTop";
    public const string ItemsRootName = "EquipmentPreparationItems";
    public const string TriggerName = "BagPackingTrigger";
    public const string SlotsRootName = "PackingSlots";
    const string PhysicalRootName = "PhysicalColliders";
    const string TopColliderName = "PPE_DeskTop_TopCollider";

    static readonly string WrongItemMessage =
        "\uC774 \uC7A5\uBE44\uB294 \uD604\uC7AC \uC900\uBE44 \uB300\uC0C1\uC774 \uC544\uB2D9\uB2C8\uB2E4.";
    static readonly string AllPackedMessage =
        "\uC7A5\uBE44 \uC218\uB0A9 \uC644\uB8CC. \uAC00\uBC29\uC744 Grip\uD574 \uCC59\uAE30\uC2ED\uC2DC\uC624.";

    public static ResponseBagPackingController Instance { get; private set; }

    [SerializeField] ResponseEquipmentManager equipmentManager;
    [SerializeField] Transform tableRoot;
    [SerializeField] Collider packingTrigger;
    [SerializeField] Transform hclMeterSlot;
    [SerializeField] Transform absorbentPadSlot;
    [SerializeField] Transform absorbentSockSlot;
    [SerializeField] ResponseBagPackableItem gasItem;
    [SerializeField] ResponseBagPackableItem padItem;
    [SerializeField] ResponseBagPackableItem sockItem;

    readonly HashSet<ResponseBagPackableItem> packedItems = new HashSet<ResponseBagPackableItem>();
    readonly HashSet<Collider> insideTrigger = new HashSet<Collider>();
    readonly Dictionary<ResponseBagPackableItem, Coroutine> fallWatches =
        new Dictionary<ResponseBagPackableItem, Coroutine>();
    bool captureStarted;
    bool subscribedWrongRelease;
    bool tableReady;
    BoxCollider tableTopCollider;
    bool tableTopIgnoresLocomotion;
    SharedXRInputContract sharedInput;
    bool finalGripSubscribed;

    public bool HClMeterPacked => IsPacked(gasItem);
    public bool AbsorbentPadPacked => IsPacked(padItem);
    public bool AbsorbentSockPacked => IsPacked(sockItem);
    public bool AllPacked => HClMeterPacked && AbsorbentPadPacked && AbsorbentSockPacked;

    void Awake()
    {
        Instance = this;
        if (equipmentManager == null)
            equipmentManager = GetComponent<ResponseEquipmentManager>();
    }

    void OnEnable()
    {
        var sequence = PPESequenceManager.Resolve();
        if (sequence != null)
            sequence.OnPhaseChanged += OnPhaseChanged;

        TrySubscribeFinalGrip();
    }

    void Start()
    {
        EnsureTableLayout();
        SubscribeWrongItemReleases();
    }

    void Update()
    {
        // The App XR Origin can be created after this Stage starts. Retry until the
        // CharacterController exists so the item-supporting tabletop never keeps
        // the trainee farther away than the visible desk front.
        if (!tableTopIgnoresLocomotion && tableTopCollider != null)
            tableTopIgnoresLocomotion = TryIgnoreLocomotionOnCollider(tableTopCollider);

        // App creates the shared XR input contract before or shortly after the
        // additive PPE Stage. Retry so final bag confirmation never depends on
        // scene initialization order.
        if (!finalGripSubscribed)
            TrySubscribeFinalGrip();
    }

    void OnDisable()
    {
        var sequence = PPESequenceManager.Resolve();
        if (sequence != null)
            sequence.OnPhaseChanged -= OnPhaseChanged;

        UnsubscribeFinalGrip();
    }

    void OnDestroy()
    {
        UnsubscribeFinalGrip();
        if (Instance == this)
            Instance = null;
    }

    void TrySubscribeFinalGrip()
    {
        if (finalGripSubscribed)
            return;

        sharedInput = SharedXRInputContract.Instance;
        if (sharedInput == null)
            return;

        sharedInput.Performed += OnSharedInputPerformed;
        finalGripSubscribed = true;
    }

    void UnsubscribeFinalGrip()
    {
        if (sharedInput != null && finalGripSubscribed)
            sharedInput.Performed -= OnSharedInputPerformed;

        sharedInput = null;
        finalGripSubscribed = false;
    }

    void OnSharedInputPerformed(SharedXRHand _, SharedXRControl control)
    {
        if (control != SharedXRControl.Select || !AllPacked)
            return;

        if (equipmentManager == null)
            equipmentManager = ResponseEquipmentManager.Instance;
        if (equipmentManager == null || equipmentManager.IsResponseBagPrepared())
            return;

        // Once all three items are packed, the next physical Grip is exclusively
        // the "take the response bag" action. Do not depend on ray/hover/select
        // classification because the PPE room uses a NearFar hand interactor.
        if (equipmentManager.TryCompleteResponseBagPreparation())
            Debug.Log("[PPE Packing] Final Grip confirmed — response bag collected.", this);
    }

    public void RestoreTableLayout()
    {
        packedItems.Clear();
        captureStarted = false;
        tableReady = false;
        foreach (var running in fallWatches.Values)
        {
            if (running != null)
                StopCoroutine(running);
        }
        fallWatches.Clear();
        EnsureTableLayout();
    }

    public void NotifyTriggerEnter(Collider col)
    {
        if (col != null)
            insideTrigger.Add(col);
    }

    public void NotifyTriggerExit(Collider col)
    {
        if (col != null)
            insideTrigger.Remove(col);
    }

    public void NotifyItemReleased(ResponseBagPackableItem item)
    {
        if (item == null || item.IsPacked)
            return;

        if (IsReleaseInsideBag(item.transform))
        {
            TryPack(item);
            return;
        }

        StopWatch(item);
        fallWatches[item] = StartCoroutine(WatchFallIntoBag(item));
    }

    public void NotifyUnknownReleased(Component host)
    {
        if (host == null || AllPacked)
            return;
        if (host.GetComponent<ResponseBagPackableItem>() != null)
            return;
        if (!IsReleaseInsideBag(host.transform))
            return;

        ShowFeedback(WrongItemMessage);
    }

    public void EnsureTableLayout()
    {
        if (equipmentManager == null)
            equipmentManager = ResponseEquipmentManager.Instance;
        if (equipmentManager == null)
            return;

        var reveal = equipmentManager.ContentsReveal;
        var bag = equipmentManager.ResponseBagRoot != null
            ? equipmentManager.ResponseBagRoot.transform
            : null;
        if (reveal == null || bag == null)
            return;
        SetBagConfirmationEnabled(bag, AllPacked);
        if (tableReady && gasItem != null && padItem != null && sockItem != null)
            return;

        var desk = FindDesk();
        if (desk == null)
            return;

        tableRoot = desk.transform;
        DisableLegacyBagInspection(bag, reveal);
        DetachPreparationItemsFromDesk(desk, reveal);
        EnsurePackingStructure(bag);
        EnsureTableTopCollider(desk);

        gasItem = BindItem(reveal.GasDetectorRoot, ResponseEquipmentManager.IdGasDetector);
        padItem = BindItem(reveal.AbsorbentPadRoot, ResponseEquipmentManager.IdAbsorbentPad);
        sockItem = BindItem(reveal.AbsorbentSockRoot, ResponseEquipmentManager.IdAbsorbentSock);
        CompactSockCollider(sockItem);
        tableReady = gasItem != null && padItem != null && sockItem != null;
        if (tableReady)
        {
            Debug.Log(
                "[PPE Packing] Table items are grabbable. Drop them inside ResponseBag_HardShell to pack.",
                this);
        }
    }

    void TryPack(ResponseBagPackableItem item)
    {
        if (item == null || item.IsPacked)
            return;
        if (!packedItems.Add(item))
            return;

        StopWatch(item);
        BeginCaptureIfNeeded();
        SnapToSlot(item);
        item.SetPacked(true);

        var id = item.ItemId;
        equipmentManager?.InspectResponseBagItem(id);
        StoreInventory(id);
        ShowFeedback(PackedMessage(id));

        if (!AllPacked)
            return;

        ShowFeedback(AllPackedMessage);
        PPETTSManager.Instance?.PlayNow(PPETTSCue.GripPackedResponseBag);
        var bag = equipmentManager != null && equipmentManager.ResponseBagRoot != null
            ? equipmentManager.ResponseBagRoot.transform
            : null;
        SetBagConfirmationEnabled(bag, true);
    }

    void SnapToSlot(ResponseBagPackableItem item)
    {
        var slot = SlotFor(item.ItemId);
        if (slot == null)
            return;

        var body = item.Body;
        if (body != null)
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
        }

        item.transform.SetParent(slot, false);
        item.transform.localPosition = Vector3.zero;
        item.transform.localRotation = Quaternion.identity;
    }

    Transform SlotFor(string itemId)
    {
        if (string.Equals(itemId, ResponseEquipmentManager.IdGasDetector, System.StringComparison.Ordinal))
            return hclMeterSlot;
        if (string.Equals(itemId, ResponseEquipmentManager.IdAbsorbentPad, System.StringComparison.Ordinal))
            return absorbentPadSlot;
        if (string.Equals(itemId, ResponseEquipmentManager.IdAbsorbentSock, System.StringComparison.Ordinal))
            return absorbentSockSlot;
        return null;
    }

    bool IsReleaseInsideBag(Transform item)
    {
        if (packingTrigger == null || item == null)
            return false;

        var cols = item.GetComponentsInChildren<Collider>();
        for (var i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i].enabled && !cols[i].isTrigger && insideTrigger.Contains(cols[i]))
                return true;
        }

        return IsPointInsidePackingVolume(ItemWorldCenter(item));
    }

    bool IsPointInsidePackingVolume(Vector3 worldPoint)
    {
        var box = packingTrigger as BoxCollider;
        if (box == null)
            return packingTrigger.bounds.Contains(worldPoint);

        var local = box.transform.InverseTransformPoint(worldPoint) - box.center;
        var half = box.size * 0.5f + new Vector3(0.08f, 0.08f, 0.08f);
        return Mathf.Abs(local.x) <= half.x
            && Mathf.Abs(local.y) <= half.y
            && Mathf.Abs(local.z) <= half.z;
    }

    static Vector3 ItemWorldCenter(Transform item)
    {
        Renderer[] renderers = item.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return item.position;

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds.center;
    }

    IEnumerator WatchFallIntoBag(ResponseBagPackableItem item)
    {
        var elapsed = 0f;
        while (elapsed < 2f && item != null && !item.IsPacked)
        {
            if (item.Grab != null && item.Grab.isSelected)
                yield break;
            if (IsReleaseInsideBag(item.transform))
            {
                TryPack(item);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (item != null && !item.IsPacked)
        {
            Debug.LogWarning(
                "[PPE Packing] Release missed bag volume for " + item.name +
                ". Put the item into the visible hard-shell interior.",
                item);
        }
    }

    void StopWatch(ResponseBagPackableItem item)
    {
        if (item == null)
            return;
        if (!fallWatches.TryGetValue(item, out var running))
            return;
        fallWatches.Remove(item);
        if (running != null)
            StopCoroutine(running);
    }

    ResponseBagPackableItem BindItem(GameObject root, string itemId)
    {
        if (root == null)
            return null;

        root.SetActive(true);
        var hover = root.GetComponent<ResponseBagContentHoverItem>();
        if (hover != null)
            hover.enabled = false;
        var simple = root.GetComponent<XRSimpleInteractable>();
        if (simple != null)
            simple.enabled = false;

        root.transform.SetParent(EnsureItemsRoot(), true);

        FitPhysicalCollider(root, itemId);
        var packable = root.GetComponent<ResponseBagPackableItem>();
        if (packable == null)
            packable = root.AddComponent<ResponseBagPackableItem>();
        packable.EditorWire(itemId, this);
        packable.SetPacked(false);
        packable.ApplyRestingPhysics();
        packable.IgnorePlayerLocomotionCollisions();
        return packable;
    }

    void DisableLegacyBagInspection(Transform bag, ResponseBagContentsRevealController reveal)
    {
        if (reveal != null)
            reveal.enabled = false;

        if (bag == null)
            return;

        var prepare = bag.GetComponent<ResponseBagPrepareInteractable>();
        if (prepare != null)
            prepare.enabled = false;
        var simple = bag.GetComponent<XRSimpleInteractable>();
        if (simple != null)
            simple.enabled = false;
    }

    static void SetBagConfirmationEnabled(Transform bag, bool enabled)
    {
        if (bag == null)
            return;

        var simple = bag.GetComponent<XRSimpleInteractable>();
        if (simple != null)
        {
            // The packing layout disables the original full-bag collider while
            // contents are being inserted. XRI still references that collider, so
            // enabling only the interactable leaves Grip with nothing to select.
            // Restore its registered collider for the explicit final bag Grip.
            if (simple.enabled)
                simple.enabled = false;

            for (var i = 0; i < simple.colliders.Count; i++)
            {
                var interactionCollider = simple.colliders[i];
                if (interactionCollider != null)
                    interactionCollider.enabled = enabled;
            }
        }

        var prepare = bag.GetComponent<ResponseBagPrepareInteractable>();
        if (prepare != null)
            prepare.enabled = enabled;

        // Register with XRI only after the final Grip collider and callback owner
        // are ready. This avoids an enabled interactable with a stale collider set.
        if (simple != null)
            simple.enabled = enabled;
    }

    void EnsurePackingStructure(Transform bag)
    {
        TryGetBagLocalBounds(bag, out var localCenter, out var localSize);
        EnsureBagPhysicalColliders(bag, localCenter, localSize);

        var triggerTf = bag.Find(TriggerName);
        if (triggerTf == null)
        {
            var go = new GameObject(TriggerName);
            triggerTf = go.transform;
            triggerTf.SetParent(bag, false);
        }

        triggerTf.localPosition = Vector3.zero;
        triggerTf.localRotation = Quaternion.identity;
        triggerTf.localScale = Vector3.one;
        var box = triggerTf.GetComponent<BoxCollider>();
        if (box == null)
            box = triggerTf.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.enabled = true;
        box.center = localCenter;
        box.size = Vector3.Max(localSize * 0.9f, new Vector3(0.22f, 0.16f, 0.14f));
        packingTrigger = box;
        var body = triggerTf.GetComponent<Rigidbody>();
        if (body == null)
            body = triggerTf.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = true;
        var relay = triggerTf.GetComponent<ResponseBagPackingTriggerRelay>();
        if (relay == null)
            relay = triggerTf.gameObject.AddComponent<ResponseBagPackingTriggerRelay>();
        relay.Controller = this;

        var slots = bag.Find(SlotsRootName);
        if (slots == null)
        {
            var go = new GameObject(SlotsRootName);
            slots = go.transform;
            slots.SetParent(bag, false);
            slots.localPosition = Vector3.zero;
            slots.localRotation = Quaternion.identity;
        }

        hclMeterSlot = EnsureNamedSlot(slots, "HClMeterSlot",
            localCenter + new Vector3(-localSize.x * 0.22f, -localSize.y * 0.12f, 0f));
        absorbentPadSlot = EnsureNamedSlot(slots, "AbsorbentPadSlot",
            localCenter + new Vector3(localSize.x * 0.22f, -localSize.y * 0.18f, 0f));
        absorbentSockSlot = EnsureNamedSlot(slots, "AbsorbentSockSlot",
            localCenter + new Vector3(0f, -localSize.y * 0.08f, 0f));
    }

    static void EnsureBagPhysicalColliders(Transform bag, Vector3 localCenter, Vector3 localSize)
    {
        if (bag == null)
            return;

        var rootColliders = bag.GetComponents<Collider>();
        for (var i = 0; i < rootColliders.Length; i++)
        {
            if (rootColliders[i] != null)
                rootColliders[i].enabled = false;
        }

        var physical = bag.Find(PhysicalRootName);
        if (physical == null)
        {
            var go = new GameObject(PhysicalRootName);
            physical = go.transform;
            physical.SetParent(bag, false);
        }

        physical.localPosition = Vector3.zero;
        physical.localRotation = Quaternion.identity;
        physical.localScale = Vector3.one;

        var half = localSize * 0.5f;
        var min = localCenter - half;
        var max = localCenter + half;
        var wall = 0.04f;

        EnsurePhysicalBox(physical, "Bottom",
            new Vector3(localCenter.x, min.y + wall * 0.5f, localCenter.z),
            new Vector3(localSize.x, wall, localSize.z));
        EnsurePhysicalBox(physical, "LeftSide",
            new Vector3(min.x + wall * 0.5f, localCenter.y, localCenter.z),
            new Vector3(wall, localSize.y, localSize.z));
        EnsurePhysicalBox(physical, "RightSide",
            new Vector3(max.x - wall * 0.5f, localCenter.y, localCenter.z),
            new Vector3(wall, localSize.y, localSize.z));
        EnsurePhysicalBox(physical, "Back",
            new Vector3(localCenter.x, localCenter.y, max.z - wall * 0.5f),
            new Vector3(localSize.x, localSize.y, wall));
    }

    static bool TryGetBagLocalBounds(Transform bag, out Vector3 localCenter, out Vector3 localSize)
    {
        var world = EncapsulateBagVisual(bag);
        if (world.size.sqrMagnitude >= 0.01f)
        {
            localCenter = bag.InverseTransformPoint(world.center);
            var lossy = bag.lossyScale;
            localSize = new Vector3(
                world.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                world.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                world.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
            return true;
        }

        var rootBox = bag.GetComponent<BoxCollider>();
        if (rootBox != null)
        {
            localCenter = rootBox.center;
            localSize = rootBox.size;
            return true;
        }

        localCenter = new Vector3(0f, 0.14f, 0f);
        localSize = new Vector3(0.36f, 0.28f, 0.22f);
        return false;
    }

    static Bounds EncapsulateBagVisual(Transform bag)
    {
        var renderers = bag.GetComponentsInChildren<Renderer>(true);
        var found = false;
        var bounds = new Bounds(bag.position, Vector3.zero);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;
            if (renderer.GetComponentInParent<ResponseBagPackableItem>() != null)
                continue;
            if (IsPackingUtilityRenderer(renderer.transform, bag))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }

        return found ? bounds : new Bounds(bag.position, Vector3.zero);
    }

    static bool IsPackingUtilityRenderer(Transform t, Transform bag)
    {
        while (t != null && t != bag)
        {
            var n = t.name;
            if (n == TriggerName || n == SlotsRootName || n == PhysicalRootName || n == ItemsRootName)
                return true;
            t = t.parent;
        }

        return false;
    }

    static void EnsurePhysicalBox(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var child = parent.Find(name);
        if (child == null)
        {
            var go = new GameObject(name);
            child = go.transform;
            child.SetParent(parent, false);
        }

        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        child.gameObject.layer = parent.gameObject.layer;

        var box = child.GetComponent<BoxCollider>();
        if (box == null)
            box = child.gameObject.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
        box.isTrigger = false;
        box.enabled = true;
        IgnoreLocomotionOnCollider(box);
    }

    static Transform EnsureNamedSlot(Transform parent, string slotName, Vector3 localPos)
    {
        var slot = parent.Find(slotName);
        if (slot == null)
        {
            var go = new GameObject(slotName);
            slot = go.transform;
            slot.SetParent(parent, false);
        }

        slot.localPosition = localPos;
        slot.localRotation = Quaternion.identity;
        slot.localScale = Vector3.one;
        return slot;
    }

    GameObject FindDesk()
    {
        var existing = GameObject.Find(DeskName);
        if (existing != null)
            return existing;

        Debug.LogWarning("[PPE Table Packing] PPE_DeskTop not found — leaving authored scene layout unchanged.", this);
        return null;
    }

    static Transform EnsureItemsRoot()
    {
        var existing = GameObject.Find(ItemsRootName);
        if (existing != null)
            return existing.transform;

        var go = new GameObject(ItemsRootName);
        return go.transform;
    }

    void DetachPreparationItemsFromDesk(GameObject desk, ResponseBagContentsRevealController reveal)
    {
        if (desk == null)
            return;

        var itemsRoot = EnsureItemsRoot();
        DetachIfDeskChild(reveal != null ? reveal.GasDetectorRoot : null, desk, itemsRoot);
        DetachIfDeskChild(reveal != null ? reveal.AbsorbentPadRoot : null, desk, itemsRoot);
        DetachIfDeskChild(reveal != null ? reveal.AbsorbentSockRoot : null, desk, itemsRoot);
    }

    static void DetachIfDeskChild(GameObject item, GameObject desk, Transform itemsRoot)
    {
        if (item == null || desk == null || itemsRoot == null)
            return;
        if (!item.transform.IsChildOf(desk.transform))
            return;
        item.transform.SetParent(itemsRoot, true);
    }

    static void IgnoreLocomotionOnCollider(Collider col)
    {
        TryIgnoreLocomotionOnCollider(col);
    }

    static bool TryIgnoreLocomotionOnCollider(Collider col)
    {
        if (col == null || col.isTrigger)
            return false;

        var ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast >= 0)
            col.gameObject.layer = ignoreRaycast;

        var controller = ResolvePlayerCharacterController();
        if (controller == null)
            return false;

        Physics.IgnoreCollision(col, controller, true);
        return true;
    }

    static CharacterController ResolvePlayerCharacterController()
    {
        var origin = PPEActiveXrOrigin.Resolve();
        if (origin == null)
            return null;
        var controller = origin.GetComponent<CharacterController>();
        if (controller != null)
            return controller;
        controller = origin.GetComponentInChildren<CharacterController>(true);
        if (controller != null)
            return controller;
        if (origin.Origin != null)
        {
            controller = origin.Origin.GetComponent<CharacterController>();
            if (controller != null)
                return controller;
            controller = origin.Origin.GetComponentInChildren<CharacterController>(true);
            if (controller != null)
                return controller;
        }

        var originTransform = PPEActiveXrOrigin.ResolveOriginTransform();
        return originTransform != null
            ? originTransform.GetComponentInChildren<CharacterController>(true)
            : null;
    }

    void EnsureTableTopCollider(GameObject desk)
    {
        if (desk == null)
            return;

        var leftover = desk.transform.Find("Physics");
        if (leftover != null)
            Destroy(leftover.gameObject);

        var bounds = EncapsulateDeskSurface(desk.transform);
        var go = GameObject.Find(TopColliderName);
        if (go == null)
            go = new GameObject(TopColliderName);

        go.transform.SetParent(null);
        var box = go.GetComponent<BoxCollider>();
        if (box == null)
            box = go.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.enabled = true;

        var topY = ResolveTableTopY();
        var sizeX = bounds.size.x;
        var sizeZ = bounds.size.z;
        if (sizeX < 0.2f || sizeX > 1.8f)
            sizeX = 1.2f;
        if (sizeZ < 0.2f || sizeZ > 1.2f)
            sizeZ = 0.7f;

        var surfaceCenter = bounds.size.sqrMagnitude >= 0.01f
            ? bounds.center
            : desk.transform.position;
        go.transform.SetPositionAndRotation(
            new Vector3(surfaceCenter.x, topY, surfaceCenter.z),
            Quaternion.identity);
        box.center = new Vector3(0f, -0.03f, 0f);
        box.size = new Vector3(sizeX, 0.06f, sizeZ);
        tableTopCollider = box;
        tableTopIgnoresLocomotion = TryIgnoreLocomotionOnCollider(box);
    }

    float ResolveTableTopY()
    {
        if (tableRoot == null)
            return 0.78f;
        var bounds = EncapsulateDeskSurface(tableRoot);
        var relative = bounds.max.y - tableRoot.position.y;
        if (bounds.size.sqrMagnitude < 0.01f || relative < 0.4f || relative > 1.15f)
            return tableRoot.position.y + 0.78f;
        return bounds.max.y;
    }

    static void CompactSockCollider(ResponseBagPackableItem sock)
    {
        if (sock == null)
            return;
        var box = sock.GetComponent<BoxCollider>();
        if (box == null)
            return;
        var size = box.size;
        var longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        if (longest <= 0.55f)
            return;
        var scale = 0.55f / longest;
        box.size = size * scale;
    }

    static void FitPhysicalCollider(GameObject root, string itemId)
    {
        var all = root.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i].gameObject == root)
                continue;
            all[i].enabled = false;
        }

        var existing = root.GetComponents<Collider>();
        for (var i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null && existing[i] is not BoxCollider)
                existing[i].enabled = false;
        }

        var box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = root.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.enabled = true;

        var bounds = EncapsulateRenderers(root.transform);
        if (bounds.size.sqrMagnitude < 0.0001f)
        {
            box.size = new Vector3(0.12f, 0.08f, 0.12f);
            box.center = Vector3.zero;
            return;
        }

        var localCenter = root.transform.InverseTransformPoint(bounds.center);
        var localSize = root.transform.InverseTransformVector(bounds.size);
        localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
        if (string.Equals(itemId, ResponseEquipmentManager.IdAbsorbentSock, System.StringComparison.Ordinal))
        {
            localSize.x = Mathf.Min(localSize.x, 0.55f);
            localSize.z = Mathf.Min(localSize.z, 0.22f);
        }

        box.center = localCenter;
        box.size = Vector3.Max(localSize, new Vector3(0.06f, 0.04f, 0.06f));
    }

    static Bounds EncapsulateDeskSurface(Transform desk)
    {
        var renderers = desk.GetComponentsInChildren<Renderer>();
        var found = false;
        var bounds = new Bounds(desk.position, Vector3.zero);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;
            if (renderer.GetComponentInParent<ResponseBagPackableItem>() != null)
                continue;
            if (IsPreparationItemRenderer(renderer))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }

        return found ? bounds : new Bounds(desk.position, Vector3.zero);
    }

    static bool IsPreparationItemRenderer(Renderer renderer)
    {
        var t = renderer.transform;
        while (t != null)
        {
            var n = t.name;
            if (n == "GasDetectorSlot" || n == "AbsorbentPadSlot" || n == "AbsorbentSockSlot"
                || n == "PadStackVisual" || n == ItemsRootName)
                return true;
            t = t.parent;
        }

        return false;
    }

    static Bounds EncapsulateRenderers(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        var found = false;
        var bounds = new Bounds(root.position, Vector3.zero);
        for (var i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !renderers[i].enabled)
                continue;
            if (!found)
            {
                bounds = renderers[i].bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderers[i].bounds);
        }

        return found ? bounds : new Bounds(root.position, Vector3.zero);
    }

    void BeginCaptureIfNeeded()
    {
        if (captureStarted)
            return;
        captureStarted = true;
        SessionEquipmentInventory.Ensure().BeginPpeBagCapture();
    }

    static void StoreInventory(string itemId)
    {
        var sharedId = itemId;
        if (string.Equals(itemId, ResponseEquipmentManager.IdGasDetector, System.StringComparison.Ordinal))
            sharedId = SharedEquipmentIds.GasDetector;
        else if (string.Equals(itemId, ResponseEquipmentManager.IdAbsorbentPad, System.StringComparison.Ordinal))
            sharedId = SharedEquipmentIds.AbsorbentPad;
        else if (string.Equals(itemId, ResponseEquipmentManager.IdAbsorbentSock, System.StringComparison.Ordinal))
            sharedId = SharedEquipmentIds.AbsorbentSock;
        SessionEquipmentInventory.Ensure().Store(sharedId);
    }

    static string PackedMessage(string itemId)
    {
        if (string.Equals(itemId, ResponseEquipmentManager.IdGasDetector, System.StringComparison.Ordinal))
            return "HCl \uCE21\uC815\uAE30 \uC900\uBE44 \uC644\uB8CC";
        if (string.Equals(itemId, ResponseEquipmentManager.IdAbsorbentPad, System.StringComparison.Ordinal))
            return "\uD761\uCC29\uD328\uB4DC \uC900\uBE44 \uC644\uB8CC";
        if (string.Equals(itemId, ResponseEquipmentManager.IdAbsorbentSock, System.StringComparison.Ordinal))
            return "\uD761\uCC29\uC0AD \uC900\uBE44 \uC644\uB8CC";
        return "\uC7A5\uBE44 \uC900\uBE44 \uC644\uB8CC";
    }

    static void ShowFeedback(string message)
    {
        var feedback = Object.FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        feedback?.ShowMessage(message);
    }

    void OnPhaseChanged(PPETrainingPhase _, PPETrainingPhase next)
    {
        if (next != PPETrainingPhase.EquipmentPreparation)
            return;
        EnsureTableLayout();
        if (AllPacked)
        {
            var bag = equipmentManager != null && equipmentManager.ResponseBagRoot != null
                ? equipmentManager.ResponseBagRoot.transform
                : null;
            SetBagConfirmationEnabled(bag, true);
            ShowFeedback(AllPackedMessage);
        }
    }

    void SubscribeWrongItemReleases()
    {
        if (subscribedWrongRelease)
            return;
        subscribedWrongRelease = true;
        var grabs = Object.FindObjectsByType<PPEHandGrabbable>(FindObjectsInactive.Include);
        for (var i = 0; i < grabs.Length; i++)
        {
            var grab = grabs[i];
            if (grab == null)
                continue;
            grab.OnReleased += _ => NotifyUnknownReleased(grab);
        }
    }

    static bool IsPacked(ResponseBagPackableItem item)
    {
        return item != null && item.IsPacked;
    }
}

/// <summary>Forwards bag-mouth trigger occupancy to the packing controller.</summary>
[DisallowMultipleComponent]
public sealed class ResponseBagPackingTriggerRelay : MonoBehaviour
{
    public ResponseBagPackingController Controller { get; set; }

    void OnTriggerEnter(Collider other)
    {
        Controller?.NotifyTriggerEnter(other);
    }

    void OnTriggerExit(Collider other)
    {
        Controller?.NotifyTriggerExit(other);
    }
}
