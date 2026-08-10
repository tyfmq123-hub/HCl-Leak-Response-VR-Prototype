using UnityEngine;

/// <summary>
/// Editable slot layout for Tools/PPE/Arrange Preparation Room Equipment.
/// </summary>
public class PPEPreparationRoomLayoutSettings : ScriptableObject
{
    [Tooltip("Per-object slot placement. Edit yawCorrectionDegrees if the model front faces the wrong way.")]
    public PPEPreparationRoomLayoutTool.SlotLayout[] slots;
}
