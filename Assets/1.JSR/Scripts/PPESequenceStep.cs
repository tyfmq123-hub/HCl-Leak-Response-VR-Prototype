/// <summary>
/// Ordered PPE preparation steps. Map to existing <see cref="PPEEquipmentManager"/> item IDs
/// via <see cref="PPESequenceManager"/>.
/// </summary>
public enum PPESequenceStep
{
    InnerGloves = 0,
    ProtectiveSuit = 1,
    ChemicalBoots = 2,
    Respirator = 3,
    ResponseBag = 4,
    FaceProtection = 5,
    ProtectiveGloves = 6,
    WasteContainer = 7,
    // Optional reference-validation step. It is deliberately not part of the
    // built-in production order; add it to Sequence Order in the Inspector.
    Helmet = 8,
}
