using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using HClTraining.App;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

/// <summary>Opt-in App locomotion regression: real XRI movement/PhysX, injected HMD pose/input.
/// Never runs during ordinary Play, changes saved scenes, or claims to emulate Link tracking.</summary>
[InitializeOnLoad]
public static class IntegrationXrHeightValidation
{
    const string Key = "HCl.XrHeightValidation";
    const string Folder = "Logs/XrHeightValidation";
    static readonly StringBuilder report = new StringBuilder();
    static bool running, failed;
    static double started;

    [InitializeOnLoadMethod]
    static void IsolateBatchTracking()
    {
        // Explicit opt-in ONLY for synthetic-pose batch tests. An installed Link
        // runtime with no session can loop in xrCreateReferenceSpace under NullGfx.
        // Change the loaded test-process copy, never save XR settings to disk.
        if (!Application.isBatchMode || !Environment.GetCommandLineArgs().Contains("-hclNoXR")) return;
        var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (settings != null) settings.InitManagerOnStart = false;
    }

    static IntegrationXrHeightValidation()
    {
        if (SessionState.GetBool(Key, false)) Hook();
    }

    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene(SceneFlowController.AppScenePath);
        Hook();
        EditorApplication.EnterPlaymode();
    }

    public static void RunPreservationChecks()
    {
        IntegrationSceneValidator.Validate();
        IntegrationWasteLidLifecycleValidation.Run();
    }

    static void Hook()
    {
        running = failed = false;
        report.Clear();
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - started > 180)
        {
            Check(false, "Timeout; inspect Unity log for an unexpected exception");
            Finish();
            return;
        }
        var app = AppBootstrap.Instance;
        if (running || app == null || app.SceneFlow == null || app.SceneFlow.IsTransitioning) return;
        running = true;
        app.StartCoroutine(Checks(app));
    }

    static IEnumerator Checks(AppBootstrap app)
    {
        // Unthrottled batch frames can be shorter than CharacterController.minMoveDistance.
        // Simulate 72 Hz, rather than accidentally skipping almost every physical move.
        Time.captureDeltaTime = 1f / 72f;
        var modes = new[] { TrainingMode.PPEOnly, TrainingMode.PreEntryOnly, TrainingMode.LeakResponseOnly };
        foreach (var mode in modes)
        {
            Check(app.ScenarioFlow.StartTraining(mode), "Start " + mode);
            yield return null;
            while (app.SceneFlow.IsTransitioning) yield return null;
            yield return new WaitForSeconds(0.5f);
            var origin = app.XRProfiles.CommonXrOrigin.GetComponent<XROrigin>();
            var cc = origin.Origin.GetComponent<CharacterController>();
            var move = origin.GetComponent<ContinuousMoveProvider>();
            var gravity = origin.GetComponentsInChildren<SharedCharacterGravityProvider>(true);
            Check(origin.RequestedTrackingOriginMode == XROrigin.TrackingOriginMode.Floor,
                mode + ": explicit Floor tracking request (not simulated provider confirmation)");
            Check(gravity.Count(g => g.isActiveAndEnabled) == 1 && move.gravityProvider == null &&
                  gravity[0].mediator == move.mediator,
                mode + ": one active gravity provider bound to the canonical mediator and move provider");

            // Test fixtures are isolated above and away from scene geometry, never saved.
            var fixtures = new GameObject("HeightRegressionFixtures");
            var floor = Box(fixtures.transform, "Floor", new Vector3(100, 19.95f, 100), new Vector3(8, 0.1f, 8));
            Box(fixtures.transform, "120mm curb", new Vector3(100, 20.06f, 100.8f), new Vector3(1, 0.12f, 0.5f));
            var pose = origin.Camera.GetComponent<TrackedPoseDriver>();
            if (pose != null) pose.enabled = false;
            move.leftHandMoveInput.bypass = null;
            move.leftHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.ManualValue;
            move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
            move.leftHandMoveInput.manualValue = Vector2.zero;

            foreach (float headHeight in new[] { 1.55f, 1.85f })
            {
                Place(origin, cc, new Vector3(100, 20, 100), headHeight);
                foreach (var provider in gravity) provider.ResetFallForce();
                yield return new WaitForSeconds(0.4f);
                float baseY = origin.Origin.transform.position.y;
                Check(Mathf.Abs(baseY - 20) < 0.06f, $"{mode}/{headHeight}: initial floor y={baseY:F5}");
                float peak = baseY;
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    float endZ = cycle % 2 == 0 ? 101.7f : 100f;
                    move.leftHandMoveInput.manualValue = cycle % 2 == 0 ? Vector2.up : Vector2.down;
                    report.AppendLine($"INFO input={move.leftHandMoveInput.ReadValue()} enabled={move.isActiveAndEnabled} mediator={move.mediator.isActiveAndEnabled} body={move.mediator.bodyTransformer.isActiveAndEnabled} speed={move.moveSpeed} origin={origin.Origin.transform.position} scale={origin.Origin.transform.lossyScale} head={origin.Camera.transform.position}");
                    float until = Time.time + 4;
                    while (Time.time < until && Mathf.Abs(origin.Origin.transform.position.z - endZ) > 0.04f)
                    {
                        peak = Mathf.Max(peak, origin.Origin.transform.position.y);
                        yield return null;
                    }
                    move.leftHandMoveInput.manualValue = Vector2.zero;
                    report.AppendLine($"INFO after origin={origin.Origin.transform.position} inputMode={move.leftHandMoveInput.inputSourceMode} move={move.isActiveAndEnabled} state={move.locomotionState} motion={move.transformation.motion} head={origin.Camera.transform.position} cc={cc.enabled}/{cc.height}/{cc.center}");
                    yield return new WaitForSeconds(0.7f);
                    report.AppendLine($"INFO capsuleGrounded={cc.isGrounded}");
                    float actualY = origin.Origin.transform.position.y;
                    Check(Mathf.Abs(origin.Origin.transform.position.z - endZ) < 0.08f,
                        $"{mode}/{headHeight}/curb {cycle}: crosses curb");
                    Check(Mathf.Abs(actualY - baseY) < 0.01f,
                        $"{mode}/{headHeight}/curb {cycle}: returns to floor; delta={actualY - baseY:F5}m, peak={peak - baseY:F5}m");
                }
                Check(peak - baseY > 0.04f && peak - baseY < 0.24f,
                    $"{mode}/{headHeight}: curb was physically climbed, peak={peak - baseY:F5}m");

                // Lean/crouch without thumbstick, then straighten: HMD height must not be normalized.
                float beforeCrouch = origin.Origin.transform.position.y;
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    origin.Camera.transform.localPosition = new Vector3(0.12f, 0.55f, 0.1f);
                    yield return new WaitForSeconds(0.15f);
                    origin.Camera.transform.localPosition = new Vector3(0, headHeight, 0);
                    yield return new WaitForSeconds(0.15f);
                }
                Check(Mathf.Abs(origin.Origin.transform.position.y - beforeCrouch) < 0.01f,
                    $"{mode}/{headHeight}: three crouch/stand cycles do not lift origin");

                if (mode == TrainingMode.PPEOnly)
                {
                    var item = UnityEngine.Object.FindObjectsByType<ResponseBagPackableItem>(FindObjectsInactive.Exclude)
                        .FirstOrDefault(p => p.ItemId == ResponseEquipmentManager.IdGasDetector);
                    Check(item != null, "Actual PPE detector is present for pickup-height regression");
                    if (item != null)
                    {
                        var savedPosition = item.transform.position;
                        var savedRotation = item.transform.rotation;
                        Place(origin, cc, new Vector3(100, 20.12f, 100), headHeight);
                        item.ApplyRestingPhysics();
                        var solid = item.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger);
                        item.transform.position += origin.Camera.transform.position - Vector3.up * 0.45f - solid.bounds.center;
                        Physics.SyncTransforms();
                        Check(Physics.GetIgnoreCollision(solid, cc), "PPE detector ignores the active player capsule");
                        yield return new WaitForSeconds(0.8f);
                        Check(Mathf.Abs(origin.Origin.transform.position.y - 20) < 0.01f,
                            $"PPE/{headHeight}: detector under hands is NOT floor; y={origin.Origin.transform.position.y:F5}");
                        item.transform.SetPositionAndRotation(savedPosition, savedRotation);
                    }
                    var packing = ResponseBagPackingController.Instance;
                    var table = packing != null ? typeof(ResponseBagPackingController).GetField("tableTopCollider",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(packing) as Collider : null;
                    Check(table != null, "Actual PPE tabletop collider exists");
                    if (table != null)
                    {
                        var tablePosition = table.transform.position;
                        table.transform.position += new Vector3(100, 20.78f, 100.6f) - table.bounds.center;
                        Place(origin, cc, new Vector3(100, 20, 100), headHeight);
                        Check(Physics.GetIgnoreCollision(table, cc), "Actual tabletop ignores active player capsule");
                        move.leftHandMoveInput.manualValue = Vector2.up;
                        yield return new WaitForSeconds(0.5f);
                        move.leftHandMoveInput.manualValue = Vector2.zero;
                        origin.Camera.transform.localPosition = new Vector3(0, 0.55f, 0.1f);
                        yield return new WaitForSeconds(0.25f);
                        origin.Camera.transform.localPosition = new Vector3(0, headHeight, 0);
                        move.leftHandMoveInput.manualValue = Vector2.up;
                        yield return new WaitForSeconds(0.9f);
                        move.leftHandMoveInput.manualValue = Vector2.zero;
                        yield return new WaitForSeconds(0.3f);
                        Check(origin.Origin.transform.position.z > 101.3f &&
                            Mathf.Abs(origin.Origin.transform.position.y - 20) < 0.01f,
                            $"PPE/{headHeight}: approach/crouch through actual tabletop does not lift player; pos={origin.Origin.transform.position:F5}");
                        table.transform.position = tablePosition;
                    }
                }
            }
            move.leftHandMoveInput.manualValue = Vector2.zero;
            Place(origin, cc, new Vector3(100, 20.12f, 100), 1.7f);
            app.PauseController.PauseTraining();
            for (int frame = 0; frame < 12; frame++) yield return null;
            Check(!gravity[0].isActiveAndEnabled && Mathf.Abs(origin.Origin.transform.position.y - 20.12f) < 0.001f,
                mode + ": paused in air without gravity drift");
            app.PauseController.ResumeTraining();
            yield return new WaitForSeconds(0.6f);
            Check(Mathf.Abs(origin.Origin.transform.position.y - 20) < 0.01f, mode + ": gravity resumes to floor");
            UnityEngine.Object.Destroy(fixtures);
            Check(app.ScenarioFlow.ReturnToStart(), mode + ": return to menu");
            Check(!gravity[0].enabled && !move.enabled, mode + ": transition freezes gravity before floor unload");
            yield return null;
            while (app.SceneFlow.IsTransitioning) yield return null;
            Check(gravity.All(g => g == null || !g.isActiveAndEnabled), mode + ": menu has no gravity drift");
        }
        Finish();
    }

    static BoxCollider Box(Transform parent, string name, Vector3 position, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = position;
        var col = go.AddComponent<BoxCollider>();
        col.size = size;
        return col;
    }

    static void Place(XROrigin origin, CharacterController cc, Vector3 floorPosition, float headHeight)
    {
        cc.enabled = false;
        origin.Origin.transform.SetPositionAndRotation(floorPosition, Quaternion.identity);
        origin.CameraFloorOffsetObject.transform.localPosition = Vector3.zero;
        origin.Camera.transform.SetLocalPositionAndRotation(new Vector3(0, headHeight, 0), Quaternion.identity);
        cc.center = new Vector3(0, headHeight / 2 + cc.skinWidth, 0);
        cc.height = headHeight;
        cc.enabled = true;
        Physics.SyncTransforms();
    }

    static void Check(bool value, string message)
    {
        if (!value) failed = true;
        report.AppendLine((value ? "PASS " : "FAIL ") + message);
    }

    static void Finish()
    {
        Time.captureDeltaTime = 0;
        report.AppendLine(failed ? "RESULT FAIL" : "RESULT PASS (injected poses/input + real XRI/PhysX; no Link/HMD)");
        File.WriteAllText(Folder + "/result.txt", report.ToString());
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || SessionState.GetBool(Key, false)) return;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        if (Application.isBatchMode) EditorApplication.Exit(failed ? 2 : 0);
    }
}
