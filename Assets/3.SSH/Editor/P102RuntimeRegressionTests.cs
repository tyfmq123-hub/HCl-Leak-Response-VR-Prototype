#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRProject.LeakResponse.Narration;
using VRProject.Valves;

namespace VRProject.LeakResponse.EditorTests
{
    public sealed class P102RuntimeRegressionTests
    {
        [Test]
        public void ValveUi_RemainsOnFixedRoot_WhenCameraAndHandwheelRotate()
        {
            GameObject cameraObject = CreateCamera("Head Camera", new Vector3(0f, 1.6f, -3f));
            GameObject fixedRoot = new GameObject("Fixed Valve Root");
            fixedRoot.transform.SetPositionAndRotation(
                new Vector3(1f, 0.4f, 2f), Quaternion.Euler(8f, 24f, -5f));
            GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = "Rotating Handwheel";
            wheel.transform.position = new Vector3(1f, 1f, 2f);
            ValveWheelClosureController controller = wheel.AddComponent<ValveWheelClosureController>();
            GameObject uiObject = new GameObject("Valve Progress Runtime");
            ValveProgressUI_Runtime ui = uiObject.AddComponent<ValveProgressUI_Runtime>();

            try
            {
                ui.Configure(controller, "고정 UI", fixedRoot.transform, cameraObject.transform);
                ui.Show();
                Vector3 originalPosition = ui.transform.position;
                Quaternion originalRotation = ui.transform.rotation;

                Assert.That(ui.transform.parent, Is.SameAs(fixedRoot.transform));
                Assert.That(ui.transform.IsChildOf(cameraObject.transform), Is.False);
                Assert.That(ui.transform.IsChildOf(wheel.transform), Is.False);

                foreach (Vector3 cameraEuler in new[]
                         {
                             new Vector3(0f, 70f, 0f),
                             new Vector3(35f, 70f, 0f),
                             new Vector3(35f, 70f, 40f),
                         })
                {
                    cameraObject.transform.rotation = Quaternion.Euler(cameraEuler);
                    Invoke(ui, "LateUpdate");
                    AssertVector(originalPosition, ui.transform.position);
                    AssertQuaternion(originalRotation, ui.transform.rotation);
                    Assert.That(Vector3.Dot(ui.transform.up, Vector3.up), Is.GreaterThan(0.9999f));
                }

                wheel.transform.rotation = Quaternion.Euler(0f, 0f, 120f);
                AssertVector(originalPosition, ui.transform.position);
                AssertQuaternion(originalRotation, ui.transform.rotation);

                cameraObject.transform.position = new Vector3(2f, 1.6f, 7f);
                for (int frame = 0; frame < 30; ++frame)
                    P102RuntimeUtility.ApplyYawOnlyBillboard(
                        ui.transform, cameraObject.transform, 1f / 60f);
                AssertVector(originalPosition, ui.transform.position);
                Assert.That(Quaternion.Angle(originalRotation, ui.transform.rotation),
                    Is.GreaterThan(90f));
                Assert.That(Vector3.Dot(ui.transform.up, Vector3.up), Is.GreaterThan(0.9999f));
                Vector3 towardViewer = Vector3.ProjectOnPlane(
                    cameraObject.transform.position - ui.transform.position, Vector3.up).normalized;
                Assert.That(Vector3.Dot(-ui.transform.forward, towardViewer), Is.GreaterThan(0.999f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(uiObject);
                UnityEngine.Object.DestroyImmediate(wheel);
                UnityEngine.Object.DestroyImmediate(fixedRoot);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void FlangeUi_IsOutsideCollider_Passive_StableAndIdempotent()
        {
            CreateFlangeRig(out GameObject cameraObject, out GameObject flangeRoot,
                out Transform leakPoint, out BoxCollider flangeCollider, out P102FlangeGazeRuntime gaze);

            try
            {
                Transform ui = gaze.CountdownTransform;
                Assert.That(ui, Is.Not.Null);
                Assert.That(ui.parent, Is.SameAs(flangeRoot.transform));
                Assert.That(ui.IsChildOf(cameraObject.transform), Is.False);
                Assert.That(gaze.IsRegisteredTargetCollider(flangeCollider), Is.True);
                Assert.That(P102RuntimeUtility.TryGetLocalColliderBounds(
                    flangeRoot.transform, new Collider[] { flangeCollider }, out Bounds localBounds), Is.True);
                Assert.That(ui.localPosition.z,
                    Is.LessThanOrEqualTo(localBounds.min.z -
                                         gaze.ConfiguredSurfaceClearanceMeters + 0.001f));
                Assert.That(ui.localPosition.y - 0.18f,
                    Is.GreaterThanOrEqualTo(localBounds.max.y +
                                            gaze.ConfiguredVerticalClearanceMeters - 0.001f));
                Vector3 horizontalViewerDirection = Vector3.ProjectOnPlane(
                    cameraObject.transform.position - ui.position, Vector3.up).normalized;
                Assert.That(Vector3.Dot(-ui.forward, horizontalViewerDirection), Is.GreaterThan(0.99f));

                foreach (Graphic graphic in ui.GetComponentsInChildren<Graphic>(true))
                    Assert.That(graphic.raycastTarget, Is.False);
                foreach (CanvasGroup group in ui.GetComponentsInChildren<CanvasGroup>(true))
                    Assert.That(group.blocksRaycasts || group.interactable, Is.False);
                foreach (GraphicRaycaster raycaster in ui.GetComponentsInChildren<GraphicRaycaster>(true))
                    Assert.That(raycaster.enabled, Is.False);

                Vector3 originalPosition = ui.position;
                Quaternion originalRotation = ui.rotation;
                cameraObject.transform.rotation = Quaternion.Euler(30f, 65f, 42f);
                Invoke(gaze, "Update");
                P102RuntimeUtility.ApplyYawOnlyBillboard(
                    ui, cameraObject.transform, 1f / 60f);
                AssertVector(originalPosition, ui.position);
                AssertQuaternion(originalRotation, ui.rotation);

                cameraObject.transform.position = new Vector3(0f, 0f, 2f);
                for (int frame = 0; frame < 30; ++frame)
                    P102RuntimeUtility.ApplyYawOnlyBillboard(
                        ui, cameraObject.transform, 1f / 60f);
                AssertVector(originalPosition, ui.position);
                Assert.That(Quaternion.Angle(originalRotation, ui.rotation), Is.GreaterThan(90f));
                Assert.That(Vector3.Dot(ui.up, Vector3.up), Is.GreaterThan(0.9999f));
                Assert.That(Vector3.Dot(-ui.forward,
                    Vector3.ProjectOnPlane(cameraObject.transform.position - ui.position,
                        Vector3.up).normalized), Is.GreaterThan(0.999f));

                gaze.Configure(null, flangeRoot.transform, leakPoint, cameraObject.transform);
                Transform[] children = flangeRoot.GetComponentsInChildren<Transform>(true);
                int countdownCount = 0;
                for (int i = 0; i < children.Length; ++i)
                {
                    if (children[i].name == "P102_FlangeGazeCountdown_Runtime")
                        ++countdownCount;
                }
                Assert.That(countdownCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gaze.gameObject);
                UnityEngine.Object.DestroyImmediate(flangeRoot);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void HeadDirection_RequiresActiveStage_AndContinuousThreeSeconds_IgnoresIncidentalColliders()
        {
            CreateFlangeRig(out GameObject cameraObject, out GameObject flangeRoot,
                out Transform leakPoint, out BoxCollider flangeCollider, out P102FlangeGazeRuntime gaze);
            GameObject narrationObject = new GameObject("LeakResponse Flow");
            P102LeakNarrationController narration =
                narrationObject.AddComponent<P102LeakNarrationController>();
            narration.enabled = false;
            narration.ConfigureRuntime(
                null, null, null, null, null, null);
            narration.SetStageActivated(true);
            gaze.Configure(narration, flangeRoot.transform, leakPoint, cameraObject.transform);
            GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "Different Collider";
            blocker.transform.SetPositionAndRotation(new Vector3(0f, 0f, -1f), Quaternion.identity);
            blocker.transform.localScale = Vector3.one * 0.25f;

            try
            {
                Physics.SyncTransforms();
                Invoke(gaze, "Update");
                Assert.That(gaze.ElapsedGazeSeconds, Is.Zero);
                Assert.That(gaze.CompletionDispatchCount, Is.Zero);

                Invoke(gaze, "OnCueCompleted", P102LeakNarrationCue.LeakReductionStarted);
                TMP_Text text = gaze.CountdownTransform.GetComponentInChildren<TMP_Text>(true);
                Assert.That(text.text, Is.EqualTo("3초"));
                Assert.That(gaze.GaugeFillAmount, Is.EqualTo(1f));
                Invoke(gaze, "TickGaze", 1f);
                Assert.That(gaze.ElapsedGazeSeconds, Is.EqualTo(1f).Within(0.0001f),
                    "Incidental environment and hand colliders must not block a visible marker.");

                cameraObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                Physics.SyncTransforms();
                Invoke(gaze, "TickGaze", 0.01f);
                Assert.That(gaze.ElapsedGazeSeconds, Is.Zero);
                cameraObject.transform.rotation = Quaternion.identity;

                blocker.SetActive(false);
                Physics.SyncTransforms();
                Invoke(gaze, "TickGaze", 2.9f);
                Assert.That(gaze.IsComplete, Is.False);
                Assert.That(gaze.ElapsedGazeSeconds, Is.EqualTo(2.9f).Within(0.0001f));

                cameraObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                Physics.SyncTransforms();
                Invoke(gaze, "TickGaze", 0.01f);
                Assert.That(gaze.ElapsedGazeSeconds, Is.Zero,
                    "Leaving the flange must reset the continuous timer immediately.");
                Assert.That(gaze.GaugeFillAmount, Is.EqualTo(1f));
                Assert.That(text.text, Is.EqualTo("3초"));

                cameraObject.transform.rotation = Quaternion.identity;
                Physics.SyncTransforms();
                Invoke(gaze, "TickGaze", 1f);
                Assert.That(text.text, Is.EqualTo("2초"));
                Invoke(gaze, "TickGaze", 1f);
                Assert.That(text.text, Is.EqualTo("1초"));
                Invoke(gaze, "TickGaze", 0.99f);
                Assert.That(gaze.IsComplete, Is.False);
                Assert.That(gaze.GaugeFillAmount, Is.InRange(0f, 0.01f));
                Invoke(gaze, "TickGaze", 0.01f);
                Assert.That(gaze.IsComplete, Is.True);
                Assert.That(gaze.IsStageActive, Is.False);
                Assert.That(gaze.CompletionDispatchCount, Is.EqualTo(1));
                Assert.That(text.text, Is.EqualTo("✓"));
                Assert.That(gaze.GaugeFillAmount, Is.Zero);
                Assert.That(narration.PendingCount, Is.EqualTo(1),
                    "The completion cue remains queued until its narration has completed.");
                Assert.That(narration.HasDispatched(P102LeakNarrationCue.LeakReductionCompleted),
                    Is.True, "Gaze completion must dispatch the next flow gate immediately.");
                Assert.That(narration.HasDispatched(
                        P102LeakNarrationCue.AbsorbentSockPlacementActivated),
                    Is.False, "The sock-placement gate must wait for completion narration.");

                Invoke(gaze, "Update");
                Assert.That(gaze.CompletionDispatchCount, Is.EqualTo(1));
                Assert.That(gaze.IsRegisteredTargetCollider(gaze.LastHitCollider), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(blocker);
                UnityEngine.Object.DestroyImmediate(narrationObject);
                UnityEngine.Object.DestroyImmediate(gaze.gameObject);
                UnityEngine.Object.DestroyImmediate(flangeRoot);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void YawOnlyBillboard_UsesViewerPosition_NotHeadRotation()
        {
            Vector3 uiPosition = new Vector3(1f, 1.2f, 2f);
            Vector3 viewerPosition = new Vector3(-2f, 1.8f, -3f);
            Quaternion first = P102RuntimeUtility.ResolveYawOnlyCanvasRotation(
                uiPosition, viewerPosition, Quaternion.Euler(25f, 30f, 40f));
            Quaternion second = P102RuntimeUtility.ResolveYawOnlyCanvasRotation(
                uiPosition, viewerPosition, Quaternion.Euler(-70f, 160f, 85f));

            AssertQuaternion(first, second);
            Assert.That(Vector3.Dot(first * Vector3.up, Vector3.up), Is.GreaterThan(0.99999f));
            Vector3 towardViewer = Vector3.ProjectOnPlane(
                viewerPosition - uiPosition, Vector3.up).normalized;
            Assert.That(Vector3.Dot(-(first * Vector3.forward), towardViewer),
                Is.GreaterThan(0.99999f));
        }

        [Test]
        public void ValveGuidance_UsesPassiveCornerMarkers_AndWaitsForDischargeCue()
        {
            GameObject cameraObject = CreateCamera("Main Camera", new Vector3(0f, 1.6f, -3f));
            GameObject suctionObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            suctionObject.name = "SUC Wheel";
            suctionObject.transform.position = new Vector3(-1f, 1f, 0f);
            ValveWheelClosureController suction =
                suctionObject.AddComponent<ValveWheelClosureController>();
            GameObject dischargeObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dischargeObject.name = "DIS Wheel";
            dischargeObject.transform.position = new Vector3(1f, 1f, 0f);
            ValveWheelClosureController discharge =
                dischargeObject.AddComponent<ValveWheelClosureController>();
            GameObject suctionAnchor = new GameObject("SUC Fixed Root");
            GameObject dischargeAnchor = new GameObject("DIS Fixed Root");
            GameObject runtimeObject = new GameObject("Valve Guidance Runtime");
            P102ValveGuidanceRuntime guidance =
                runtimeObject.AddComponent<P102ValveGuidanceRuntime>();

            try
            {
                guidance.Configure(suction, discharge, null, cameraObject.transform,
                    suctionAnchor.transform, dischargeAnchor.transform);

                guidance.ApplyCue(P102LeakNarrationCue.SuctionValveActivated);
                Assert.That(guidance.IsSuctionGuidanceVisible, Is.True);
                Assert.That(guidance.IsDischargeGuidanceVisible, Is.False);

                GameObject suctionMarkers = FindRuntimeObject("P102_SUC_ValveGuidance_Runtime");
                Assert.That(suctionMarkers, Is.Not.Null);
                Assert.That(suctionMarkers.GetComponentsInChildren<LineRenderer>(true).Length,
                    Is.EqualTo(8));
                Assert.That(suctionMarkers.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(suctionMarkers.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                Assert.That(suctionMarkers.GetComponentsInChildren<XRGrabInteractable>(true), Is.Empty);

                Invoke(guidance, "OnSuctionClosed");
                Assert.That(guidance.IsSuctionGuidanceVisible, Is.False);
                Assert.That(guidance.IsDischargeGuidanceVisible, Is.False,
                    "Discharge guidance must wait for its authored narration cue.");

                guidance.ApplyCue(P102LeakNarrationCue.DischargeValveActivated);
                Assert.That(guidance.IsSuctionGuidanceVisible, Is.False);
                Assert.That(guidance.IsDischargeGuidanceVisible, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runtimeObject);
                UnityEngine.Object.DestroyImmediate(dischargeAnchor);
                UnityEngine.Object.DestroyImmediate(suctionAnchor);
                UnityEngine.Object.DestroyImmediate(dischargeObject);
                UnityEngine.Object.DestroyImmediate(suctionObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void RecoveryTongs_UsesRotatedModelLocalHandle_AndCanBeEnabledImmediately()
        {
            GameObject tongs = new GameObject("INT_RecoveryTongs_01");
            GameObject tongsMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tongsMesh.name = "Tongs Mesh";
            tongsMesh.transform.SetParent(tongs.transform, false);
            tongsMesh.transform.localScale = new Vector3(0.10f, 0.72f, 0.08f);
            GameObject ghost = new GameObject("INT_RecoveryTongs_01_ObjectGhost");
            ghost.transform.SetPositionAndRotation(
                new Vector3(1f, 0.8f, 2f), Quaternion.Euler(0f, 0f, -90f));
            GameObject runtimeObject = new GameObject("P102 Recovery Tongs Runtime Test");
            P102RecoveryTongsRuntime runtime =
                runtimeObject.AddComponent<P102RecoveryTongsRuntime>();

            try
            {
                Assert.That(runtime.Configure(tongs, ghost.transform, null, null, null), Is.True);
                XRGrabInteractable grab = tongs.GetComponent<XRGrabInteractable>();
                Assert.That(runtime.HandleGrabCollider, Is.Not.Null);
                Assert.That(runtime.HandleGrabCollider.enabled, Is.True);
                Assert.That(grab.colliders.Count, Is.EqualTo(1));
                Assert.That(grab.colliders[0], Is.SameAs(runtime.HandleGrabCollider));
                Assert.That(grab.useDynamicAttach, Is.True);
                Assert.That(grab.matchAttachRotation, Is.False);
                Assert.That(grab.snapToColliderVolume, Is.True);
                Assert.That(grab.addDefaultGrabTransformers, Is.False);
                Assert.That(grab.trackScale, Is.False);
                Assert.That(grab.singleGrabTransformersCount, Is.EqualTo(1));
                Assert.That(grab.GetSingleGrabTransformerAt(0),
                    Is.TypeOf<UnityEngine.XR.Interaction.Toolkit.Transformers.
                        XRSingleGrabFreeTransformer>());

                Transform handle = runtime.HandleGrabCollider.transform;
                Assert.That(handle.localPosition.y, Is.LessThan(-0.1f),
                    "The rotated model must still resolve its handle from the local long axis.");
                Assert.That(runtime.IsGrabEnabled, Is.False);
                Invoke(runtime, "EnableRecoveryInteraction");
                Assert.That(runtime.IsGrabEnabled, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runtimeObject);
                UnityEngine.Object.DestroyImmediate(ghost);
                UnityEngine.Object.DestroyImmediate(tongs);
            }
        }

        [Test]
        public void RecoveryTongs_UsesStableDynamicAttach_AndHidesLegacyPoseGhosts()
        {
            GameObject tongs = new GameObject("INT_RecoveryTongs_01");
            GameObject tongsMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tongsMesh.name = "Tongs Mesh";
            tongsMesh.transform.SetParent(tongs.transform, false);
            tongsMesh.transform.localScale = new Vector3(0.10f, 0.72f, 0.08f);
            GameObject attachObject = new GameObject(P102RuntimeUtility.RightHandPoseAttachName);
            attachObject.transform.SetParent(tongs.transform, false);
            attachObject.transform.SetLocalPositionAndRotation(
                new Vector3(0.04f, -0.2f, 0.03f), Quaternion.Euler(15f, 30f, 45f));
            GameObject poseGhostsRoot = new GameObject(
                P102RuntimeUtility.HandPoseGhostsRootName);
            poseGhostsRoot.transform.SetParent(tongs.transform, false);
            GameObject poseGhostVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            poseGhostVisual.name = "Right Hand Pose Ghost Visual";
            poseGhostVisual.transform.SetParent(poseGhostsRoot.transform, false);
            poseGhostVisual.transform.localPosition = new Vector3(50f, 50f, 50f);
            poseGhostVisual.transform.localScale = Vector3.one * 8f;
            GameObject ghost = new GameObject("INT_RecoveryTongs_01_ObjectGhost");
            GameObject runtimeObject = new GameObject("P102 Recovery Tongs Authored Attach Test");
            P102RecoveryTongsRuntime runtime =
                runtimeObject.AddComponent<P102RecoveryTongsRuntime>();

            try
            {
                Assert.That(runtime.Configure(tongs, ghost.transform, null, null, null), Is.True);
                XRGrabInteractable grab = tongs.GetComponent<XRGrabInteractable>();
                Assert.That(grab.attachTransform, Is.SameAs(attachObject.transform));
                Assert.That(grab.useDynamicAttach, Is.True);
                Assert.That(grab.matchAttachPosition, Is.True);
                Assert.That(grab.matchAttachRotation, Is.False);
                Assert.That(grab.snapToColliderVolume, Is.True);
                Assert.That(grab.addDefaultGrabTransformers, Is.False);
                Assert.That(grab.trackScale, Is.False);
                Assert.That(grab.singleGrabTransformersCount, Is.EqualTo(1));
                Assert.That(poseGhostVisual.GetComponent<Renderer>().enabled, Is.False);
                Assert.That(poseGhostsRoot.activeSelf, Is.False);
                Assert.That(runtime.HandleGrabCollider.transform.localPosition.y,
                    Is.LessThan(-0.1f),
                    "Pose ghost renderers must not distort the physical tongs bounds.");
                Invoke(runtime, "EnableRecoveryInteraction");
                Assert.That(poseGhostVisual.GetComponent<Renderer>().enabled, Is.True,
                    "The authored tongs hand ghost must be visible while pickup is enabled.");
                Assert.That(poseGhostsRoot.activeSelf, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runtimeObject);
                UnityEngine.Object.DestroyImmediate(ghost);
                UnityEngine.Object.DestroyImmediate(tongs);
            }
        }

        [Test]
        public void WasteContainerLid_UsesVisibleHandleAttach_AndHidesLegacyPoseGhosts()
        {
            GameObject container = new GameObject("INT_WasteContainer_01");
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "HazWasteContainer_Body";
            body.transform.SetParent(container.transform, false);
            GameObject lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lid.name = "HazWasteContainer_Lid";
            lid.transform.SetParent(container.transform, false);
            lid.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            GameObject attachObject = new GameObject(P102RuntimeUtility.RightHandPoseAttachName);
            attachObject.transform.SetParent(lid.transform, false);
            attachObject.transform.SetLocalPositionAndRotation(
                new Vector3(0.1f, 0.2f, -0.05f), Quaternion.Euler(20f, 40f, 60f));
            GameObject poseGhostsRoot = new GameObject(
                P102RuntimeUtility.HandPoseGhostsRootName);
            poseGhostsRoot.transform.SetParent(lid.transform, false);
            GameObject poseGhostVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            poseGhostVisual.name = "Right Hand Pose Ghost Visual";
            poseGhostVisual.transform.SetParent(poseGhostsRoot.transform, false);
            poseGhostVisual.transform.localPosition = new Vector3(50f, 50f, 50f);
            poseGhostVisual.transform.localScale = Vector3.one * 8f;
            GameObject sideGhost = new GameObject("HazWasteContainer_Lid_ObjectGhost");
            sideGhost.transform.SetPositionAndRotation(
                new Vector3(1.25f, 0.8f, -0.45f), Quaternion.Euler(17f, 123f, -28f));
            GameObject absorptionObject = new GameObject("Absorption Controller");
            var absorption = absorptionObject.AddComponent<
                VRProject.LeakResponse.HClAbsorptionVisualController>();
            GameObject runtimeObject = new GameObject("P102 Waste Lid Authored Attach Test");
            P102WasteContainerRuntime runtime =
                runtimeObject.AddComponent<P102WasteContainerRuntime>();
            Vector3 authoredLidPosition = lid.transform.position;
            Quaternion authoredLidRotation = lid.transform.rotation;
            Vector3 authoredSideGhostPosition = sideGhost.transform.position;
            Quaternion authoredSideGhostRotation = sideGhost.transform.rotation;

            try
            {
                Assert.That(runtime.Configure(container, null, absorption), Is.True);
                Assert.That(Vector3.Distance(lid.transform.position, authoredLidPosition),
                    Is.LessThan(0.0001f),
                    "Runtime setup must preserve the Scene-authored closed lid position.");
                Assert.That(Quaternion.Angle(lid.transform.rotation, authoredLidRotation),
                    Is.LessThan(0.001f),
                    "Runtime setup must preserve the Scene-authored closed lid rotation.");
                Assert.That(Vector3.Distance(sideGhost.transform.position,
                        authoredSideGhostPosition), Is.LessThan(0.0001f),
                    "Runtime setup must preserve the Scene-authored side ghost position.");
                Assert.That(Quaternion.Angle(sideGhost.transform.rotation,
                        authoredSideGhostRotation), Is.LessThan(0.001f),
                    "Runtime setup must preserve the Scene-authored side ghost rotation.");
                XRGrabInteractable grab = lid.GetComponent<XRGrabInteractable>();
                Assert.That(grab.attachTransform,
                    Is.SameAs(runtime.LidHandleCollider.transform));
                Assert.That(grab.attachTransform, Is.Not.SameAs(attachObject.transform),
                    "Removed Hand Pose Tool attach must never drive the visible lid.");
                Assert.That(grab.useDynamicAttach, Is.False);
                Assert.That(grab.matchAttachPosition, Is.False);
                Assert.That(grab.matchAttachRotation, Is.False);
                Assert.That(grab.snapToColliderVolume, Is.True);
                Assert.That(grab.addDefaultGrabTransformers, Is.False);
                Assert.That(grab.trackScale, Is.False);
                Assert.That(grab.singleGrabTransformersCount, Is.EqualTo(1));
                Assert.That(poseGhostVisual.GetComponent<Renderer>().enabled, Is.False);
                Assert.That(poseGhostsRoot.activeSelf, Is.False);
                Invoke(runtime, "OnCueStarted",
                    P102LeakNarrationCue.ContainerAndLidGhostActivated);
                Assert.That(poseGhostVisual.GetComponent<Renderer>().enabled, Is.True,
                    "The authored lid hand ghost must be visible while lid pickup is enabled.");
                Assert.That(poseGhostsRoot.activeSelf, Is.True);
                Assert.That(Vector3.Distance(
                        runtime.LidHandleCollider.bounds.center,
                        lid.GetComponent<Renderer>().bounds.center),
                    Is.LessThan(1f),
                    "Pose ghost renderers must not distort the lid handle bounds.");
                Assert.That(FindRuntimeObject(
                        "P102_WasteContainerLeftWallCollider_Runtime"),
                    Is.Null,
                    "Solid runtime bin walls trap the player and tongs inside the container.");
            }
            finally
            {
                GameObject closedGhost = FindRuntimeObject(
                    "HazWasteContainer_Lid_ClosedGhost_Runtime");
                if (closedGhost != null)
                    UnityEngine.Object.DestroyImmediate(closedGhost);
                UnityEngine.Object.DestroyImmediate(runtimeObject);
                UnityEngine.Object.DestroyImmediate(absorptionObject);
                UnityEngine.Object.DestroyImmediate(sideGhost);
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        static void CreateFlangeRig(out GameObject cameraObject, out GameObject flangeRoot,
            out Transform leakPoint, out BoxCollider flangeCollider, out P102FlangeGazeRuntime gaze)
        {
            cameraObject = CreateCamera("XR Origin/Camera Offset/Main Camera", new Vector3(0f, 0f, -2f));
            flangeRoot = new GameObject("Fixed Flange Root");
            GameObject geometry = GameObject.CreatePrimitive(PrimitiveType.Cube);
            geometry.name = "Flange Child Collider";
            geometry.transform.SetParent(flangeRoot.transform, false);
            geometry.transform.localScale = new Vector3(0.6f, 0.5f, 0.1f);
            flangeCollider = geometry.GetComponent<BoxCollider>();
            var leakPointObject = new GameObject("LeakPoint");
            leakPointObject.transform.SetParent(flangeRoot.transform, false);
            leakPoint = leakPointObject.transform;
            GameObject runtimeObject = new GameObject("P102 Flange Gaze Runtime");
            gaze = runtimeObject.AddComponent<P102FlangeGazeRuntime>();
            Physics.SyncTransforms();
            gaze.Configure(null, flangeRoot.transform, leakPoint, cameraObject.transform);
        }

        static GameObject CreateCamera(string name, Vector3 position)
        {
            var cameraObject = new GameObject(name, typeof(Camera));
            cameraObject.transform.position = position;
            cameraObject.transform.rotation = Quaternion.identity;
            return cameraObject;
        }

        static GameObject FindRuntimeObject(string exactName)
        {
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; ++i)
            {
                if (transforms[i] != null && transforms[i].name == exactName)
                    return transforms[i].gameObject;
            }
            return null;
        }

        static void Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing test hook method {methodName}");
            method.Invoke(target, arguments);
        }

        static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.00001f));
        }

        static void AssertQuaternion(Quaternion expected, Quaternion actual)
        {
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(0.0001f));
        }
    }

    public sealed class P102ValveUiRecoveryRegressionTests
    {
        // These tests exercise only runtime-created objects; no Scene or Prefab mutation is required.
        GameObject host;
        GameObject suctionObject;
        GameObject dischargeObject;
        GameObject suctionAnchor;
        GameObject dischargeAnchor;
        P102ValveGuidanceRuntime guidance;
        ValveWheelClosureController suction;
        ValveWheelClosureController discharge;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("P102 valve UI recovery regression host");
            suction = CreateValve("P102 test suction", out suctionObject);
            discharge = CreateValve("P102 test discharge", out dischargeObject);
            suctionAnchor = new GameObject("P102 test suction fixed root");
            dischargeAnchor = new GameObject("P102 test discharge fixed root");
            guidance = host.AddComponent<P102ValveGuidanceRuntime>();
            guidance.Configure(suction, discharge, null, null,
                suctionAnchor.transform, dischargeAnchor.transform);
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (suctionObject != null) UnityEngine.Object.DestroyImmediate(suctionObject);
            if (dischargeObject != null) UnityEngine.Object.DestroyImmediate(dischargeObject);
            if (suctionAnchor != null) UnityEngine.Object.DestroyImmediate(suctionAnchor);
            if (dischargeAnchor != null) UnityEngine.Object.DestroyImmediate(dischargeAnchor);
        }

        [Test]
        public void Initialization_LeavesBothValveGuidesAndGaugesOff()
        {
            Assert.That(guidance.CurrentStage, Is.EqualTo(P102ValveGuidanceRuntime.GuidanceStage.None));
            AssertExclusiveState(false, false);
        }

        [Test]
        public void ValveCues_KeepGuidanceMutuallyExclusiveAndHideAtBoundaries()
        {
            guidance.ApplyCue(P102LeakNarrationCue.LeakSceneArrival);
            AssertExclusiveState(false, false);

            guidance.ApplyCue(P102LeakNarrationCue.SuctionValveActivated);
            AssertExclusiveState(true, false);

            suction.OnFullyClosed.Invoke();
            AssertExclusiveState(false, false);

            guidance.ApplyCue(P102LeakNarrationCue.DischargeValveActivated);
            AssertExclusiveState(false, true);

            discharge.OnFullyClosed.Invoke();
            AssertExclusiveState(false, false);
        }

        [Test]
        public void RuntimeGuidance_HasNoInteractionComponentsAndNoRaycastGraphics()
        {
            guidance.ApplyCue(P102LeakNarrationCue.SuctionValveActivated);
            GameObject marker = FindRuntimeObject("P102_SUC_ValveGuidance_Runtime");
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(marker.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(marker.GetComponentsInChildren<XRGrabInteractable>(true), Is.Empty);

            foreach (Transform child in marker.GetComponentsInChildren<Transform>(true))
                Assert.That(child.gameObject.layer, Is.EqualTo(2), child.name);
            foreach (Graphic graphic in guidance.SuctionProgress.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False, graphic.name);
        }

        [Test]
        public void RuntimeUpdate_DoesNotOverwriteExternalWheelTransform()
        {
            guidance.ApplyCue(P102LeakNarrationCue.SuctionValveActivated);
            Vector3 externalPosition = new Vector3(1.2f, 0.7f, -0.4f);
            Quaternion externalRotation = Quaternion.Euler(17f, 31f, 83f);
            Vector3 externalScale = new Vector3(1.1f, 0.9f, 1.2f);
            suction.transform.SetPositionAndRotation(externalPosition, externalRotation);
            suction.transform.localScale = externalScale;

            InvokePrivate(guidance, "Update");

            Assert.That(Vector3.Distance(suction.transform.position, externalPosition), Is.LessThan(0.00001f));
            Assert.That(Quaternion.Angle(suction.transform.rotation, externalRotation), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(suction.transform.localScale, externalScale), Is.LessThan(0.00001f));
        }

        [Test]
        public void FlowCue_QueuesUntilNarrationPlaybackCanRun()
        {
            var flowObject = new GameObject("LeakResponse Flow");

            try
            {
                P102LeakNarrationController flow =
                    flowObject.AddComponent<P102LeakNarrationController>();
                flow.enabled = false;
                flow.SetStageActivated(true);
                flow.ConfigureRuntime(suction, discharge, discharge, null, null, null);
                string order = string.Empty;
                flow.CueStarted += cue => order += $"S{(int)cue:00}";
                flow.CueCompleted += cue => order += $"C{(int)cue:00}";

                Assert.That(flow.EnqueueOnce(P102LeakNarrationCue.DischargeValveActivated), Is.True);
                Assert.That(order, Is.Empty);
                Assert.That(flow.PendingCount, Is.EqualTo(1));
                Assert.That(flow.IsPlaying, Is.False);
                Assert.That(flow.HasDispatched(P102LeakNarrationCue.DischargeValveActivated), Is.True);
                Assert.That(flowObject.GetComponent<AudioSource>(), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(flowObject);
            }
        }

        [Test]
        public void WrongDirectionCannotAdvanceClosure_ButClockwiseLogicalCloseReachesOneHundred()
        {
            float wrongDirection = ValveWheelClosureController.CalculateClosurePercent(0f, -45f, 0f, 90f);
            float halfway = ValveWheelClosureController.CalculateClosurePercent(0f, 45f, 0f, 90f);
            float complete = ValveWheelClosureController.CalculateClosurePercent(0f, 90f, 0f, 90f);

            Assert.That(wrongDirection, Is.EqualTo(0f));
            Assert.That(halfway, Is.EqualTo(50f));
            Assert.That(complete, Is.EqualTo(100f));
        }

        [Test]
        public void EachValveFullyClosedEvent_FiresExactlyOnce()
        {
            InvokePrivate(suction, "Start");
            InvokePrivate(discharge, "Start");
            int suctionCount = 0;
            int dischargeCount = 0;
            suction.OnFullyClosed.AddListener(() => suctionCount++);
            discharge.OnFullyClosed.AddListener(() => dischargeCount++);

            suction.SetClosurePercent(100f);
            suction.SetClosurePercent(100f);
            discharge.SetClosurePercent(100f);
            discharge.SetClosurePercent(100f);

            Assert.That(suctionCount, Is.EqualTo(1));
            Assert.That(dischargeCount, Is.EqualTo(1));
        }

        void AssertExclusiveState(bool suctionExpected, bool dischargeExpected)
        {
            Assert.That(guidance.IsSuctionGuidanceVisible, Is.EqualTo(suctionExpected));
            Assert.That(guidance.IsDischargeGuidanceVisible, Is.EqualTo(dischargeExpected));
            Assert.That(guidance.SuctionProgress.gameObject.activeSelf, Is.EqualTo(suctionExpected));
            Assert.That(guidance.DischargeProgress.gameObject.activeSelf, Is.EqualTo(dischargeExpected));
        }

        static ValveWheelClosureController CreateValve(string objectName, out GameObject valveObject)
        {
            valveObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            valveObject.name = objectName;
            Rigidbody body = valveObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            valveObject.AddComponent<HingeJoint>();
            valveObject.AddComponent<XRGrabInteractable>();
            valveObject.AddComponent<ValveWheelGrabTransformer>();
            return valveObject.AddComponent<ValveWheelClosureController>();
        }

        static GameObject FindRuntimeObject(string exactName)
        {
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; ++i)
            {
                if (transforms[i] != null && transforms[i].name == exactName)
                    return transforms[i].gameObject;
            }
            return null;
        }

        static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(target, null);
        }
    }
}
#endif
