using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRProject.Valves
{
    /// <summary>
    /// Temporary, state-change-only diagnostics for manual VR validation.
    /// It never changes input, selection, physics, filters, or transforms.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ValveGrabSelectionDiagnostics : MonoBehaviour
    {
        const string LogPrefix = "[Valve Grab Debug]";

        enum ExitReason
        {
            InputInactive,
            SelectActionDisabled,
            InteractorCanSelectFalse,
            InteractableCanSelectFalse,
            SelectFilterFalse,
            InteractionLayerMismatch,
            InteractionGroupBlocked,
            ComponentDisabled,
            ManualSelectCancel,
            InteractableDestroyed,
            Unknown,
        }

        struct SelectionSnapshot
        {
            public string targetPath;
            public string interactorName;
            public string interactorPath;
            public string handedness;
            public bool interactorEnabled;
            public bool interactorActive;
            public bool hoveringTarget;
            public bool interactableExists;
            public bool interactableEnabled;
            public bool interactableActive;
            public bool selectEntered;
            public int selectedHands;
            public string selectingInteractors;
            public bool? selectActionEnabled;
            public bool selectBool;
            public float selectValue;
            public bool logicalSelectActive;
            public bool logicalSelectPerformed;
            public bool logicalSelectCompletedThisFrame;
            public bool? gripButton;
            public float? gripValue;
            public bool layerMatch;
            public bool groupBlocked;
            public string groupName;
            public string groupActiveInteractor;
            public bool valveFilterResult;
            public int maximumHands;
            public string managerFilters;
            public string interactorFilters;
            public string interactableFilters;
            public string attachTransformPath;
            public Vector3 attachWorldPosition;
            public string nearestRimCollider;
            public Vector3 nearestRimColliderClosestPoint;
            public bool nearestRimColliderEnabled;
            public bool nearestRimColliderActive;
            public int activeRimColliderCount;
            public int configuredRimColliderCount;
            public float minimumSurfaceDistance;
            public float maximumSurfaceDistance;
            public bool surfaceProximityAllowed;
            public bool canSafelyEvaluateSelection;
            public bool? managerCanSelect;
            public bool? interactorCanSelect;
            public bool? interactableCanSelect;
        }

        [SerializeField] XRGrabInteractable grabInteractable;
        [SerializeField] ValveWheelGrabTransformer grabTransformer;
        [SerializeField] NearFarInteractor leftNearFarInteractor;
        [SerializeField] NearFarInteractor rightNearFarInteractor;
        [SerializeField] bool logGripAttempts = true;
        [SerializeField, Min(0.001f)] float minimumDeltaToLog = 0.05f;
        [SerializeField, Min(0.05f)] float deltaLogCooldown = 0.25f;

        readonly Dictionary<IXRSelectInteractor, SelectionSnapshot> lastSelectionSnapshots = new();
        readonly Dictionary<XRBaseInputInteractor, bool> lastGripStates = new();

        int lastSelectedHandCount = -1;
        ValveWheelGrabTransformer.HandMode lastMode = (ValveWheelGrabTransformer.HandMode)(-1);
        float lastGain = float.NaN;
        int lastLoggedDeltaFrame = -1;
        int lastLoggedEvaluationFrame = -1;
        float lastLoggedDelta;
        float nextDeltaLogTime;
        string lastLimitHit = "NONE";
        string lastTwoHandReason = string.Empty;
        bool pivotLogged;
        bool subscribed;

        void Reset()
        {
            ResolveReferences();
        }

        void Awake()
        {
            ResolveReferences();
        }

        void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            if (Application.isPlaying)
                CaptureModeChange(true);
        }

        void Start()
        {
            if (Application.isPlaying)
                LogPivotStateOnce();
        }

        void OnDisable()
        {
            Unsubscribe();
            lastSelectionSnapshots.Clear();
            lastGripStates.Clear();
            lastSelectedHandCount = -1;
            lastMode = (ValveWheelGrabTransformer.HandMode)(-1);
            lastGain = float.NaN;
            lastLoggedDeltaFrame = -1;
            lastLoggedEvaluationFrame = -1;
            lastLimitHit = "NONE";
            lastTwoHandReason = string.Empty;
            pivotLogged = false;
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;

            CaptureGripAttempts();
            CaptureModeChange(false);
            CaptureRotationState();
            CaptureSelectionInputChanges();
        }

        void Subscribe()
        {
            if (subscribed || grabInteractable == null)
                return;

            grabInteractable.hoverEntered.AddListener(OnHoverEntered);
            grabInteractable.hoverExited.AddListener(OnHoverExited);
            grabInteractable.selectEntered.AddListener(OnSelectEntered);
            grabInteractable.selectExited.AddListener(OnSelectExited);
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || grabInteractable == null)
                return;

            grabInteractable.hoverEntered.RemoveListener(OnHoverEntered);
            grabInteractable.hoverExited.RemoveListener(OnHoverExited);
            grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
            grabInteractable.selectExited.RemoveListener(OnSelectExited);
            subscribed = false;
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            SelectionSnapshot snapshot = CaptureSnapshot(args.interactorObject as IXRSelectInteractor,
                grabInteractable != null ? grabInteractable.interactionManager : null, true);
            Log($"Hover Enter: {snapshot.interactorName}\n{FormatSnapshot("HOVER SURFACE PROXIMITY", snapshot)}");
        }

        void OnHoverExited(HoverExitEventArgs args) =>
            Log($"Hover Exit: {InteractorName(args.interactorObject)} | canceled={args.isCanceled}");

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            SelectionSnapshot snapshot = CaptureSnapshot(args.interactorObject, args.manager, true);
            lastSelectionSnapshots[args.interactorObject] = snapshot;
            Log($"Select Enter: {snapshot.interactorName}\n{FormatSnapshot("ENTER STATE", snapshot)}");
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            lastSelectionSnapshots.TryGetValue(args.interactorObject, out SelectionSnapshot before);
            SelectionSnapshot after = CaptureSnapshot(args.interactorObject, args.manager, true);
            ExitReason reason = ClassifyExitReason(args, before, after);

            var message = new StringBuilder(1024);
            message.Append("SELECT EXIT REASON:\n")
                .Append(ToReasonCode(reason)).Append('\n')
                .Append("canceled=").Append(args.isCanceled).Append('\n')
                .Append(FormatSnapshot("BEFORE EXIT (last selected frame)", before)).Append('\n')
                .Append(FormatSnapshot("AFTER EXIT (event callback)", after));
            Log(message.ToString());

            lastSelectionSnapshots.Remove(args.interactorObject);
        }

        void CaptureSelectionInputChanges()
        {
            if (grabInteractable == null)
                return;

            for (int i = 0; i < grabInteractable.interactorsSelecting.Count; ++i)
            {
                IXRSelectInteractor interactor = grabInteractable.interactorsSelecting[i];
                SelectionSnapshot current = CaptureSnapshot(interactor, grabInteractable.interactionManager, false);
                if (lastSelectionSnapshots.TryGetValue(interactor, out SelectionSnapshot previous) &&
                    HasImportantInputStateChanged(previous, current))
                {
                    Log($"Selection Input State Changed: {current.interactorName} | " +
                        $"selectActionEnabled={FormatNullable(current.selectActionEnabled)} | " +
                        $"selectBool={current.selectBool} | selectValue={current.selectValue:0.###} | " +
                        $"logicalSelectActive={current.logicalSelectActive} | " +
                        $"gripButton={FormatNullable(current.gripButton)} | gripValue={FormatNullable(current.gripValue)}");
                }

                lastSelectionSnapshots[interactor] = current;
            }
        }

        void CaptureGripAttempts()
        {
            // Dormant additive LeakResponse valves must not report every global Grip
            // while another Stage owns input. Their interactables are disabled by the
            // Stage activation contract.
            if (!logGripAttempts || grabInteractable == null || !grabInteractable.isActiveAndEnabled)
                return;

            CaptureGripAttempt(leftNearFarInteractor, "Left");
            CaptureGripAttempt(rightNearFarInteractor, "Right");
        }

        void CaptureGripAttempt(NearFarInteractor interactor, string handLabel)
        {
            if (interactor == null)
                return;

            bool gripActive = IsGripAttemptActive(interactor);
            bool wasActive = lastGripStates.TryGetValue(interactor, out bool previous) && previous;
            lastGripStates[interactor] = gripActive;
            if (!gripActive || wasActive)
                return;

            SelectionSnapshot snapshot = CaptureSnapshot(interactor,
                grabInteractable != null ? grabInteractable.interactionManager : null, true);
            Log($"Grip Attempt ({handLabel}):\n{FormatSnapshot("GRIP ATTEMPT", snapshot)}");
        }

        static bool IsGripAttemptActive(XRBaseInputInteractor interactor)
        {
            XRInputButtonReader input = interactor.selectInput;
            bool selectPerformed = input != null && input.ReadIsPerformed();
            ReadRawGrip(interactor.handedness, out bool? gripButton, out float? gripValue);
            return gripButton == true || selectPerformed || gripValue >= 0.5f;
        }

        SelectionSnapshot CaptureSnapshot(IXRSelectInteractor interactor, XRInteractionManager manager,
            bool evaluateSelection)
        {
            var snapshot = new SelectionSnapshot
            {
                targetPath = ComponentPath(grabInteractable),
                interactorName = InteractorName(interactor),
                interactorPath = ComponentPath(interactor as Component),
                interactableExists = grabInteractable != null,
                interactableEnabled = grabInteractable != null && grabInteractable.enabled,
                interactableActive = grabInteractable != null && grabInteractable.gameObject.activeInHierarchy,
                selectEntered = IsSelectedBy(interactor),
                selectedHands = grabInteractable != null ? grabInteractable.interactorsSelecting.Count : 0,
                selectingInteractors = SelectingInteractorNames(),
                maximumHands = grabTransformer != null ? grabTransformer.MaximumHands : -1,
                maximumSurfaceDistance = grabTransformer != null
                    ? grabTransformer.MaxGrabSurfaceDistance
                    : float.NaN,
            };

            snapshot.valveFilterResult = EvaluateValveFilterWithoutCallingProcess(interactor,
                out snapshot.attachWorldPosition, out Collider nearestRimCollider,
                out snapshot.minimumSurfaceDistance, out snapshot.surfaceProximityAllowed);
            snapshot.nearestRimCollider = nearestRimCollider != null ? nearestRimCollider.name : "none";
            snapshot.nearestRimColliderClosestPoint = nearestRimCollider != null
                ? nearestRimCollider.ClosestPoint(snapshot.attachWorldPosition)
                : Vector3.zero;
            snapshot.nearestRimColliderEnabled = nearestRimCollider != null && nearestRimCollider.enabled;
            snapshot.nearestRimColliderActive = nearestRimCollider != null &&
                nearestRimCollider.gameObject.activeInHierarchy;
            CountActiveColliders(out snapshot.activeRimColliderCount,
                out snapshot.configuredRimColliderCount);

            Transform attachTransform = interactor?.GetAttachTransform(grabInteractable);
            snapshot.attachTransformPath = HierarchyPath(attachTransform);

            if (interactor is Behaviour interactorBehaviour)
            {
                snapshot.interactorEnabled = interactorBehaviour.enabled;
                snapshot.interactorActive = interactorBehaviour.gameObject.activeInHierarchy;
            }

            snapshot.layerMatch = interactor != null && grabInteractable != null &&
                (interactor.interactionLayers & grabInteractable.interactionLayers) != 0;
            snapshot.hoveringTarget = interactor is IXRHoverInteractor hoverInteractor &&
                grabInteractable != null && hoverInteractor.interactablesHovered.Contains(grabInteractable);

            if (interactor is IXRGroupMember groupMember && groupMember.containingGroup != null)
            {
                IXRInteractionGroup group = groupMember.containingGroup;
                snapshot.groupName = group.groupName;
                snapshot.groupActiveInteractor = InteractorName(group.activeInteractor);
                snapshot.groupBlocked = group.activeInteractor != null &&
                    !ReferenceEquals(group.activeInteractor, interactor);
            }
            else
            {
                snapshot.groupName = "none";
                snapshot.groupActiveInteractor = "none";
            }

            if (interactor is XRBaseInputInteractor inputInteractor)
            {
                snapshot.handedness = inputInteractor.handedness.ToString();
                XRInputButtonReader input = inputInteractor.selectInput;
                snapshot.selectActionEnabled = IsSelectActionEnabled(input);
                snapshot.selectBool = input.ReadIsPerformed();
                snapshot.selectValue = input.ReadValue();
                snapshot.logicalSelectActive = inputInteractor.logicalSelectState.active;
                snapshot.logicalSelectPerformed = inputInteractor.logicalSelectState.isPerformed;
                snapshot.logicalSelectCompletedThisFrame =
                    inputInteractor.logicalSelectState.wasCompletedThisFrame;
                ReadRawGrip(inputInteractor.handedness, out snapshot.gripButton, out snapshot.gripValue);
            }

            snapshot.managerFilters = FilterNames(manager?.selectFilters);
            snapshot.interactorFilters = interactor is XRBaseInteractor baseInteractor
                ? FilterNames(baseInteractor.selectFilters)
                : "unavailable";
            snapshot.interactableFilters = grabInteractable != null
                ? FilterNames(grabInteractable.selectFilters)
                : "unavailable";

            snapshot.canSafelyEvaluateSelection = AllFiltersSafeForDiagnosticEvaluation(
                manager?.selectFilters,
                interactor is XRBaseInteractor xrInteractor ? xrInteractor.selectFilters : null,
                grabInteractable?.selectFilters);

            if (evaluateSelection && snapshot.canSafelyEvaluateSelection && manager != null &&
                interactor != null && grabInteractable != null)
            {
                // The only project filter is ValveWheelGrabTransformer. Its Process method only
                // reads selection capacity, the interactor attach pose, and collider surfaces.
                snapshot.interactorCanSelect = interactor.CanSelect(grabInteractable);
                snapshot.interactableCanSelect = ((IXRSelectInteractable)grabInteractable).IsSelectableBy(interactor);
                snapshot.managerCanSelect = manager.CanSelect(interactor, grabInteractable);
            }

            return snapshot;
        }

        bool EvaluateValveFilterWithoutCallingProcess(IXRSelectInteractor interactor,
            out Vector3 attachWorldPosition, out Collider nearestRimCollider,
            out float minimumSurfaceDistance, out bool surfaceProximityAllowed)
        {
            attachWorldPosition = Vector3.zero;
            nearestRimCollider = null;
            minimumSurfaceDistance = float.PositiveInfinity;
            surfaceProximityAllowed = false;
            if (grabInteractable == null || grabTransformer == null)
                return true;

            surfaceProximityAllowed = grabTransformer.TryEvaluateSurfaceProximity(interactor,
                grabInteractable, out attachWorldPosition, out nearestRimCollider,
                out minimumSurfaceDistance);

            for (int i = 0; i < grabInteractable.interactorsSelecting.Count; ++i)
            {
                if (ReferenceEquals(grabInteractable.interactorsSelecting[i], interactor))
                    return true;
            }

            return grabInteractable.interactorsSelecting.Count < Mathf.Clamp(grabTransformer.MaximumHands, 1, 2) &&
                surfaceProximityAllowed;
        }

        void CountActiveColliders(out int activeCount, out int configuredCount)
        {
            activeCount = 0;
            configuredCount = grabInteractable != null ? grabInteractable.colliders.Count : 0;
            for (int i = 0; i < configuredCount; ++i)
            {
                Collider colliderComponent = grabInteractable.colliders[i];
                if (colliderComponent != null && colliderComponent.enabled &&
                    colliderComponent.gameObject.activeInHierarchy)
                    ++activeCount;
            }
        }

        bool IsSelectedBy(IXRSelectInteractor interactor)
        {
            if (grabInteractable == null || interactor == null)
                return false;
            for (int i = 0; i < grabInteractable.interactorsSelecting.Count; ++i)
            {
                if (ReferenceEquals(grabInteractable.interactorsSelecting[i], interactor))
                    return true;
            }
            return false;
        }

        static ExitReason ClassifyExitReason(SelectExitEventArgs args, SelectionSnapshot before,
            SelectionSnapshot after)
        {
            if (!after.interactableExists)
                return ExitReason.InteractableDestroyed;
            if (!after.interactorEnabled || !after.interactorActive ||
                !after.interactableEnabled || !after.interactableActive)
                return ExitReason.ComponentDisabled;
            if (args.isCanceled)
                return ExitReason.ManualSelectCancel;
            if (after.selectActionEnabled == false)
                return ExitReason.SelectActionDisabled;
            // In StateChange mode XRI clears logicalSelectState.active as part of OnSelectExiting,
            // even when the physical action is still performed. Use the raw reader after exit and
            // the last snapshot captured while selected so a filter/layer exit is not mislabeled.
            if (!after.selectBool || after.logicalSelectCompletedThisFrame ||
                before.logicalSelectCompletedThisFrame ||
                (before.interactorName != null && !before.logicalSelectActive))
                return ExitReason.InputInactive;
            if (!after.layerMatch)
                return ExitReason.InteractionLayerMismatch;
            if (!after.valveFilterResult)
                return ExitReason.SelectFilterFalse;
            if (after.groupBlocked)
                return ExitReason.InteractionGroupBlocked;
            if (after.interactorCanSelect == false)
                return ExitReason.InteractorCanSelectFalse;
            if (after.interactableCanSelect == false)
                return ExitReason.InteractableCanSelectFalse;

            // The previous snapshot is retained because an input pulse can recover before the
            // Select Exit event is delivered. It is evidence, not a reason guess.
            _ = before;
            return ExitReason.Unknown;
        }

        void CaptureModeChange(bool force)
        {
            if (grabTransformer == null)
                return;

            int handCount = grabTransformer.CurrentHandCount;
            ValveWheelGrabTransformer.HandMode mode = grabTransformer.CurrentMode;
            float gain = grabTransformer.CurrentRotationGain;
            if (!force && handCount == lastSelectedHandCount && mode == lastMode &&
                Mathf.Approximately(gain, lastGain))
                return;

            ValveWheelGrabTransformer.HandMode previousMode = lastMode;
            Log($"Mode: {mode} | selectedHands={handCount} | " +
                $"leftSelected={ContainsHandName("Left")} | rightSelected={ContainsHandName("Right")} | " +
                $"interactors=[{grabTransformer.GetActiveInteractorName(0)}, {grabTransformer.GetActiveInteractorName(1)}] | " +
                $"rotationGain={gain:0.###} | handModeTransition=true");
            if (mode == ValveWheelGrabTransformer.HandMode.TwoHand ||
                previousMode == ValveWheelGrabTransformer.HandMode.TwoHand)
                LogTwoHandState("MODE_TRANSITION");
            lastSelectedHandCount = handCount;
            lastMode = mode;
            lastGain = gain;
        }

        void CaptureRotationState()
        {
            if (grabTransformer == null || grabTransformer.LastEvaluationFrame < 0 ||
                grabTransformer.LastEvaluationFrame == lastLoggedEvaluationFrame)
                return;

            lastLoggedEvaluationFrame = grabTransformer.LastEvaluationFrame;
            float delta = grabTransformer.LastAppliedDeltaAngle;
            bool limitChanged = grabTransformer.LastLimitHit != lastLimitHit;
            bool ignoredDelta = !grabTransformer.LastCombinedDeltaValid;
            bool meaningfulRotation = Mathf.Abs(delta) >= minimumDeltaToLog;
            bool twoHandReasonChanged = grabTransformer.CurrentMode == ValveWheelGrabTransformer.HandMode.TwoHand &&
                grabTransformer.LastTwoHandReason != lastTwoHandReason;
            if (!limitChanged && !ignoredDelta && !meaningfulRotation)
            {
                if (twoHandReasonChanged)
                    LogTwoHandState(grabTransformer.LastTwoHandReason);
                return;
            }
            if (!limitChanged && Time.unscaledTime < nextDeltaLogTime)
                return;
            if (!limitChanged && !ignoredDelta && Mathf.Abs(delta - lastLoggedDelta) < minimumDeltaToLog &&
                Mathf.Sign(delta) == Mathf.Sign(lastLoggedDelta))
                return;

            Log($"Rotation State: leftSelected={ContainsHandName("Left")} | rightSelected={ContainsHandName("Right")} | " +
                $"selectedHands={grabTransformer.CurrentHandCount} | mode={grabTransformer.CurrentMode} | " +
                $"hand0={grabTransformer.GetActiveInteractorName(0)} rawDelta={grabTransformer.GetLastRawHandDelta(0):0.###} " +
                $"valid={grabTransformer.GetLastRawHandDeltaValid(0)} | " +
                $"hand1={grabTransformer.GetActiveInteractorName(1)} rawDelta={grabTransformer.GetLastRawHandDelta(1):0.###} " +
                $"valid={grabTransformer.GetLastRawHandDeltaValid(1)} | " +
                $"combinedDelta={grabTransformer.LastCombinedDeltaAngle:0.###} valid={grabTransformer.LastCombinedDeltaValid} | " +
                $"appliedDelta={delta:0.###} | currentTargetAngle={grabTransformer.CurrentTargetAngle:0.###} | " +
                $"openLimit={grabTransformer.MaxOpenRotationDegrees:0.###} | " +
                $"closeLimit={grabTransformer.MaxCloseRotationDegrees:0.###} | limitHit={grabTransformer.LastLimitHit}");
            if (grabTransformer.CurrentMode == ValveWheelGrabTransformer.HandMode.TwoHand &&
                (twoHandReasonChanged || meaningfulRotation))
                LogTwoHandState(grabTransformer.LastTwoHandReason);
            if (limitChanged || meaningfulRotation)
                LogLimitState();
            lastLoggedDelta = delta;
            lastLoggedDeltaFrame = grabTransformer.LastDeltaFrame;
            lastLimitHit = grabTransformer.LastLimitHit;
            nextDeltaLogTime = Time.unscaledTime + deltaLogCooldown;
        }

        void LogTwoHandState(string reason)
        {
            if (grabTransformer == null)
                return;

            float leftDelta = grabTransformer.GetLastRawHandDelta(0);
            float rightDelta = grabTransformer.GetLastRawHandDelta(1);
            bool leftValid = grabTransformer.GetLastRawHandDeltaValid(0);
            bool rightValid = grabTransformer.GetLastRawHandDeltaValid(1);
            bool leftMoved = leftValid && Mathf.Abs(leftDelta) >= grabTransformer.TwoHandMinimumDeltaDegrees;
            bool rightMoved = rightValid && Mathf.Abs(rightDelta) >= grabTransformer.TwoHandMinimumDeltaDegrees;
            bool sameDirection = leftMoved && rightMoved && Mathf.Sign(leftDelta) == Mathf.Sign(rightDelta);

            Debug.Log($"[Valve Two Hand Debug] leftSelected={ContainsHandName("Left")} | " +
                $"rightSelected={ContainsHandName("Right")} | leftRawDelta={leftDelta:0.###} | " +
                $"rightRawDelta={rightDelta:0.###} | leftMoved={leftMoved} | rightMoved={rightMoved} | " +
                $"sameDirection={sameDirection} | combinedDelta={grabTransformer.LastCombinedDeltaAngle:0.###} | " +
                $"appliedDelta={grabTransformer.LastAppliedDeltaAngle:0.###} | reason={reason}", this);
            lastTwoHandReason = reason;
        }

        void LogLimitState()
        {
            Debug.Log($"[Valve Limit Debug] targetAngleBefore={grabTransformer.LastTargetAngleBefore:0.###} | " +
                $"appliedDelta={grabTransformer.LastAppliedDeltaAngle:0.###} | " +
                $"targetAngleAfter={grabTransformer.LastTargetAngleAfter:0.###} | " +
                $"openLimit={-grabTransformer.MaxOpenRotationDegrees:0.###} | " +
                $"closeLimit={grabTransformer.MaxCloseRotationDegrees:0.###} | " +
                $"limitHit={grabTransformer.LastLimitHit} | outputRotation={grabTransformer.LastOutputRotation:F4} | " +
                $"initialRotation={grabTransformer.InitialWheelWorldRotation:F4}", this);
        }

        void LogPivotStateOnce()
        {
            if (pivotLogged || grabInteractable == null)
                return;

            pivotLogged = true;
            Transform wheel = grabInteractable.transform;
            HingeJoint hinge = wheel.GetComponent<HingeJoint>();
            Rigidbody body = wheel.GetComponent<Rigidbody>();
            Renderer rendererComponent = wheel.GetComponent<Renderer>();

            Vector3 wheelPivotWorld = wheel.position;
            Vector3 meshCenterWorld = rendererComponent != null ? rendererComponent.bounds.center : wheelPivotWorld;
            Vector3 hingeAnchorWorld = hinge != null ? wheel.TransformPoint(hinge.anchor) : wheelPivotWorld;
            Vector3 hingeConnectedAnchorWorld = hinge != null && hinge.connectedBody != null
                ? hinge.connectedBody.transform.TransformPoint(hinge.connectedAnchor)
                : hinge != null ? hinge.connectedAnchor : wheelPivotWorld;
            Vector3 kinematicAnchorWorld = hinge != null && hinge.connectedBody != null
                ? hinge.connectedBody.position
                : hingeConnectedAnchorWorld;
            Vector3 rigidbodyCenterOfMassWorld = body != null ? body.worldCenterOfMass : wheelPivotWorld;
            Vector3 rimCenterWorld = wheelPivotWorld;
            if (grabInteractable.colliders.Count > 0)
            {
                rimCenterWorld = Vector3.zero;
                int colliderCount = 0;
                for (int i = 0; i < grabInteractable.colliders.Count; ++i)
                {
                    Collider colliderComponent = grabInteractable.colliders[i];
                    if (colliderComponent == null)
                        continue;
                    rimCenterWorld += colliderComponent.bounds.center;
                    ++colliderCount;
                }
                if (colliderCount > 0)
                    rimCenterWorld /= colliderCount;
                else
                    rimCenterWorld = wheelPivotWorld;
            }

            Vector3 hingeAxisWorld = hinge != null
                ? wheel.TransformDirection(hinge.axis).normalized
                : wheel.forward;
            float maxCenterError = Mathf.Max(
                Vector3.Distance(wheelPivotWorld, meshCenterWorld),
                Vector3.Distance(wheelPivotWorld, hingeAnchorWorld),
                Vector3.Distance(wheelPivotWorld, hingeConnectedAnchorWorld),
                Vector3.Distance(wheelPivotWorld, kinematicAnchorWorld),
                Vector3.Distance(wheelPivotWorld, rimCenterWorld),
                Vector3.Distance(wheelPivotWorld, rigidbodyCenterOfMassWorld));

            Debug.Log($"[Valve Pivot Debug] wheelPivotWorld={wheelPivotWorld:F6} | " +
                $"meshCenterWorld={meshCenterWorld:F6} | hingeAnchorWorld={hingeAnchorWorld:F6} | " +
                $"hingeConnectedAnchorWorld={hingeConnectedAnchorWorld:F6} | " +
                $"kinematicAnchorWorld={kinematicAnchorWorld:F6} | rimCenterWorld={rimCenterWorld:F6} | " +
                $"rigidbodyCenterOfMassWorld={rigidbodyCenterOfMassWorld:F6} | " +
                $"maxCenterError={maxCenterError:0.######}m | hingeAxisWorld={hingeAxisWorld:F6}", this);
        }

        bool ContainsHandName(string handName)
        {
            for (int i = 0; i < grabTransformer.CurrentHandCount; ++i)
            {
                if (grabTransformer.GetActiveInteractorName(i).IndexOf(handName,
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        static bool HasImportantInputStateChanged(SelectionSnapshot previous, SelectionSnapshot current) =>
            previous.selectActionEnabled != current.selectActionEnabled ||
            previous.selectBool != current.selectBool ||
            previous.logicalSelectActive != current.logicalSelectActive ||
            previous.gripButton != current.gripButton;

        static bool? IsSelectActionEnabled(XRInputButtonReader input)
        {
            if (input == null)
                return false;

            switch (input.inputSourceMode)
            {
                case XRInputButtonReader.InputSourceMode.Unused:
                    return false;
                case XRInputButtonReader.InputSourceMode.InputAction:
                    return input.inputActionPerformed != null && input.inputActionPerformed.enabled;
                case XRInputButtonReader.InputSourceMode.InputActionReference:
                    return input.inputActionReferencePerformed != null &&
                        input.inputActionReferencePerformed.action != null &&
                        input.inputActionReferencePerformed.action.enabled;
                case XRInputButtonReader.InputSourceMode.ManualValue:
                    return true;
                default:
                    return null;
            }
        }

        static void ReadRawGrip(InteractorHandedness handedness, out bool? gripButton,
            out float? gripValue)
        {
            gripButton = null;
            gripValue = null;
            XRNode node = handedness == InteractorHandedness.Left
                ? XRNode.LeftHand
                : handedness == InteractorHandedness.Right ? XRNode.RightHand : XRNode.HardwareTracker;
            if (node == XRNode.HardwareTracker)
                return;

            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return;
            if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool button))
                gripButton = button;
            if (device.TryGetFeatureValue(CommonUsages.grip, out float value))
                gripValue = value;
        }

        static bool AllFiltersSafeForDiagnosticEvaluation(params IXRFilterList<IXRSelectFilter>[] lists)
        {
            for (int listIndex = 0; listIndex < lists.Length; ++listIndex)
            {
                IXRFilterList<IXRSelectFilter> list = lists[listIndex];
                if (list == null)
                    continue;
                for (int i = 0; i < list.count; ++i)
                {
                    if (list.GetAt(i) is not ValveWheelGrabTransformer)
                        return false;
                }
            }

            return true;
        }

        static string FilterNames(IXRFilterList<IXRSelectFilter> filters)
        {
            if (filters == null)
                return "unavailable";
            if (filters.count == 0)
                return "none";

            var names = new StringBuilder();
            for (int i = 0; i < filters.count; ++i)
            {
                if (i > 0)
                    names.Append(',');
                IXRSelectFilter filter = filters.GetAt(i);
                names.Append(filter is Object unityObject ? unityObject.name : filter?.GetType().Name ?? "null");
            }

            return names.ToString();
        }

        string SelectingInteractorNames()
        {
            if (grabInteractable == null || grabInteractable.interactorsSelecting.Count == 0)
                return "none";

            var names = new StringBuilder();
            for (int i = 0; i < grabInteractable.interactorsSelecting.Count; ++i)
            {
                if (i > 0)
                    names.Append(',');
                names.Append(InteractorName(grabInteractable.interactorsSelecting[i]));
            }

            return names.ToString();
        }

        static string FormatSnapshot(string label, SelectionSnapshot value)
        {
            var result = new StringBuilder(768);
            result.Append(label).Append(':').Append('\n')
                .Append("targetPath=").Append(value.targetPath ?? "null").Append('\n')
                .Append("interactor=").Append(value.interactorName ?? "null").Append('\n')
                .Append("interactorPath=").Append(value.interactorPath ?? "null").Append('\n')
                .Append("handedness=").Append(value.handedness ?? "None").Append('\n')
                .Append("interactorEnabled=").Append(value.interactorEnabled).Append('\n')
                .Append("interactorActiveInHierarchy=").Append(value.interactorActive).Append('\n')
                .Append("hoveringTarget=").Append(value.hoveringTarget).Append('\n')
                .Append("interactableExists=").Append(value.interactableExists).Append('\n')
                .Append("interactableEnabled=").Append(value.interactableEnabled).Append('\n')
                .Append("interactableActiveInHierarchy=").Append(value.interactableActive).Append('\n')
                .Append("selectEntered=").Append(value.selectEntered).Append('\n')
                .Append("selectedHands=").Append(value.selectedHands).Append('\n')
                .Append("selectingInteractors=").Append(value.selectingInteractors ?? "none").Append('\n')
                .Append("selectActionEnabled=").Append(FormatNullable(value.selectActionEnabled)).Append('\n')
                .Append("selectBool=").Append(value.selectBool).Append('\n')
                .Append("selectValue=").Append(value.selectValue.ToString("0.###")).Append('\n')
                .Append("logicalSelectActive=").Append(value.logicalSelectActive).Append('\n')
                .Append("logicalSelectPerformed=").Append(value.logicalSelectPerformed).Append('\n')
                .Append("logicalSelectCompletedThisFrame=").Append(value.logicalSelectCompletedThisFrame).Append('\n')
                .Append("gripButton=").Append(FormatNullable(value.gripButton)).Append('\n')
                .Append("gripValue=").Append(FormatNullable(value.gripValue)).Append('\n')
                .Append("interactionLayerMatch=").Append(value.layerMatch).Append('\n')
                .Append("interactionGroupBlocked=").Append(value.groupBlocked).Append('\n')
                .Append("interactionGroup=").Append(value.groupName ?? "none").Append('\n')
                .Append("groupActiveInteractor=").Append(value.groupActiveInteractor ?? "none").Append('\n')
                .Append("managerFilters=").Append(value.managerFilters ?? "unavailable").Append('\n')
                .Append("interactorFilters=").Append(value.interactorFilters ?? "unavailable").Append('\n')
                .Append("interactableFilters=").Append(value.interactableFilters ?? "unavailable").Append('\n')
                .Append("attachTransformPath=").Append(value.attachTransformPath ?? "none").Append('\n')
                .Append("attachWorldPosition=").Append(value.attachWorldPosition.ToString("F4")).Append('\n')
                .Append("nearestRimCollider=").Append(value.nearestRimCollider ?? "none").Append('\n')
                .Append("nearestRimColliderClosestPointWorldPosition=")
                .Append(value.nearestRimColliderClosestPoint.ToString("F4")).Append('\n')
                .Append("nearestRimColliderEnabled=").Append(value.nearestRimColliderEnabled).Append('\n')
                .Append("nearestRimColliderActiveInHierarchy=").Append(value.nearestRimColliderActive).Append('\n')
                .Append("activeRimColliders=").Append(value.activeRimColliderCount).Append('/')
                .Append(value.configuredRimColliderCount).Append('\n')
                .Append("minimumSurfaceDistance = ").Append(value.minimumSurfaceDistance.ToString("0.####")).Append("m\n")
                .Append("maxGrabSurfaceDistance = ").Append(value.maximumSurfaceDistance.ToString("0.####")).Append("m\n")
                .Append("distanceAccepted = ").Append(value.surfaceProximityAllowed).Append('\n')
                .Append("valveSelectFilterResult=").Append(value.valveFilterResult).Append('\n')
                .Append("maximumHands=").Append(value.maximumHands).Append('\n')
                .Append("safeSelectionReevaluation=").Append(value.canSafelyEvaluateSelection).Append('\n')
                .Append("interactorCanSelect=").Append(FormatNullable(value.interactorCanSelect)).Append('\n')
                .Append("interactableCanSelect=").Append(FormatNullable(value.interactableCanSelect)).Append('\n')
                .Append("managerCanSelect=").Append(FormatNullable(value.managerCanSelect));
            return result.ToString();
        }

        static string ToReasonCode(ExitReason reason)
        {
            switch (reason)
            {
                case ExitReason.InputInactive: return "INPUT_INACTIVE";
                case ExitReason.SelectActionDisabled: return "SELECT_ACTION_DISABLED";
                case ExitReason.InteractorCanSelectFalse: return "INTERACTOR_CAN_SELECT_FALSE";
                case ExitReason.InteractableCanSelectFalse: return "INTERACTABLE_CAN_SELECT_FALSE";
                case ExitReason.SelectFilterFalse: return "SELECT_FILTER_FALSE";
                case ExitReason.InteractionLayerMismatch: return "INTERACTION_LAYER_MISMATCH";
                case ExitReason.InteractionGroupBlocked: return "INTERACTION_GROUP_BLOCKED";
                case ExitReason.ComponentDisabled: return "COMPONENT_DISABLED";
                case ExitReason.ManualSelectCancel: return "MANUAL_SELECT_CANCEL";
                case ExitReason.InteractableDestroyed: return "INTERACTABLE_DESTROYED";
                default: return "UNKNOWN";
            }
        }

        static string FormatNullable(bool? value) => value.HasValue ? value.Value.ToString() : "unavailable";
        static string FormatNullable(float? value) => value.HasValue ? value.Value.ToString("0.###") : "unavailable";

        static string InteractorName(object interactor) =>
            interactor is Component component ? component.name : interactor?.ToString() ?? "null";

        static string ComponentPath(Component component) =>
            component != null ? HierarchyPath(component.transform) : "null";

        static string HierarchyPath(Transform target)
        {
            if (target == null)
                return "none";

            var names = new Stack<string>();
            Transform current = target;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            var path = new StringBuilder();
            if (target.gameObject.scene.IsValid() && !string.IsNullOrEmpty(target.gameObject.scene.name))
                path.Append(target.gameObject.scene.name).Append('/');
            while (names.Count > 0)
            {
                if (path.Length > 0 && path[path.Length - 1] != '/')
                    path.Append('/');
                path.Append(names.Pop());
            }
            return path.ToString();
        }

        void ResolveReferences()
        {
            if (grabInteractable == null)
                grabInteractable = GetComponent<XRGrabInteractable>();
            if (grabTransformer == null)
                grabTransformer = GetComponent<ValveWheelGrabTransformer>();
        }

        void Log(string message) => Debug.Log($"{LogPrefix} {message}", this);

        void OnValidate()
        {
            minimumDeltaToLog = Mathf.Max(0.001f, minimumDeltaToLog);
            deltaLogCooldown = Mathf.Max(0.05f, deltaLogCooldown);
            ResolveReferences();
        }
    }

}
