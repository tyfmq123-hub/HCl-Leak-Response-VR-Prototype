using System;

/// <summary>Player-relative destinations available to wearable PPE.</summary>
[Flags]
public enum PPEEquipSlot
{
    None = 0,
    Head = 1 << 0,
    Face = 1 << 1,
    LeftHand = 1 << 2,
    RightHand = 1 << 3,
    Body = 1 << 4,
    LeftFoot = 1 << 5,
    RightFoot = 1 << 6,
    Radio = 1 << 7,
    GasDetector = 1 << 8,
}
