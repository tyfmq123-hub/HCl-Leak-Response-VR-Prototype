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

    /// <summary>Raised while XRI is entering selection.</summary>
    public event Action<PPEGrabHand> OnGrabStarted;

    /// <summary>Raised after XRI has completed selection.</summary>
    public event Action<PPEGrabHand> OnGrabbed;

    /// <summary>Raised after XRI has released the object.</summary>
    public event Action<PPEGrabHand> OnReleased;

    public bool IsGrabbed { get; private set; }
    public PPEGrabHand CurrentHand { get; private set; } = PPEGrabHand.Unknown;

    protected override void OnSelectEntering(SelectEnterEventArgs args)
    {
        base.OnSelectEntering(args);

        CurrentHand = ResolveHand(args.interactorObject);
        OnGrabStarted?.Invoke(CurrentHand);
        grabStarted.Invoke(CurrentHand);
    }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);

        IsGrabbed = true;
        CurrentHand = ResolveHand(args.interactorObject);
        OnGrabbed?.Invoke(CurrentHand);
        grabbed.Invoke(CurrentHand);
    }

    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        var releasedHand = ResolveHand(args.interactorObject);
        if (releasedHand == PPEGrabHand.Unknown)
            releasedHand = CurrentHand;

        base.OnSelectExited(args);

        IsGrabbed = false;
        OnReleased?.Invoke(releasedHand);
        released.Invoke(releasedHand);
        CurrentHand = PPEGrabHand.Unknown;
    }

    static PPEGrabHand ResolveHand(IXRInteractor interactor)
    {
        if (interactor == null)
            return PPEGrabHand.Unknown;

        switch (interactor.handedness)
        {
            case InteractorHandedness.Left:
                return PPEGrabHand.Left;
            case InteractorHandedness.Right:
                return PPEGrabHand.Right;
        }

        // Compatibility fallback for older/custom interactors that do not expose
        // handedness but are parented under a named hand/controller transform.
        for (var current = interactor.transform; current != null; current = current.parent)
        {
            if (current.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
                return PPEGrabHand.Left;
            if (current.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
                return PPEGrabHand.Right;
        }

        return PPEGrabHand.Unknown;
    }
}
