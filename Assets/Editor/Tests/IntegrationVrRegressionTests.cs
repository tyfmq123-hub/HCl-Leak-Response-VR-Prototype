#if UNITY_INCLUDE_TESTS
using System;
using JH.EntrySafety;
using NUnit.Framework;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRProject.LeakResponse;
using VRProject.LeakResponse.Narration;

public sealed class IntegrationVrRegressionTests
{
    [Serializable] sealed class Manifest { public Cue[] cues = Array.Empty<Cue>(); }
    [Serializable] sealed class Cue { public string audio = ""; public string text = ""; public string sha256 = ""; }

    [Test]
    public void Narration_All83CanonicalClipsMatchReviewedScriptAndDecodeInUnity()
    {
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("docs/audio/narration-manifest.json"));
        var script = File.ReadAllText("docs/audio/FULL_COURSE_TTS_SCRIPT.md");
        Assert.That(manifest.cues.Length, Is.EqualTo(83));
        foreach (var cue in manifest.cues)
        {
            using var hash = SHA256.Create();
            Assert.That(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(cue.audio)))
                .Replace("-", "").ToLowerInvariant(), Is.EqualTo(cue.sha256), cue.audio);
            Assert.That(script, Does.Contain(cue.text), cue.audio);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(cue.audio);
            Assert.That(clip, Is.Not.Null, cue.audio);
            Assert.That(clip.length, Is.GreaterThan(0.5f), cue.audio);
            Assert.That(clip.channels, Is.EqualTo(1), cue.audio);
        }
    }

    [Test]
    public void Windsock_ProgressStartsDuringVoice_CompletionWaitsForVoice_WithGrace()
    {
        var state = new EntrySafetyStateMachine();
        while (state.CurrentStep != EntrySafetyStep.CheckInitialWind)
            Assert.That(state.TryCompleteStep(state.CurrentStep, EntrySafetyCompletionMode.Normal), Is.True);
        var gaze = new EntrySafetyGazeCoordinator(state, 3f, 0.2f, 0.45f);
        gaze.Update(EntrySafetyGazeTargetId.InitialWindsock, true, 1.5f, false);
        gaze.Update(EntrySafetyGazeTargetId.None, false, 0.3f, false);
        Assert.That(gaze.Progress01, Is.EqualTo(0.5f).Within(0.001f));
        gaze.Update(EntrySafetyGazeTargetId.InitialWindsock, true, 1.5f, false);
        Assert.That(gaze.Progress01, Is.EqualTo(1f));
        Assert.That(state.CurrentStep, Is.EqualTo(EntrySafetyStep.CheckInitialWind));
        gaze.Update(EntrySafetyGazeTargetId.InitialWindsock, true, 0.01f);
        Assert.That(state.CurrentStep, Is.EqualTo(EntrySafetyStep.WaitWindChange));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FullEyeShader_CopiesWholeTextureAndArraySliceWithBlackBars(bool array)
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/XRGameViewFullFrame.shader");
        Assert.That(shader.isSupported, Is.True);
        var material = new Material(shader);
        var source = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
        var sourceArray = new Texture2DArray(8, 8, 2, TextureFormat.RGBA32, false, true);
        var target = new RenderTexture(64, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(64, 32, TextureFormat.RGBA32, false, true);
        var previous = RenderTexture.active;
        var cmd = new CommandBuffer();
        try
        {
            var pixels = new Color[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = i % 8 < 4 ? Color.red : Color.green;
            source.SetPixels(pixels); source.Apply();
            sourceArray.SetPixels(pixels, 1); sourceArray.Apply();
            material.SetTexture("_EyeTexture", array ? (Texture)sourceArray : source);
            material.SetVector("_SourceRect", new Vector4(1, 1, 0, 0));
            material.SetFloat("_EyeSlice", 1);
            material.SetFloat("_ManualSRGBRead", 0);
            if (array) material.EnableKeyword("EYE_ARRAY");
            target.Create();
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(false, true, Color.black);
            cmd.SetViewport(XRGameViewFullFrame.FitViewport(8, 8, 64, 32));
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(cmd);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 64, 32), 0, 0); readback.Apply();
            Assert.That(readback.GetPixel(4, 16).maxColorComponent, Is.LessThan(0.02f));
            Assert.That(readback.GetPixel(20, 16).r, Is.GreaterThan(0.9f));
            Assert.That(readback.GetPixel(44, 16).g, Is.GreaterThan(0.9f));
            Assert.That(readback.GetPixel(60, 16).maxColorComponent, Is.LessThan(0.02f));
        }
        finally
        {
            RenderTexture.active = previous;
            cmd.Dispose(); target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(sourceArray);
            UnityEngine.Object.DestroyImmediate(readback);
        }
    }
    [TestCase(1920, 1080)]
    [TestCase(1000, 1000)]
    [TestCase(800, 1200)]
    public void FullEyeMirror_FitsWithoutCroppingOrStretching(int width, int height)
    {
        Rect fit = XRGameViewFullFrame.FitViewport(1832, 1920, width, height);
        Assert.That(fit.width / fit.height, Is.EqualTo(1832f / 1920f).Within(0.0001f));
        Assert.That(fit.xMin, Is.GreaterThanOrEqualTo(-0.001f));
        Assert.That(fit.yMin, Is.GreaterThanOrEqualTo(-0.001f));
        Assert.That(fit.xMax, Is.LessThanOrEqualTo(width + 0.001f));
        Assert.That(fit.yMax, Is.LessThanOrEqualTo(height + 0.001f));
    }
    [Test]
    public void Drops_RebindingAfterZeroIntensity_DoesNotCaptureZeroAsAuthoredRate()
    {
        var root = new GameObject("Drops regression");
        var puddle = new GameObject("Puddle");
        try
        {
            var particles = root.AddComponent<ParticleSystem>();
            var emission = particles.emission;
            emission.rateOverTime = 10f;
            var controller = root.AddComponent<HClAbsorptionVisualController>();
            controller.ConfigureRuntime(puddle.transform, null, particles, null, root.transform, null, null);
            controller.SetLeakIntensity01(0f);
            Assert.That(particles.emission.rateOverTimeMultiplier, Is.Zero);
            controller.ConfigureRuntime(puddle.transform, null, particles, null, root.transform, null, null);
            controller.SetLeakIntensity01(0.7f);
            Assert.That(particles.emission.rateOverTimeMultiplier, Is.EqualTo(7f).Within(0.001f));
            Assert.That(particles.emission.enabled, Is.True);
            particles.Simulate(1f, false, true);
            Assert.That(particles.particleCount, Is.GreaterThan(0));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(puddle); }
    }

    [Test]
    public void CarryLock_IgnoresGripReleasesAndRepeatedPresses_RestoresOriginalBinding()
    {
        var root = new GameObject("Carrying controller");
        try
        {
            var interactor = root.AddComponent<XRRayInteractor>();
            var reader = interactor.selectInput;
            reader.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            interactor.selectActionTrigger = XRBaseInputInteractor.InputTriggerType.Toggle;
            using (var carry = new P102CarryInputLock(interactor))
            {
                for (int i = 0; i < 30; i++)
                {
                    reader.manualPerformed = i % 2 == 0;
                    reader.manualValue = reader.manualPerformed ? 1f : 0f;
                    reader.manualFramePerformed = Time.frameCount;
                    reader.manualFrameCompleted = Time.frameCount;
                    carry.ReadPhysicalGrip(out bool held, out bool pressed);
                    Assert.That(held, Is.EqualTo(reader.manualPerformed));
                    Assert.That(pressed, Is.EqualTo(reader.manualPerformed));
                    Assert.That(reader.ReadIsPerformed(), Is.True);
                    Assert.That(reader.ReadWasCompletedThisFrame(), Is.False);
                    Assert.That(reader.ReadValue(), Is.EqualTo(1f));
                    Assert.That(interactor.selectActionTrigger, Is.EqualTo(XRBaseInputInteractor.InputTriggerType.State));
                }
            }
            Assert.That(reader.bypass, Is.Null);
            Assert.That(interactor.selectActionTrigger, Is.EqualTo(XRBaseInputInteractor.InputTriggerType.Toggle));
            Assert.That(reader.ReadIsPerformed(), Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [TestCase(6.9f, 7f, true)]
    [TestCase(7.1f, 7f, false)]
    [TestCase(9.9f, 10f, true)]
    [TestCase(10.1f, 10f, false)]
    public void WindsockCone_AcquireAndRetainUseExplicitAngularMargins(float angle, float tolerance, bool expected)
    {
        var target = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 20f;
        Assert.That(EntrySafetyCameraGazeAdapter.IsInsideGazeCone(
            new Ray(Vector3.zero, Vector3.forward), target, tolerance, 60f), Is.EqualTo(expected));
        Assert.That(EntrySafetyCameraGazeAdapter.IsInsideGazeCone(
            new Ray(Vector3.zero, Vector3.forward), Vector3.back * 10f, tolerance, 60f), Is.False);
    }
}
#endif
