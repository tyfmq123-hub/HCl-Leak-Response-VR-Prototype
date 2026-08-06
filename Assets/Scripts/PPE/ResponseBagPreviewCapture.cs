using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if UNITY_RENDER_PIPELINES_UNIVERSAL || USING_URP
using UnityEngine.Rendering.Universal;
#endif

/// <summary>
/// Shared showcase poses (match ResponseBagThumbnailRenderer defaults)
/// and a small runtime studio capture that turns a 3D mesh into a UI Sprite.
/// </summary>
public static class ResponseBagPreviewCapture
{
    public struct Pose
    {
        public Vector3 euler;
        public Vector3 offset;
        public float fill;
    }

    public static readonly Pose Gas = new Pose
    {
        euler = new Vector3(-90f, -90f, 0f),
        offset = new Vector3(0f, 0.02f, 0f),
        fill = 0.78f,
    };

    public static readonly Pose Sock = new Pose
    {
        euler = new Vector3(18f, -20f, 55f),
        offset = Vector3.zero,
        fill = 0.78f,
    };

    public static readonly Pose Pad = new Pose
    {
        euler = new Vector3(78f, 12f, 0f),
        offset = Vector3.zero,
        fill = 0.72f,
    };

    public static Pose ResolvePose(string itemId)
    {
        if (itemId == ResponseEquipmentManager.IdGasDetector)
            return Gas;
        if (itemId == ResponseEquipmentManager.IdAbsorbentSock)
            return Sock;
        if (itemId == ResponseEquipmentManager.IdAbsorbentPad)
            return Pad;
        return Gas;
    }

    /// <summary>
    /// Returns null on failure so callers can keep their PNG fallback.
    /// </summary>
    public static Sprite CaptureSprite(GameObject source, string itemId, int size = 512)
    {
        if (source == null)
            return null;

        var pose = ResolvePose(itemId);
        GameObject clone = null;
        Camera cam = null;
        Light key = null;
        Light fill = null;
        RenderTexture rt = null;
        Texture2D tex = null;

        try
        {
            clone = Object.Instantiate(source);
            clone.name = source.name + "_RuntimeThumb";
            clone.hideFlags = HideFlags.HideAndDontSave;
            StripClone(clone);

            var stagePos = new Vector3(0f, -500f, 0f);
            clone.transform.position = stagePos;
            clone.transform.rotation = Quaternion.Euler(pose.euler.x, pose.euler.y, 0f);
            if (Mathf.Abs(pose.euler.z) > 0.01f)
                clone.transform.Rotate(0f, 0f, pose.euler.z, Space.World);
            clone.transform.localScale = Vector3.one;

            var bounds = CalculateBounds(clone);
            clone.transform.position = stagePos - (bounds.center - stagePos) + pose.offset;
            // Re-center properly: move so bounds center lands on stagePos + offset.
            bounds = CalculateBounds(clone);
            clone.transform.position += stagePos + pose.offset - bounds.center;
            bounds = CalculateBounds(clone);

            var camGo = new GameObject("ResponseBagThumbCam")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.09f, 0f);
            cam.orthographic = true;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.enabled = false;
            cam.cullingMask = ~0;
            cam.stereoTargetEye = StereoTargetEyeMask.None;

#if UNITY_RENDER_PIPELINES_UNIVERSAL || USING_URP
            var add = cam.GetUniversalAdditionalCameraData();
            if (add != null)
            {
                add.renderType = CameraRenderType.Base;
                add.renderPostProcessing = false;
                add.requiresColorOption = CameraOverrideOption.Off;
                add.requiresDepthOption = CameraOverrideOption.Off;
            }
#endif

            var maxDim = Mathf.Max(bounds.size.x, bounds.size.y, 0.001f);
            cam.orthographicSize = (maxDim * 0.5f) / Mathf.Max(0.05f, pose.fill);
            cam.transform.position = bounds.center + new Vector3(0f, 0f, -Mathf.Max(1.5f, maxDim * 2f));
            cam.transform.rotation = Quaternion.identity;

            key = CreateLight(bounds.center, new Vector3(35f, -40f, 0f), 1.4f);
            fill = CreateLight(bounds.center, new Vector3(20f, 50f, 0f), 0.55f);

            rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 1;
            rt.filterMode = FilterMode.Bilinear;
            cam.targetTexture = rt;

            RenderCameraToTexture(cam, rt);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            PunchNearBlackToTransparent(tex);
            tex.Apply(false, false);
            RenderTexture.active = prev;

            if (!HasEnoughContent(tex))
            {
                Object.Destroy(tex);
                tex = null;
                return null;
            }

            return Sprite.Create(
                tex,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[ResponseBagPreviewCapture] " + ex.Message);
            if (tex != null)
                Object.Destroy(tex);
            return null;
        }
        finally
        {
            if (cam != null)
                cam.targetTexture = null;
            if (rt != null)
                RenderTexture.ReleaseTemporary(rt);
            if (cam != null)
                Object.Destroy(cam.gameObject);
            if (key != null)
                Object.Destroy(key.gameObject);
            if (fill != null)
                Object.Destroy(fill.gameObject);
            if (clone != null)
                Object.Destroy(clone);
        }
    }

    static void RenderCameraToTexture(Camera cam, RenderTexture rt)
    {
        // URP does not reliably draw with Camera.Render() — use SubmitRenderRequest.
        var request = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, request))
        {
            cam.SubmitRenderRequest(request);
            return;
        }

        cam.Render();
    }

    static bool HasEnoughContent(Texture2D tex)
    {
        var pixels = tex.GetPixels32();
        var opaque = 0;
        for (var i = 0; i < pixels.Length; i += 8)
        {
            if (pixels[i].a > 24)
                opaque++;
        }

        // Need a meaningful share of sampled pixels to accept the capture.
        return opaque > pixels.Length / 8 / 40;
    }

    public static void ApplyCapturedSprite(Image image, Sprite sprite, ref Sprite ownedSprite)
    {
        if (image == null || sprite == null)
            return;

        if (ownedSprite != null && ownedSprite != image.sprite)
        {
            var oldTex = ownedSprite.texture;
            Object.Destroy(ownedSprite);
            if (oldTex != null)
                Object.Destroy(oldTex);
            ownedSprite = null;
        }

        ownedSprite = sprite;
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    static Light CreateLight(Vector3 stagePos, Vector3 euler, float intensity)
    {
        var go = new GameObject("ResponseBagThumbLight")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        go.transform.position = stagePos;
        go.transform.rotation = Quaternion.Euler(euler);
        return light;
    }

    static void StripClone(GameObject root)
    {
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb != null)
                DestroyCompat(mb);
        }

        foreach (var col in root.GetComponentsInChildren<Collider>(true))
        {
            if (col != null)
                DestroyCompat(col);
        }

        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb != null)
                DestroyCompat(rb);
        }

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            r.enabled = true;
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length <= 1)
                continue;
            var kept = new List<Material>(mats.Length);
            for (var i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null)
                    continue;
                if (m.name.IndexOf("Outline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (m.shader != null &&
                    m.shader.name.IndexOf("Outline", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                kept.Add(m);
            }

            if (kept.Count != mats.Length)
                r.sharedMaterials = kept.ToArray();
        }

        root.SetActive(true);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.SetActive(true);
    }

    static void DestroyCompat(Object obj)
    {
        if (obj == null)
            return;
        if (Application.isPlaying)
            Object.Destroy(obj);
        else
            Object.DestroyImmediate(obj);
    }

    static Bounds CalculateBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = default;
        var has = false;
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled)
                continue;
            if (!has)
            {
                bounds = r.bounds;
                has = true;
            }
            else
                bounds.Encapsulate(r.bounds);
        }

        if (!has)
            bounds = new Bounds(root.transform.position, Vector3.one * 0.2f);
        return bounds;
    }

    static void PunchNearBlackToTransparent(Texture2D tex)
    {
        var pixels = tex.GetPixels32();
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            if (c.r < 12 && c.g < 12 && c.b < 12)
                pixels[i] = new Color32(0, 0, 0, 0);
        }

        tex.SetPixels32(pixels);
    }
}
