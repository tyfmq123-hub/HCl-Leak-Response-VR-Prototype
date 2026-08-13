using TMPro;
using UnityEngine;

/// <summary>
/// Cinematic subtitle-style Korean name for response-bag content hover.
/// Fixed lower-center of the XR view — not a floating world label above the item.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagItemNameLabel : MonoBehaviour
{
    [SerializeField] Canvas canvas;
    [SerializeField] RectTransform panelRect;
    [SerializeField] TextMeshProUGUI labelText;
    [SerializeField] Camera xrCamera;

    [Header("Subtitle placement (camera local)")]
    [SerializeField] float distanceFromCamera = 0.9f;
    [SerializeField] float verticalOffset = -0.08f;
    [SerializeField] Vector2 canvasSize = new Vector2(520f, 72f);
    [SerializeField] float worldScale = 0.00115f;

    [Header("Hide")]
    [SerializeField] [Min(0f)] float hideDelayAfterHoverExit = 0.25f;

    float hideAtUnscaled = -1f;
    bool visible;

    public bool IsVisible => visible && gameObject.activeInHierarchy;

    void Awake()
    {
        if (canvas == null)
            canvas = GetComponent<Canvas>();
        ResolveCamera();
        HideImmediate();
    }

    void LateUpdate()
    {
        ResolveCamera();

        if (hideAtUnscaled > 0f && Time.unscaledTime >= hideAtUnscaled)
        {
            hideAtUnscaled = -1f;
            HideImmediate();
            return;
        }

        if (!visible && hideAtUnscaled < 0f)
            return;

        // Keep following camera during delay fade window.
        UpdateSubtitlePose();
    }

    public void EditorWire(Canvas canvasRoot, RectTransform panel, TextMeshProUGUI text, Camera camera)
    {
        canvas = canvasRoot;
        panelRect = panel;
        labelText = text;
        xrCamera = camera;
    }

    /// <summary>Subtitle show — bounds/anchor ignored; pose follows the XR camera.</summary>
    public void Show(string koreanName, Transform anchor, Bounds worldBounds)
    {
        ShowSubtitle(koreanName);
    }

    public void ShowSubtitle(string koreanName)
    {
        hideAtUnscaled = -1f;

        if (labelText != null)
            labelText.text = string.IsNullOrEmpty(koreanName) ? string.Empty : koreanName;

        visible = true;
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        ResolveCamera();
        UpdateSubtitlePose();
    }

    public void Hide()
    {
        if (hideDelayAfterHoverExit <= 0f)
        {
            HideImmediate();
            return;
        }

        // Keep last subtitle briefly for cinematic feel.
        hideAtUnscaled = Time.unscaledTime + hideDelayAfterHoverExit;
        visible = false;
    }

    public void HideImmediate()
    {
        visible = false;
        hideAtUnscaled = -1f;

        if (labelText != null)
            labelText.text = string.Empty;

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    void UpdateSubtitlePose()
    {
        if (xrCamera == null)
            return;

        var cam = xrCamera.transform;
        var pos = cam.position
                  + cam.forward * distanceFromCamera
                  + cam.up * verticalOffset;

        transform.position = pos;
        // Face the camera (readable), upright relative to camera.
        transform.rotation = Quaternion.LookRotation(transform.position - cam.position, cam.up);

        if (panelRect != null)
            panelRect.sizeDelta = canvasSize;

        var s = worldScale;
        transform.localScale = new Vector3(s, s, s);
    }

    void ResolveCamera()
    {
        if (xrCamera != null)
            return;

        var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin != null && origin.Camera != null)
        {
            xrCamera = origin.Camera;
            return;
        }

        xrCamera = Camera.main;
    }
}
