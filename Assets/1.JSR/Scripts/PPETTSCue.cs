/// <summary>
/// PPE room TTS cue ids. Final-course clips are loaded by numeric prefix from
/// Resources/TrainingNarration/PPE; legacy scene-assigned clips remain fallback-only.
/// </summary>
public enum PPETTSCue
{
    EmergencyIntro = 1,
    WearEssentialPPE = 2,
    WearInOrder = 3,
    // Name retained for serialized compatibility; the final narration teaches direct hand selection.
    PointWithRay = 4,
    OutlineShowsSelectable = 5,
    // Numeric slot retained so the existing 36-clip mapping does not shift.
    // This legacy button-equip instruction is no longer enqueued.
    LegacyGripButtonEquipInstruction = 6,
    BButtonCancel = 7,
    WearInnerGloves = 8,
    InnerGlovesEquipped = 9,
    WearChemicalSuit = 10,
    ChemicalSuitEquipped = 11,
    WearChemicalBoots = 12,
    ChemicalBootsEquipped = 13,
    WearRespirator = 14,
    RespiratorEquipped = 15,
    WearFaceProtection = 16,
    FaceProtectionEquipped = 17,
    WearOuterGloves = 18,
    OuterGlovesEquipped = 19,
    EssentialPPEComplete = 20,
    PrepareResponseBag = 21,
    ResponseBagReady = 22,
    PrepareWasteContainer = 23,
    WasteContainerReady = 24,
    CheckingPrepStatus = 25,
    AllFieldPrepComplete = 26,
    GoToFieldEntrance = 27,
    MovingToFieldEntrance = 28,
    WrongPPEOrder = 29,
    WearGlovesFirst = 30,
    OuterGlovesAfterRespirator = 31,
    AlreadyEquipped = 32,
    CompleteEssentialPPEFirst = 33,
    MissingPPE = 34,
    PrepareResponseBagFirst = 35,
    CheckCurrentRequiredPPE = 36,
    GripPackedResponseBag = 37,
}
