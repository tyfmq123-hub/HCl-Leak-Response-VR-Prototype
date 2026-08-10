using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Shared preview camera + RenderTexture for response bag content inspection.
/// </summary>
[DisallowMultipleComponent]
public class ResponseBagItemPreviewController : MonoBehaviour
{
    public const string PreviewLayerName = "ResponseBagPreview";

    [Header("Stage")]
    [SerializeField] Transform previewObjectRoot;
    [SerializeField] Camera previewCamera;
    [SerializeField] Light previewLight;
    [SerializeField] RawImage destinationImage;

    [Header("Render")]
    [SerializeField] int textureSize = 512;
    [SerializeField] float fitPadding = 1.15f;
    [SerializeField] float rotateDegreesPerSecond = 25f;

    RenderTexture renderTexture;
    GameObject currentInstance;
    int previewLayer = -1;
    bool stageActive;

    public bool IsActive => stageActive;

    void Awake()
    {
        previewLayer = LayerMask.NameToLayer(PreviewLayerName);
        EnsureStageRefs();
        SetStageActive(false);
    }

    void OnDestroy()
    {
        ClearPreview();
        ReleaseRenderTexture();
    }

    void Update()
    {
        if (!stageActive || currentInstance == null)
            return;

        currentInstance.transform.Rotate(0f, rotateDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.World);
    }

    public void EditorWire(
        Transform objectRoot,
        Camera camera,
        Light light,
        RawImage image)
    {
        previewObjectRoot = objectRoot;
        previewCamera = camera;
        previewLight = light;
        destinationImage = image;
    }

    public void ActivateStage()
    {
        EnsureStageRefs();
        EnsureRenderTexture();
        SetStageActive(true);
    }

    public void DeactivateStage()
    {
        ClearPreview();
        SetStageActive(false);
        ReleaseRenderTexture();
    }

    public void ShowPrefab(GameObject prefab)
    {
        EnsureStageRefs();
        if (!stageActive)
            ActivateStage();

        ClearPreview();

        if (prefab == null)
        {
            Debug.LogWarning("[Response Bag Preview] PreviewPrefab is not assigned.", this);
            return;
        }

        currentInstance = Instantiate(prefab, previewObjectRoot);
        currentInstance.name = prefab.name + "_PreviewCopy";
        StripGameplayComponents(currentInstance);
        ApplyPreviewLayerRecursive(currentInstance);
        FitToCamera(currentInstance);
    }

    public void ClearPreview()
    {
        if (currentInstance == null)
            return;

        if (Application.isPlaying)
            Destroy(currentInstance);
        else
            DestroyImmediate(currentInstance);

        currentInstance = null;
    }

    void EnsureStageRefs()
    {
        if (previewObjectRoot == null)
        {
            var root = transform.Find("PreviewObjectRoot");
            if (root != null)
                previewObjectRoot = root;
        }

        if (previewCamera == null)
            previewCamera = GetComponentInChildren<Camera>(true);

        if (previewLight == null)
            previewLight = GetComponentInChildren<Light>(true);
    }

    void EnsureRenderTexture()
    {
        if (renderTexture != null)
            return;

        renderTexture = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.ARGB32)
        {
            name = "ResponseBagPreviewRT",
            antiAliasing = 1,
        };
        renderTexture.Create();

        if (previewCamera != null)
        {
            previewCamera.targetTexture = renderTexture;
            if (previewLayer >= 0)
                previewCamera.cullingMask = 1 << previewLayer;
        }

        if (destinationImage != null)
            destinationImage.texture = renderTexture;
    }

    void ReleaseRenderTexture()
    {
        if (previewCamera != null)
            previewCamera.targetTexture = null;

        if (destinationImage != null)
            destinationImage.texture = null;

        if (renderTexture == null)
            return;

        renderTexture.Release();
        Destroy(renderTexture);
        renderTexture = null;
    }

    void SetStageActive(bool active)
    {
        stageActive = active;

        if (previewCamera != null)
            previewCamera.enabled = active;

        if (previewLight != null)
            previewLight.enabled = active;

        if (previewObjectRoot != null)
            previewObjectRoot.gameObject.SetActive(active);
    }

    static void StripGameplayComponents(GameObject root)
    {
        if (root == null)
            return;

        DestroyAll<AudioSource>(root);
        DestroyAll<ResponseBagGazeTarget>(root);
        DestroyAll<ResponseBagContentHoverItem>(root);
        DestroyAll<ResponseBagPrepareInteractable>(root);
        DestroyAll<ResponseBagInteractable>(root);
        DestroyAll<PPEOutlineHighlight>(root);
        DestroyAll<PPEInteractableOutline>(root);
        DestroyAll<PPEEquipable>(root);
        DestroyAll<PPERayInteractionDebug>(root);
        DestroyAll<XRGrabInteractable>(root);
        DestroyAll<XRSimpleInteractable>(root);
        DestroyAll<XRBaseInteractable>(root);
        DestroyAll<Rigidbody>(root);
        DestroyAll<Collider>(root);
    }

    static void DestroyAll<T>(GameObject root) where T : Component
    {
        var comps = root.GetComponentsInChildren<T>(true);
        for (var i = 0; i < comps.Length; i++)
        {
            if (comps[i] == null)
                continue;
            // Immediate: RequireComponent (e.g. HoverItem → XRSimpleInteractable) fails with deferred Destroy.
            Object.DestroyImmediate(comps[i]);
        }
    }

    void ApplyPreviewLayerRecursive(GameObject root)
    {
        if (previewLayer < 0)
            previewLayer = LayerMask.NameToLayer(PreviewLayerName);
        if (previewLayer < 0)
            return;

        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.layer = previewLayer;
    }

    void FitToCamera(GameObject instance)
    {
        if (instance == null || previewCamera == null || previewObjectRoot == null)
            return;

        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            return;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        var center = bounds.center;
        instance.transform.position -= (center - previewObjectRoot.position);

        bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        var extents = bounds.extents;
        var radius = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));
        if (radius < 0.001f)
            radius = 0.1f;

        var fov = previewCamera.fieldOfView * Mathf.Deg2Rad;
        var distance = (radius * fitPadding) / Mathf.Tan(fov * 0.5f);
        previewCamera.transform.localPosition = new Vector3(0f, 0f, -distance);
        previewCamera.transform.localRotation = Quaternion.identity;
        previewObjectRoot.localPosition = Vector3.zero;
    }
}
