using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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

        public float ClosurePercent => currentClosurePercent;
        public float Closure01 => currentClosurePercent * 0.01f;
        public float LeakIntensity01 => currentLeakIntensity;
        public bool IsFullyOpen => currentState == ValveState.Open;
        public bool IsFullyClosed => currentState == ValveState.FullyClosed;
        public float CurrentHingeAngle => currentHingeAngle;
        public ValveState CurrentState => currentState;
        public RotationDirection CurrentRotationDirection => currentRotationDirection;
        public bool IsGrabbed => isGrabbed;
        public float MaxOpenRotationDegrees => grabTransformer != null ? grabTransformer.MaxOpenRotationDegrees : 0f;
        public float MaxCloseRotationDegrees => grabTransformer != null ? grabTransformer.MaxCloseRotationDegrees : 0f;
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
            if (wheelRigidbody != null)
            {
                wheelRigidbody.linearVelocity = Vector3.zero;
                wheelRigidbody.angularVelocity = Vector3.zero;
            }

            initialHingeAngle = hingeJoint != null && float.IsFinite(hingeJoint.angle) ? hingeJoint.angle : 0f;
            currentHingeAngle = initialHingeAngle;
            closureAngleSign = DetermineClosureAngleSign();
            ApplyJointLimits();
        }

        void Start()
        {
            ApplyState(initialClosurePercent, true, false);
            initialized = true;
            wasFullyOpen = IsFullyOpen;
            wasFullyClosed = IsFullyClosed;

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
                ? grabTransformer.CurrentTargetAngle
                : currentHingeAngle - initialHingeAngle;
            float signedAngle = logicalAngle * closureAngleSign;
            float closure = CalculateClosurePercent(initialClosurePercent, signedAngle,
                MaxOpenRotationDegrees, MaxCloseRotationDegrees);
            ApplyState(closure, false, true);
            isGrabbed = grabInteractable != null && grabInteractable.isSelected;
        }

        public void SetClosurePercent(float percent)
        {
            ApplyState(percent, true, initialized);
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

            if (!invokeBoundaryEvents)
                return;

            if (IsFullyOpen && !wasFullyOpen)
                onFullyOpened?.Invoke();
            if (IsFullyClosed && !wasFullyClosed)
                onFullyClosed?.Invoke();
            wasFullyOpen = IsFullyOpen;
            wasFullyClosed = IsFullyClosed;
        }

        float DetermineClosureAngleSign()
        {
            if (hingeJoint == null)
                return invertRotationDirection ? -1f : 1f;

            Transform front = valveFrontReference != null ? valveFrontReference : transform;
            Vector3 axisWorld = transform.TransformDirection(hingeJoint.axis).normalized;
            float alignment = Vector3.Dot(axisWorld, front.forward) >= 0f ? 1f : -1f;
            float sign = closeDirection == CloseDirection.ClockwiseFromFront ? -alignment : alignment;
            return invertRotationDirection ? -sign : sign;
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
