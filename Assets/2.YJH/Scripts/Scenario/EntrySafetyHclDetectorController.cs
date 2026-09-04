using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JH.EntrySafety
{
    /// <summary>Controls the single grabbable HCl detector across both Goal 1 measurements.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class EntrySafetyHclDetectorController : MonoBehaviour
    {
        private const string RetrievalAnchorName = "PreEntry_ItemRetrievalAnchor";
        private const string TrackedWristName = "L_Wrist";
        private const float PresentationSeconds = 0.28f;
        private const float ConcealedHeight = -0.18f;
        private const float ReadyHeight = 0.58f;
        private const float MinimumVisibleDetectorExtent = 0.08f;
        private const float GripHandDisplayClearance = 0.055f;
        private static readonly Quaternion PresentationLocalRotation = Quaternion.Euler(0f, 0f, 90f);

        [SerializeField] private GameObject _detectorInstance;
        [SerializeField] private EntrySafetyHclDetectorView _view;
        [SerializeField, Range(-1f, 1f)] private float _minimumDisplayGazeDot = 0.75f;
        [SerializeField, Min(0.01f)] private float _requiredHoldSeconds = 3f;
        [SerializeField, Min(0f)] private float _initialResultPpm = 3f;
        [SerializeField, Min(0f)] private float _reducedResultPpm = 0.8f;

        private EntrySafetyTriggerHoldTimer _initialTriggerTimer;
        private EntrySafetyTriggerHoldTimer _reducedTriggerTimer;
        private Camera _gazeCamera;
        private Transform _originalParent;
        private Vector3 _originalLocalPosition;
        private Quaternion _originalLocalRotation;
        private Vector3 _originalLocalScale;
        private Transform _presentationAnchor;
        private XRGrabInteractable _grabInteractable;
        private Rigidbody _rigidbody;
        private BoxCollider _grabCollider;
        private Coroutine _presentationRoutine;
        private Coroutine _equippedConfirmationRoutine;
        private Transform _heldHandAnchor;
        private Transform _spawnedGripGhost;
        private Transform _displayClearedGripGhost;
        private Vector3 _gripGhostWristLocalPosition;
        private Quaternion _gripGhostWristLocalRotation;
        private bool _hasCapturedGripPose;
        private Transform _heldWristAnchor;
        private GameObject _heldGripHandVisual;
        private readonly Dictionary<string, GhostBonePose> _capturedGhostBonePoses =
            new Dictionary<string, GhostBonePose>(StringComparer.Ordinal);
        private readonly List<HandBonePoseBinding> _handBonePoseBindings = new List<HandBonePoseBinding>();
        private readonly List<AnimatorState> _handAnimatorStates = new List<AnimatorState>();
        private readonly List<RendererState> _heldHandRendererStates = new List<RendererState>();

        private struct HandBonePoseBinding
        {
            public Transform HandBone;
            public Vector3 OriginalLocalPosition;
            public Quaternion OriginalLocalRotation;
            public Vector3 OriginalLocalScale;
            public Vector3 TargetLocalScale;
            public Vector3 TargetDetectorLocalPosition;
            public Quaternion TargetDetectorLocalRotation;
        }

        private struct GhostBonePose
        {
            public Vector3 LocalScale;
            public Vector3 DetectorLocalPosition;
            public Quaternion DetectorLocalRotation;
        }

        private struct AnimatorState
        {
            public Animator Animator;
            public bool WasEnabled;
        }

        private struct RendererState
        {
            public Renderer Renderer;
            public bool WasEnabled;
        }

        public event Action DetectorGrabbed;
        public bool IsEquipped { get; private set; }
        public bool IsAwaitingPickup { get; private set; }
        public bool IsPickupReady { get; private set; }
        public bool IsMeasuring => IsInitialMeasuring || IsReducedMeasuring;
        public bool IsInitialMeasuring => _initialTriggerTimer != null && _initialTriggerTimer.IsHolding && !_initialTriggerTimer.IsCompleted;
        public bool IsReducedMeasuring => _reducedTriggerTimer != null && _reducedTriggerTimer.IsHolding && !_reducedTriggerTimer.IsCompleted;
        public float MeasureProgress01 => _initialTriggerTimer?.Progress01 ?? 0f;
        public float ReducedMeasureProgress01 => _reducedTriggerTimer?.Progress01 ?? 0f;
        public bool HasInitialResult { get; private set; }
        public bool HasReducedResult { get; private set; }
        public float InitialResultPpm => _initialResultPpm;
        public float ReducedResultPpm => _reducedResultPpm;
        public Transform DetectorTransform => _detectorInstance != null ? _detectorInstance.transform : null;
        public Transform ProgressAnchor
        {
            get
            {
                if (_view != null && _view.DisplayTransform != null)
                    return _view.DisplayTransform;
                return _detectorInstance != null ? _detectorInstance.transform : null;
            }
        }

        public bool IsLookingAtDisplay
        {
            get
            {
                var displayTransform = _view == null ? null : _view.DisplayTransform;
                _gazeCamera = EntrySafetyAppXrResolve.PreferLiveMainCamera(_gazeCamera);
                if (displayTransform == null || _gazeCamera == null)
                    return false;

                var cameraTransform = _gazeCamera.transform;
                var directionToDisplay = displayTransform.position - cameraTransform.position;
                return directionToDisplay.sqrMagnitude > Mathf.Epsilon &&
                       Vector3.Dot(cameraTransform.forward, directionToDisplay.normalized) >= _minimumDisplayGazeDot;
            }
        }

        private void Awake()
        {
            ResolveCanonicalDetector();
            CaptureOriginalPose();
            EnsureTriggerTimers();
            if (_detectorInstance != null)
                _detectorInstance.SetActive(false);
        }

        private void OnEnable()
        {
            Application.onBeforeRender += ApplyHeldPoseForRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ApplyHeldPoseForRender;
        }

        private void OnDestroy()
        {
            ReleaseCapturedHandPresentation();
            StopPresentationRoutines();
        }

        private void LateUpdate()
        {
            ApplyHeldPoseForRender();
        }

        private void ApplyHeldPoseForRender()
        {
            if (IsEquipped && _heldHandAnchor != null && _hasCapturedGripPose)
                FollowCapturedGripPose();
        }

        public bool TryEquip()
        {
            return TryPresentForPickup();
        }

        public bool TryPresentForPickup()
        {
            ResolveCanonicalDetector();
            var anchor = ResolveRetrievalAnchor();
            if (IsEquipped || IsAwaitingPickup || _detectorInstance == null || anchor == null)
                return false;

            _presentationAnchor = anchor;
            _detectorInstance.SetActive(true);
            var detectorTransform = _detectorInstance.transform;
            // The authored detector starts below the disabled Scene-only XR rig. That
            // rig is collapsed to zero scale by integration, so worldPositionStays=true
            // copied a zero world scale into the live detector. Reparent in local space
            // and explicitly restore the authored scale before fitting its collider.
            detectorTransform.SetParent(anchor, false);
            detectorTransform.localScale = HasUsableScale(_originalLocalScale)
                ? _originalLocalScale
                : Vector3.one;
            SetPresentationPose(ConcealedHeight);
            EnsurePhysicalGrab();
            if (_grabInteractable == null || _grabCollider == null)
            {
                RestoreOriginalPose();
                _detectorInstance.SetActive(false);
                _presentationAnchor = null;
                return false;
            }

            StopPresentationRoutines();
            IsAwaitingPickup = true;
            IsPickupReady = false;
            _grabInteractable.enabled = false;
            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;
            _view?.ShowReady();
            _presentationRoutine = StartCoroutine(AnimatePresentation());
            Debug.Log(
                $"[PreEntry Detector] Presentation started at {_detectorInstance.transform.position}; " +
                $"anchor={anchor.position}.", this);
            return true;
        }

        public bool TryCompletePickup(Transform fallbackHandAnchor)
        {
            if (!IsAwaitingPickup || !IsPickupReady || _detectorInstance == null ||
                !_detectorInstance.activeInHierarchy)
                return false;

            var handAnchor = ResolveTrackedLeftHandAnchor(fallbackHandAnchor);
            if (handAnchor == null || !TryLatchToCapturedHand(handAnchor))
                return false;

            StopPresentationRoutines();
            _presentationAnchor = null;
            IsAwaitingPickup = false;
            IsPickupReady = false;
            IsEquipped = true;
            _view?.ShowReady();
            _equippedConfirmationRoutine = StartCoroutine(ConfirmEquippedPresentation());
            return true;
        }

        public bool UpdateInitialMeasurement(bool triggerPressed, float deltaTime)
        {
            return UpdateInitialMeasurement(triggerPressed, true, deltaTime);
        }

        public bool UpdateInitialMeasurement(bool triggerPressed, bool isLookingAtDisplay, float deltaTime)
        {
            if (!IsEquipped || HasInitialResult)
                return false;

            EnsureTriggerTimers();
            _initialTriggerTimer.Update(triggerPressed && isLookingAtDisplay, deltaTime);
            if (_initialTriggerTimer.IsCompleted)
            {
                HasInitialResult = true;
                _view?.ShowInitialResult(_initialResultPpm);
                return true;
            }

            if (_initialTriggerTimer.IsHolding)
                _view?.ShowMeasuring(_initialTriggerTimer.Progress01);
            else
                _view?.ShowReady();
            return false;
        }

        public bool UpdateReducedMeasurement(bool triggerPressed, float deltaTime)
        {
            return UpdateReducedMeasurement(triggerPressed, true, deltaTime);
        }

        public bool UpdateReducedMeasurement(bool triggerPressed, bool isLookingAtDisplay, float deltaTime)
        {
            if (!IsEquipped || !HasInitialResult || HasReducedResult)
                return false;

            EnsureTriggerTimers();
            _reducedTriggerTimer.Update(triggerPressed && isLookingAtDisplay, deltaTime);
            if (_reducedTriggerTimer.IsCompleted)
            {
                HasReducedResult = true;
                _view?.ShowReducedResult(_reducedResultPpm);
                return true;
            }

            if (_reducedTriggerTimer.IsHolding)
                _view?.ShowReducedMeasuring(_reducedTriggerTimer.Progress01, _initialResultPpm, _reducedResultPpm);
            else
                _view?.ShowInitialResult(_initialResultPpm);
            return false;
        }

        public bool TryStore()
        {
            if (!IsEquipped || !HasReducedResult)
                return false;

            ReleaseCapturedHandPresentation();
            StopPresentationRoutines();
            if (_grabInteractable != null)
                _grabInteractable.enabled = false;
            _detectorInstance.SetActive(false);
            IsEquipped = false;
            IsAwaitingPickup = false;
            IsPickupReady = false;
            return true;
        }

        public void ResetDetector()
        {
            ResolveCanonicalDetector();
            ReleaseCapturedHandPresentation();
            StopPresentationRoutines();
            if (_grabInteractable != null)
                _grabInteractable.enabled = false;
            IsEquipped = false;
            IsAwaitingPickup = false;
            IsPickupReady = false;
            HasInitialResult = false;
            HasReducedResult = false;
            EnsureTriggerTimers();
            _initialTriggerTimer.Reset();
            _reducedTriggerTimer.Reset();
            if (_detectorInstance != null)
            {
                RestoreOriginalPose();
                _detectorInstance.SetActive(false);
            }
        }

        private void EnsureTriggerTimers()
        {
            if (_initialTriggerTimer == null)
                _initialTriggerTimer = new EntrySafetyTriggerHoldTimer(_requiredHoldSeconds);
            if (_reducedTriggerTimer == null)
                _reducedTriggerTimer = new EntrySafetyTriggerHoldTimer(_requiredHoldSeconds);
        }

        private void ResolveCanonicalDetector()
        {
            // The integrated XR hierarchy contains a transform-only path matching the
            // authored detector. Reference rebinding can therefore point the GameObject
            // field at that empty path object while the serialized view still belongs to
            // the real detector prefab. The view and detector are authored on the same
            // root, so restore that canonical reference before moving it to the live hand.
            if (_view != null && _detectorInstance != _view.gameObject)
                _detectorInstance = _view.gameObject;

        }

        private void CaptureOriginalPose()
        {
            if (_detectorInstance == null || _originalParent != null)
                return;

            var detectorTransform = _detectorInstance.transform;
            _originalParent = detectorTransform.parent;
            _originalLocalPosition = detectorTransform.localPosition;
            _originalLocalRotation = detectorTransform.localRotation;
            _originalLocalScale = detectorTransform.localScale;
        }

        private void RestoreOriginalPose()
        {
            if (_detectorInstance == null)
                return;

            var detectorTransform = _detectorInstance.transform;
            detectorTransform.SetParent(_originalParent, false);
            detectorTransform.localPosition = _originalLocalPosition;
            detectorTransform.localRotation = _originalLocalRotation;
            detectorTransform.localScale = _originalLocalScale;
        }

        private static Transform ResolveRetrievalAnchor()
        {
            var named = GameObject.Find(RetrievalAnchorName);
            if (named != null)
                return named.transform;

            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == RetrievalAnchorName)
                    return transforms[i];
            }
            return null;
        }

        private void EnsurePhysicalGrab()
        {
            ResolveCanonicalDetector();
            if (_detectorInstance == null)
                return;

            _rigidbody = _detectorInstance.GetComponent<Rigidbody>();
            if (_rigidbody == null)
                _rigidbody = _detectorInstance.AddComponent<Rigidbody>();
            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;

            _grabCollider = _detectorInstance.GetComponent<BoxCollider>();
            if (_grabCollider == null)
                _grabCollider = _detectorInstance.AddComponent<BoxCollider>();
            FitGrabCollider(_grabCollider, _detectorInstance.transform);
            _grabCollider.isTrigger = false;
            _grabCollider.enabled = true;

            _grabInteractable = _detectorInstance.GetComponent<XRGrabInteractable>();
            if (_grabInteractable == null)
                _grabInteractable = _detectorInstance.AddComponent<XRGrabInteractable>();
            _grabInteractable.colliders.Clear();
            _grabInteractable.colliders.Add(_grabCollider);
            // XRI is used only to recognize the second Grip. The authored Ghost owns the
            // final detector/hand pose, so XRI must never run its own attach calculation.
            // Dynamic attachment briefly produced NaN and advanced the scenario before
            // the detector was visible on Quest.
            _grabInteractable.useDynamicAttach = false;
            _grabInteractable.matchAttachPosition = false;
            _grabInteractable.matchAttachRotation = false;
            _grabInteractable.snapToColliderVolume = false;
            _grabInteractable.throwOnDetach = false;
            _grabInteractable.selectMode = InteractableSelectMode.Single;
            _grabInteractable.movementType = XRGrabInteractable.MovementType.Kinematic;
            _grabInteractable.trackPosition = false;
            _grabInteractable.trackRotation = false;
            // The scenario's deliberate Grip gate owns both presses. Keep XRI disabled so
            // a stale Select state cannot consume the opening Grip and auto-equip the item.
            _grabInteractable.enabled = false;
        }

        private IEnumerator AnimatePresentation()
        {
            var elapsed = 0f;
            while (elapsed < PresentationSeconds && IsAwaitingPickup && _detectorInstance != null &&
                   _presentationAnchor != null)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / PresentationSeconds);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                SetPresentationPose(Mathf.LerpUnclamped(ConcealedHeight, ReadyHeight, eased));
                yield return null;
            }

            if (IsAwaitingPickup && _detectorInstance != null && _presentationAnchor != null)
                SetPresentationPose(ReadyHeight);
            _presentationRoutine = null;

            // The input adapter requires a full release before it can emit the second
            // deliberate Grip. Arm the pickup only after the detector is fully visible
            // and the authored Ghost pose has been captured.
            IsPickupReady = IsAwaitingPickup && CacheGripGhostPose();
            if (IsAwaitingPickup && !IsPickupReady)
                Debug.LogError("[PreEntry Detector] Detector is visible, but its authored Ghost pose was not available.", this);
        }

        private bool TryLatchToCapturedHand(Transform handAnchor)
        {
            if (handAnchor == null || _detectorInstance == null)
                return false;

            // XRI may move the detector before selectEntered is delivered. Capture the
            // authored Ghost while it is still waiting over the bag, then use that
            // immutable snapshot instead of depending on the attachment hierarchy here.
            if (!_hasCapturedGripPose && !CacheGripGhostPose())
                return false;

            _heldHandAnchor = handAnchor;
            _heldWristAnchor = FindActiveDescendantByName(handAnchor, TrackedWristName);
            if (_heldWristAnchor == null)
            {
                _heldHandAnchor = null;
                Debug.LogError("[PreEntry Detector] The active left wearable has no L_Wrist transform.", this);
                return false;
            }

            ApplyCapturedPoseToTrackedHand(handAnchor);
            // The detector must leave the bag hierarchy before the bag or its scene is
            // hidden. Parenting it to the exact live wearable hand also makes the
            // authored Ghost offset deterministic across additive Scene handoffs.
            _detectorInstance.transform.SetParent(handAnchor, true);
            if (!FollowCapturedGripPose())
            {
                RestoreTrackedHandPose();
                _heldHandAnchor = null;
                _heldWristAnchor = null;
                return false;
            }
            if (!CreateHeldGripHandVisual(handAnchor))
            {
                RestoreTrackedHandPose();
                _heldHandAnchor = null;
                _heldWristAnchor = null;
                return false;
            }
            HideGripGhostRenderers();
            return true;
        }

        private bool CacheGripGhostPose()
        {
            if (_detectorInstance == null)
                return false;

            var ghost = ResolveRuntimeGripGhost(_detectorInstance.transform);
            if (ghost == null || !ghost.IsChildOf(_detectorInstance.transform))
                return false;

            var detectorTransform = _detectorInstance.transform;
            _spawnedGripGhost = ghost;
            if (_displayClearedGripGhost != ghost && _view != null && _view.DisplayTransform != null)
            {
                // Keep the authored finger pose, but place the palm behind the front
                // display instead of through it. This same adjusted pose is used by the
                // cyan guide and by the orange held-hand replacement.
                // TextMeshPro's readable face is opposite Transform.forward. Move the
                // hand along +forward so it sits behind the readable display, not in
                // front of it.
                ghost.position += _view.DisplayTransform.forward * GripHandDisplayClearance;
                _displayClearedGripGhost = ghost;
            }
            var ghostWrist = FindActiveDescendantByName(ghost, TrackedWristName, includeInactive: true);
            if (ghostWrist == null)
            {
                _spawnedGripGhost = null;
                return false;
            }
            _gripGhostWristLocalPosition = detectorTransform.InverseTransformPoint(ghostWrist.position);
            _gripGhostWristLocalRotation = Quaternion.Inverse(detectorTransform.rotation) * ghostWrist.rotation;
            if (!IsFinite(_gripGhostWristLocalPosition) || !IsFinite(_gripGhostWristLocalRotation) ||
                QuaternionMagnitudeSquared(_gripGhostWristLocalRotation) < 0.000001f)
            {
                _spawnedGripGhost = null;
                return false;
            }
            _capturedGhostBonePoses.Clear();

            var ghostBones = ghost.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < ghostBones.Length; i++)
            {
                var ghostBone = ghostBones[i];
                if (ghostBone == null || !ghostBone.name.StartsWith("L_", StringComparison.Ordinal) ||
                    _capturedGhostBonePoses.ContainsKey(ghostBone.name))
                    continue;

                _capturedGhostBonePoses.Add(ghostBone.name, new GhostBonePose
                {
                    LocalScale = ghostBone.localScale,
                    DetectorLocalPosition = detectorTransform.InverseTransformPoint(ghostBone.position),
                    DetectorLocalRotation = Quaternion.Inverse(detectorTransform.rotation) * ghostBone.rotation
                });
            }

            _hasCapturedGripPose = _capturedGhostBonePoses.Count > 0;
            return _hasCapturedGripPose;
        }

        private static Transform ResolveRuntimeGripGhost(Transform detectorTransform)
        {
            if (detectorTransform == null)
                return null;

            var attachment = detectorTransform.GetComponentInParent<EntrySafetyHandGhostAttachment>(true);
            if (attachment == null)
                return null;

            attachment.RefreshAttachment();
            return attachment.SpawnedGhost != null ? attachment.SpawnedGhost.transform : null;
        }

        private static Transform ResolveTrackedLeftHandAnchor(Transform trackedLeftController = null)
        {
            // Use the exact Common XR controller already validated by the bag zone. When
            // the wearable chemical glove is active below that controller, pose its wrist;
            // otherwise fall back to the tracked controller itself. Do not perform an
            // unrelated global Left Controller search in an additive scene stack.
            var wearable = FindActiveWearableBelow(trackedLeftController);
            if (wearable == null)
                wearable = EntrySafetyAppXrResolve.FindActiveByName("PPE_VrHand_Left_Wearable");
            if (wearable != null &&
                (trackedLeftController == null || wearable.IsChildOf(trackedLeftController)))
                return wearable;

            // PreEntry requires the chemical glove equipped in PPE. Falling back to a
            // controller origin makes the detector technically active but sends it away
            // from the rendered hand, which looks exactly like a missing detector.
            Debug.LogError("[PreEntry Detector] Active left wearable hand was not found; pickup was not completed.");
            return null;
        }

        private static Transform FindActiveWearableBelow(Transform trackedLeftController)
        {
            if (trackedLeftController == null || !trackedLeftController.gameObject.activeInHierarchy)
                return null;

            var descendants = trackedLeftController.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < descendants.Length; i++)
            {
                var candidate = descendants[i];
                if (candidate != null && candidate.name == "PPE_VrHand_Left_Wearable" &&
                    candidate.gameObject.activeInHierarchy)
                    return candidate;
            }
            return null;
        }

        private bool FollowCapturedGripPose()
        {
            if (_detectorInstance == null || _heldHandAnchor == null || _heldWristAnchor == null ||
                !_hasCapturedGripPose)
                return false;

            var detectorTransform = _detectorInstance.transform;
            // Align the real tracked wrist to the authored ghost wrist. The wearable hand
            // root has a model-specific correction rotation, so aligning root-to-root can
            // leave the detector active but outside the rendered hand/HMD view.
            var targetRotation = _heldWristAnchor.rotation * Quaternion.Inverse(_gripGhostWristLocalRotation);
            var scaledGhostOffset = Vector3.Scale(
                _gripGhostWristLocalPosition,
                detectorTransform.lossyScale);
            var targetPosition = _heldWristAnchor.position - targetRotation * scaledGhostOffset;
            if (!IsFinite(targetPosition) || !IsFinite(targetRotation) ||
                QuaternionMagnitudeSquared(targetRotation) < 0.000001f)
            {
                Debug.LogError("[PreEntry Detector] Rejected an invalid Ghost grip transform before it reached the Rigidbody.", this);
                return false;
            }

            detectorTransform.SetPositionAndRotation(targetPosition, targetRotation);
            ApplyBoundHandBonePose();
            HideGripGhostRenderers();
            return true;
        }

        private IEnumerator ConfirmEquippedPresentation()
        {
            // Do not advance the Scenario on the same input frame. The detector must
            // survive one rendered frame on the live hand with an enabled renderer.
            // Headless batch mode has no rendered end-of-frame callback, so use one
            // normal frame there and preserve the rendered-frame check everywhere else.
            if (Application.isBatchMode)
                yield return null;
            else
                yield return new WaitForEndOfFrame();
            _equippedConfirmationRoutine = null;

            if (!IsEquipped || _detectorInstance == null || !_detectorInstance.activeInHierarchy ||
                _heldHandAnchor == null || _heldWristAnchor == null ||
                !TryGetDetectorVisualBounds(out var visualBounds) ||
                Vector3.Distance(visualBounds.center, _heldWristAnchor.position) > 0.45f)
            {
                Debug.LogError("[PreEntry Detector] Pickup was rejected because the held detector was not visibly attached.", this);
                IsEquipped = false;
                IsAwaitingPickup = true;
                IsPickupReady = true;
                yield break;
            }

            Debug.Log(
                $"[PreEntry Detector] Pickup confirmed; parent={_heldHandAnchor.name}, " +
                $"wristDistance={Vector3.Distance(visualBounds.center, _heldWristAnchor.position):F3}m, " +
                $"visualCenter={visualBounds.center}.", this);
            DetectorGrabbed?.Invoke();
        }

        public bool TryGetDetectorVisualBounds(out Bounds visualBounds)
        {
            visualBounds = default;
            if (_detectorInstance == null)
                return false;
            var renderers = _detectorInstance.GetComponentsInChildren<Renderer>(true);
            var found = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (IsDetectorBodyRenderer(renderer))
                {
                    if (!found)
                    {
                        visualBounds = renderer.bounds;
                        found = true;
                    }
                    else
                    {
                        visualBounds.Encapsulate(renderer.bounds);
                    }
                }
            }
            if (!found || !IsFinite(visualBounds.center) || !IsFinite(visualBounds.size))
                return false;

            var largestExtent = Mathf.Max(
                visualBounds.size.x,
                Mathf.Max(visualBounds.size.y, visualBounds.size.z));
            return largestExtent >= MinimumVisibleDetectorExtent;
        }

        private static bool HasUsableScale(Vector3 scale)
        {
            return IsFinite(scale) &&
                   Mathf.Abs(scale.x) > 0.0001f &&
                   Mathf.Abs(scale.y) > 0.0001f &&
                   Mathf.Abs(scale.z) > 0.0001f;
        }

        private bool IsDetectorBodyRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer.GetComponent<TMPro.TMP_Text>() != null)
                return false;

            // The runtime attachment can destroy/recreate its Ghost, which turns the
            // cached Unity reference into fake-null. Identify that hierarchy by its
            // stable container name instead of trusting the cached object reference.
            for (var current = renderer.transform;
                 current != null && current != _detectorInstance.transform;
                 current = current.parent)
            {
                if (current.name == "VRHandPoseTool_Ghosts")
                    return false;
                if (current.name == "PreEntry_DetectorHeldHand")
                    return false;
            }
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float QuaternionMagnitudeSquared(Quaternion value)
        {
            return value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        }

        private void HideGripGhostRenderers()
        {
            if (_spawnedGripGhost == null)
                return;

            var renderers = _spawnedGripGhost.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = false;
            }
        }

        private void ApplyCapturedPoseToTrackedHand(Transform handAnchor)
        {
            RestoreTrackedHandPose();
            if (handAnchor == null || _capturedGhostBonePoses.Count == 0)
                return;

            var animators = handAnchor.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;
                _handAnimatorStates.Add(new AnimatorState
                {
                    Animator = animator,
                    WasEnabled = animator.enabled
                });
                animator.enabled = false;
            }

            var handBonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var handTransforms = handAnchor.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < handTransforms.Length; i++)
            {
                var candidate = handTransforms[i];
                if (candidate != null && !handBonesByName.ContainsKey(candidate.name))
                    handBonesByName.Add(candidate.name, candidate);
            }

            foreach (var pair in _capturedGhostBonePoses)
            {
                // The controller tracks the real wrist. Replacing its local pose with the
                // prefab wrist would move the reference used to align the detector.
                if (pair.Key == TrackedWristName)
                    continue;
                if (!handBonesByName.TryGetValue(pair.Key, out var handBone))
                    continue;

                var pose = pair.Value;
                _handBonePoseBindings.Add(new HandBonePoseBinding
                {
                    HandBone = handBone,
                    OriginalLocalPosition = handBone.localPosition,
                    OriginalLocalRotation = handBone.localRotation,
                    OriginalLocalScale = handBone.localScale,
                    TargetLocalScale = pose.LocalScale,
                    TargetDetectorLocalPosition = pose.DetectorLocalPosition,
                    TargetDetectorLocalRotation = pose.DetectorLocalRotation
                });
            }

            _handBonePoseBindings.Sort((left, right) =>
                GetHierarchyDepth(left.HandBone).CompareTo(GetHierarchyDepth(right.HandBone)));

            ApplyBoundHandBonePose();
        }

        private void ApplyBoundHandBonePose()
        {
            if (_detectorInstance == null)
                return;

            var detectorTransform = _detectorInstance.transform;
            for (var i = 0; i < _handBonePoseBindings.Count; i++)
            {
                var binding = _handBonePoseBindings[i];
                if (binding.HandBone == null)
                    continue;
                binding.HandBone.localScale = binding.TargetLocalScale;
                // The live wearable has a different outer model correction than the
                // authored Ghost. Copying only bone-local rotations therefore left the
                // real palm rotated away from the Ghost. Rebuild each bone from its
                // detector-relative authored pose so the orange hand occupies the exact
                // cyan Ghost silhouette regardless of those outer corrections.
                binding.HandBone.SetPositionAndRotation(
                    detectorTransform.TransformPoint(binding.TargetDetectorLocalPosition),
                    detectorTransform.rotation * binding.TargetDetectorLocalRotation);
            }
        }

        private static int GetHierarchyDepth(Transform transform)
        {
            var depth = 0;
            for (var current = transform; current != null; current = current.parent)
                depth++;
            return depth;
        }

        private void RestoreTrackedHandPose()
        {
            for (var i = 0; i < _handBonePoseBindings.Count; i++)
            {
                var binding = _handBonePoseBindings[i];
                if (binding.HandBone == null)
                    continue;
                binding.HandBone.localPosition = binding.OriginalLocalPosition;
                binding.HandBone.localRotation = binding.OriginalLocalRotation;
                binding.HandBone.localScale = binding.OriginalLocalScale;
            }
            _handBonePoseBindings.Clear();

            for (var i = 0; i < _handAnimatorStates.Count; i++)
            {
                var state = _handAnimatorStates[i];
                if (state.Animator != null)
                    state.Animator.enabled = state.WasEnabled;
            }
            _handAnimatorStates.Clear();
        }

        private void ReleaseCapturedHandPresentation()
        {
            RestoreHeldHandRenderers();
            RestoreTrackedHandPose();
            _heldHandAnchor = null;
            _heldWristAnchor = null;
            _spawnedGripGhost = null;
            _displayClearedGripGhost = null;
            _hasCapturedGripPose = false;
            _capturedGhostBonePoses.Clear();
        }

        private bool CreateHeldGripHandVisual(Transform handAnchor)
        {
            RestoreHeldHandRenderers();
            if (_spawnedGripGhost == null || _detectorInstance == null || handAnchor == null)
                return false;

            SkinnedMeshRenderer sourceRenderer = null;
            var wearableRenderers = handAnchor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (var i = 0; i < wearableRenderers.Length; i++)
            {
                var candidate = wearableRenderers[i];
                if (candidate == null || !candidate.gameObject.activeInHierarchy)
                    continue;
                if (sourceRenderer == null || candidate.bounds.size.sqrMagnitude > sourceRenderer.bounds.size.sqrMagnitude)
                    sourceRenderer = candidate;
            }
            if (sourceRenderer == null)
                return false;

            _heldGripHandVisual = Instantiate(
                _spawnedGripGhost.gameObject,
                _detectorInstance.transform,
                false);
            _heldGripHandVisual.name = "PreEntry_DetectorHeldHand";

            var heldRenderers = _heldGripHandVisual.GetComponentsInChildren<Renderer>(true);
            if (heldRenderers.Length == 0)
            {
                Destroy(_heldGripHandVisual);
                _heldGripHandVisual = null;
                return false;
            }

            var sourceMaterials = sourceRenderer.sharedMaterials;
            for (var i = 0; i < heldRenderers.Length; i++)
            {
                var renderer = heldRenderers[i];
                if (renderer == null)
                    continue;
                if (sourceMaterials.Length > 0)
                {
                    var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                    for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                        materials[materialIndex] = sourceMaterials[Mathf.Min(materialIndex, sourceMaterials.Length - 1)];
                    renderer.sharedMaterials = materials;
                }
                renderer.SetPropertyBlock(null);
                renderer.enabled = true;
            }

            for (var i = 0; i < wearableRenderers.Length; i++)
            {
                var renderer = wearableRenderers[i];
                if (renderer == null)
                    continue;
                _heldHandRendererStates.Add(new RendererState
                {
                    Renderer = renderer,
                    WasEnabled = renderer.enabled
                });
                renderer.enabled = false;
            }
            return true;
        }

        private void RestoreHeldHandRenderers()
        {
            for (var i = 0; i < _heldHandRendererStates.Count; i++)
            {
                var state = _heldHandRendererStates[i];
                if (state.Renderer != null)
                    state.Renderer.enabled = state.WasEnabled;
            }
            _heldHandRendererStates.Clear();

            if (_heldGripHandVisual != null)
            {
                Destroy(_heldGripHandVisual);
                _heldGripHandVisual = null;
            }
        }

        private static Transform FindActiveDescendantByName(
            Transform root,
            string objectName,
            bool includeInactive = false)
        {
            if (root == null)
                return null;

            var descendants = root.GetComponentsInChildren<Transform>(includeInactive);
            for (var i = 0; i < descendants.Length; i++)
            {
                var candidate = descendants[i];
                if (candidate != null && candidate.name == objectName &&
                    (includeInactive || candidate.gameObject.activeInHierarchy))
                    return candidate;
            }
            return null;
        }

        private void SetPresentationPose(float height)
        {
            if (_detectorInstance == null || _presentationAnchor == null)
                return;

            // The bag is rotated in the PreEntry scene. Its local Y/Z axes therefore
            // produced a sideways launch. Use world-up for the lift while retaining the
            // bag-facing yaw, then roll the detector so its display is upright.
            var detectorTransform = _detectorInstance.transform;
            detectorTransform.position = _presentationAnchor.position + Vector3.up * height;
            detectorTransform.rotation = _presentationAnchor.rotation * PresentationLocalRotation;
            OrientDisplayTowardViewer(detectorTransform);
        }

        private void OrientDisplayTowardViewer(Transform detectorTransform)
        {
            if (detectorTransform == null || _view == null || _view.DisplayTransform == null)
                return;

            _gazeCamera = EntrySafetyAppXrResolve.PreferLiveMainCamera(_gazeCamera);
            if (_gazeCamera == null)
                return;

            var display = _view.DisplayTransform;
            var towardViewer = Vector3.ProjectOnPlane(
                _gazeCamera.transform.position - display.position,
                Vector3.up).normalized;
            if (towardViewer.sqrMagnitude < 0.001f)
                return;

            // The TMP display's readable front faces opposite Transform.forward. Always
            // orient that readable face toward the HMD; preserving whichever side was
            // already visible could keep the detector's back facing the trainee.
            var desiredForward = -towardViewer;
            var desiredDisplayRotation = Quaternion.LookRotation(desiredForward, Vector3.up);
            var correction = desiredDisplayRotation * Quaternion.Inverse(display.rotation);
            detectorTransform.rotation = correction * detectorTransform.rotation;
        }

        private void StopPresentationRoutines()
        {
            if (_presentationRoutine != null)
            {
                StopCoroutine(_presentationRoutine);
                _presentationRoutine = null;
            }
            if (_equippedConfirmationRoutine != null)
            {
                StopCoroutine(_equippedConfirmationRoutine);
                _equippedConfirmationRoutine = null;
            }
        }

        private static void FitGrabCollider(BoxCollider collider, Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                collider.center = Vector3.zero;
                collider.size = new Vector3(0.12f, 0.22f, 0.08f);
                return;
            }

            var hasPoint = false;
            var localBounds = new Bounds();
            for (var i = 0; i < renderers.Length; i++)
            {
                var bounds = renderers[i].bounds;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    var localPoint = root.InverseTransformPoint(corner);
                    if (!hasPoint)
                    {
                        localBounds = new Bounds(localPoint, Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPoint);
                    }
                }
            }

            collider.center = localBounds.center;
            collider.size = Vector3.Max(localBounds.size, new Vector3(0.08f, 0.12f, 0.06f));
        }
    }
}
