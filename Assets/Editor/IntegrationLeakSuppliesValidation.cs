using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HClTraining.App;
using HClTraining.SharedXR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using VRProject.LeakResponse;
using VRProject.LeakResponse.Narration;
using VRProject.ObjectInstallation;
using VRProject.Valves;

/// <summary>Opt-in actual App/PhysX regression; injected tracking and cue completion, no scene saves.</summary>
[InitializeOnLoad]
public static class IntegrationLeakSuppliesValidation
{
    const string Key = "HCl.LeakSuppliesValidation";
    const string Folder = "Logs/LeakSuppliesValidation";
    static readonly StringBuilder report = new StringBuilder();
    static bool running, failed;
    static double started;
    static IntegrationLeakSuppliesValidation() { if (SessionState.GetBool(Key, false)) Hook(); }
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene(SceneFlowController.AppScenePath);
        Hook();
        EditorApplication.EnterPlaymode();
    }
    public static void RunFull()
    {
        SessionState.SetBool(Key + ".Full", true);
        Run();
    }
    static void Hook()
    {
        running = failed = false;
        report.Clear();
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.playModeStateChanged += Changed;
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - started > 180) { Check(false, "Timeout; inspect log"); Finish(); return; }
        var app = AppBootstrap.Instance;
        if (running || app == null || app.SceneFlow == null || app.SceneFlow.IsTransitioning) return;
        running = true;
        app.StartCoroutine(Checks(app));
    }
    static IEnumerator Checks(AppBootstrap app)
    {
        Time.captureDeltaTime = 1f / 72;
        bool full = SessionState.GetBool(Key + ".Full", false);
        Check(app.ScenarioFlow.StartTraining(full ? TrainingMode.Full : TrainingMode.LeakResponseOnly), "Start actual App " + (full ? "Full" : "LeakResponseOnly"));
        yield return null;
        while (app.SceneFlow.IsTransitioning) yield return null;
        if (full)
        {
            var loadout = PPELoadoutPresets.FieldReadyComplete;
            loadout.source = PPELoadoutSource.PpeSceneResult;
            PPELoadoutPresets.ReplaceLoadout(loadout);
            app.Inventory.SyncFromLoadout();
            app.PPEState.ApplyPersistentState();
            app.ScenarioFlow.CompleteCurrentStage();
            yield return null;
            while (app.Session.CurrentStage != TrainingStage.PreEntry || app.SceneFlow.IsTransitioning) yield return null;
            yield return new WaitForSeconds(3);
            Check(app.Session.CurrentStage == TrainingStage.PreEntry, "Full routes through PreEntry with dormant Leak dependency");
            Check(app.ScenarioFlow.CompletePreEntryAndHandoff(), "Real additive PreEntry -> Leak handoff");
            yield return null;
            while (app.SceneFlow.IsTransitioning) yield return null;
        }
        yield return new WaitForSeconds(3);
        var drops = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include)
            .Single(p => p.name == "FallingDrops");
        var visual = UnityEngine.Object.FindAnyObjectByType<HClAbsorptionVisualController>();
        Describe(drops, visual);
        Capture(drops, "01-normal-leak.png");
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            IntegrationVisualAudit.Render(Folder + "/02-front-leak.png", drops.transform.position + Vector3.down * .2f, new Vector3(-1.4f, .9f, 1.4f));
            IntegrationVisualAudit.Render(Folder + "/03-side-leak.png", drops.transform.position + Vector3.down * .2f, new Vector3(-1.4f, .6f, -.2f));
        }
        Check(drops.gameObject.activeInHierarchy && drops.isPlaying && drops.particleCount > 0,
            $"Normal entry emits actual liquid particles: count={drops.particleCount}, rate={drops.emission.rateOverTimeMultiplier}");
        // The runner must NOT call Play, change culling or intensity before the
        // normal-entry assertion/captures: that would conceal the handoff regression.
        float initialIntensity = visual.CurrentLeakIntensity;
        visual.SetLeakIntensity01(0);
        yield return new WaitForSeconds(.5f);
        Check(!drops.isEmitting && drops.emission.rateOverTimeMultiplier == 0, "Zero leak remains stopped across frames");
        visual.SetLeakIntensity01(initialIntensity);
        yield return new WaitForSeconds(.5f);
        Check(drops.isPlaying && drops.particleCount > 0, "Positive leak resumes after zero without changing authored particles");
        drops.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        app.PauseController.PauseTraining();
        for (int frame = 0; frame < 8; frame++) yield return null;
        Check(!drops.isPlaying, "Pause does not restart stopped liquid simulation");
        app.PauseController.ResumeTraining();
        yield return new WaitForSeconds(.5f);
        Check(drops.isPlaying && drops.particleCount > 0, "Resume reconciles positive leak without another valve event");

        var handler = UnityEngine.Object.FindAnyObjectByType<P102SharedBagItemHandler>();
        var narrator = Field<P102LeakNarrationController>(handler, "narration");
        narrator.StopAll();
        Invoke(UnityEngine.Object.FindAnyObjectByType<StageLifecycleAdapter>(), "OnLeakCueCompleted", P102LeakNarrationCue.LeakSceneArrival);
        var origin = app.XRProfiles.CommonXrOrigin.GetComponent<XROrigin>();
        var cc = origin.Origin.GetComponent<CharacterController>();
        var pose = origin.Camera.GetComponent<TrackedPoseDriver>();
        if (pose != null) pose.enabled = false;
        var move = origin.GetComponent<ContinuousMoveProvider>();
        move.leftHandMoveInput.bypass = null;
        move.leftHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.ManualValue;
        move.leftHandMoveInput.manualValue = Vector2.zero;
        move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
        var fixture = new GameObject("LeakSuppliesRegressionFloor");
        fixture.transform.position = new Vector3(100, 19.95f, 100);
        fixture.AddComponent<BoxCollider>().size = new Vector3(8, .1f, 8);
        var anchor = handler.RetrievalAnchor;
        var savedAnchor = anchor.position;
        anchor.position = new Vector3(100, 20.85f, 100);
        foreach (string kind in new[] { "sock", "pad" })
        {
            narrator.StopAll();
            var source = Field<XRGrabInteractable>(handler, kind + "Source");
            var group = Field<PreplacedGhostInstallGroup>(handler, kind + "Group");
            cc.enabled = false;
            origin.Origin.transform.SetPositionAndRotation(new Vector3(100, 20, 100), Quaternion.identity);
            origin.CameraFloorOffsetObject.transform.localPosition = Vector3.zero;
            origin.Camera.transform.SetLocalPositionAndRotation(new Vector3(0, 1.7f, 0), Quaternion.identity);
            cc.height = 1.7f;
            cc.center = new Vector3(0, .85f + cc.skinWidth, 0);
            cc.enabled = true;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.4f);
            float baseY = origin.Origin.transform.position.y;
            var handPose = handler.LeftController.GetComponent<TrackedPoseDriver>();
            if (handPose != null) handPose.enabled = false;
            handler.LeftController.position = handler.RetrievalZone.bounds.center;
            Physics.SyncTransforms();
            Invoke(handler, "ResolveStageBindings");
            handler.LeftController.position = handler.RetrievalZone.bounds.center;
            Check(handler.IsLeftHandInAccessZone(), kind + ": hand in actual retrieval zone");
            source.enabled = true;
            Invoke(handler, "OnCueCompleted", kind == "sock" ? P102LeakNarrationCue.AbsorbentSockPlacementActivated : P102LeakNarrationCue.AbsorbentPadPlacementActivated);
            Invoke(handler, "OnSharedInputPerformed", SharedXRHand.Left, SharedXRControl.Grip);
            Invoke(handler, "OnSharedInputCanceled", SharedXRHand.Left, SharedXRControl.Grip);
            Check(handler.PresentedSource == source, kind + ": actual Grip press/release presents authored source");
            float peak = baseY, until = Time.time + .65f;
            while (Time.time < until)
            {
                peak = Mathf.Max(peak, origin.Origin.transform.position.y);
                yield return null;
            }
            var solids = source.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
            foreach (var c in solids)
                report.AppendLine($"INFO {kind} solid={c.name} center={c.bounds.center:F4} size={c.bounds.size:F4} ignore={Physics.GetIgnoreCollision(c, cc)} layer={c.gameObject.layer}");
            Check(solids.Length > 0 && solids.All(c => Physics.GetIgnoreCollision(c, cc)), kind + ": all source solids ignore only player capsule");
            Check(peak - baseY < .01f, $"{kind}: no player lift during rising presentation; peakDelta={peak - baseY:F5}m, end={origin.Origin.transform.position:F5}");
            // Select/release through actual XRI, not just a transform-only test.
            var testHand = new GameObject("LeakSuppliesRegressionHand");
            testHand.transform.SetParent(origin.transform, false);
            testHand.transform.position = source.transform.position + Vector3.up * .08f;
            var ray = testHand.AddComponent<XRRayInteractor>();
            ray.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            ray.selectInput.manualPerformed = true;
            ray.selectInput.manualValue = 1;
            ray.selectActionTrigger = XRBaseInputInteractor.InputTriggerType.State;
            var manager = ray.interactionManager;
            manager.SelectEnter(ray, (IXRSelectInteractable)source);
            yield return new WaitForSeconds(.15f);
            Check(source.isSelected && solids.All(c => Physics.GetIgnoreCollision(c, cc)), kind + ": actual XRI grab remains available without player collision");
            ray.selectInput.manualPerformed = false;
            ray.selectInput.manualValue = 0;
            if (source.isSelected) manager.SelectExit(ray, (IXRSelectInteractable)source);
            yield return new WaitForSeconds(.2f);
            Check(!source.isSelected && solids.All(c => Physics.GetIgnoreCollision(c, cc)), kind + ": release retains player exclusion");
            UnityEngine.Object.Destroy(testHand);
            source.gameObject.SetActive(false);
            source.gameObject.SetActive(true);
            cc.enabled = false;
            cc.enabled = true;
            yield return new WaitForFixedUpdate();
            yield return null;
            Check(solids.All(c => Physics.GetIgnoreCollision(c, cc)), kind + ": source/capsule reactivation restores collision pairs");
            Check(solids.All(c => c.gameObject.layer == 0 && !Physics.GetIgnoreCollision(c, fixture.GetComponent<Collider>())), kind + ": original grab layer and floor collision preserved");
            Check(group.TryInstallAll(kind == "sock" ? "Sock" : "Pad", out var count) && count > 0,
                kind + ": existing installation flow still completes");
            yield return null;
        }
        anchor.position = savedAnchor;
        UnityEngine.Object.Destroy(fixture);
        // Use the actual discharge valve, not only the visual controller's setter.
        Field<ValveWheelClosureController>(visual, "valveStateProvider").SetClosurePercent(100);
        yield return new WaitForSeconds(.5f);
        Check(visual.CurrentLeakIntensity == 0 && !drops.isEmitting,
            "Actual discharge valve closure stops liquid; synchronization never reopens it");
        yield return new WaitForSeconds(8);
        Check(visual.IsFinalCleanupComplete && !drops.isPlaying,
            "Final absorption cleanup stays stopped, including child mist");
        Finish();
    }
    static void Describe(ParticleSystem p, HClAbsorptionVisualController visual)
    {
        var r = p.GetComponent<ParticleSystemRenderer>();
        var m = p.main;
        report.AppendLine($"INFO mesh={r.mesh?.name} meshBounds={r.mesh?.bounds} size3D={m.startSize3D} xyz={m.startSizeX.constantMin}/{m.startSizeX.constantMax}, {m.startSizeY.constantMin}/{m.startSizeY.constantMax}, {m.startSizeZ.constantMin}/{m.startSizeZ.constantMax} lifetime={m.startLifetime.constantMin}/{m.startLifetime.constantMax} layer={p.gameObject.layer}");
        report.AppendLine($"INFO liquid intensity={visual.CurrentLeakIntensity} capturedRate={Field<float>(visual, "authoredEmissionRateMultiplier")} active={p.gameObject.activeInHierarchy} playing={p.isPlaying} count={p.particleCount} emission={p.emission.enabled}/{p.emission.rateOverTimeMultiplier} pos={p.transform.position} scale={p.transform.lossyScale} rot={p.transform.eulerAngles}");
        report.AppendLine($"INFO liquid lifetime={m.startLifetime} speed={m.startSpeed} size={m.startSize} color={m.startColor.color} gravity={m.gravityModifierMultiplier} space={m.simulationSpace} scaling={m.scalingMode} culling={m.cullingMode} renderer={r.enabled} mode={r.renderMode} bounds={r.bounds} maxSize={r.maxParticleSize} material={r.sharedMaterial?.name} shader={r.sharedMaterial?.shader?.name} supported={r.sharedMaterial?.shader?.isSupported} layer={p.gameObject.layer}");
        var particles = new ParticleSystem.Particle[p.particleCount];
        p.GetParticles(particles);
        foreach (var item in particles.Take(8)) report.AppendLine($"INFO particle pos={item.position} velocity={item.velocity} remaining={item.remainingLifetime} size={item.GetCurrentSize(p)} color={item.GetCurrentColor(p)}");
    }
    static void Capture(ParticleSystem drops, string name)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        IntegrationVisualAudit.Render(Folder + "/" + name, drops.transform.position + Vector3.down * .35f, new Vector3(1.7f, .45f, -1.7f));
    }
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    static void Check(bool value, string message) { failed |= !value; report.AppendLine((value ? "PASS " : "FAIL ") + message); File.WriteAllText(Folder + "/result.txt", report.ToString()); }
    static void Finish()
    {
        Time.captureDeltaTime = 0;
        report.AppendLine(failed ? "RESULT FAIL" : "RESULT PASS (real App/PhysX; injected tracking/cues, not Link hardware)");
        File.WriteAllText(Folder + "/result.txt", report.ToString());
        SessionState.SetBool(Key, false);
        SessionState.SetBool(Key + ".Full", false);
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || SessionState.GetBool(Key, false)) return;
        EditorApplication.playModeStateChanged -= Changed;
        if (Application.isBatchMode) EditorApplication.Exit(failed ? 2 : 0);
    }
}
