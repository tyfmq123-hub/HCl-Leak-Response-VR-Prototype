using UnityEngine;

/// <summary>
/// Disabled. Previously drove gaze-based bag contents presentation.
/// Kept so scene references do not break.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagCameraGazeDetector : MonoBehaviour
{
    [SerializeField] Camera xrCamera;
    [SerializeField] LayerMask gazeLayerMask = ~0;
    [SerializeField] float maxDistance = 3f;

    void OnEnable()
    {
        // Keep disabled — gaze presentation caused unstable bag/content behavior.
        enabled = false;
    }

    public void EditorWire(Camera camera, LayerMask mask, float distance)
    {
        xrCamera = camera;
        gazeLayerMask = mask;
        maxDistance = distance;
    }
}
