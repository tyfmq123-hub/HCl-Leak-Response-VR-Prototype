using UnityEngine;

/// <summary>
/// Shared release-to-equip trigger for wearable PPE. InnerGloves keep their
/// existing donning path. Other wearables use the same distance-on-SelectExited
/// rule against the live target. Face/Suit/Helmet have no Ghost mesh.
/// </summary>
public static class PPEReleaseEquip
{
    public static bool IsWearableReleaseItem(PPEEquipable item)
    {
        if (item == null || string.IsNullOrEmpty(item.ItemId))
            return false;
        var id = item.ItemId;
        return string.Equals(id, PPEEquipmentManager.IdChemicalGloves, System.StringComparison.Ordinal) ||
               string.Equals(id, PPEEquipmentManager.IdChemicalBoots, System.StringComparison.Ordinal) ||
               string.Equals(id, PPEEquipmentManager.IdChemicalSuit, System.StringComparison.Ordinal) ||
               string.Equals(id, PPEEquipmentManager.IdFaceRespirator, System.StringComparison.Ordinal) ||
               string.Equals(id, PPEEquipmentManager.IdFaceShield, System.StringComparison.Ordinal) ||
               string.Equals(id, PPEEquipmentManager.IdHelmet, System.StringComparison.Ordinal);
    }

    public static bool TryEquipOnRelease(PPEHandGrabbable grabbable, PPEGrabHand capturedHolding)
    {
        if (grabbable == null)
            return false;

        var member = grabbable.GetComponent<PPEGlovePairMember>();
        if (member != null && member.IsInnerGlove)
            return false;

        if (member != null)
            return TryEquipPairMember(member, capturedHolding);

        var item = grabbable.GetComponent<PPEEquipable>();
        if (!IsWearableReleaseItem(item))
            return false;

        return TryEquipSingle(grabbable, item);
    }

    static bool TryEquipPairMember(PPEGlovePairMember member, PPEGrabHand capturedHolding)
    {
        var coordinator = member != null ? member.Coordinator : null;
        if (member == null || coordinator == null)
            return false;

        var target = member.TargetTransform;
        var follower = target != null ? target.GetComponent<PPEGloveHandTargetFollower>() : null;
        follower?.RefreshForRelease();

        var targetValid = member.IsTargetTracking && target != null;
        var equipRef = member.EnsureEquipReference();
        var maxDistance = member.PlacementDistance;
        var sequenceAllowed = IsSequenceAllowed(member.LogicalEquipable);
        var correctHolding = member.IsOppositeHandGrab(capturedHolding);
        var evaluation = PPEPlacementValidation.Evaluate(
            equipRef,
            target,
            targetValid,
            sequenceAllowed,
            maxDistance,
            member.UseRotationValidation,
            member.PlacementAngleTolerance);

        string result;
        if (!correctHolding)
            result = "WRONG_HOLDING_HAND";
        else if (!evaluation.TargetValid)
            result = "NO_VALID_TARGET_POSE";
        else if (!evaluation.SequenceValid)
            result = "SEQUENCE_BLOCKED";
        else if (!evaluation.DistanceValid)
            result = "TOO_FAR";
        else if (!evaluation.AngleValid)
            result = "ANGLE";
        else
            result = member.GloveSide == PPEGrabHand.Right ? "EQUIP_RIGHT" : "EQUIP_LEFT";

        if (member.IsTrackedHandGlove)
        {
            PPEGloveReleaseDiagnostics.Log(
                member,
                capturedHolding,
                target != null ? target.position : Vector3.zero,
                equipRef,
                evaluation.Distance,
                maxDistance,
                targetValid,
                result);
        }
        else
        {
            LogRelease(
                ItemLabel(member),
                TargetLabel(member),
                targetValid,
                evaluation.Distance,
                maxDistance,
                sequenceAllowed,
                result);
        }

        if (result != "EQUIP_LEFT" && result != "EQUIP_RIGHT")
            return false;

        coordinator.NotifyPairSideFromRelease(member);
        member.GetComponent<PPEStoredDisplayState>()?.NotifyEquipped();
        return true;
    }

    static bool TryEquipSingle(PPEHandGrabbable grabbable, PPEEquipable item)
    {
        Transform target = null;
        var targetLabel = "None";
        var placement = FindPlacement();
        if (placement != null)
            placement.TryGetReleaseTarget(grabbable, out target, out targetLabel);

        if (target == null)
        {
            target = ResolveFallbackTarget(item);
            targetLabel = FallbackTargetLabel(item);
        }

        var body = FindBody();
        var targetValid = target != null && (body == null || body.HasTracking);
        var equipRef = item.EnsurePlacementReference();
        var maxDistance = item.EquipDistance;
        var sequenceAllowed = IsSequenceAllowed(item);
        var evaluation = PPEPlacementValidation.Evaluate(
            equipRef,
            target,
            targetValid,
            sequenceAllowed,
            maxDistance,
            item.UseRotationValidation,
            item.EquipAngleTolerance);

        string result;
        if (!evaluation.TargetValid)
            result = "NO_VALID_TARGET_POSE";
        else if (!evaluation.SequenceValid)
            result = "SEQUENCE_BLOCKED";
        else if (!evaluation.DistanceValid)
            result = "TOO_FAR";
        else if (!evaluation.AngleValid)
            result = "ANGLE";
        else
            result = "EQUIPPED";

        if (IsHelmet(item))
        {
            LogHelmetRelease(
                item,
                target,
                equipRef,
                targetValid,
                sequenceAllowed,
                evaluation.Distance,
                maxDistance,
                result);
        }
        else
        {
            LogRelease(
                ItemLabel(item),
                targetLabel,
                targetValid,
                evaluation.Distance,
                maxDistance,
                sequenceAllowed,
                result);
        }

        if (result != "EQUIPPED")
            return false;

        if (placement != null)
            placement.EquipAfterRelease(item);
        else
            PPEEquipmentManager.Instance?.Equip(item);
        return true;
    }

    static bool IsSequenceAllowed(PPEEquipable item)
    {
        if (item == null)
            return false;
        var sequence = PPESequenceManager.Resolve();
        return sequence == null || sequence.CanEquip(item.ItemId);
    }

    static Transform ResolveFallbackTarget(PPEEquipable item)
    {
        var body = FindBody();
        var anchors = body != null ? body.AnchorSet : null;
        if (anchors == null)
            return null;
        if (item.UsesSlot(PPEEquipSlot.Face))
            return anchors.Face;
        if (item.UsesSlot(PPEEquipSlot.Body))
            return anchors.Body;
        if (item.UsesSlot(PPEEquipSlot.Head))
            return anchors.Head;
        return null;
    }

    static string FallbackTargetLabel(PPEEquipable item)
    {
        if (item == null)
            return "None";
        if (item.UsesSlot(PPEEquipSlot.Face))
            return "Face";
        if (item.UsesSlot(PPEEquipSlot.Body))
            return "Torso";
        if (item.UsesSlot(PPEEquipSlot.Head))
            return "Head";
        return "None";
    }

    static string ItemLabel(PPEGlovePairMember member)
    {
        var id = member != null && member.LogicalEquipable != null
            ? member.LogicalEquipable.ItemId
            : string.Empty;
        var side = member != null && member.GloveSide == PPEGrabHand.Right ? "Right" : "Left";
        if (string.Equals(id, PPEEquipmentManager.IdChemicalBoots, System.StringComparison.Ordinal))
            return side + "Boot";
        if (string.Equals(id, PPEEquipmentManager.IdChemicalGloves, System.StringComparison.Ordinal))
            return side + "ProtectiveGlove";
        return side + "Member";
    }

    static string ItemLabel(PPEEquipable item)
    {
        if (item == null)
            return "(none)";
        if (string.Equals(item.ItemId, PPEEquipmentManager.IdFaceRespirator, System.StringComparison.Ordinal))
            return "Respirator";
        if (string.Equals(item.ItemId, PPEEquipmentManager.IdFaceShield, System.StringComparison.Ordinal))
            return "FaceProtection";
        if (string.Equals(item.ItemId, PPEEquipmentManager.IdChemicalSuit, System.StringComparison.Ordinal))
            return "ProtectiveSuit";
        if (string.Equals(item.ItemId, PPEEquipmentManager.IdHelmet, System.StringComparison.Ordinal))
            return "Helmet";
        return item.ItemId;
    }

    static bool IsHelmet(PPEEquipable item)
    {
        return item != null &&
               string.Equals(item.ItemId, PPEEquipmentManager.IdHelmet, System.StringComparison.Ordinal);
    }

    static string TargetLabel(PPEGlovePairMember member)
    {
        if (member == null)
            return "None";
        if (member.TargetKind == PPEPairTargetKind.BodyLowerLeg)
            return member.GloveSide == PPEGrabHand.Right ? "RightLowerLeg" : "LeftLowerLeg";
        return member.GloveSide == PPEGrabHand.Right ? "RightHand" : "LeftHand";
    }

    static void LogRelease(
        string item,
        string target,
        bool targetValid,
        float distance,
        float maxDistance,
        bool sequenceAllowed,
        string result)
    {
        Debug.Log(
            "[PPE ReleaseEquip]\n" +
            "Item=" + item + "\n" +
            "Target=" + target + "\n" +
            "TargetValid=" + (targetValid ? "True" : "False") + "\n" +
            "Distance=" + (float.IsInfinity(distance) ? "inf" : distance.ToString("0.000")) + "\n" +
            "Max=" + maxDistance.ToString("0.000") + "\n" +
            "Sequence=" + (sequenceAllowed ? "True" : "False") + "\n" +
            "Result=" + result);
    }

    static void LogHelmetRelease(
        PPEEquipable item,
        Transform target,
        Transform equipReference,
        bool targetValid,
        bool sequenceAllowed,
        float distance,
        float maxDistance,
        string result)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var equipPosition = equipReference != null
            ? equipReference.position
            : new Vector3(float.NaN, float.NaN, float.NaN);
        var targetPosition = target != null
            ? target.position
            : new Vector3(float.NaN, float.NaN, float.NaN);
        var diagnosticResult = result == "NO_VALID_TARGET_POSE" ? "TARGET_INVALID" : result;
        Debug.Log(
            "[PPE Helmet Release]\n\n" +
            "Item = " + ItemLabel(item) + "\n" +
            "TargetValid = " + (targetValid ? "true" : "false") + "\n" +
            "SequenceAllowed = " + (sequenceAllowed ? "true" : "false") + "\n\n" +
            "EquipReference = " + equipPosition.ToString("F4") + "\n" +
            "HeadTarget = " + targetPosition.ToString("F4") + "\n\n" +
            "Distance = " + (float.IsInfinity(distance) ? "inf" : distance.ToString("0.000")) + "\n" +
            "MaxDistance = " + maxDistance.ToString("0.000") + "\n\n" +
            "Result = " + diagnosticResult,
            item);
#endif
    }

    static PPEPlacementGuideManager FindPlacement()
    {
        return Object.FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
    }

    static PPEPlayerBodyReference FindBody()
    {
        return Object.FindAnyObjectByType<PPEPlayerBodyReference>(FindObjectsInactive.Exclude);
    }
}

public static class PPEGloveReleaseDiagnostics
{
    const float VisualPositionTolerance = 0.08f;
    const float VisualRotationTolerance = 30f;
    const float MarkerSize = 0.035f;
    const float MarkerDuration = 5f;

    public static void Log(
        PPEGlovePairMember member,
        PPEGrabHand holdingHand,
        Vector3 targetPalmPosition,
        Transform equipReference,
        float distance,
        float maxDistance,
        bool targetValid,
        string result)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var ghostVisualAligned = IsGhostVisualAligned(member, targetPalmPosition);
        var equipPosition = equipReference != null
            ? equipReference.position
            : new Vector3(float.NaN, float.NaN, float.NaN);
        Debug.Log(
            "[PPE Glove Release Alignment]\n\n" +
            "Member = " + MemberLabel(member) + "\n" +
            "HoldingHand = " + holdingHand + "\n\n" +
            "TargetPalmPosition = " + targetPalmPosition.ToString("F4") + "\n" +
            "EquipReferencePosition = " + equipPosition.ToString("F4") + "\n\n" +
            "Distance = " + (float.IsInfinity(distance) ? "inf" : distance.ToString("0.000")) + "\n" +
            "MaxDistance = " + maxDistance.ToString("0.000") + "\n\n" +
            "GhostVisualAligned = " + (ghostVisualAligned ? "true" : "false") + "\n" +
            "TargetValid = " + (targetValid ? "true" : "false") + "\n\n" +
            "Result = " + result,
            member);

        DrawMarker(targetPalmPosition, Color.cyan);
        if (equipReference != null)
        {
            DrawMarker(equipPosition, new Color(1f, 0.8f, 0.1f, 1f));
            Debug.DrawLine(
                targetPalmPosition,
                equipPosition,
                distance <= maxDistance ? Color.green : Color.red,
                MarkerDuration,
                false);
        }
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static bool IsGhostVisualAligned(PPEGlovePairMember member, Vector3 targetPalmPosition)
    {
        if (member == null || member.VisualRoot == null || member.TargetTransform == null)
            return false;
        var calibration = member.GhostPoseCalibrationOrNull;
        if (calibration == null)
            return false;
        var targetRotation = member.TargetTransform.rotation;
        var expectedPosition = targetPalmPosition +
                               targetRotation *
                               (calibration.localPosition + member.GhostVisualOriginOffset);
        var expectedRotation = targetRotation * calibration.localRotation;
        return Vector3.Distance(member.VisualRoot.position, expectedPosition) <= VisualPositionTolerance &&
               Quaternion.Angle(member.VisualRoot.rotation, expectedRotation) <= VisualRotationTolerance;
    }

    static void DrawMarker(Vector3 position, Color color)
    {
        Debug.DrawLine(position - Vector3.right * MarkerSize, position + Vector3.right * MarkerSize, color, MarkerDuration, false);
        Debug.DrawLine(position - Vector3.up * MarkerSize, position + Vector3.up * MarkerSize, color, MarkerDuration, false);
        Debug.DrawLine(position - Vector3.forward * MarkerSize, position + Vector3.forward * MarkerSize, color, MarkerDuration, false);
    }

    static string MemberLabel(PPEGlovePairMember member)
    {
        if (member == null)
            return "(none)";
        return member.name.EndsWith("Physical", System.StringComparison.Ordinal)
            ? member.name.Substring(0, member.name.Length - "Physical".Length)
            : member.name;
    }
#endif
}
