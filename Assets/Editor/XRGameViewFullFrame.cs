using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using UnityEngine.Experimental.Rendering;

/// <summary>Editor-only, letterboxed copy of the complete left-eye viewport.
/// Changes neither the HMD projection nor the eye render target. No extra scene camera.</summary>
[InitializeOnLoad]
public static class XRGameViewFullFrame
{
    const string Preference = "HCl.XR.GameView.FullEye";
    const string Menu = "Window/HCl VR/Full Eye in Game View";
    static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    static Material material;
    public static string LastStatus { get; private set; } = "XR not running";

    static XRGameViewFullFrame()
    {
        RenderPipelineManager.endContextRendering += Render;
        AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
    }

    [MenuItem(Menu)]
    static void Toggle() => EditorPrefs.SetBool(Preference, !EditorPrefs.GetBool(Preference, true));
    [MenuItem(Menu, true)]
    static bool Validate() { UnityEditor.Menu.SetChecked(Menu, EditorPrefs.GetBool(Preference, true)); return true; }

    public static Rect FitViewport(float sourceWidth, float sourceHeight, float width, float height)
    {
        float scale = Mathf.Min(width / Mathf.Max(1f, sourceWidth), height / Mathf.Max(1f, sourceHeight));
        return new Rect((width - sourceWidth * scale) * 0.5f,
            (height - sourceHeight * scale) * 0.5f, sourceWidth * scale, sourceHeight * scale);
    }

    static void Render(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!EditorApplication.isPlaying || !EditorPrefs.GetBool(Preference, true)) return;
        Camera camera = cameras.Find(c => c != null && c.cameraType == CameraType.Game &&
            c.stereoEnabled && c.targetTexture == null);
        if (camera == null) return;
        SubsystemManager.GetSubsystems(displays);
        var display = displays.Find(d => d.running && d.GetRenderPassCount() > 0);
        if (display == null) return;
        display.GetRenderPass(0, out var pass);
        if (pass.GetRenderParameterCount() == 0) return;
        pass.GetRenderParameter(camera, 0, out var eye);
        var source = display.GetRenderTextureForRenderPass(0);
        // Do not corrupt output if a provider returns an unresolved MSAA texture.
        if (source == null || source.bindTextureMS)
        {
            LastStatus = "Provider did not expose a resolved eye texture; standard mirror retained";
            return;
        }
        if (material == null)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/XRGameViewFullFrame.shader");
            if (shader == null) return;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        Rect viewport = eye.viewport;
        Rect fit = FitViewport(viewport.width, viewport.height, Screen.width, Screen.height);
        material.SetTexture("_EyeTexture", source);
        material.SetVector("_SourceRect", new Vector4(viewport.width / source.width,
            viewport.height / source.height, viewport.x / source.width, viewport.y / source.height));
        material.SetFloat("_EyeSlice", eye.textureArraySlice);
        material.SetFloat("_ManualSRGBRead", source.graphicsFormat == GraphicsFormat.R8G8B8A8_UNorm &&
            QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f : 0f);
        if (source.dimension == TextureDimension.Tex2DArray) material.EnableKeyword("EYE_ARRAY");
        else material.DisableKeyword("EYE_ARRAY");
        var cmd = CommandBufferPool.Get("HCl Full Eye Desktop Mirror");
        cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        cmd.SetViewport(new Rect(0, 0, Screen.width, Screen.height));
        cmd.ClearRenderTarget(false, true, Color.black);
        cmd.SetViewport(fit);
        cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
        cmd.SetViewport(new Rect(0, 0, Screen.width, Screen.height));
        context.ExecuteCommandBuffer(cmd);
        context.Submit();
        CommandBufferPool.Release(cmd);
        LastStatus = $"Full left eye {viewport.width}x{viewport.height}; fit {fit.width:F0}x{fit.height:F0}; no crop";
    }

    static void Cleanup()
    {
        RenderPipelineManager.endContextRendering -= Render;
        if (material != null) Object.DestroyImmediate(material);
    }
}
