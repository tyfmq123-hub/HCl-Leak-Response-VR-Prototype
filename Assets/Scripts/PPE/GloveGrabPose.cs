using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Loose PPE glove grab: snaps to a cuff-edge <see cref="gripPoint"/> so the
/// glove hangs below the hand instead of seating into the palm like a wearable.
/// </summary>
[DisallowMultipleComponent]
public class GloveGrabPose : XRGrabInteractable
{
    [Header("Cuff Grip")]
    [Tooltip("Attach at the outer cuff/opening rim. Glove hangs below the VR hand.")]
    [SerializeField] Transform gripPoint;

    public Transform GripPoint => gripPoint;

    public void EditorWire(Transform cuffGrip)
    {
        gripPoint = cuffGrip;
        attachTransform = cuffGrip;
        useDynamicAttach = false;
        matchAttachPosition = true;
        matchAttachRotation = true;
        trackPosition = true;
        trackRotation = true;
        movementType = MovementType.Kinematic;
        attachEaseInTime = 0.12f;
        throwOnDetach = false;
    }

    public override Transform GetAttachTransform(IXRInteractor interactor)
    {
        if (gripPoint != null)
            return gripPoint;
        return base.GetAttachTransform(interactor);
    }

    protected override void OnSelectEntering(SelectEnterEventArgs args)
    {
        if (gripPoint != null)
            attachTransform = gripPoint;
        useDynamicAttach = false;
        base.OnSelectEntering(args);
    }
}
