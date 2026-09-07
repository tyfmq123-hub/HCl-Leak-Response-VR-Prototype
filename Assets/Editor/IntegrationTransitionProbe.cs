using HClTraining.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Explicit batch diagnostic. Never runs in a player or a normal editor play session.
[InitializeOnLoad]
public static class IntegrationTransitionProbe
{
    const string ActiveKey = "HCl.TransitionProbe.Active";
    static bool measuring;
    static int lastFrame = -1;
    static int frames, slowFrames, renderedFrames, lostCameraFrames;
    static double previousTime, startedAt, worstFrameMs;
    static string from;
    static Camera persistentCamera;

    static IntegrationTransitionProbe()
    {
        EditorApplication.update += EnsureSampler;
        RenderPipelineManager.endCameraRendering += OnRendered;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                SessionState.SetBool(ActiveKey, false);
        };
    }

    public static void RunSmoke()
    {
        SessionState.SetBool(ActiveKey, true);
        AppPlayModeSmokeRunner.Run();
    }

    static void EnsureSampler()
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying) return;
        var app = AppBootstrap.Instance;
        if (app != null && app.GetComponent<IntegrationTransitionFrameSampler>() == null)
            app.gameObject.AddComponent<IntegrationTransitionFrameSampler>();
    }

    static void OnRendered(ScriptableRenderContext context, Camera camera)
    {
        if (measuring && camera == persistentCamera) renderedFrames++;
    }

    public static void SampleFrame()
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying ||
            lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        var app = AppBootstrap.Instance;
        if (app == null || app.SceneFlow == null) return;
        double now = Time.realtimeSinceStartupAsDouble;
        bool transitioning = app.SceneFlow.IsTransitioning;
        if (!measuring && transitioning)
        {
            measuring = true;
            startedAt = previousTime = now;
            worstFrameMs = 0;
            frames = slowFrames = renderedFrames = lostCameraFrames = 0;
            from = app.SceneFlow.CurrentContentScene.name;
            persistentCamera = app.MainCamera;
        }
        if (!measuring) return;
        double frameMs = (now - previousTime) * 1000;
        previousTime = now;
        worstFrameMs = System.Math.Max(worstFrameMs, frameMs);
        frames++;
        if (frameMs > 50) slowFrames++;
        if (persistentCamera == null || !persistentCamera.isActiveAndEnabled ||
            app.MainCamera != persistentCamera) lostCameraFrames++;
        if (transitioning) return;
        Debug.Log($"[TransitionProbe] {from} -> {app.SceneFlow.CurrentContentScene.name}; " +
            $"seconds={now - startedAt:F3}; worstFrameMs={worstFrameMs:F1}; " +
            $"frames={frames}; framesOver50ms={slowFrames}; renderedFrames={renderedFrames}; " +
            $"lostCameraFrames={lostCameraFrames}; loadingPriority={Application.backgroundLoadingPriority}");
        measuring = false;
    }
}

// Sample before the Editor's smoke driver can immediately request another transition.
public sealed class IntegrationTransitionFrameSampler : MonoBehaviour
{
    void LateUpdate() => IntegrationTransitionProbe.SampleFrame();
}
