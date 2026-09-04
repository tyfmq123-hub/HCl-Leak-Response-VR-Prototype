using System.Text;
using UnityEngine;

/// <summary>Single-entry InnerGlove grab and placement Console logs.</summary>
public static class PPEInnerGloveRuntimeLog
{
    public static void LogGrab(
        PPEHandGrabbable grabbable,
        PPEGlovePairMember member,
        GameObject guideRoot,
        bool wrongHand)
    {
        var resolved = grabbable != null ? grabbable.LastHandResolution : default;
        var sequence = PPESequenceManager.Resolve();
        var required = sequence != null && sequence.CurrentRequiredStep != null
            ? sequence.CurrentRequiredStep.Value.ToString()
            : "(none)";
        var logical = member != null ? member.LogicalEquipable : null;
        var holding = grabbable != null ? grabbable.CurrentHand : PPEGrabHand.Unknown;
        var side = member != null ? member.GloveSide : PPEGrabHand.Unknown;
        var tracking = member != null && member.IsTargetTracking;
        var sequenceAllowed = logical != null &&
                              (sequence == null || sequence.CanEquip(logical.ItemId));

        var log = new StringBuilder();
        log.AppendLine("[PPE InnerGlove Grab]");
        log.AppendLine();
        log.AppendLine("PhysicalMember: " + (member != null ? member.name : "(null)"));
        log.AppendLine("MemberSide: " + side);
        log.AppendLine();
        log.AppendLine("Interactor:");
        log.AppendLine(string.IsNullOrEmpty(resolved.InteractorName) ? "(null)" : resolved.InteractorName);
        log.AppendLine();
        log.AppendLine("InteractorPath:");
        log.AppendLine(string.IsNullOrEmpty(resolved.InteractorPath) ? "(null)" : resolved.InteractorPath);
        log.AppendLine();
        log.AppendLine("InteractorType:");
        log.AppendLine(string.IsNullOrEmpty(resolved.InteractorType) ? "(null)" : resolved.InteractorType);
        log.AppendLine();
        log.AppendLine("InteractorHandedness:");
        log.AppendLine(string.IsNullOrEmpty(resolved.InteractorHandedness) ? "None" : resolved.InteractorHandedness);
        log.AppendLine();
        log.AppendLine("ResolvedHoldingHand:");
        log.AppendLine(holding.ToString());
        log.AppendLine();
        log.AppendLine("HandResolutionSource:");
        log.AppendLine(string.IsNullOrEmpty(resolved.SourceLabel) ? "fallback" : resolved.SourceLabel);
        log.AppendLine();
        log.AppendLine("TargetHand:");
        log.AppendLine(side.ToString());
        log.AppendLine();
        log.AppendLine("WrongHand:");
        log.AppendLine(wrongHand ? "True" : "False");
        log.AppendLine();
        log.AppendLine("TargetTracking:");
        log.AppendLine(tracking ? "True" : "False");
        log.AppendLine();
        log.AppendLine("LogicalItem:");
        log.AppendLine(logical != null ? logical.ItemId : "(null)");
        log.AppendLine();
        log.AppendLine("CurrentSequenceStep:");
        log.AppendLine(required);
        log.AppendLine();
        log.AppendLine("SequenceAllowed:");
        log.AppendLine(sequenceAllowed ? "True" : "False");
        log.AppendLine();
        log.AppendLine("GuideCreated:");
        log.AppendLine(guideRoot != null ? "True" : "False");
        log.AppendLine();
        log.AppendLine("GuideActive:");
        log.AppendLine(guideRoot != null && guideRoot.activeInHierarchy ? "True" : "False");

        Debug.Log(log.ToString(), member != null ? member : grabbable);
    }

    public static void LogPlacement(
        PPEGlovePairMember member,
        bool tracking,
        float distance,
        float enterRadius,
        bool insideZone,
        bool sequenceAllowed,
        float dwell,
        float holdDuration,
        string state,
        string firstFalse)
    {
        var side = member != null ? member.GloveSide : PPEGrabHand.Unknown;
        var dist = float.IsInfinity(distance) ? "inf" : distance.ToString("0.000");
        var falseLabel = string.IsNullOrEmpty(firstFalse) || firstFalse == "IDLE" || firstFalse == "READY"
            ? "None"
            : firstFalse;
        Debug.Log(
            "[PPE InnerGlove Placement] " + side +
            " | Tracking=" + (tracking ? "True" : "False") +
            " | Dist=" + dist + "/" + enterRadius.ToString("0.000") +
            " | Inside=" + (insideZone ? "True" : "False") +
            " | Sequence=" + (sequenceAllowed ? "True" : "False") +
            " | State=" + state +
            " | Dwell=" + dwell.ToString("0.00") + "/" + holdDuration.ToString("0.00") +
            " | FirstFalse=" + falseLabel,
            member);
    }

    public static void LogRelease(
        PPEGlovePairMember member,
        PPEGrabHand holding,
        bool targetValid,
        float distance,
        float maxDistance,
        bool correctHoldingHand,
        bool sequenceAllowed,
        string result)
    {
        var side = member != null ? member.GloveSide : PPEGrabHand.Unknown;
        var dist = float.IsInfinity(distance) ? "inf" : distance.ToString("0.000");
        Debug.Log(
            "[PPE InnerGlove Release]\n" +
            "\n" +
            "Member: " + side + "\n" +
            "HoldingHand: " + holding + "\n" +
            "Target: " + side + "\n" +
            "\n" +
            "TargetValid: " + (targetValid ? "True" : "False") + "\n" +
            "Distance: " + dist + "\n" +
            "MaxDistance: " + maxDistance.ToString("0.000") + "\n" +
            "\n" +
            "CorrectHoldingHand: " + (correctHoldingHand ? "True" : "False") + "\n" +
            "SequenceAllowed: " + (sequenceAllowed ? "True" : "False") + "\n" +
            "\n" +
            "Result: " + result,
            member);
    }
}
