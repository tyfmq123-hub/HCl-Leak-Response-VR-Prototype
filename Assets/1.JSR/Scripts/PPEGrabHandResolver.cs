using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Resolves which XR hand selected a PPE grabbable. Name-walking the full
/// parent chain is not used in production because it matches unrelated
/// objects such as LeftInnerGlovePhysical.
/// </summary>
public static class PPEGrabHandResolver
{
    const float PinchJointDistance = 0.028f;
    const float NearObjectDistance = 0.22f;

    public readonly struct Result
    {
        public readonly PPEGrabHand Hand;
        public readonly string Source;
        public readonly string SourceLabel;
        public readonly string InteractorName;
        public readonly string InteractorPath;
        public readonly string InteractorType;
        public readonly string InteractorHandedness;
        public readonly string Log;

        public Result(
            PPEGrabHand hand,
            string source,
            string sourceLabel,
            string interactorName,
            string interactorPath,
            string interactorType,
            string interactorHandedness,
            string log)
        {
            Hand = hand;
            Source = source;
            SourceLabel = sourceLabel;
            InteractorName = interactorName;
            InteractorPath = interactorPath;
            InteractorType = interactorType;
            InteractorHandedness = interactorHandedness;
            Log = log;
        }
    }

    public static PPEGrabHand Resolve(IXRInteractor interactor, Transform interactableRoot)
    {
        return ResolveDetailed(interactor, interactableRoot).Hand;
    }

    public static Result ResolveDetailed(IXRInteractor interactor, Transform interactableRoot)
    {
        var log = new StringBuilder();
        log.AppendLine("[PPE Hand Detection]");
        log.AppendLine("Member: " + (interactableRoot != null ? interactableRoot.name : "(null)"));

        if (interactor == null)
        {
            log.AppendLine("Interactor: (null)");
            log.AppendLine("Resolved HoldingHand: Unknown");
            log.AppendLine("Resolution Source: null interactor");
            return new Result(
                PPEGrabHand.Unknown,
                "null interactor",
                "fallback",
                "(null)",
                "(null)",
                "(null)",
                "None",
                log.ToString());
        }

        var interactorGo = interactor.transform != null ? interactor.transform.gameObject : null;
        log.AppendLine("Interactor GameObject: " + (interactorGo != null ? interactorGo.name : "(null)"));
        log.AppendLine("Interactor Transform Path: " + BuildPath(interactor.transform));
        log.AppendLine("Interactor Type: " + interactor.GetType().Name);
        log.AppendLine("Interactor handedness: " + interactor.handedness);
        log.AppendLine("Interactor parent: " + (interactor.transform.parent != null ? interactor.transform.parent.name : "(none)"));
        log.AppendLine("Interactor root: " + (interactor.transform.root != null ? interactor.transform.root.name : "(none)"));

        var fromXri = FromInteractorHandedness(interactor);
        var fromEvents = FromHandTrackingEvents(interactor.transform);
        var fromNode = FromClosestHandDevice(interactor.transform);
        var fromPinch = FromPinchNearObject(interactableRoot);
        var fromPalm = FromCloserPalm(interactableRoot);
        var fromOwnName = FromOwnGameObjectName(interactorGo);

        log.AppendLine("XRNode (closest device to interactor): " + FormatHand(fromNode));
        log.AppendLine("XRHandTrackingEvents: " + FormatHand(fromEvents));
        log.AppendLine("XRHands pinch near object: " + FormatHand(fromPinch));
        log.AppendLine("Closer XR Palm to object: " + FormatHand(fromPalm));
        log.AppendLine("Own GameObject name token: " + FormatHand(fromOwnName));

        string source;
        PPEGrabHand resolved;

        // The selecting interactor is the only authoritative hand for a SelectAttempt.
        // Pinch / closer-palm used to override it from the object's shelf position,
        // so a second glove grabbed by the already-equipped hand was recorded as the
        // still-bare hand until a hand-off moved the object.
        if (fromXri != PPEGrabHand.Unknown)
        {
            resolved = fromXri;
            source = "Interactor.handedness";
        }
        else if (fromEvents != PPEGrabHand.Unknown)
        {
            resolved = fromEvents;
            source = "XRHandTrackingEvents";
        }
        else if (fromNode != PPEGrabHand.Unknown)
        {
            resolved = fromNode;
            source = "XRNode";
        }
        else if (fromOwnName != PPEGrabHand.Unknown)
        {
            resolved = fromOwnName;
            source = "Interactor GameObject name";
        }
        else if (fromPinch != PPEGrabHand.Unknown)
        {
            resolved = fromPinch;
            source = "XRHands pinch near object";
        }
        else if (fromPalm != PPEGrabHand.Unknown)
        {
            resolved = fromPalm;
            source = "Closer XR Palm to object";
        }
        else
        {
            resolved = PPEGrabHand.Unknown;
            source = "none — Unknown (no authoritative side)";
        }

        log.AppendLine("Resolved HoldingHand: " + resolved);
        log.AppendLine("Resolution Source: " + source);
        return new Result(
            resolved,
            source,
            ToSourceLabel(source),
            interactorGo != null ? interactorGo.name : "(null)",
            BuildPath(interactor.transform),
            interactor.GetType().Name,
            interactor.handedness.ToString(),
            log.ToString());
    }

    static string ToSourceLabel(string source)
    {
        if (source == "Interactor.handedness")
            return "XRI handedness";
        if (source == "XRNode")
            return "XRNode";
        if (source == "XRHands pinch near object" || source == "Closer XR Palm to object")
            return "XRHandDevice";
        if (source == "XRHandTrackingEvents")
            return "serialized rig";
        return "fallback";
    }

    static PPEGrabHand FromInteractorHandedness(IXRInteractor interactor)
    {
        switch (interactor.handedness)
        {
            case InteractorHandedness.Left:
                return PPEGrabHand.Left;
            case InteractorHandedness.Right:
                return PPEGrabHand.Right;
        }

        if (interactor is XRBaseInteractor xrBase)
        {
            switch (xrBase.handedness)
            {
                case InteractorHandedness.Left:
                    return PPEGrabHand.Left;
                case InteractorHandedness.Right:
                    return PPEGrabHand.Right;
            }
        }

        return PPEGrabHand.Unknown;
    }

    static PPEGrabHand FromHandTrackingEvents(Transform transform)
    {
        if (transform == null)
            return PPEGrabHand.Unknown;

        var events = transform.GetComponentInParent<XRHandTrackingEvents>(true);
        if (events == null)
            return PPEGrabHand.Unknown;

        switch (events.handedness)
        {
            case Handedness.Left:
                return PPEGrabHand.Left;
            case Handedness.Right:
                return PPEGrabHand.Right;
            default:
                return PPEGrabHand.Unknown;
        }
    }

    static PPEGrabHand FromClosestHandDevice(Transform transform)
    {
        if (transform == null)
            return PPEGrabHand.Unknown;

        var origin = transform.position;
        var leftDist = DistanceToNode(XRNode.LeftHand, origin);
        var rightDist = DistanceToNode(XRNode.RightHand, origin);
        if (float.IsPositiveInfinity(leftDist) && float.IsPositiveInfinity(rightDist))
            return PPEGrabHand.Unknown;
        if (leftDist + 0.02f < rightDist)
            return PPEGrabHand.Left;
        if (rightDist + 0.02f < leftDist)
            return PPEGrabHand.Right;
        return PPEGrabHand.Unknown;
    }

    static float DistanceToNode(XRNode node, Vector3 origin)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return float.PositiveInfinity;
        if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out var position))
            return float.PositiveInfinity;
        return Vector3.Distance(origin, position);
    }

    static PPEGrabHand FromPinchNearObject(Transform interactableRoot)
    {
        if (interactableRoot == null)
            return PPEGrabHand.Unknown;

        var leftPinch = IsPinchingNear(PPEGrabHand.Left, interactableRoot.position);
        var rightPinch = IsPinchingNear(PPEGrabHand.Right, interactableRoot.position);
        if (leftPinch && !rightPinch)
            return PPEGrabHand.Left;
        if (rightPinch && !leftPinch)
            return PPEGrabHand.Right;
        return PPEGrabHand.Unknown;
    }

    static bool IsPinchingNear(PPEGrabHand hand, Vector3 objectPosition)
    {
        if (!PPEXrHandsJointPose.TryGetWorldPose(hand, XRHandJointID.Palm, out var palm))
            return false;
        if (Vector3.Distance(palm.position, objectPosition) > NearObjectDistance)
            return false;
        if (!PPEXrHandsJointPose.TryGetWorldPose(hand, XRHandJointID.ThumbTip, out var thumb))
            return false;
        if (!PPEXrHandsJointPose.TryGetWorldPose(hand, XRHandJointID.IndexTip, out var index))
            return false;
        return Vector3.Distance(thumb.position, index.position) <= PinchJointDistance;
    }

    static PPEGrabHand FromCloserPalm(Transform interactableRoot)
    {
        if (interactableRoot == null)
            return PPEGrabHand.Unknown;

        var hasLeft = PPEXrHandsJointPose.TryGetWorldPose(PPEGrabHand.Left, XRHandJointID.Palm, out var leftPalm);
        var hasRight = PPEXrHandsJointPose.TryGetWorldPose(PPEGrabHand.Right, XRHandJointID.Palm, out var rightPalm);
        var target = interactableRoot.position;
        if (hasLeft && hasRight)
        {
            var leftDist = Vector3.Distance(leftPalm.position, target);
            var rightDist = Vector3.Distance(rightPalm.position, target);
            if (leftDist > NearObjectDistance && rightDist > NearObjectDistance)
                return PPEGrabHand.Unknown;
            if (rightDist + 0.03f < leftDist)
                return PPEGrabHand.Right;
            if (leftDist + 0.03f < rightDist)
                return PPEGrabHand.Left;
            return PPEGrabHand.Unknown;
        }

        if (hasLeft && Vector3.Distance(leftPalm.position, target) <= NearObjectDistance)
            return PPEGrabHand.Left;
        if (hasRight && Vector3.Distance(rightPalm.position, target) <= NearObjectDistance)
            return PPEGrabHand.Right;
        return PPEGrabHand.Unknown;
    }

    static PPEGrabHand FromOwnGameObjectName(GameObject gameObject)
    {
        if (gameObject == null)
            return PPEGrabHand.Unknown;

        var name = gameObject.name;
        var startsLeft = name.StartsWith("Left", System.StringComparison.OrdinalIgnoreCase);
        var startsRight = name.StartsWith("Right", System.StringComparison.OrdinalIgnoreCase);
        if (startsLeft && !startsRight)
            return PPEGrabHand.Left;
        if (startsRight && !startsLeft)
            return PPEGrabHand.Right;
        return PPEGrabHand.Unknown;
    }

    static string BuildPath(Transform transform)
    {
        if (transform == null)
            return "(null)";
        var names = new List<string>(8);
        for (var current = transform; current != null; current = current.parent)
            names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
    }

    static string FormatHand(PPEGrabHand hand)
    {
        return hand == PPEGrabHand.Unknown ? "Unknown" : hand.ToString();
    }
}
