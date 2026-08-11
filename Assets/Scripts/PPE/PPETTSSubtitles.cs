/// <summary>
/// Korean subtitle copy for each <see cref="PPETTSCue"/>.
/// Kept in sync with Assets/Audio/PPE/TTS English clip order.
/// </summary>
public static class PPETTSSubtitles
{
    public static string Get(PPETTSCue cue)
    {
        switch (cue)
        {
            case PPETTSCue.EmergencyIntro:
                return "긴급 상황입니다. 화학물질 누출 대응을 준비하세요.";
            case PPETTSCue.WearEssentialPPE:
                return "필수 개인보호구(PPE)를 착용하세요.";
            case PPETTSCue.WearInOrder:
                return "정해진 순서대로 착용하세요.";
            case PPETTSCue.PointWithRay:
                return "컨트롤러 레이로 장비를 가리키세요.";
            case PPETTSCue.OutlineShowsSelectable:
                return "외곽선이 보이는 장비를 선택할 수 있습니다.";
            case PPETTSCue.GripSelectThenEquip:
                return "그랩으로 보호구를 선택한 뒤, 그랩 버튼으로 착용하세요.";
            case PPETTSCue.BButtonCancel:
                return "B 버튼으로 선택을 취소할 수 있습니다.";
            case PPETTSCue.WearInnerGloves:
                return "속장갑을 착용하세요.";
            case PPETTSCue.InnerGlovesEquipped:
                return "속장갑을 착용했습니다.";
            case PPETTSCue.WearChemicalSuit:
                return "내산성 화학보호복을 착용하세요.";
            case PPETTSCue.ChemicalSuitEquipped:
                return "내산성 화학보호복을 착용했습니다.";
            case PPETTSCue.WearChemicalBoots:
                return "내화학 안전장화를 착용하세요.";
            case PPETTSCue.ChemicalBootsEquipped:
                return "내화학 안전장화를 착용했습니다.";
            case PPETTSCue.WearRespirator:
                return "전면형 호흡보호구를 착용하세요.";
            case PPETTSCue.RespiratorEquipped:
                return "전면형 호흡보호구를 착용했습니다.";
            case PPETTSCue.WearFaceProtection:
                return "안면보호구를 착용하세요.";
            case PPETTSCue.FaceProtectionEquipped:
                return "안면보호구를 착용했습니다.";
            case PPETTSCue.WearOuterGloves:
                return "외부 보호장갑을 착용하세요.";
            case PPETTSCue.OuterGlovesEquipped:
                return "외부 보호장갑을 착용했습니다.";
            case PPETTSCue.EssentialPPEComplete:
                return "필수 PPE 착용이 완료되었습니다.";
            case PPETTSCue.PrepareResponseBag:
                return "대응가방을 준비하세요. Y로 내용물을 확인하세요.";
            case PPETTSCue.ResponseBagReady:
                return "대응가방 준비가 완료되었습니다.";
            case PPETTSCue.PrepareWasteContainer:
                return "유해폐기물 수거용기를 준비하세요.";
            case PPETTSCue.WasteContainerReady:
                return "유해폐기물 수거용기 준비가 완료되었습니다.";
            case PPETTSCue.CheckingPrepStatus:
                return "현장 준비 상태를 확인합니다.";
            case PPETTSCue.AllFieldPrepComplete:
                return "모든 현장 준비가 완료되었습니다.";
            case PPETTSCue.GoToFieldEntrance:
                return "현장 입구로 이동하세요.";
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
            default:
                return string.Empty;
        }
    }
}
