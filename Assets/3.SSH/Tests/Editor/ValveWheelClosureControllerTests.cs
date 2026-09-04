using NUnit.Framework;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRProject.Valves.Tests
{
    public sealed class ValveWheelClosureControllerTests
    {
        [TestCase(0f, 30f)]
        [TestCase(36f, 40f)]
        [TestCase(-108f, 0f)]
        [TestCase(252f, 100f)]
        [TestCase(-999f, 0f)]
        [TestCase(999f, 100f)]
        public void ClosureCalculation_UsesInitialThirtyPercentAndClamps(float angle, float expected)
        {
            Assert.That(ValveWheelClosureController.CalculateClosurePercent(30f, angle, 108f, 252f),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void ReversingDirection_ReversesClosureChange()
        {
            float normal = ValveWheelClosureController.CalculateClosurePercent(30f, 36f, 108f, 252f);
            float inverted = ValveWheelClosureController.CalculateClosurePercent(30f, -36f, 108f, 252f);
            Assert.That(normal, Is.EqualTo(40f).Within(0.0001f));
            Assert.That(inverted, Is.EqualTo(20f).Within(0.0001f));
        }

        [Test]
        public void ConfiguredClockwiseDirection_IncreasesClosure()
        {
            float closure = ValveWheelClosureController.CalculateClosurePercent(30f, 36f, 108f, 252f);
            Assert.That(closure, Is.GreaterThan(30f));
        }

        [Test]
        public void ConfiguredCounterClockwiseDirection_DecreasesClosure()
        {
            float closure = ValveWheelClosureController.CalculateClosurePercent(30f, -36f, 108f, 252f);
            Assert.That(closure, Is.LessThan(30f));
        }

        [Test]
        public void FrontAxisConvention_MapsClockwiseToLogicalCloseForEitherOrientation()
        {
            Assert.That(ValveWheelClosureController.CalculateCloseAngleSign(
                Vector3.forward, Vector3.forward,
                ValveWheelClosureController.CloseDirection.ClockwiseFromFront), Is.EqualTo(-1f));
            Assert.That(ValveWheelClosureController.CalculateCloseAngleSign(
                Vector3.back, Vector3.forward,
                ValveWheelClosureController.CloseDirection.ClockwiseFromFront), Is.EqualTo(1f));
        }

        [Test]
        public void FullyOpenTripleHaptic_RequiresPositiveStateAndOneTransition()
        {
            Assert.That(ValveWheelClosureController.ShouldTriggerFullyOpenHaptic(false, true, true), Is.False);
            Assert.That(ValveWheelClosureController.ShouldTriggerFullyOpenHaptic(false, false, true), Is.False);
            Assert.That(ValveWheelClosureController.ShouldTriggerFullyOpenHaptic(true, true, true), Is.False);
            Assert.That(ValveWheelClosureController.ShouldTriggerFullyOpenHaptic(true, false, true), Is.True);
        }

        [TestCase(0f, 1f)]
        [TestCase(30f, 0.7f)]
        [TestCase(100f, 0f)]
        public void LinearLeakIntensity_IsInverseOfClosure(float closure, float expected)
        {
            Assert.That(ValveWheelClosureController.CalculateLinearLeakIntensity01(closure),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void JointLimits_UseConfiguredOpenAndCloseAngles()
        {
            GameObject gameObject = new GameObject("Valve joint limit test");
            try
            {
                gameObject.AddComponent<Rigidbody>();
                HingeJoint hinge = gameObject.AddComponent<HingeJoint>();
                gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                ValveWheelGrabTransformer transformer = gameObject.AddComponent<ValveWheelGrabTransformer>();

                Assert.That(float.IsFinite(hinge.limits.min), Is.True);
                Assert.That(float.IsFinite(hinge.limits.max), Is.True);
                Assert.That(transformer.ApplyConfiguredJointLimits(), Is.True);
                Assert.That(hinge.limits.min, Is.EqualTo(-108f).Within(0.0001f));
                Assert.That(hinge.limits.max, Is.EqualTo(252f).Within(0.0001f));
                Assert.That(hinge.useLimits, Is.True);
                Assert.That(hinge.extendedLimits, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void JointLimits_NonFiniteConfigurationLeavesExistingLimitsUntouched()
        {
            GameObject gameObject = new GameObject("Valve invalid limit test");
            try
            {
                gameObject.AddComponent<Rigidbody>();
                HingeJoint hinge = gameObject.AddComponent<HingeJoint>();
                gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                ValveWheelGrabTransformer transformer = gameObject.AddComponent<ValveWheelGrabTransformer>();
                JointLimits original = hinge.limits;
                original.min = -12f;
                original.max = 34f;
                hinge.limits = original;
                typeof(ValveWheelGrabTransformer).GetField("maxOpenRotationDegrees",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(transformer, float.NaN);

                Assert.That(transformer.ApplyConfiguredJointLimits(), Is.False);
                Assert.That(hinge.limits.min, Is.EqualTo(-12f).Within(0.0001f));
                Assert.That(hinge.limits.max, Is.EqualTo(34f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void GrabProjection_RemovesAxisDepthAndNormalizes()
        {
            bool valid = ValveWheelGrabTransformer.TryProjectHandDirection(
                new Vector3(1f, 2f, 3f), new Vector3(4f, 6f, 100f), Vector3.forward, 0.02f,
                out Vector3 direction);

            Assert.That(valid, Is.True);
            Assert.That(Vector3.Distance(direction, new Vector3(0.6f, 0.8f, 0f)), Is.LessThan(0.0001f));
        }

        [Test]
        public void SignedAngle_HasOppositeSignsForOppositeHandDirections()
        {
            Assert.That(ValveWheelGrabTransformer.TryCalculateSignedAngle(
                Vector3.right, Vector3.up, Vector3.forward, 90f, out float counterClockwise), Is.True);
            Assert.That(ValveWheelGrabTransformer.TryCalculateSignedAngle(
                Vector3.right, Vector3.down, Vector3.forward, 90f, out float clockwise), Is.True);

            Assert.That(counterClockwise, Is.EqualTo(90f).Within(0.0001f));
            Assert.That(clockwise, Is.EqualTo(-90f).Within(0.0001f));
        }

        [Test]
        public void SignedAngle_RejectsMomentaryLargeJump()
        {
            Vector3 almostOpposite = Quaternion.AngleAxis(150f, Vector3.forward) * Vector3.right;
            Assert.That(ValveWheelGrabTransformer.TryCalculateSignedAngle(
                Vector3.right, almostOpposite, Vector3.forward, 90f, out float angle), Is.False);
            Assert.That(angle, Is.EqualTo(0f));
        }

        [Test]
        public void OneHandMode_DoesNotRotateValve()
        {
            float result = ValveWheelGrabTransformer.ApplyRotationGain(10f, 1, 0.55f, 1f, false);
            Assert.That(result, Is.EqualTo(0f));
        }

        [Test]
        public void TwoHandMode_AppliesTwoHandRotationGain()
        {
            float result = ValveWheelGrabTransformer.ApplyRotationGain(10f, 2, 0.55f, 1f, false);
            Assert.That(result, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void SamePhysicalHandDelta_OnlyTwoHandsRotate()
        {
            float oneHand = ValveWheelGrabTransformer.ApplyRotationGain(10f, 1, 0.55f, 1f, false);
            float twoHands = ValveWheelGrabTransformer.ApplyRotationGain(10f, 2, 0.55f, 1f, false);
            Assert.That(oneHand, Is.EqualTo(0f));
            Assert.That(twoHands, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void TwoHandsMovingSameDirection_UseAverageDelta()
        {
            bool valid = ValveWheelGrabTransformer.TryCombineHandDeltas(
                8f, true, 12f, true, 0.02f, out float combined, out string reason);
            Assert.That(valid, Is.True);
            Assert.That(combined, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(reason, Is.EqualTo("BOTH_HANDS_APPLIED"));
        }

        [Test]
        public void InvalidLeftTracking_UsesRightWhileBothHandsRemainGrabbed()
        {
            bool valid = ValveWheelGrabTransformer.TryCombineHandDeltas(
                150f, false, 7f, true, 0.02f, out float combined, out string reason);
            Assert.That(valid, Is.True);
            Assert.That(combined, Is.EqualTo(7f).Within(0.0001f));
            Assert.That(reason, Is.EqualTo("RIGHT_TRACKED_WITH_TWO_HAND_GRAB"));
        }

        [Test]
        public void InvalidRightTracking_UsesLeftWhileBothHandsRemainGrabbed()
        {
            bool valid = ValveWheelGrabTransformer.TryCombineHandDeltas(
                7f, true, 150f, false, 0.02f, out float combined, out string reason);
            Assert.That(valid, Is.True);
            Assert.That(combined, Is.EqualTo(7f).Within(0.0001f));
            Assert.That(reason, Is.EqualTo("LEFT_TRACKED_WITH_TWO_HAND_GRAB"));
        }

        [Test]
        public void LeftTrackingFrame_RotatesWhileBothHandsRemainGrabbed()
        {
            bool valid = ValveWheelGrabTransformer.TryCombineHandDeltas(
                10f, true, 0f, true, 0.02f, out float combined, out string reason);
            Assert.That(valid, Is.True);
            Assert.That(combined, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(reason, Is.EqualTo("LEFT_TRACKED_WITH_TWO_HAND_GRAB"));
        }

        [Test]
        public void RightTrackingFrame_RotatesWhileBothHandsRemainGrabbed()
        {
            bool valid = ValveWheelGrabTransformer.TryCombineHandDeltas(
                0f, true, 10f, true, 0.02f, out float combined, out string reason);
            Assert.That(valid, Is.True);
            Assert.That(combined, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(reason, Is.EqualTo("RIGHT_TRACKED_WITH_TWO_HAND_GRAB"));
        }

        [Test]
        public void TwoHandsMovingOppositeDirections_IgnoreFrame()
        {
            bool valid = ValveWheelGrabTransformer.TryCombineHandDeltas(
                8f, true, -7f, true, 0.02f, out float combined, out string reason);
            Assert.That(valid, Is.False);
            Assert.That(combined, Is.EqualTo(0f));
            Assert.That(reason, Is.EqualTo("OPPOSITE_DIRECTION"));
        }

        [Test]
        public void BothInvalidTrackingVectors_IgnoreFrame()
        {
            bool valid = ValveWheelGrabTransformer.TryCombineHandDeltas(
                0f, false, 0f, false, 0.02f, out float combined, out string reason);
            Assert.That(valid, Is.False);
            Assert.That(combined, Is.EqualTo(0f));
            Assert.That(reason, Is.EqualTo("INVALID_BOTH_VECTORS"));
        }

        [TestCase(1, 2)]
        [TestCase(2, 1)]
        public void HandModeTransitionFrame_DoesNotApplyAngle(int previousHands, int currentHands)
        {
            Assert.That(ValveWheelGrabTransformer.ModeForHandCount(previousHands),
                Is.Not.EqualTo(ValveWheelGrabTransformer.ModeForHandCount(currentHands)));
            float transitionDelta = ValveWheelGrabTransformer.ApplyRotationGain(
                15f, currentHands, 0.55f, 1f, true);
            Assert.That(transitionDelta, Is.EqualTo(0f));
        }

        [Test]
        public void AfterOneOfTwoHandsReleases_RotationStops()
        {
            float transitionDelta = ValveWheelGrabTransformer.ApplyRotationGain(
                10f, 1, 0.55f, 1f, true);
            float nextFrameDelta = ValveWheelGrabTransformer.ApplyRotationGain(
                10f, 1, 0.55f, 1f, false);
            Assert.That(transitionDelta, Is.EqualTo(0f));
            Assert.That(nextFrameDelta, Is.EqualTo(0f));
        }

        [TestCase(0, 1, true)]
        [TestCase(1, 0, true)]
        [TestCase(0, 0, false)]
        [TestCase(1, 1, false)]
        [TestCase(0, 2, false)]
        public void ValveRotation_RequiresDistinctLeftAndRightHands(
            int firstHandKey, int secondHandKey, bool expected)
        {
            Assert.That(
                ValveWheelGrabTransformer.HasRequiredHandPair(firstHandKey, secondHandKey),
                Is.EqualTo(expected));
        }

        [TestCase("LeftHand", 0)]
        [TestCase("Right Controller", 1)]
        public void HandednessFallback_UsesInteractorParentHierarchy(
            string parentName, int expectedKey)
        {
            GameObject parent = new GameObject(parentName);
            GameObject child = new GameObject("Near-Far Interactor");
            try
            {
                child.transform.SetParent(parent.transform);
                Assert.That(
                    ValveWheelGrabTransformer.HandKeyFromTransformHierarchy(
                        child.transform),
                    Is.EqualTo(expectedKey));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void WithNoSelectingHands_TransformerDoesNotApplyRotation()
        {
            float result = ValveWheelGrabTransformer.ApplyRotationGain(30f, 0, 0.55f, 1f, false);
            Assert.That(result, Is.EqualTo(0f));
        }

        [Test]
        public void AfterBothHandsRelease_RotationStops()
        {
            float twoHandDelta = ValveWheelGrabTransformer.ApplyRotationGain(12f, 2, 0.55f, 1f, false);
            float releasedDelta = ValveWheelGrabTransformer.ApplyRotationGain(12f, 0, 0.55f, 1f, false);
            Assert.That(twoHandDelta, Is.EqualTo(12f).Within(0.0001f));
            Assert.That(releasedDelta, Is.EqualTo(0f));
        }

        [Test]
        public void HandNearCenter_IsIgnoredWithoutNaN()
        {
            bool valid = ValveWheelGrabTransformer.TryProjectHandDirection(
                Vector3.zero, new Vector3(0.00001f, 0f, 4f), Vector3.forward, 0.02f,
                out Vector3 direction);

            Assert.That(valid, Is.False);
            Assert.That(float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsNaN(direction.z), Is.False);
        }

        [TestCase(-999f, -108f)]
        [TestCase(0f, 0f)]
        [TestCase(999f, 252f)]
        public void TransformerAngle_ClampsToOpenAndClosedLimits(float input, float expected)
        {
            Assert.That(ValveWheelGrabTransformer.ClampAngle(input, -108f, 252f),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void AtEitherLimit_OutwardDeltaStaysClampedAndReverseDeltaMovesImmediately()
        {
            float openLimit = ValveWheelGrabTransformer.ClampAngle(-108f - 5f, -108f, 252f);
            float leaveOpenLimit = ValveWheelGrabTransformer.ClampAngle(openLimit + 5f, -108f, 252f);
            float closeLimit = ValveWheelGrabTransformer.ClampAngle(252f + 5f, -108f, 252f);
            float leaveCloseLimit = ValveWheelGrabTransformer.ClampAngle(closeLimit - 5f, -108f, 252f);

            Assert.That(openLimit, Is.EqualTo(-108f));
            Assert.That(leaveOpenLimit, Is.EqualTo(-103f));
            Assert.That(closeLimit, Is.EqualTo(252f));
            Assert.That(leaveCloseLimit, Is.EqualTo(247f));
        }

        [Test]
        public void CustomOpenAndCloseLimitsMapToZeroAndOneHundredPercent()
        {
            Assert.That(ValveWheelClosureController.CalculateClosurePercent(30f, -45f, 45f, 90f),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(ValveWheelClosureController.CalculateClosurePercent(30f, 90f, 45f, 90f),
                Is.EqualTo(100f).Within(0.0001f));
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        public void MaximumHandsTwo_RejectsThirdInteractor(int selectedHands, bool expected)
        {
            Assert.That(ValveWheelGrabTransformer.CanAcceptAdditionalHand(selectedHands, 2), Is.EqualTo(expected));
        }

        [TestCase(0f, true)]
        [TestCase(0.02f, true)]
        [TestCase(0.035f, true)]
        [TestCase(0.08f, true)]
        [TestCase(0.15f, true)]
        [TestCase(0.151f, false)]
        [TestCase(float.NaN, false)]
        [TestCase(float.PositiveInfinity, false)]
        public void SurfaceDistance_UsesInclusiveFifteenCentimeterLimit(float distance, bool expected)
        {
            Assert.That(ValveWheelGrabTransformer.IsSurfaceDistanceAllowed(distance, 0.15f),
                Is.EqualTo(expected));
        }

        [Test]
        public void SurfaceDistance_UsesClosestPointAndAllowsPointsInsideCollider()
        {
            GameObject gameObject = new GameObject("Valve surface proximity test");
            try
            {
                CapsuleCollider collider = gameObject.AddComponent<CapsuleCollider>();
                collider.radius = 0.1f;
                collider.height = 0.2f;

                Assert.That(ValveWheelGrabTransformer.TryGetMinimumSurfaceDistance(
                    Vector3.zero, new Collider[] { collider }, out Collider nearest,
                    out float distance), Is.True);
                Assert.That(nearest, Is.SameAs(collider));
                Assert.That(distance, Is.EqualTo(0f).Within(0.000001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SurfaceDistance_IgnoresDisabledAndInactiveColliders()
        {
            GameObject disabledObject = new GameObject("Disabled RimCollider");
            GameObject inactiveObject = new GameObject("Inactive RimCollider");
            try
            {
                CapsuleCollider disabledCollider = disabledObject.AddComponent<CapsuleCollider>();
                disabledCollider.enabled = false;
                CapsuleCollider inactiveCollider = inactiveObject.AddComponent<CapsuleCollider>();
                inactiveObject.SetActive(false);

                Assert.That(ValveWheelGrabTransformer.TryGetMinimumSurfaceDistance(
                    Vector3.zero, new Collider[] { null, disabledCollider, inactiveCollider },
                    out Collider nearest, out float distance), Is.False);
                Assert.That(nearest, Is.Null);
                Assert.That(distance, Is.EqualTo(float.PositiveInfinity));
            }
            finally
            {
                Object.DestroyImmediate(disabledObject);
                Object.DestroyImmediate(inactiveObject);
            }
        }

        [Test]
        public void ConstrainedPose_KeepsCenterAndOnlyAddsLocalZRotation()
        {
            Vector3 center = new Vector3(3f, -2f, 7f);
            Quaternion startRotation = Quaternion.Euler(23f, -17f, 41f);
            Vector3 worldAxis = startRotation * Vector3.forward;

            Pose result = ValveWheelGrabTransformer.CalculateConstrainedPose(
                center, startRotation, worldAxis, 65f);
            Quaternion expectedRotation = startRotation * Quaternion.AngleAxis(65f, Vector3.forward);

            Assert.That(Vector3.Distance(result.position, center), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(result.rotation, expectedRotation), Is.LessThan(0.0001f));
        }

        [Test]
        public void ConstrainedPose_RepeatedProcessPhasesUseSameAbsoluteInitialReference()
        {
            Vector3 center = new Vector3(-1.5f, 1.25f, -2.9f);
            Quaternion initialRotation = Quaternion.Euler(12f, 34f, 56f);
            Vector3 worldAxis = initialRotation * Vector3.forward;

            Pose dynamicPose = ValveWheelGrabTransformer.CalculateConstrainedPose(
                center, initialRotation, worldAxis, -108f);
            Pose fixedPose = ValveWheelGrabTransformer.CalculateConstrainedPose(
                center, initialRotation, worldAxis, -108f);
            Pose beforeRenderPose = ValveWheelGrabTransformer.CalculateConstrainedPose(
                center, initialRotation, worldAxis, -108f);

            Assert.That(Vector3.Distance(dynamicPose.position, center), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(dynamicPose.rotation, fixedPose.rotation), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(dynamicPose.rotation, beforeRenderPose.rotation), Is.LessThan(0.0001f));
        }

        [Test]
        public void OpenAndClosedEndpoints_KeepDistinctLogicalAnglesDespiteEquivalentQuaternion()
        {
            Pose openPose = ValveWheelGrabTransformer.CalculateConstrainedPose(
                Vector3.zero, Quaternion.identity, Vector3.forward, -108f);
            Pose closedPose = ValveWheelGrabTransformer.CalculateConstrainedPose(
                Vector3.zero, Quaternion.identity, Vector3.forward, 252f);

            Assert.That(Quaternion.Angle(openPose.rotation, closedPose.rotation), Is.LessThan(0.0001f));
            Assert.That(252f - (-108f), Is.EqualTo(360f));
            Assert.That(ValveWheelClosureController.CalculateClosurePercent(30f, -108f, 108f, 252f),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(ValveWheelClosureController.CalculateClosurePercent(30f, 252f, 108f, 252f),
                Is.EqualTo(100f).Within(0.0001f));
        }

        [Test]
        public void SampleScene_ValveTracksRotationButNotPosition()
        {
            string scene = ReadSampleScene();
            string interactableBlock = FindWheelComponentBlock(scene,
                "UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable");

            StringAssert.Contains("m_Enabled: 1", interactableBlock);
            StringAssert.Contains("m_MovementType: 1", interactableBlock);
            StringAssert.Contains("m_TrackPosition: 0", interactableBlock);
            StringAssert.Contains("m_TrackRotation: 1", interactableBlock);
            StringAssert.Contains("m_TrackScale: 0", interactableBlock);
            StringAssert.Contains("m_ThrowOnDetach: 0", interactableBlock);
            StringAssert.Contains("m_AddDefaultGrabTransformers: 0", interactableBlock);
        }

        [Test]
        public void SampleScene_ValvePivotAndHingeAnchorsShareTheSameCenter()
        {
            string scene = ReadSampleScene();
            string hingeBlock = FindWheelComponentBlock(scene, "HingeJoint:", "--- !u!59");

            StringAssert.Contains("m_Anchor: {x: 0, y: 0, z: 0}", hingeBlock);
            StringAssert.Contains("m_ConnectedAnchor: {x: 0, y: 0, z: 0}", hingeBlock);
            StringAssert.Contains("m_Axis: {x: 0, y: 0, z: 1}", hingeBlock);
            Assert.That(Regex.Matches(scene, "(?m)^  m_GameObject: \\{fileID: 1084262770\\}$").Count,
                Is.EqualTo(9), "The wheel should retain one Transform plus eight active non-Transform components after legacy hand-pose support is removed.");
            Assert.That(Regex.Matches(scene,
                    "(?ms)^--- !u!54 &\\d+\\r?\\n(?:(?!^--- !u!).)*m_GameObject: \\{fileID: 1084262770\\}").Count,
                Is.EqualTo(1), "The wheel must keep exactly one Rigidbody.");
        }

        [Test]
        public void SampleScene_ValveHasNoInstanceHighlightButGrabRemainsEnabled()
        {
            string scene = ReadSampleScene();
            Match wheelGameObject = Regex.Match(scene,
                "(?ms)^--- !u!1 &1084262770\\r?\\n(?:(?!^--- !u!).)*");
            Assert.That(wheelGameObject.Success, Is.True);
            StringAssert.Contains("m_Layer: 0", wheelGameObject.Value);
            Assert.That(Regex.IsMatch(scene,
                    "(?ms)^--- !u!114 &\\d+\\r?\\n(?:(?!^--- !u!).)*m_GameObject: \\{fileID: 1084262770\\}(?:(?!^--- !u!).)*ProcedureHighlight"),
                Is.False);

            string interactableBlock = FindWheelComponentBlock(scene,
                "UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable");
            StringAssert.Contains("m_Enabled: 1", interactableBlock);
            StringAssert.Contains("m_InteractionLayers:", interactableBlock);
            StringAssert.Contains("m_Bits: 1", interactableBlock);
        }

        [Test]
        public void SampleScene_ValveGrabUsesAllWheelRimCollidersAndFixedAttach()
        {
            string scene = ReadSampleScene();
            string transformerBlock = FindWheelComponentBlock(scene,
                "VRProject.Valves.ValveWheelGrabTransformer");
            StringAssert.Contains("maxGrabSurfaceDistance: 0.15", transformerBlock);

            string interactableBlock = FindWheelComponentBlock(scene,
                "UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable");
            StringAssert.Contains("m_SelectMode: 1", interactableBlock);
            StringAssert.Contains("m_StartingSingleGrabTransformers: []", interactableBlock);
            StringAssert.Contains("m_StartingMultipleGrabTransformers:", interactableBlock);
            StringAssert.Contains("- {fileID: 1084262779}", interactableBlock);
            StringAssert.Contains("m_AttachTransform: {fileID: 2146000111}", interactableBlock);
            Match colliderList = Regex.Match(interactableBlock,
                "(?ms)^  m_Colliders:\\r?\\n(?<items>(?:  - \\{fileID: \\d+\\}\\r?\\n)+)");
            Assert.That(colliderList.Success, Is.True);
            Assert.That(Regex.Matches(colliderList.Groups["items"].Value, "fileID:").Count,
                Is.EqualTo(10));
            StringAssert.Contains("- {fileID: 1862715519}", colliderList.Groups["items"].Value);
            StringAssert.Contains("- {fileID: 935302004}", colliderList.Groups["items"].Value);
            StringAssert.Contains("m_Name: RimCollider_01", scene);
            StringAssert.Contains("m_Name: RimCollider_10", scene);
            StringAssert.Contains("m_Name: CenterShaftAttach", scene);
        }

        static string ReadSampleScene() => File.ReadAllText(
            Path.Combine(Application.dataPath, "Scenes", "06_LeakResponse.unity"));

        static string FindWheelComponentBlock(string scene, string identifier, string header = "--- !u!114")
        {
            Match match = Regex.Match(scene,
                $"(?ms)^{Regex.Escape(header)} &\\d+\\r?\\n" +
                $"(?=(?:(?!^--- !u!).)*m_GameObject: \\{{fileID: 1084262770\\}})" +
                $"(?=(?:(?!^--- !u!).)*{Regex.Escape(identifier)})(?:(?!^--- !u!).)*");
            Assert.That(match.Success, Is.True, $"Could not find component block containing '{identifier}'.");
            return match.Value;
        }

        [Test]
        public void Awake_ClearsOnlyStartupRigidBodyVelocity()
        {
            GameObject gameObject = new GameObject("Valve startup velocity test");
            try
            {
                Rigidbody body = gameObject.AddComponent<Rigidbody>();
                HingeJoint hinge = gameObject.AddComponent<HingeJoint>();
                gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                ValveWheelClosureController controller = gameObject.AddComponent<ValveWheelClosureController>();
                body.linearVelocity = new Vector3(1f, 2f, 3f);
                body.angularVelocity = new Vector3(3f, 2f, 1f);

                typeof(ValveWheelClosureController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, null);

                Assert.That(body.linearVelocity.sqrMagnitude, Is.LessThan(0.0000001f));
                Assert.That(body.angularVelocity.sqrMagnitude, Is.LessThan(0.0000001f));
                Assert.That(hinge.useMotor, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void BoundaryEvents_FireOncePerEntry()
        {
            GameObject gameObject = new GameObject("Valve test");
            try
            {
                ValveWheelClosureController controller = gameObject.AddComponent<ValveWheelClosureController>();
                typeof(ValveWheelClosureController).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, null);
                int opened = 0;
                int closed = 0;
                controller.OnFullyOpened.AddListener(() => opened++);
                controller.OnFullyClosed.AddListener(() => closed++);

                controller.SetClosurePercent(0f);
                controller.SetClosurePercent(0f);
                controller.SetClosurePercent(100f);
                controller.SetClosurePercent(100f);

                Assert.That(opened, Is.EqualTo(1));
                Assert.That(closed, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void FullyClosedEvent_FiresBeforeLatchAndLatchBlocksReopen()
        {
            GameObject gameObject = new GameObject("Valve closed latch test");
            try
            {
                ValveWheelClosureController controller = gameObject.AddComponent<ValveWheelClosureController>();
                typeof(ValveWheelClosureController).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, null);
                bool latchStateInsideEvent = true;
                controller.OnFullyClosed.AddListener(() => latchStateInsideEvent = controller.IsClosedLatched);

                controller.SetClosurePercent(100f);
                controller.SetClosurePercent(0f);

                Assert.That(latchStateInsideEvent, Is.False);
                Assert.That(controller.IsClosedLatched, Is.True);
                Assert.That(controller.ClosurePercent, Is.EqualTo(100f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void TransformerClosedLatch_PinsPhysicalWheelAtTheClosedEndpoint()
        {
            GameObject gameObject = new GameObject("Valve transformer closed latch test");
            try
            {
                gameObject.AddComponent<Rigidbody>();
                gameObject.AddComponent<HingeJoint>();
                gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                ValveWheelGrabTransformer transformer = gameObject.AddComponent<ValveWheelGrabTransformer>();
                transformer.ConfigureTravelLimits(0f, 504f);
                transformer.ConfigureCloseConvention(-1f);

                transformer.SetClosedLatched(true);

                Assert.That(transformer.IsClosedLatched, Is.True);
                Assert.That(transformer.CurrentLogicalClosureAngle, Is.EqualTo(504f).Within(0.0001f));
                Assert.That(ValveWheelClosureController.CalculateClosurePercent(
                    0f, transformer.CurrentLogicalClosureAngle, 0f, 504f),
                    Is.EqualTo(100f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Configurator_IsSelectionBasedWithoutProductionMenuAndHasNoLegacyGlobalEntryPoints()
        {
            string source = ReadConfigurator();

            StringAssert.DoesNotContain("[MenuItem", source);
            StringAssert.Contains("Selection.activeGameObject", source);
            StringAssert.Contains("Selection.gameObjects", source);
            StringAssert.DoesNotContain("Configure Suction Valve Wheel", source);
            StringAssert.DoesNotContain("ConfigureAndSaveBatch", source);
            StringAssert.DoesNotContain("OpenScene(", source);
            StringAssert.DoesNotContain("SaveScene(", source);
            StringAssert.DoesNotContain("GameObject.Find", source);
        }

        [Test]
        public void Configurator_DoesNotReferenceHandPoseOrSceneWideRigObjects()
        {
            string source = ReadConfigurator();
            string[] forbiddenTokens =
            {
                "LeftHand",
                "RightHand",
                "ControllerHandGripAnimator",
                "VRHandPoseTool_VisualOffset",
                "AnimatorController",
                "Avatar",
                "AssetDatabase",
                "FindLoadedSceneObjects",
                "FindSceneComponents",
                "NearFarInteractor",
            };

            foreach (string token in forbiddenTokens)
                StringAssert.DoesNotContain(token, source, $"Unexpected rig/asset dependency: {token}");
        }

        [Test]
        public void Configurator_DeclaresIdempotentRimGrabAndSingleUndoConfiguration()
        {
            string source = ReadConfigurator();

            StringAssert.Contains("const string CenterShaftColliderName", source);
            StringAssert.Contains("ConfigureCenterShaftCollider", source);
            StringAssert.Contains("ConfigureRimColliders", source);
            StringAssert.Contains("grab.colliders.Add(rimColliders[index])", source);
            StringAssert.Contains("Undo.RegisterCreatedObjectUndo", source);
            StringAssert.Contains("Undo.AddComponent", source);
            StringAssert.Contains("Undo.RecordObject", source);
            StringAssert.Contains("Undo.SetTransformParent", source);
            StringAssert.Contains("Undo.CollapseUndoOperations", source);
            StringAssert.Contains("Undo.RevertAllDownToGroup", source);
            StringAssert.Contains("EditorSceneManager.MarkSceneDirty", source);
            StringAssert.DoesNotContain("EditorSceneManager.SaveScene", source);
        }

        static string ReadConfigurator() => File.ReadAllText(
            Path.Combine(Application.dataPath, "3.SSH", "Editor", "ValveSystem", "ValveWheelSceneConfigurator.cs"));
    }
}
