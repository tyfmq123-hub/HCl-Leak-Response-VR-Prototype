using UnityEngine;

/// <summary>
/// Short contextual equip hints on grab. Reuses PPEEquipFeedbackUI; no new HUD.
/// Session-once first-use line, then per-item reminders. No TTS / sequence changes.
/// </summary>
public static class PPEEquipGuidanceHints
{
    const float FirstUseDuration = 3.2f;
    const float ItemDuration = 2.4f;

    const string FirstUseHint =
        "PPE를 집은 뒤, 반투명 착용 위치로 가져가 놓으면 착용됩니다.";

    static bool firstUseShown;
    static PPEEquipFeedbackUI cachedUi;

    public static void NotifyGrabbed(
        PPEEquipable item,
        PPEGlovePairMember gloveMember,
        PPEGrabHand holdingHand = PPEGrabHand.Unknown)
    {
        if (item == null)
            return;

        var isFaceOrSuit = item.UsesSlot(PPEEquipSlot.Face) ||
                           item.UsesSlot(PPEEquipSlot.Head) ||
                           item.UsesSlot(PPEEquipSlot.Body);
        var ui = ResolveUi(activateIfNeeded: gloveMember != null || isFaceOrSuit);
        if (ui == null || !ui.isActiveAndEnabled)
            return;

        if (gloveMember != null && gloveMember.IsSameHandGrab(holdingHand))
        {
            ui.ShowMessage(WrongHandMessage(gloveMember), ItemDuration);
            return;
        }

        if (isFaceOrSuit)
        {
            ui.ShowMessage(ResolveItemHint(item, null), ItemDuration);
            return;
        }

        if (gloveMember != null && gloveMember.IsInnerGlove)
        {
            ui.ShowMessage(ResolveItemHint(item, gloveMember), ItemDuration);
            return;
        }

        if (!firstUseShown)
        {
            firstUseShown = true;
            ui.ShowMessage(FirstUseHint, FirstUseDuration);
            return;
        }

        var hint = ResolveItemHint(item, gloveMember);
        if (string.IsNullOrEmpty(hint))
            return;

        ui.ShowMessage(hint, ItemDuration);
    }

    static string ResolveItemHint(PPEEquipable item, PPEGlovePairMember gloveMember)
    {
        if (gloveMember != null)
        {
            var id = item != null ? item.ItemId : string.Empty;
            var left = gloveMember.GloveSide == PPEGrabHand.Left;
            if (string.Equals(id, PPEEquipmentManager.IdChemicalBoots, System.StringComparison.Ordinal) ||
                gloveMember.TargetKind == PPEPairTargetKind.BodyLowerLeg)
            {
                return "장화를 착용 위치에 가져간 뒤 놓으세요.";
            }

            if (string.Equals(id, PPEEquipmentManager.IdInnerGloves, System.StringComparison.Ordinal))
            {
                return left
                    ? "왼손에 가져간 뒤 놓으면 착용됩니다."
                    : "오른손에 가져간 뒤 놓으면 착용됩니다.";
            }

            return "반대쪽 손에 가져간 뒤 놓으면 착용됩니다.";
        }

        var itemId = item.ItemId ?? string.Empty;
        if (string.Equals(itemId, PPEEquipmentManager.IdInnerGloves, System.StringComparison.Ordinal))
            return "손에 가져간 뒤 놓으면 착용됩니다.";
        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalGloves, System.StringComparison.Ordinal))
            return "반대쪽 손에 가져간 뒤 놓으면 착용됩니다.";
        if (string.Equals(itemId, PPEEquipmentManager.IdFaceRespirator, System.StringComparison.Ordinal))
            return "호흡보호구를 얼굴 앞으로 가져간 뒤 놓으세요.";
        if (string.Equals(itemId, PPEEquipmentManager.IdFaceShield, System.StringComparison.Ordinal))
            return "안면보호구를 얼굴 앞으로 가져간 뒤 놓으세요.";
        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalBoots, System.StringComparison.Ordinal))
            return "장화를 착용 위치에 가져간 뒤 놓으세요.";
        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalSuit, System.StringComparison.Ordinal))
            return "보호복을 몸 쪽으로 가져간 뒤 놓으세요.";
        if (string.Equals(itemId, PPEEquipmentManager.IdResponseBag, System.StringComparison.Ordinal) ||
            string.Equals(itemId, PPEEquipmentManager.IdWasteContainer, System.StringComparison.Ordinal))
            return string.Empty;
        if (string.Equals(itemId, PPEEquipmentManager.IdHelmet, System.StringComparison.Ordinal))
            return "안전모를 머리 위로 가져간 뒤 놓으세요.";
        return string.Empty;
    }

    static string WrongHandMessage(PPEGlovePairMember gloveMember)
    {
        if (gloveMember != null && gloveMember.IsInnerGlove)
        {
            return gloveMember.GloveSide == PPEGrabHand.Left
                ? "왼손 장갑은 오른손으로 잡아주세요."
                : "오른손 장갑은 왼손으로 잡아주세요.";
        }

        return "반대 손으로 장갑을 잡아주세요.";
    }

    static PPEEquipFeedbackUI ResolveUi(bool activateIfNeeded = false)
    {
        if (cachedUi != null && cachedUi.isActiveAndEnabled)
            return cachedUi;

        cachedUi = Object.FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        if (cachedUi != null && activateIfNeeded && !cachedUi.gameObject.activeSelf)
            cachedUi.gameObject.SetActive(true);
        return cachedUi;
    }
}
