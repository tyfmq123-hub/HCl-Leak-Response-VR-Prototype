using System;
using UnityEngine;

/// <summary>
/// Per-item pose applied only to response-bag UI preview clones
/// (never to source FBX / scene equipment).
/// </summary>
[Serializable]
public class ResponseBagItemPreviewPose
{
    public enum FitPrimary
    {
        MaxExtent = 0,
        Height = 1,
        Width = 2,
    }

    [Tooltip("Matches ResponseEquipmentManager item ids (GasDetector, AbsorbentSock, AbsorbentPad).")]
    public string itemId = "";

    [Tooltip("Local Euler rotation applied to the UI preview clone.")]
    public Vector3 previewLocalEuler = new Vector3(15f, 180f, 0f);

    [Tooltip("Applied after renderer-bounds centering, in meters along the card axes (X=right, Y=up, Z=forward/into card). Negative Z pulls toward the viewer.")]
    public Vector3 previewPositionOffset = Vector3.zero;

    [Tooltip("Final uniform scale multiplier after bounds fit.")]
    [Min(0.05f)]
    public float previewScaleMultiplier = 1f;

    [Tooltip("1.10–1.15 ≈ 10–15% padding around the fitted model.")]
    [Min(1f)]
    public float fitPadding = 1.12f;

    [Tooltip("Reserved for RenderTexture previews; GearPanel uses in-card 3D clones.")]
    public bool useOrthographicCamera = true;

    public FitPrimary fitPrimary = FitPrimary.MaxExtent;

    [Tooltip("Slow yaw spin. Off by default.")]
    public bool autoRotation = false;

    [Min(0f)]
    public float autoRotationSpeed = 20f;
}
