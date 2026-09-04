/// <summary>
/// Korean subtitle copy for each <see cref="PPETTSCue"/>.
/// Kept in sync with Assets/1.JSR/Audio/PPE/TTS English clip order.
/// </summary>
public static class PPETTSSubtitles
{
    public static string Get(PPETTSCue cue)
    {
        switch (cue)
        {
            case PPETTSCue.EmergencyIntro:
                return "염산 이송 설비에서 누출이 감지되었습니다. 현장에 바로 접근하지 말고 보호구와 대응 장비를 준비하십시오.";
            case PPETTSCue.WearEssentialPPE:
                return "화이트보드에서 누출 위치와 준비 순서를 충분히 확인하십시오.";
            case PPETTSCue.WearInOrder:
                return "착용 장비는 오른쪽 장비 보관대에 있습니다. 보관대를 보며 안내되는 순서대로 착용하고 대응 장비를 준비하십시오.";
            case PPETTSCue.PointWithRay:
                return "손을 장비 가까이 가져가 직접 선택하십시오.";
            case PPETTSCue.OutlineShowsSelectable:
                return "외곽선이 보이는 장비를 선택할 수 있습니다.";
            case PPETTSCue.LegacyGripButtonEquipInstruction:
                return "장비를 손으로 잡아 지정된 착용 위치에서 놓으십시오.";
            case PPETTSCue.BButtonCancel:
                return "잘못 놓친 장비는 원래 위치로 돌아갑니다.";
            case PPETTSCue.WearInnerGloves:
                return "속장갑을 Grip으로 선택하고, 다시 Grip을 눌러 착용하십시오.";
            case PPETTSCue.InnerGlovesEquipped:
                return "속장갑을 착용했습니다.";
            case PPETTSCue.WearChemicalSuit:
                return "내산성 화학보호복을 Grip으로 선택하고, 다시 Grip을 눌러 착용하십시오.";
            case PPETTSCue.ChemicalSuitEquipped:
                return "내산성 화학보호복을 착용했습니다.";
            case PPETTSCue.WearChemicalBoots:
                return "내화학 안전장화를 Grip으로 선택하고, 다시 Grip을 눌러 착용하십시오.";
            case PPETTSCue.ChemicalBootsEquipped:
                return "내화학 안전장화를 착용했습니다.";
            case PPETTSCue.WearRespirator:
                return "전면형 호흡보호구를 Grip으로 선택하고, 다시 Grip을 눌러 착용하십시오.";
            case PPETTSCue.RespiratorEquipped:
                return "전면형 호흡 보호구 착용을 확인했습니다.";
            case PPETTSCue.WearFaceProtection:
                return "안면보호구를 Grip으로 선택하고, 다시 Grip을 눌러 착용하십시오.";
            case PPETTSCue.FaceProtectionEquipped:
                return "안면보호구를 착용했습니다.";
            case PPETTSCue.WearOuterGloves:
                return "외부 보호장갑을 Grip으로 선택하고, 다시 Grip을 눌러 착용하십시오.";
            case PPETTSCue.OuterGlovesEquipped:
                return "외부 보호장갑을 착용했습니다.";
            case PPETTSCue.EssentialPPEComplete:
                return "PPE 착용이 완료되었습니다. 이제 대응 장비를 준비하세요.";
            case PPETTSCue.PrepareResponseBag:
                return "대응가방을 Grip으로 선택하고, 왼손 Y로 내용물을 확인한 뒤 다시 Grip을 눌러 준비하십시오.";
            case PPETTSCue.ResponseBagReady:
                return "대응가방 준비가 완료되었습니다.";
            case PPETTSCue.PrepareWasteContainer:
                return "수거용기를 바라보고 선택한 뒤 Grip을 눌러 준비하십시오.";
            case PPETTSCue.WasteContainerReady:
                return "유해폐기물 수거용기 준비가 완료되었습니다.";
            case PPETTSCue.CheckingPrepStatus:
                return "현장 준비 상태를 확인합니다.";
            case PPETTSCue.AllFieldPrepComplete:
                return "현장 진입에 필요한 개인보호구와 대응 장비 준비가 완료되었습니다.";
            case PPETTSCue.GoToFieldEntrance:
                return "오른손 A를 눌러 현장 진입 전 안전 확인 단계로 이동하십시오.";
            case PPETTSCue.MovingToFieldEntrance:
                return "현장 입구로 이동합니다.";
            case PPETTSCue.WrongPPEOrder:
                return "착용 순서가 올바르지 않습니다.";
            case PPETTSCue.WearGlovesFirst:
                return "먼저 속장갑을 착용하세요.";
            case PPETTSCue.OuterGlovesAfterRespirator:
                return "외부 장갑은 호흡보호구 이후에 착용하세요.";
            case PPETTSCue.AlreadyEquipped:
                return "이미 착용한 장비입니다.";
            case PPETTSCue.CompleteEssentialPPEFirst:
                return "먼저 필수 PPE를 완료하세요.";
            case PPETTSCue.MissingPPE:
                return "필요한 PPE가 누락되었습니다.";
            case PPETTSCue.PrepareResponseBagFirst:
                return "먼저 대응가방 내용물을 확인하세요.";
            case PPETTSCue.CheckCurrentRequiredPPE:
                return "현재 필요한 PPE를 확인하세요.";
            case PPETTSCue.GripPackedResponseBag:
                return "장비 수납이 완료되었습니다. 가방을 Grip해 챙기십시오.";
            default:
                return string.Empty;
        }
    }
}
