using UnityEngine;

/// <summary>
/// Shared release-to-equip trigger for wearable PPE. InnerGloves keep their
/// existing donning path. Other wearables use the same distance-on-SelectExited
/// rule against the Ghost's live target.
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
               string.Equals(id, PPEEquipmentManager.IdFaceShield, System.StringComparison.Ordinal);
    }

    public static void TryEquipOnRelease(PPEHandGrabbable grabbable, PPEGrabHand capturedHolding)
    {
        if (grabbable == null)
            return;

        var member = grabbable.GetComponent<PPEGlovePairMember>();
        if (member != null && member.IsInnerGlove)
            return;

        if (member != null)
        {
            TryEquipPairMember(member, capturedHolding);
            return;
        }

        var item = grabbable.GetComponent<PPEEquipable>();
        if (!IsWearableReleaseItem(item))
            return;

        TryEquipSingle(grabbable, item);
    }

    static void TryEquipPairMember(PPEGlovePairMember member, PPEGrabHand capturedHolding)
    {
        var coordinator = member != null ? member.Coordinator : null;
        if (member == null || coordinator == null)
            return;

        var target = member.TargetTransform;
        var follower = target != null ? target.GetComponent<PPEGloveHandTargetFollower>() : null;
        follower?.RefreshForRelease();

        var targetValid = member.IsTargetTracking && target != null;
        var equipRef = member.EnsureEquipReference();
        var maxDistance = member.PlacementDistance;
        var sequenceAllowed = IsSequenceAllowed(member.LogicalEquipable);
        var correctHolding = member.IsOppositeHandGrab(capturedHolding);
        var distance = equipRef != null && targetValid
            ? Vector3.Distance(equipRef.position, target.position)
            : float.PositiveInfinity;

        string result;
        if (!correctHolding)
            result = "WRONG_HOLDING_HAND";
        else if (!targetValid)
            result = "NO_VALID_TARGET_POSE";
        else if (!sequenceAllowed)
            result = "SEQUENCE_BLOCKED";
        else if (distance > maxDistance)
            result = "TOO_FAR";
        else
            result = member.GloveSide == PPEGrabHand.Right ? "EQUIP_RIGHT" : "EQUIP_LEFT";

        LogRelease(
            ItemLabel(member),
            TargetLabel(member),
            targetValid,
            distance,
            maxDistance,
            sequenceAllowed,
            result);

        if (result != "EQUIP_LEFT" && result != "EQUIP_RIGHT")
            return;

        coordinator.NotifyPairSideFromRelease(member);
    }

    static void TryEquipSingle(PPEHandGrabbable grabbable, PPEEquipable item)
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
        var distance = equipRef != null && targetValid
            ? Vector3.Distance(equipRef.position, target.position)
            : float.PositiveInfinity;

        string result;
        if (!targetValid)
            result = "NO_VALID_TARGET_POSE";
        else if (!sequenceAllowed)
            result = "SEQUENCE_BLOCKED";
        else if (distance > maxDistance)
            result = "TOO_FAR";
        else
            result = "EQUIPPED";

        LogRelease(
            ItemLabel(item),
            targetLabel,
            targetValid,
            distance,
            maxDistance,
            sequenceAllowed,
            result);

        if (result != "EQUIPPED")
            return;

        if (placement != null)
            placement.EquipAfterRelease(item);
        else
            PPEEquipmentManager.Instance?.Equip(item);
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
        return item.ItemId;
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

    static PPEPlacementGuideManager FindPlacement()
    {
        return Object.FindAnyObjectByType<PPEPlacementGuideManager>(FindObjectsInactive.Include);
    }

    static PPEPlayerBodyReference FindBody()
    {
        return Object.FindAnyObjectByType<PPEPlayerBodyReference>(FindObjectsInactive.Exclude);
    }
}
