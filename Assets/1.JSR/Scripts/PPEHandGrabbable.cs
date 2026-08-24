using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>Identifies the hand that owns a PPE grab.</summary>
public enum PPEGrabHand
{
    Unknown,
    Left,
    Right,
}

/// <summary>Inspector event carrying the hand that grabbed or released the PPE.</summary>
[Serializable]
public sealed class PPEGrabHandUnityEvent : UnityEvent<PPEGrabHand>
{
}

/// <summary>
/// XRI grab interaction for wearable PPE. It works with both direct hand and ray
/// interactors and deliberately does not request or perform PPE equip.
/// </summary>
[DisallowMultipleComponent]
public class PPEHandGrabbable : XRGrabInteractable
{
    [Header("PPE Grab Events")]
    [SerializeField]
    PPEGrabHandUnityEvent grabStarted = new PPEGrabHandUnityEvent();

    [SerializeField]
    PPEGrabHandUnityEvent grabbed = new PPEGrabHandUnityEvent();

    [SerializeField]
    PPEGrabHandUnityEvent released = new PPEGrabHandUnityEvent();

    /// <summary>
    /// Raised before XRI applies grab. Used to release a cabinet stored lock
    /// without introducing a second grab system.
    /// </summary>
    public event Action GrabStarting;

    /// <summary>Raised while XRI is entering selection.</summary>
    public event Action<PPEGrabHand> OnGrabStarted;

    /// <summary>Raised after XRI has completed selection.</summary>
    public event Action<PPEGrabHand> OnGrabbed;

    /// <summary>Raised after XRI has released the object.</summary>
    public event Action<PPEGrabHand> OnReleased;

    public bool IsGrabbed { get; private set; }
    public PPEGrabHand CurrentHand { get; private set; } = PPEGrabHand.Unknown;
    public PPEGrabHand GrabSessionHoldingHand { get; private set; } = PPEGrabHand.Unknown;
    public PPEGrabHandResolver.Result LastHandResolution { get; private set; }

    /// <summary>Full room reset: skip release-to-equip while XRI selection is cancelled.</summary>
    public static bool SuppressReleaseEquip { get; set; }

    protected override void Awake()
    {
        EnsureProximityFilter();
        EnsureCabinetStoredDisplay();
        base.Awake();
        ApplyWearableThrowPolicy();
        var filter = GetComponent<PPEDirectGrabProximityFilter>();
        filter?.RegisterRuntimeSelectFilter();
    }

    protected override void OnSelectEntering(SelectEnterEventArgs args)
    {
        ApplyWearableThrowPolicy();
        GrabStarting?.Invoke();
        base.OnSelectEntering(args);

        CurrentHand = ResolveHand(args.interactorObject, transform);
        GrabSessionHoldingHand = CurrentHand;
        OnGrabStarted?.Invoke(CurrentHand);
        grabStarted.Invoke(CurrentHand);
    }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        ApplyWearableThrowPolicy();
        base.OnSelectEntered(args);

        IsGrabbed = true;
        LastHandResolution = PPEGrabHandResolver.ResolveDetailed(args.interactorObject, transform);
        CurrentHand = LastHandResolution.Hand;
        GrabSessionHoldingHand = CurrentHand;
        if (IsInnerGloveMember())
        {
            var member = GetComponent<PPEGlovePairMember>();
            Debug.Log(
                "[InnerGlove Grab] Member=" + (member != null ? member.GloveSide.ToString() : "?") +
                " CapturedHolding=" + GrabSessionHoldingHand,
                this);
        }
        else
        {
            Debug.Log(LastHandResolution.Log, this);
        }
        OnGrabbed?.Invoke(CurrentHand);
        grabbed.Invoke(CurrentHand);
    }

    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        ApplyWearableThrowPolicy();

        var sessionHand = GrabSessionHoldingHand != PPEGrabHand.Unknown
            ? GrabSessionHoldingHand
            : CurrentHand;
        var equippedThisRelease = false;
        if (!SuppressReleaseEquip)
        {
            if (IsInnerGloveMember())
                equippedThisRelease = PPEInnerGloveDonning.TryEquipInnerGloveOnRelease(this, sessionHand);
            else
                equippedThisRelease = PPEReleaseEquip.TryEquipOnRelease(this, sessionHand);
        }

        var stored = GetComponent<PPEStoredDisplayState>();
        if (equippedThisRelease)
            stored?.NotifyEquipped();

        var releasedHand = sessionHand;
        if (!IsInnerGloveMember())
        {
            releasedHand = ResolveHand(args.interactorObject, transform);
            if (releasedHand == PPEGrabHand.Unknown)
                releasedHand = sessionHand;
        }

        base.OnSelectExited(args);

        if (equippedThisRelease)
            stored?.NotifyEquipped();
        else if (!SuppressReleaseEquip)
            stored?.NotifyInvalidReleaseIfHeld();

        IsGrabbed = false;
        OnReleased?.Invoke(releasedHand);
        released.Invoke(releasedHand);
        CurrentHand = PPEGrabHand.Unknown;
        GrabSessionHoldingHand = PPEGrabHand.Unknown;
    }

    static PPEGrabHand ResolveHand(IXRInteractor interactor, Transform interactableRoot)
    {
        return PPEGrabHandResolver.Resolve(interactor, interactableRoot);
    }

    public void ApplyWearableThrowPolicy()
    {
        if (!IsCabinetWearable())
            return;

        throwOnDetach = false;
        forceGravityOnDetach = false;
    }

    [System.Obsolete("Use ApplyWearableThrowPolicy.")]
    public void ApplyInnerGloveThrowPolicy()
    {
        ApplyWearableThrowPolicy();
    }

    bool IsCabinetWearable()
    {
        if (GetComponent<PPEGlovePairCoordinator>() != null)
            return false;
        if (GetComponent<PPEGlovePairMember>() != null)
            return true;
        var equipable = GetComponent<PPEEquipable>();
        if (equipable == null)
            return false;
        return !string.Equals(equipable.ItemId, "ResponseBag", StringComparison.Ordinal);
    }

    bool IsInnerGloveMember()
    {
        var member = GetComponent<PPEGlovePairMember>();
        return member != null && member.IsInnerGlove;
    }

    void EnsureCabinetStoredDisplay()
    {
        var equipable = GetComponent<PPEEquipable>();
        var gloveMember = GetComponent<PPEGlovePairMember>();
        if (equipable == null && gloveMember == null)
            return;
        if (equipable != null &&
            string.Equals(equipable.ItemId, "ResponseBag", StringComparison.Ordinal))
            return;
        if (GetComponent<PPEGlovePairCoordinator>() != null)
            return;

        if (GetComponent<PPEStoredDisplayState>() == null)
            gameObject.AddComponent<PPEStoredDisplayState>();
        PPEPrimitiveGrabVolumes.EnsureForWearable(gameObject);
    }

    void EnsureProximityFilter()
    {
        var filter = GetComponent<PPEDirectGrabProximityFilter>();
        if (filter == null)
            filter = gameObject.AddComponent<PPEDirectGrabProximityFilter>();
        filter.RegisterWithInteractable();
    }
}
