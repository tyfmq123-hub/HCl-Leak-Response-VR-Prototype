using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRProject.Valves
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(HingeJoint), typeof(XRGrabInteractable))]
    public sealed class ValveWheelClosureController : MonoBehaviour
    {
        public enum CloseDirection
        {
            ClockwiseFromFront,
            CounterClockwiseFromFront,
        }

        public enum ValveState
        {
            Open,
            PartiallyClosed,
            FullyClosed,
        }

        public enum RotationDirection
        {
            Opening,
            Closing,
            Idle,
        }

        [Serializable]
        public sealed class FloatEvent : UnityEvent<float> { }

        [Header("Wheel")]
        [SerializeField] new HingeJoint hingeJoint;
        [SerializeField] Rigidbody wheelRigidbody;
        [SerializeField] XRGrabInteractable grabInteractable;
        [SerializeField] ValveWheelGrabTransformer grabTransformer;
        [Tooltip("Its forward direction identifies the side from which clockwise/counter-clockwise is viewed.")]
        [SerializeField] Transform valveFrontReference;

        [Header("Valve State")]
        [SerializeField, Range(0f, 100f)] float initialClosurePercent = 30f;
        [SerializeField] CloseDirection closeDirection = CloseDirection.ClockwiseFromFront;
        [Tooltip("Legacy serialized field retained for Scene compatibility. It is intentionally ignored; Front + Axis + Close Direction are authoritative.")]
        [SerializeField] bool invertRotationDirection;
        [SerializeField, Min(0.0001f)] float changeEpsilon = 0.01f;

        [Header("Leak")]
        [SerializeField] ValveLeakOutput leakOutput;
        [SerializeField] AnimationCurve leakIntensityByClosure = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        [SerializeField] FloatEvent onClosurePercentChanged = new FloatEvent();
        [SerializeField] FloatEvent onLeakIntensityChanged = new FloatEvent();
        [SerializeField] UnityEvent onFullyOpened = new UnityEvent();
        [SerializeField] UnityEvent onFullyClosed = new UnityEvent();

        [HideInInspector, SerializeField] float currentHingeAngle;
        [HideInInspector, SerializeField] float currentClosurePercent;
        [HideInInspector, SerializeField] float currentLeakIntensity;
        [HideInInspector, SerializeField] ValveState currentState;
        [HideInInspector, SerializeField] RotationDirection currentRotationDirection;
        [HideInInspector, SerializeField] bool isGrabbed;

        float initialHingeAngle;
        float closureAngleSign = 1f;
        bool initialized;
        bool wasFullyOpen;
        bool wasFullyClosed;
        bool closedLatched;
        bool fullyOpenHapticArmed;
        Coroutine fullyOpenHapticRoutine;

        public float ClosurePercent => currentClosurePercent;
        public float Closure01 => currentClosurePercent * 0.01f;
        public float LeakIntensity01 => currentLeakIntensity;
        public bool IsFullyOpen => currentState == ValveState.Open;
        public bool IsFullyClosed => currentState == ValveState.FullyClosed;
        public float CurrentHingeAngle => currentHingeAngle;
        public ValveState CurrentState => currentState;
        public RotationDirection CurrentRotationDirection => currentRotationDirection;
        public bool IsGrabbed => isGrabbed;
        public bool IsClosedLatched => closedLatched;
        public float MaxOpenRotationDegrees => grabTransformer != null ? grabTransformer.MaxOpenRotationDegrees : 0f;
        public float MaxCloseRotationDegrees => grabTransformer != null ? grabTransformer.MaxCloseRotationDegrees : 0f;
        public float InitialClosurePercent => initialClosurePercent;
        public FloatEvent OnClosurePercentChanged => onClosurePercentChanged;
        public FloatEvent OnLeakIntensityChanged => onLeakIntensityChanged;
        public UnityEvent OnFullyOpened => onFullyOpened;
        public UnityEvent OnFullyClosed => onFullyClosed;

        void Reset()
        {
            ResolveReferences();
            valveFrontReference = transform;
        }

        void Awake()
        {
            ResolveReferences();
            // Older Scene data can still contain this removed option. Read and clear
            // it at runtime so the serialized compatibility field does not affect the
            // authoritative Front + Axis + Close Direction convention.
            if (invertRotationDirection)
                invertRotationDirection = false;
            if (wheelRigidbody != null)
            {
                wheelRigidbody.linearVelocity = Vector3.zero;
                wheelRigidbody.angularVelocity = Vector3.zero;
            }

            initialHingeAngle = hingeJoint != null && float.IsFinite(hingeJoint.angle) ? hingeJoint.angle : 0f;
            currentHingeAngle = initialHingeAngle;
            closureAngleSign = DetermineClosureAngleSign();
            grabTransformer?.ConfigureCloseConvention(closureAngleSign);
            ApplyJointLimits();
        }

        void Start()
        {
            ApplyState(initialClosurePercent, true, false);
            initialized = true;
            wasFullyOpen = IsFullyOpen;
            wasFullyClosed = IsFullyClosed;
            fullyOpenHapticArmed = currentClosurePercent > changeEpsilon;
            if (IsFullyClosed)
                LatchClosed();

            if (leakOutput == null)
                Debug.LogWarning($"[{nameof(ValveWheelClosureController)}] No leak output is connected on '{name}'. State and events remain available.", this);
        }

        void FixedUpdate()
        {
            if (hingeJoint == null)
                return;

            float measuredAngle = hingeJoint.angle;
            if (!float.IsFinite(measuredAngle))
                return;

            currentHingeAngle = measuredAngle;
            // HingeJoint.angle can wrap after 180 degrees. The transformer owns the unwrapped,
            // clamped logical angle, so closure state uses that value whenever it is available.
            float logicalAngle = grabTransformer != null && grabTransformer.ConstraintReferenceInitialized
                ? grabTransformer.CurrentLogicalClosureAngle
                : currentHingeAngle - initialHingeAngle;
            float closure = CalculateClosurePercent(initialClosurePercent, logicalAngle,
                MaxOpenRotationDegrees, MaxCloseRotationDegrees);
            ApplyState(closure, false, true);
            isGrabbed = grabInteractable != null && grabInteractable.isSelected;
        }

        public void SetClosurePercent(float percent)
        {
            ApplyState(percent, true, initialized);
        }

        public void ConfigureRuntime(float newInitialClosurePercent, float newMaxOpenRotationDegrees,
            float newMaxCloseRotationDegrees, CloseDirection newCloseDirection)
        {
            initialClosurePercent = Mathf.Clamp(newInitialClosurePercent, 0f, 100f);
            closeDirection = newCloseDirection;
            invertRotationDirection = false;
            closedLatched = false;
            grabTransformer?.SetClosedLatched(false);
            grabTransformer?.ConfigureTravelLimits(newMaxOpenRotationDegrees, newMaxCloseRotationDegrees);
            closureAngleSign = DetermineClosureAngleSign();
            grabTransformer?.ConfigureCloseConvention(closureAngleSign);
            ApplyJointLimits();

            if (initialized)
            {
                ApplyState(initialClosurePercent, true, false);
                wasFullyOpen = IsFullyOpen;
                wasFullyClosed = IsFullyClosed;
                fullyOpenHapticArmed = currentClosurePercent > changeEpsilon;
            }
        }

        public void ApplyJointLimits()
        {
            if (grabTransformer == null)
                return;
            grabTransformer.ApplyConfiguredJointLimits();
        }

        public static float CalculateClosurePercent(float initialPercent, float signedHingeAngle,
            float maxOpenRotationDegrees, float maxCloseRotationDegrees)
        {
            float initial = Mathf.Clamp(initialPercent, 0f, 100f);
            if (!float.IsFinite(signedHingeAngle) || !float.IsFinite(maxOpenRotationDegrees) ||
                !float.IsFinite(maxCloseRotationDegrees) || maxOpenRotationDegrees < 0f ||
                maxCloseRotationDegrees < 0f)
                return initial;

            if (signedHingeAngle < 0f)
            {
                if (maxOpenRotationDegrees <= 0f)
                    return initial;
                float openProgress = Mathf.Clamp01(-signedHingeAngle / maxOpenRotationDegrees);
                return Mathf.Lerp(initial, 0f, openProgress);
            }

            if (maxCloseRotationDegrees <= 0f)
                return initial;
            float closeProgress = Mathf.Clamp01(signedHingeAngle / maxCloseRotationDegrees);
            return Mathf.Lerp(initial, 100f, closeProgress);
        }

        public static float CalculateLinearLeakIntensity01(float closurePercent)
        {
            return 1f - Mathf.Clamp01(closurePercent * 0.01f);
        }

        void ApplyState(float percent, bool force, bool invokeBoundaryEvents)
        {
            float clamped = Mathf.Clamp(percent, 0f, 100f);
            if (closedLatched && clamped < 100f)
                clamped = 100f;
            float previous = currentClosurePercent;
            float delta = clamped - previous;

            currentRotationDirection = Mathf.Abs(delta) <= changeEpsilon
                ? RotationDirection.Idle
                : delta > 0f ? RotationDirection.Closing : RotationDirection.Opening;

            bool changed = force || Mathf.Abs(delta) > changeEpsilon;
            currentClosurePercent = clamped;
            currentState = clamped <= changeEpsilon
                ? ValveState.Open
                : clamped >= 100f - changeEpsilon
                    ? ValveState.FullyClosed
                    : ValveState.PartiallyClosed;

            float leak = leakIntensityByClosure == null || leakIntensityByClosure.length == 0
                ? CalculateLinearLeakIntensity01(clamped)
                : Mathf.Clamp01(leakIntensityByClosure.Evaluate(clamped * 0.01f));
            bool leakChanged = force || Mathf.Abs(leak - currentLeakIntensity) > changeEpsilon * 0.01f;
            currentLeakIntensity = leak;

            if (changed)
                onClosurePercentChanged?.Invoke(currentClosurePercent);
            if (leakChanged)
                onLeakIntensityChanged?.Invoke(currentLeakIntensity);
            if (leakChanged)
                leakOutput?.SetLeakIntensity01(currentLeakIntensity);

            if (clamped > changeEpsilon && !IsFullyClosed)
                fullyOpenHapticArmed = true;

            if (invokeBoundaryEvents)
            {
                if (IsFullyOpen && !wasFullyOpen)
                {
                    onFullyOpened?.Invoke();
                    if (ShouldTriggerFullyOpenHaptic(fullyOpenHapticArmed, wasFullyOpen, IsFullyOpen))
                    {
                        fullyOpenHapticArmed = false;
                        StartFullyOpenHaptic();
                    }
                }

                if (IsFullyClosed && !wasFullyClosed)
                {
                    // Boundary observers must run before the mechanical latch is applied.
                    onFullyClosed?.Invoke();
                    SendHapticToSelectingInteractors(0.75f, 0.9f);
                    LatchClosed();
                }
            }
            wasFullyOpen = IsFullyOpen;
            wasFullyClosed = IsFullyClosed;
        }

        float DetermineClosureAngleSign()
        {
            if (hingeJoint == null)
                return closeDirection == CloseDirection.ClockwiseFromFront ? -1f : 1f;

            Transform front = valveFrontReference != null ? valveFrontReference : transform;
            Vector3 axisWorld = transform.TransformDirection(hingeJoint.axis).normalized;
            return CalculateCloseAngleSign(axisWorld, front.forward, closeDirection);
        }

        public static float CalculateCloseAngleSign(Vector3 axisWorld, Vector3 frontForward,
            CloseDirection direction)
        {
            float alignment = Vector3.Dot(axisWorld.normalized, frontForward.normalized) >= 0f ? 1f : -1f;
            return direction == CloseDirection.ClockwiseFromFront ? -alignment : alignment;
        }

        public static bool ShouldTriggerFullyOpenHaptic(bool armed, bool wasOpen, bool isOpen)
        {
            return armed && !wasOpen && isOpen;
        }

        void LatchClosed()
        {
            if (closedLatched)
                return;

            closedLatched = true;
            grabTransformer?.SetClosedLatched(true);
            if (wheelRigidbody != null)
            {
                wheelRigidbody.linearVelocity = Vector3.zero;
                wheelRigidbody.angularVelocity = Vector3.zero;
            }
        }

        void StartFullyOpenHaptic()
        {
            if (fullyOpenHapticRoutine != null)
                StopCoroutine(fullyOpenHapticRoutine);
            fullyOpenHapticRoutine = StartCoroutine(PlayFullyOpenHaptic());
        }

        IEnumerator PlayFullyOpenHaptic()
        {
            for (int pulse = 0; pulse < 3; ++pulse)
            {
                SendHapticToSelectingInteractors(0.55f, 0.08f);
                if (pulse < 2)
                    yield return new WaitForSeconds(0.16f);
            }

            fullyOpenHapticRoutine = null;
        }

        void SendHapticToSelectingInteractors(float amplitude, float duration)
        {
            if (grabInteractable == null)
                return;

            for (int i = 0; i < grabInteractable.interactorsSelecting.Count; ++i)
                TrySendHapticImpulse(grabInteractable.interactorsSelecting[i], amplitude, duration);
        }

        static bool TrySendHapticImpulse(object interactor, float amplitude, float duration)
        {
            return interactor is XRBaseInputInteractor inputInteractor &&
                   inputInteractor.SendHapticImpulse(amplitude, duration);
        }

        void ResolveReferences()
        {
            if (hingeJoint == null) hingeJoint = GetComponent<HingeJoint>();
            if (wheelRigidbody == null) wheelRigidbody = GetComponent<Rigidbody>();
            if (grabInteractable == null) grabInteractable = GetComponent<XRGrabInteractable>();
            if (grabTransformer == null) grabTransformer = GetComponent<ValveWheelGrabTransformer>();
            if (valveFrontReference == null) valveFrontReference = transform;
        }

        void OnValidate()
        {
            initialClosurePercent = Mathf.Clamp(initialClosurePercent, 0f, 100f);
            changeEpsilon = Mathf.Max(0.0001f, changeEpsilon);
            ResolveReferences();
        }

        void OnDrawGizmosSelected()
        {
            HingeJoint joint = hingeJoint != null ? hingeJoint : GetComponent<HingeJoint>();
            if (joint == null) return;
            Vector3 origin = transform.TransformPoint(joint.anchor);
            Vector3 axis = transform.TransformDirection(joint.axis).normalized;
            float size = HandleSize();
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin - axis * size, origin + axis * size);
            Gizmos.color = Color.cyan;
            Transform front = valveFrontReference != null ? valveFrontReference : transform;
            Gizmos.DrawRay(origin, front.forward * size);
        }

        float HandleSize()
        {
            Renderer rendererComponent = GetComponent<Renderer>();
            return rendererComponent != null ? Mathf.Max(0.1f, rendererComponent.bounds.extents.magnitude * 0.4f) : 0.25f;
        }
    }
}
