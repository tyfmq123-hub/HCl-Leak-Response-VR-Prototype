using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace VRProject.Valves
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody), typeof(HingeJoint))]
    public sealed class ValveWheelGrabTransformer : XRBaseGrabTransformer, IXRDropTransformer, IXRSelectFilter
    {
        public enum HandMode
        {
            None,
            SingleHand,
            TwoHand,
        }

        const float VectorEpsilon = 0.000001f;
        const float AngleEpsilon = 0.0001f;
        const int SupportedHandCapacity = 2;
        const float RotationLogCooldown = 0.25f;

        [Header("Wheel")]
        [SerializeField] new HingeJoint hingeJoint;
        [SerializeField, Min(0.001f)] float minimumHandRadius = 0.02f;
        [SerializeField, Range(1f, 179f)] float maximumAngularStep = 90f;

        [Header("Rotation")]
        [Tooltip("Legacy serialized value. One-hand rotation is disabled; two distinct hands are required.")]
        [SerializeField, HideInInspector, Min(0f)] float oneHandRotationGain;
        [SerializeField, Min(0f)] float twoHandRotationGain = 1f;
        [Tooltip("Minimum angular movement required from each hand while in two-hand mode.")]
        [SerializeField, Min(0f)] float twoHandMinimumDeltaDegrees = 0.02f;

        [Header("Limits")]
        [Tooltip("Maximum counter-clockwise rotation from the initial wheel pose.")]
        [SerializeField, Min(0f)] float maxOpenRotationDegrees = 108f;
        [Tooltip("Maximum clockwise rotation from the initial wheel pose.")]
        [SerializeField, Min(0f)] float maxCloseRotationDegrees = 252f;

        [Header("Close Convention (Runtime)")]
        [Tooltip("Physical hinge-angle sign that means closing. Configured from valve front, hinge axis, and Close Direction.")]
        [SerializeField, HideInInspector] float closeAngleSign = 1f;
        [SerializeField, HideInInspector] bool closedLatched;

        [Header("Selection")]
        [SerializeField, Range(1, SupportedHandCapacity)] int maximumHands = SupportedHandCapacity;

        [Header("Surface Proximity")]
        [SerializeField, Min(0f)] float maxGrabSurfaceDistance = 0.15f;

        readonly IXRSelectInteractor[] activeInteractors = new IXRSelectInteractor[SupportedHandCapacity];
        readonly Vector3[] previousHandDirections = new Vector3[SupportedHandCapacity];
        readonly bool[] hasPreviousHandDirection = new bool[SupportedHandCapacity];
        readonly float[] handDeltaAngles = new float[SupportedHandCapacity];
        readonly bool[] handDeltaValid = new bool[SupportedHandCapacity];
        readonly Vector3[] currentHandPositions = new Vector3[SupportedHandCapacity];
        readonly Vector3[] currentProjectedHandDirections = new Vector3[SupportedHandCapacity];

        Rigidbody wheelRigidbody;
        Vector3 fixedWheelCenterWorld;
        Vector3 initialHingeWorldAxis;
        Quaternion initialWheelWorldRotation;
        float targetHingeAngle;
        float minimumHingeAngle;
        float maximumHingeAngle;
        bool constraintReferenceInitialized;
        int activeHandCount;
        bool grabIsValid;
        int lastProcessFrame = -1;
        bool lastProcessHadSelection;
        int lastDeltaFrame = -1;
        float lastAppliedDeltaAngle;
        int lastEvaluationFrame = -1;
        float lastCombinedDeltaAngle;
        bool lastCombinedDeltaValid;
        string lastTwoHandReason = "MODE_TRANSITION";
        float lastTargetAngleBefore;
        float lastTargetAngleAfter;
        Quaternion lastOutputRotation = Quaternion.identity;
        string lastLimitHit = "NONE";
        float nextRotationLogTime;
        bool pendingPhysicsSample;
        int pendingPhysicsFixedUpdates;
        Quaternion pendingWheelRotationBefore;
        Quaternion pendingRigidbodyRotationBefore;
        float pendingHingeAngleBefore;
        Quaternion pendingOutputTargetRotation;

        // This component is explicitly registered in the Multiple list. XRI 3.4.1 also
        // runs that list for a single selection when the Single list is empty.
        protected override RegistrationMode registrationMode => RegistrationMode.None;

        public bool canProcessOnDrop => false;
        public bool IsGrabProcessActive => grabIsValid && HasRequiredHandPair();
        public int CurrentHandCount => activeHandCount;
        public HandMode CurrentMode => ModeForHandCount(activeHandCount);
        public float CurrentRotationGain => GainForHandCount(activeHandCount, oneHandRotationGain, twoHandRotationGain);
        public float OneHandRotationGain => oneHandRotationGain;
        public float TwoHandRotationGain => twoHandRotationGain;
        public float TwoHandMinimumDeltaDegrees => twoHandMinimumDeltaDegrees;
        public int MaximumHands => maximumHands;
        public float MaxGrabSurfaceDistance => maxGrabSurfaceDistance;
        public float MaxOpenRotationDegrees => maxOpenRotationDegrees;
        public float MaxCloseRotationDegrees => maxCloseRotationDegrees;
        public float CloseAngleSign => closeAngleSign;
        public float CurrentLogicalClosureAngle => targetHingeAngle * closeAngleSign;
        public bool IsClosedLatched => closedLatched;
        public int LastProcessFrame => lastProcessFrame;
        public bool LastProcessHadSelection => lastProcessHadSelection;
        public bool ProcessedThisFrame => lastProcessFrame == Time.frameCount;
        public int LastDeltaFrame => lastDeltaFrame;
        public float LastAppliedDeltaAngle => lastAppliedDeltaAngle;
        public int LastEvaluationFrame => lastEvaluationFrame;
        public float LastCombinedDeltaAngle => lastCombinedDeltaAngle;
        public bool LastCombinedDeltaValid => lastCombinedDeltaValid;
        public string LastTwoHandReason => lastTwoHandReason;
        public float LastTargetAngleBefore => lastTargetAngleBefore;
        public float LastTargetAngleAfter => lastTargetAngleAfter;
        public Quaternion LastOutputRotation => lastOutputRotation;
        public Quaternion InitialWheelWorldRotation => initialWheelWorldRotation;
        public Vector3 FixedWheelCenterWorld => fixedWheelCenterWorld;
        public Vector3 InitialHingeWorldAxis => initialHingeWorldAxis;
        public bool ConstraintReferenceInitialized => constraintReferenceInitialized;
        public float CurrentTargetAngle => targetHingeAngle;
        public string LastLimitHit => lastLimitHit;
        public float GetLastRawHandDelta(int index) => index >= 0 && index < activeHandCount ? handDeltaAngles[index] : 0f;
        public bool GetLastRawHandDeltaValid(int index) => index >= 0 && index < activeHandCount && handDeltaValid[index];
        public string GetActiveInteractorName(int index) => index >= 0 && index < activeHandCount &&
            activeInteractors[index] is Component component
            ? component.transform.name
            : "NONE";

        public override void OnLink(XRGrabInteractable grabInteractable)
        {
            base.OnLink(grabInteractable);
            ResolveReferences(grabInteractable);
            ApplyConfiguredJointLimits();
            InitializeConstraintReference(grabInteractable);
        }

        public override void OnGrab(XRGrabInteractable grabInteractable)
        {
            ResolveReferences(grabInteractable);
            ResetGrabState();

            if (hingeJoint == null || grabInteractable == null || grabInteractable.interactorsSelecting.Count == 0)
                return;

            if (!TryGetConfiguredLimits(out minimumHingeAngle, out maximumHingeAngle))
            {
                Debug.LogError($"[{nameof(ValveWheelGrabTransformer)}] Cannot grab '{name}': configured open/close limits must be finite and non-negative.", this);
                return;
            }

            if (!constraintReferenceInitialized && !InitializeConstraintReference(grabInteractable))
            {
                Debug.LogError($"[{nameof(ValveWheelGrabTransformer)}] Cannot grab '{name}': the fixed wheel reference is invalid.", this);
                return;
            }

            targetHingeAngle = Mathf.Clamp(targetHingeAngle, minimumHingeAngle, maximumHingeAngle);
            grabIsValid = IsFinite(fixedWheelCenterWorld) && IsFinite(initialWheelWorldRotation) &&
                IsFinite(initialHingeWorldAxis);

            if (!grabIsValid)
                return;

            RecaptureSelectedHands(grabInteractable);
            ClearRigidbodyVelocity();
        }

        public override void OnGrabCountChanged(XRGrabInteractable grabInteractable, Pose targetPose, Vector3 localScale)
        {
            if (!grabIsValid || grabInteractable == null || grabInteractable.interactorsSelecting.Count == 0)
                return;

            if (closedLatched)
            {
                targetHingeAngle = ClosedPhysicalAngle;
                lastTargetAngleBefore = targetHingeAngle;
                lastTargetAngleAfter = targetHingeAngle;
                lastAppliedDeltaAngle = 0f;
                lastLimitHit = "CLOSED";
                targetPose = CurrentConstrainedPose();
                lastOutputRotation = targetPose.rotation;
                return;
            }

            // A 1->2 or 2->1 transition deliberately establishes a fresh baseline.
            // No angle is applied on this transition frame.
            RecaptureSelectedHands(grabInteractable);
            lastAppliedDeltaAngle = 0f;
            lastCombinedDeltaAngle = 0f;
            lastCombinedDeltaValid = false;
            lastTwoHandReason = "MODE_TRANSITION";
            lastEvaluationFrame = Time.frameCount;
        }

        public override void Process(XRGrabInteractable grabInteractable,
            XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
        {
            lastProcessFrame = Time.frameCount;
            lastProcessHadSelection = grabInteractable != null && grabInteractable.interactorsSelecting.Count > 0;
            lastAppliedDeltaAngle = 0f;

            if (!grabIsValid || grabInteractable == null || grabInteractable.interactorsSelecting.Count == 0)
                return;

            if (closedLatched)
            {
                targetHingeAngle = ClosedPhysicalAngle;
                lastTargetAngleBefore = targetHingeAngle;
                lastTargetAngleAfter = targetHingeAngle;
                lastAppliedDeltaAngle = 0f;
                lastCombinedDeltaAngle = 0f;
                lastCombinedDeltaValid = false;
                lastLimitHit = "CLOSED";
                targetPose = CurrentConstrainedPose();
                lastOutputRotation = targetPose.rotation;
                return;
            }

            if (!HandSetMatches(grabInteractable))
            {
                RecaptureSelectedHands(grabInteractable);
                targetPose = CurrentConstrainedPose();
                return;
            }

            for (int i = 0; i < activeHandCount; ++i)
                CaptureHandDelta(grabInteractable, i);

            float combinedDelta = 0f;
            bool hasCombinedDelta = false;
            if (activeHandCount < SupportedHandCapacity)
            {
                lastTwoHandReason = "WAITING_FOR_BOTH_HANDS";
            }
            else if (!HasRequiredHandPair())
            {
                lastTwoHandReason = "LEFT_AND_RIGHT_REQUIRED";
            }
            else
            {
                hasCombinedDelta = TryCombineHandDeltas(
                    handDeltaAngles[0], handDeltaValid[0],
                    handDeltaAngles[1], handDeltaValid[1],
                    twoHandMinimumDeltaDegrees,
                    out combinedDelta, out lastTwoHandReason);
            }

            lastEvaluationFrame = Time.frameCount;
            lastCombinedDeltaAngle = combinedDelta;
            lastCombinedDeltaValid = hasCombinedDelta;

            float appliedDelta = 0f;
            lastTargetAngleBefore = targetHingeAngle;
            if (hasCombinedDelta)
            {
                float gainedDelta = ApplyRotationGain(combinedDelta, activeHandCount,
                    oneHandRotationGain, twoHandRotationGain, false);
                if (float.IsFinite(gainedDelta) && Mathf.Abs(gainedDelta) > AngleEpsilon)
                {
                    float previousTargetHingeAngle = targetHingeAngle;
                    targetHingeAngle = ClampAngle(previousTargetHingeAngle + gainedDelta,
                        minimumHingeAngle, maximumHingeAngle);
                    appliedDelta = targetHingeAngle - previousTargetHingeAngle;
                    lastAppliedDeltaAngle = appliedDelta;
                    if (Mathf.Abs(appliedDelta) > AngleEpsilon)
                        lastDeltaFrame = Time.frameCount;
                }
            }
            lastTargetAngleAfter = targetHingeAngle;

            float logicalClosureAngle = targetHingeAngle * closeAngleSign;
            lastLimitHit = Mathf.Abs(logicalClosureAngle + maxOpenRotationDegrees) <= AngleEpsilon
                ? "OPEN"
                : Mathf.Abs(logicalClosureAngle - maxCloseRotationDegrees) <= AngleEpsilon ? "CLOSED" : "NONE";

            Pose inputTargetPose = targetPose;
            Quaternion wheelRotationBefore = transform.rotation;
            Quaternion rigidbodyRotationBefore = wheelRigidbody != null ? wheelRigidbody.rotation : wheelRotationBefore;
            float hingeAngleBefore = hingeJoint != null && float.IsFinite(hingeJoint.angle) ? hingeJoint.angle : 0f;

            targetPose = CurrentConstrainedPose();
            lastOutputRotation = targetPose.rotation;
            TryLogRotationApplication(grabInteractable, updatePhase, appliedDelta, inputTargetPose,
                targetPose, wheelRotationBefore, rigidbodyRotationBefore, hingeAngleBefore);
            // Intentionally leave localScale unchanged.
        }

        public void OnDrop(XRGrabInteractable grabInteractable, DropEventArgs args)
        {
            ResolveReferences(grabInteractable);
            ClearRigidbodyVelocity();
            ResetGrabState();
        }

        bool IXRSelectFilter.Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (!(interactable is XRGrabInteractable grabInteractable) ||
                !ReferenceEquals(grabInteractable.gameObject, gameObject))
                return true;

            for (int i = 0; i < grabInteractable.interactorsSelecting.Count; ++i)
            {
                if (ReferenceEquals(grabInteractable.interactorsSelecting[i], interactor))
                    return true;
            }

            if (grabInteractable.interactorsSelecting.Count == 1 &&
                !HasRequiredHandPair(
                    HandSortKey(grabInteractable.interactorsSelecting[0]),
                    HandSortKey(interactor)))
            {
                return false;
            }

            // XRI already requires this interactable's rim Collider to be a valid
            // selection target. Rechecking the controller attach point against that
            // Collider rejects legitimate direct grabs because the rendered hand can
            // touch the rim while its tracking origin remains farther away.
            return CanAcceptAdditionalHand(
                grabInteractable.interactorsSelecting.Count, maximumHands);
        }

        public bool TryEvaluateSurfaceProximity(IXRSelectInteractor interactor,
            XRGrabInteractable grabInteractable, out Vector3 attachWorldPosition,
            out Collider nearestCollider, out float minimumSurfaceDistance)
        {
            attachWorldPosition = Vector3.zero;
            nearestCollider = null;
            minimumSurfaceDistance = float.PositiveInfinity;
            if (interactor == null || grabInteractable == null)
                return false;

            Transform attachTransform = interactor.GetAttachTransform(grabInteractable);
            if (attachTransform == null || !IsFinite(attachTransform.position))
                return false;

            attachWorldPosition = attachTransform.position;
            return TryGetMinimumSurfaceDistance(attachWorldPosition, grabInteractable.colliders,
                    out nearestCollider, out minimumSurfaceDistance) &&
                IsSurfaceDistanceAllowed(minimumSurfaceDistance, maxGrabSurfaceDistance);
        }

        public static bool TryGetMinimumSurfaceDistance(Vector3 attachWorldPosition,
            IReadOnlyList<Collider> colliders, out Collider nearestCollider,
            out float minimumSurfaceDistance)
        {
            nearestCollider = null;
            minimumSurfaceDistance = float.PositiveInfinity;
            if (!IsFinite(attachWorldPosition) || colliders == null)
                return false;

            for (int i = 0; i < colliders.Count; ++i)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                    continue;

                Vector3 closestPoint = collider.ClosestPoint(attachWorldPosition);
                if (!IsFinite(closestPoint))
                    continue;

                float surfaceDistance = Vector3.Distance(attachWorldPosition, closestPoint);
                if (!float.IsFinite(surfaceDistance) || surfaceDistance < 0f ||
                    surfaceDistance >= minimumSurfaceDistance)
                    continue;

                nearestCollider = collider;
                minimumSurfaceDistance = surfaceDistance;
            }

            return nearestCollider != null;
        }

        public static bool IsSurfaceDistanceAllowed(float minimumSurfaceDistance,
            float maximumSurfaceDistance) =>
            float.IsFinite(minimumSurfaceDistance) && minimumSurfaceDistance >= 0f &&
            float.IsFinite(maximumSurfaceDistance) && maximumSurfaceDistance >= 0f &&
            minimumSurfaceDistance <= maximumSurfaceDistance;

        void CaptureHandDelta(XRGrabInteractable grabInteractable, int index)
        {
            handDeltaValid[index] = false;
            handDeltaAngles[index] = 0f;
            currentHandPositions[index] = Vector3.zero;
            currentProjectedHandDirections[index] = Vector3.zero;

            IXRSelectInteractor interactor = activeInteractors[index];
            Transform handAttach = interactor?.GetAttachTransform(grabInteractable);
            if (handAttach == null)
                return;

            currentHandPositions[index] = handAttach.position;
            if (!TryProjectHandDirection(
                    fixedWheelCenterWorld, currentHandPositions[index], initialHingeWorldAxis, minimumHandRadius,
                    out Vector3 currentDirection))
            {
                // A hand can briefly cross the wheel center or lose tracking. Discard the
                // old baseline so tracking recovery cannot apply a large catch-up rotation.
                hasPreviousHandDirection[index] = false;
                return;
            }

            currentProjectedHandDirections[index] = currentDirection;

            if (!hasPreviousHandDirection[index])
            {
                previousHandDirections[index] = currentDirection;
                hasPreviousHandDirection[index] = true;
                return;
            }

            if (!TryCalculateSignedAngle(previousHandDirections[index], currentDirection, initialHingeWorldAxis,
                    maximumAngularStep, out float deltaAngle))
            {
                // Re-seed after an outlier. Keeping a stale direction here makes a later
                // valid frame jump by the whole angle accumulated while tracking was invalid.
                previousHandDirections[index] = currentDirection;
                hasPreviousHandDirection[index] = true;
                return;
            }

            previousHandDirections[index] = currentDirection;
            handDeltaAngles[index] = deltaAngle;
            handDeltaValid[index] = true;
        }

        void RecaptureSelectedHands(XRGrabInteractable grabInteractable)
        {
            ClearHandState();
            if (grabInteractable == null)
                return;

            activeHandCount = Mathf.Min(grabInteractable.interactorsSelecting.Count,
                Mathf.Clamp(maximumHands, 1, SupportedHandCapacity));
            for (int i = 0; i < activeHandCount; ++i)
                activeInteractors[i] = grabInteractable.interactorsSelecting[i];

            if (activeHandCount == 2 && HandSortKey(activeInteractors[1]) < HandSortKey(activeInteractors[0]))
                (activeInteractors[0], activeInteractors[1]) = (activeInteractors[1], activeInteractors[0]);

            for (int i = 0; i < activeHandCount; ++i)
            {
                Transform handAttach = activeInteractors[i]?.GetAttachTransform(grabInteractable);
                hasPreviousHandDirection[i] = handAttach != null && TryProjectHandDirection(
                    fixedWheelCenterWorld, handAttach.position, initialHingeWorldAxis, minimumHandRadius,
                    out previousHandDirections[i]);
            }
        }

        bool HandSetMatches(XRGrabInteractable grabInteractable)
        {
            int expectedCount = Mathf.Min(grabInteractable.interactorsSelecting.Count,
                Mathf.Clamp(maximumHands, 1, SupportedHandCapacity));
            if (expectedCount != activeHandCount)
                return false;

            for (int i = 0; i < activeHandCount; ++i)
            {
                bool found = false;
                for (int selectedIndex = 0; selectedIndex < grabInteractable.interactorsSelecting.Count; ++selectedIndex)
                    found |= ReferenceEquals(activeInteractors[i], grabInteractable.interactorsSelecting[selectedIndex]);
                if (!found)
                    return false;
            }

            return true;
        }

        Pose CurrentConstrainedPose() => CalculateConstrainedPose(
            fixedWheelCenterWorld, initialWheelWorldRotation, initialHingeWorldAxis, targetHingeAngle);

        void ClearRigidbodyVelocity()
        {
            if (wheelRigidbody == null)
                return;

            wheelRigidbody.linearVelocity = Vector3.zero;
            wheelRigidbody.angularVelocity = Vector3.zero;
        }

        void ResetGrabState()
        {
            ClearHandState();
            grabIsValid = false;
            lastAppliedDeltaAngle = 0f;
            lastCombinedDeltaAngle = 0f;
            lastCombinedDeltaValid = false;
            lastLimitHit = "NONE";
        }

        void ClearHandState()
        {
            activeHandCount = 0;
            for (int i = 0; i < SupportedHandCapacity; ++i)
            {
                activeInteractors[i] = null;
                previousHandDirections[i] = Vector3.zero;
                hasPreviousHandDirection[i] = false;
                handDeltaAngles[i] = 0f;
                handDeltaValid[i] = false;
                currentHandPositions[i] = Vector3.zero;
                currentProjectedHandDirections[i] = Vector3.zero;
            }
        }

        void TryLogRotationApplication(XRGrabInteractable grabInteractable,
            XRInteractionUpdateOrder.UpdatePhase updatePhase, float appliedDelta, Pose inputTargetPose,
            Pose outputTargetPose, Quaternion wheelRotationBefore, Quaternion rigidbodyRotationBefore,
            float hingeAngleBefore)
        {
            if (Mathf.Abs(appliedDelta) <= AngleEpsilon || Time.unscaledTime < nextRotationLogTime)
                return;

            nextRotationLogTime = Time.unscaledTime + RotationLogCooldown;
            Vector3 sourceHandPosition = activeHandCount > 0 ? currentHandPositions[0] : Vector3.zero;
            Vector3 projectedHandVector = activeHandCount > 0 ? currentProjectedHandDirections[0] : Vector3.zero;
            bool targetPoseChanged = Quaternion.Angle(inputTargetPose.rotation, outputTargetPose.rotation) > AngleEpsilon;
            string reason = !grabInteractable.trackRotation
                ? "XR_GRAB_TRACK_ROTATION_DISABLED"
                : targetPoseChanged ? "TARGET_POSE_CHANGED" : "TARGET_POSE_UNCHANGED";

            Debug.Log($"[Valve Rotation Apply] phase={updatePhase} | calculatedDelta={appliedDelta:0.###}deg | " +
                $"hingeWorldAxis={initialHingeWorldAxis:F4} | sourceHandPosition={sourceHandPosition:F4} | " +
                $"projectedHandVector={projectedHandVector:F4} | " +
                $"inputTargetPoseRotation={inputTargetPose.rotation:F4} | " +
                $"outputTargetPoseRotation={outputTargetPose.rotation:F4} | targetPoseChanged={targetPoseChanged} | " +
                $"wheelTransformRotationBefore={wheelRotationBefore:F4} | wheelTransformRotationAfter={transform.rotation:F4} | " +
                $"rigidbodyRotationBefore={rigidbodyRotationBefore:F4} | " +
                $"rigidbodyRotationAfter={(wheelRigidbody != null ? wheelRigidbody.rotation : transform.rotation):F4} | " +
                $"hingeAngleBefore={hingeAngleBefore:0.###} | trackRotation={grabInteractable.trackRotation} | " +
                $"movementType={grabInteractable.movementType} | reason={reason}", this);

            pendingPhysicsSample = true;
            // XRInteractionManager writes the Kinematic/Velocity target before this component's
            // default-order FixedUpdate. The following FixedUpdate still precedes physics, so the
            // second callback is the first reliable sample after that physics step completed.
            pendingPhysicsFixedUpdates = 2;
            pendingWheelRotationBefore = wheelRotationBefore;
            pendingRigidbodyRotationBefore = rigidbodyRotationBefore;
            pendingHingeAngleBefore = hingeAngleBefore;
            pendingOutputTargetRotation = outputTargetPose.rotation;
        }

        void FixedUpdate()
        {
            if (!pendingPhysicsSample || --pendingPhysicsFixedUpdates > 0)
                return;

            pendingPhysicsSample = false;
            Quaternion wheelRotationAfter = transform.rotation;
            Quaternion rigidbodyRotationAfter = wheelRigidbody != null ? wheelRigidbody.rotation : wheelRotationAfter;
            float hingeAngleAfter = hingeJoint != null && float.IsFinite(hingeJoint.angle) ? hingeJoint.angle : 0f;
            float wheelChange = Quaternion.Angle(pendingWheelRotationBefore, wheelRotationAfter);
            float rigidbodyChange = Quaternion.Angle(pendingRigidbodyRotationBefore, rigidbodyRotationAfter);
            string result = wheelChange > AngleEpsilon || rigidbodyChange > AngleEpsilon ||
                Mathf.Abs(hingeAngleAfter - pendingHingeAngleBefore) > AngleEpsilon
                ? "APPLIED"
                : "RIGIDBODY_OR_HINGE_DID_NOT_APPLY_TARGET";

            Debug.Log($"[Valve Rotation Apply/Next Physics] targetRotation={pendingOutputTargetRotation:F4} | " +
                $"wheelTransformRotationAfter={wheelRotationAfter:F4} | rigidbodyRotationAfter={rigidbodyRotationAfter:F4} | " +
                $"hingeAngleBefore={pendingHingeAngleBefore:0.###} | hingeAngleAfter={hingeAngleAfter:0.###} | " +
                $"wheelRotationChange={wheelChange:0.###}deg | rigidbodyRotationChange={rigidbodyChange:0.###}deg | " +
                $"result={result}", this);
        }

        public static bool TryCombineHandDeltas(float leftDelta, bool leftValid,
            float rightDelta, bool rightValid, float minimumDeltaDegrees,
            out float combinedDelta, out string reason)
        {
            leftValid &= float.IsFinite(leftDelta);
            rightValid &= float.IsFinite(rightDelta);
            combinedDelta = 0f;
            float minimum = float.IsFinite(minimumDeltaDegrees) ? Mathf.Max(0f, minimumDeltaDegrees) : 0f;

            bool leftMoving = leftValid && Mathf.Abs(leftDelta) >= minimum;
            bool rightMoving = rightValid && Mathf.Abs(rightDelta) >= minimum;
            if (!leftMoving && !rightMoving)
            {
                reason = !leftValid && !rightValid
                    ? "INVALID_BOTH_VECTORS"
                    : "BOTH_HANDS_STATIONARY";
                return false;
            }

            // Both hands must remain selected before this method is called. Tracking
            // updates for the two controllers do not necessarily arrive in the same
            // rendered frame, so a valid delta from either held hand must be usable.
            if (!leftMoving || !rightMoving)
            {
                combinedDelta = leftMoving ? leftDelta : rightDelta;
                reason = leftMoving
                    ? "LEFT_TRACKED_WITH_TWO_HAND_GRAB"
                    : "RIGHT_TRACKED_WITH_TWO_HAND_GRAB";
                return float.IsFinite(combinedDelta);
            }

            if (Mathf.Sign(leftDelta) != Mathf.Sign(rightDelta))
            {
                reason = "OPPOSITE_DIRECTION";
                return false;
            }

            combinedDelta = (leftDelta + rightDelta) * 0.5f;
            reason = "BOTH_HANDS_APPLIED";
            return float.IsFinite(combinedDelta);
        }

        public static float ApplyRotationGain(float rawDelta, int handCount,
            float oneHandGain, float twoHandGain, bool handSetChanged)
        {
            if (handSetChanged || handCount < SupportedHandCapacity || !float.IsFinite(rawDelta))
                return 0f;

            float gain = GainForHandCount(handCount, oneHandGain, twoHandGain);
            float result = rawDelta * gain;
            return float.IsFinite(result) ? result : 0f;
        }

        public static float GainForHandCount(int handCount, float oneHandGain, float twoHandGain)
        {
            if (handCount < SupportedHandCapacity)
                return 0f;
            float gain = twoHandGain;
            return float.IsFinite(gain) ? Mathf.Max(0f, gain) : 0f;
        }

        public static bool HasRequiredHandPair(int firstHandKey, int secondHandKey) =>
            (firstHandKey == 0 && secondHandKey == 1) ||
            (firstHandKey == 1 && secondHandKey == 0);

        bool HasRequiredHandPair() =>
            activeHandCount == SupportedHandCapacity &&
            HasRequiredHandPair(
                HandSortKey(activeInteractors[0]),
                HandSortKey(activeInteractors[1]));

        public static HandMode ModeForHandCount(int handCount) => handCount <= 0
            ? HandMode.None
            : handCount == 1 ? HandMode.SingleHand : HandMode.TwoHand;

        public static bool CanAcceptAdditionalHand(int selectingHandCount, int configuredMaximumHands) =>
            Mathf.Max(0, selectingHandCount) < Mathf.Clamp(configuredMaximumHands, 1, SupportedHandCapacity);

        static int HandSortKey(IXRSelectInteractor interactor)
        {
            if (interactor is XRBaseInputInteractor inputInteractor)
            {
                if (inputInteractor.handedness == InteractorHandedness.Left)
                    return 0;
                if (inputInteractor.handedness == InteractorHandedness.Right)
                    return 1;
            }

            return interactor is Component component
                ? HandKeyFromTransformHierarchy(component.transform)
                : 2;
        }

        public static int HandKeyFromTransformHierarchy(Transform interactorTransform)
        {
            for (Transform current = interactorTransform;
                 current != null;
                 current = current.parent)
            {
                if (current.name.IndexOf(
                        "Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return 0;
                if (current.name.IndexOf(
                        "Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return 1;
            }

            return 2;
        }

        public static bool TryProjectHandDirection(Vector3 center, Vector3 handPosition, Vector3 axis,
            float minimumRadius, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!IsFinite(center) || !IsFinite(handPosition) || !IsFinite(axis) ||
                axis.sqrMagnitude <= VectorEpsilon)
                return false;

            Vector3 normalizedAxis = axis.normalized;
            Vector3 centerToHand = handPosition - center;
            Vector3 projected = centerToHand - Vector3.Dot(centerToHand, normalizedAxis) * normalizedAxis;
            float safeMinimumRadius = Mathf.Max(0.001f, minimumRadius);
            if (!IsFinite(projected) || projected.sqrMagnitude < safeMinimumRadius * safeMinimumRadius)
                return false;

            direction = projected.normalized;
            return IsFinite(direction);
        }

        public static bool TryCalculateSignedAngle(Vector3 previousDirection, Vector3 currentDirection,
            Vector3 axis, float maximumStep, out float signedAngle)
        {
            signedAngle = 0f;
            if (!IsFinite(previousDirection) || !IsFinite(currentDirection) || !IsFinite(axis) ||
                previousDirection.sqrMagnitude <= VectorEpsilon ||
                currentDirection.sqrMagnitude <= VectorEpsilon || axis.sqrMagnitude <= VectorEpsilon)
                return false;

            float angle = Vector3.SignedAngle(previousDirection, currentDirection, axis);
            if (!float.IsFinite(angle) || Mathf.Abs(angle) > Mathf.Clamp(maximumStep, 1f, 179f))
                return false;

            signedAngle = angle;
            return true;
        }

        public static float ClampAngle(float angle, float minimum, float maximum)
        {
            if (!float.IsFinite(angle) || !float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum > maximum)
                return 0f;
            return Mathf.Clamp(angle, minimum, maximum);
        }

        public void ConfigureCloseConvention(float physicalCloseAngleSign)
        {
            closeAngleSign = physicalCloseAngleSign < 0f ? -1f : 1f;
            ApplyConfiguredJointLimits();

            if (constraintReferenceInitialized)
            {
                TryGetConfiguredLimits(out minimumHingeAngle, out maximumHingeAngle);
                targetHingeAngle = Mathf.Clamp(targetHingeAngle, minimumHingeAngle, maximumHingeAngle);
            }
        }

        public void ConfigureTravelLimits(float newMaxOpenRotationDegrees, float newMaxCloseRotationDegrees)
        {
            maxOpenRotationDegrees = Mathf.Max(0f, newMaxOpenRotationDegrees);
            maxCloseRotationDegrees = Mathf.Max(0f, newMaxCloseRotationDegrees);
            ApplyConfiguredJointLimits();

            if (constraintReferenceInitialized)
            {
                TryGetConfiguredLimits(out minimumHingeAngle, out maximumHingeAngle);
                targetHingeAngle = Mathf.Clamp(targetHingeAngle, minimumHingeAngle, maximumHingeAngle);
            }
        }

        public void SetClosedLatched(bool latched)
        {
            closedLatched = latched;
            if (!latched)
                return;

            targetHingeAngle = ClosedPhysicalAngle;
            ClearRigidbodyVelocity();
        }

        public bool ApplyConfiguredJointLimits()
        {
            if (hingeJoint == null)
                hingeJoint = GetComponent<HingeJoint>();
            if (hingeJoint == null || !TryGetConfiguredLimits(out float minimum, out float maximum))
                return false;

            JointLimits limits = hingeJoint.limits;
            limits.min = minimum;
            limits.max = maximum;
            hingeJoint.useLimits = true;
            hingeJoint.extendedLimits = true;
            hingeJoint.limits = limits;
            return true;
        }

        public bool TryGetConfiguredLimits(out float minimum, out float maximum)
        {
            if (closeAngleSign >= 0f)
            {
                minimum = -maxOpenRotationDegrees;
                maximum = maxCloseRotationDegrees;
            }
            else
            {
                minimum = -maxCloseRotationDegrees;
                maximum = maxOpenRotationDegrees;
            }
            return float.IsFinite(maxOpenRotationDegrees) && float.IsFinite(maxCloseRotationDegrees) &&
                maxOpenRotationDegrees >= 0f && maxCloseRotationDegrees >= 0f;
        }

        float ClosedPhysicalAngle => maxCloseRotationDegrees * closeAngleSign;

        public static Pose CalculateConstrainedPose(Vector3 center, Quaternion rotationAtGrab,
            Vector3 worldAxis, float angleDelta)
        {
            if (!IsFinite(center) || !IsFinite(rotationAtGrab) || !IsFinite(worldAxis) ||
                worldAxis.sqrMagnitude <= VectorEpsilon || !float.IsFinite(angleDelta))
                return new Pose(center, rotationAtGrab);

            Quaternion rotation = Quaternion.AngleAxis(angleDelta, worldAxis.normalized) * rotationAtGrab;
            return new Pose(center, rotation);
        }

        bool InitializeConstraintReference(XRGrabInteractable grabInteractable)
        {
            if (constraintReferenceInitialized)
                return true;

            ResolveReferences(grabInteractable);
            if (hingeJoint == null || grabInteractable == null)
                return false;

            Vector3 localAxis = hingeJoint.axis;
            if (!IsFinite(localAxis) || localAxis.sqrMagnitude <= VectorEpsilon)
                return false;

            fixedWheelCenterWorld = grabInteractable.transform.position;
            initialWheelWorldRotation = grabInteractable.transform.rotation;
            initialHingeWorldAxis = grabInteractable.transform.TransformDirection(localAxis.normalized).normalized;
            targetHingeAngle = 0f;
            lastTargetAngleBefore = 0f;
            lastTargetAngleAfter = 0f;
            lastOutputRotation = initialWheelWorldRotation;
            constraintReferenceInitialized = IsFinite(fixedWheelCenterWorld) &&
                IsFinite(initialWheelWorldRotation) && IsFinite(initialHingeWorldAxis);
            return constraintReferenceInitialized;
        }

        static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        static bool IsFinite(Quaternion value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) &&
            float.IsFinite(value.z) && float.IsFinite(value.w);

        void ResolveReferences(XRGrabInteractable grabInteractable)
        {
            if (hingeJoint == null)
                hingeJoint = grabInteractable != null ? grabInteractable.GetComponent<HingeJoint>() : GetComponent<HingeJoint>();
            if (wheelRigidbody == null)
                wheelRigidbody = grabInteractable != null ? grabInteractable.GetComponent<Rigidbody>() : GetComponent<Rigidbody>();
        }

        void OnValidate()
        {
            if (!float.IsFinite(maxOpenRotationDegrees) || maxOpenRotationDegrees < 0f)
                maxOpenRotationDegrees = 108f;
            if (!float.IsFinite(maxCloseRotationDegrees) || maxCloseRotationDegrees < 0f)
                maxCloseRotationDegrees = 252f;
            minimumHandRadius = Mathf.Max(0.001f, minimumHandRadius);
            maximumAngularStep = Mathf.Clamp(maximumAngularStep, 1f, 179f);
            oneHandRotationGain = 0f;
            twoHandRotationGain = Mathf.Max(0f, twoHandRotationGain);
            twoHandMinimumDeltaDegrees = float.IsFinite(twoHandMinimumDeltaDegrees)
                ? Mathf.Max(0f, twoHandMinimumDeltaDegrees)
                : 0.02f;
            maximumHands = Mathf.Clamp(maximumHands, 1, SupportedHandCapacity);
            maxGrabSurfaceDistance = float.IsFinite(maxGrabSurfaceDistance)
                ? Mathf.Max(0f, maxGrabSurfaceDistance)
                : 0.15f;
            if (hingeJoint == null)
                hingeJoint = GetComponent<HingeJoint>();
            ApplyConfiguredJointLimits();
        }
    }
}
