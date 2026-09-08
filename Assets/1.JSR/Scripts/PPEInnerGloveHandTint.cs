using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PPE-owned visual feedback for InnerGloves. Tints the existing XR default-hand
/// meshes after each side is equipped so the trainee can see the donning result.
/// Color matches RightInnerGlovePhysical. Does not change tracking, ray origin,
/// Common XR, or chemical-glove meshes.
/// </summary>
public static class PPEInnerGloveHandTint
{
    public const string LeftDefaultHandName = "PPE_DefaultHand_Left";
    public const string RightDefaultHandName = "PPE_DefaultHand_Right";
    const string RightPhysicalName = "RightInnerGlovePhysical";

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    static readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();
    static readonly List<GameObject> LeftHands = new List<GameObject>(4);
    static readonly List<GameObject> RightHands = new List<GameObject>(4);

    // Average albedo of PPE_glrove_basecolor, the material on RightInnerGlovePhysical.
    public static readonly Color WornHandColor = new Color(0.378f, 0.495f, 0.492f, 1f);

    static bool resolvedFromPhysical;
    static Color resolvedWornColor = WornHandColor;

    public static void ApplyFromInnerGlovePair()
    {
        var pair = PPEGlovePairCoordinator.InnerGlovesInstance;
        if (pair == null)
        {
            Apply(false, false);
            return;
        }

        Apply(pair.LeftEquipped, pair.RightEquipped);
    }

    public static void Apply(bool leftWorn, bool rightWorn)
    {
        CollectHands();
        ApplyToCollected(LeftHands, leftWorn);
        ApplyToCollected(RightHands, rightWorn);
    }

    public static void ApplyToHand(GameObject root, bool worn)
    {
        if (root == null)
            return;

        var wornColor = ResolveWornHandColor();
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!worn)
            {
                renderer.SetPropertyBlock(null);
                continue;
            }

            renderer.GetPropertyBlock(PropertyBlock);
            PropertyBlock.SetColor(BaseColorId, wornColor);
            PropertyBlock.SetColor(ColorId, wornColor);
            renderer.SetPropertyBlock(PropertyBlock);
        }
    }

    static Color ResolveWornHandColor()
    {
        if (resolvedFromPhysical)
            return resolvedWornColor;

        if (TrySampleRightInnerGloveColor(out var sampled))
        {
            resolvedWornColor = sampled;
            resolvedFromPhysical = true;
            return resolvedWornColor;
        }

        return WornHandColor;
    }

    static bool TrySampleRightInnerGloveColor(out Color color)
    {
        color = WornHandColor;
        var renderer = FindRightInnerGloveRenderer();
        if (renderer == null)
            return false;

        var material = renderer.sharedMaterial;
        if (material == null)
            return false;

        var tint = Color.white;
        if (material.HasProperty(BaseColorId))
            tint = material.GetColor(BaseColorId);
        else if (material.HasProperty(ColorId))
            tint = material.GetColor(ColorId);

        Texture texture = null;
        if (material.HasProperty(BaseMapId))
            texture = material.GetTexture(BaseMapId);
        if (texture == null && material.HasProperty(MainTexId))
            texture = material.GetTexture(MainTexId);

        if (texture != null && TryAverageTextureColor(texture, out var sampled))
        {
            color = sampled * tint;
            color.a = 1f;
            return color.maxColorComponent > 0.02f;
        }

        color = tint;
        color.a = 1f;
        return tint.maxColorComponent > 0.02f && tint != Color.white;
    }

    static Renderer FindRightInnerGloveRenderer()
    {
        var pair = PPEGlovePairCoordinator.InnerGlovesInstance;
        var fromPair = FindRenderer(pair != null ? pair.RightMember : null);
        if (fromPair != null)
            return fromPair;

        var members = Object.FindObjectsByType<PPEGlovePairMember>(FindObjectsInactive.Include);
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (member == null || !member.IsInnerGlove || member.GloveSide != PPEGrabHand.Right)
                continue;
            var renderer = FindRenderer(member);
            if (renderer != null)
                return renderer;
        }

        var named = GameObject.Find(RightPhysicalName);
        return named != null ? named.GetComponentInChildren<Renderer>(true) : null;
    }

    static Renderer FindRenderer(PPEGlovePairMember member)
    {
        if (member == null)
            return null;
        var root = member.VisualRoot != null ? member.VisualRoot : member.transform;
        return root.GetComponentInChildren<Renderer>(true);
    }

    static bool TryAverageTextureColor(Texture texture, out Color color)
    {
        color = default;
        if (texture == null)
            return false;

        RenderTexture rt = null;
        Texture2D readback = null;
        var previous = RenderTexture.active;
        try
        {
            rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            readback = new Texture2D(8, 8, TextureFormat.RGBA32, false, false);
            readback.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
            readback.Apply(false, false);
            var pixels = readback.GetPixels();
            if (pixels == null || pixels.Length == 0)
                return false;

            var r = 0f;
            var g = 0f;
            var b = 0f;
            for (var i = 0; i < pixels.Length; i++)
            {
                r += pixels[i].r;
                g += pixels[i].g;
                b += pixels[i].b;
            }

            var inv = 1f / pixels.Length;
            color = new Color(r * inv, g * inv, b * inv, 1f);
            return color.maxColorComponent > 0.02f;
        }
        catch
        {
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null)
                RenderTexture.ReleaseTemporary(rt);
            if (readback != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(readback);
                else
                    Object.DestroyImmediate(readback);
            }
        }
    }

    static void ApplyToCollected(List<GameObject> hands, bool worn)
    {
        for (var i = 0; i < hands.Count; i++)
            ApplyToHand(hands[i], worn);
    }

    static void CollectHands()
    {
        LeftHands.Clear();
        RightHands.Clear();

        var wearables = Object.FindObjectsByType<ChemicalGlovesWearableVisual>(FindObjectsInactive.Include);
        for (var i = 0; i < wearables.Length; i++)
        {
            var wearable = wearables[i];
            if (wearable == null)
                continue;
            AddUnique(LeftHands, wearable.LeftBareHand);
            AddUnique(RightHands, wearable.RightBareHand);
        }

        var origin = PPEActiveXrOrigin.ResolveOriginTransform();
        if (origin != null)
        {
            AddUnique(LeftHands, FindChild(origin, "Camera Offset/Left Controller/Visuals/" + LeftDefaultHandName));
            AddUnique(RightHands, FindChild(origin, "Camera Offset/Right Controller/Visuals/" + RightDefaultHandName));
        }
    }

    static GameObject FindChild(Transform root, string path)
    {
        var child = root.Find(path);
        return child != null ? child.gameObject : null;
    }

    static void AddUnique(List<GameObject> list, GameObject value)
    {
        if (value == null || list.Contains(value))
            return;
        list.Add(value);
    }
}
