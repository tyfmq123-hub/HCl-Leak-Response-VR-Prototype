using UnityEngine;

/// <summary>
/// Keeps a world-space item name plate facing the XR camera.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagWorldItemNameBillboard : MonoBehaviour
{
    Camera xrCamera;

    void LateUpdate()
    {
        if (xrCamera == null)
        {
            var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null && origin.Camera != null)
                xrCamera = origin.Camera;
            else
                xrCamera = Camera.main;
        }

        if (xrCamera == null)
            return;

        // Face the camera so the canvas front is readable (+Z toward viewer).
        var toCam = xrCamera.transform.position - transform.position;
        if (toCam.sqrMagnitude < 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
    }
}
