using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Captures the XR interactor that last pressed a UI element (for spawn-into-hand).
/// Attach to the same GameObject as the UI Button, or rely on EventSystem bubbling.
/// </summary>
[DisallowMultipleComponent]
public class XRUISelectHandCapture : MonoBehaviour, IPointerDownHandler, ISubmitHandler
{
    public static IXRSelectInteractor LastSelectInteractor { get; private set; }
    public static float LastSelectTime { get; private set; }

    public void OnPointerDown(PointerEventData eventData) => Capture(eventData);

    public void OnSubmit(BaseEventData eventData)
    {
        if (eventData is PointerEventData pointer)
            Capture(pointer);
    }

    static void Capture(PointerEventData eventData)
    {
        if (eventData is TrackedDeviceEventData tracked &&
            tracked.interactor is IXRSelectInteractor selectInteractor)
        {
            LastSelectInteractor = selectInteractor;
            LastSelectTime = Time.unscaledTime;
            return;
        }

        // Mouse / editor fallback: pick a free XRRayInteractor if any.
        var rays = Object.FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Exclude);
        for (var i = 0; i < rays.Length; i++)
        {
            var ray = rays[i];
            if (ray == null || !ray.isActiveAndEnabled)
                continue;
            if (ray is IXRSelectInteractor selectable && !selectable.hasSelection)
            {
                LastSelectInteractor = selectable;
                LastSelectTime = Time.unscaledTime;
                return;
            }
        }
    }

    public static void Clear()
    {
        LastSelectInteractor = null;
        LastSelectTime = 0f;
    }
}
