using System.Collections;
using HClTraining.SharedXR;
using TMPro;
using UnityEngine;

namespace JH.EntrySafety
{
    public enum EntrySafetyStopAttemptResult
    {
        Ignored = 0,
        Missed = 1,
        Stopped = 2,
    }

    /// <summary>
    /// Owns the fixed P-102 training row on the authored four-row P/T panel.
    /// The other facility rows remain environment-only readouts.
    /// </summary>
    public sealed class EntrySafetyGoal2PtPanelController : MonoBehaviour
    {
        private static readonly Color RunNeonColor = new Color(0.15f, 1f, 0.35f, 1f);
        private static readonly Color StopNeonColor = new Color(1f, 0.12f, 0.25f, 1f);
        private static readonly Color StopZoneOutsideColor = new Color(0.20f, 0.50f, 1.00f, 0.28f);
        private static readonly Color StopZoneInsideColor = new Color(0.35f, 0.70f, 1.00f, 0.42f);
        private const float MinStopZoneRadiusMeters = 0.04f;
        private const float MaxStopZoneRadiusMeters = 0.40f;
        private const float StopButtonWorldSizeMeters = 0.08f;
        private const string P102PressureName = "PressureMesh_P-102";
        private const string P102TemperatureName = "TemperatureMesh_P-102";
        private const string P102StatusName = "StatusMesh_P-102";
        private const string P102StopPressName = "StopButton_P102_Press";
        private const string P102Temperature = "72.3 °C";

        [Header("Existing scene objects")]
        [SerializeField] private Transform _xrOrigin;
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _playerAnchor;
        [SerializeField] private Transform _panelFocus;
        [SerializeField] private Transform _p102Press;
        [SerializeField] private Transform _rightHand;
        [SerializeField] private EntrySafetyGazeTargetBehaviour _pressureTarget;
        [SerializeField] private EntrySafetyGazeTargetBehaviour _temperatureTarget;
        [SerializeField] private EntrySafetyGazeTargetBehaviour _runTarget;
        [SerializeField] private EntrySafetyGazeTargetBehaviour _stopTarget;
        [SerializeField] private EntrySafetyGazeTargetBehaviour _reducedPressureTarget;
        [SerializeField] private EntrySafetyGazeHighlightVisual _gazeHighlight;
        [SerializeField] private Vector2 _readoutHighlightSizeMeters = new Vector2(0.14f, 0.08f);
        [SerializeField] private Vector2 _statusHighlightSizeMeters = new Vector2(0.20f, 0.08f);
        [SerializeField] private Vector2 _readoutHighlightPaddingMeters = new Vector2(0.008f, 0.028f);
        [SerializeField, Range(0.3f, 1.2f)] private float _readoutHighlightWidthScale = 0.65f;

        [Header("Physical readouts")]
        [SerializeField] private TMP_Text _p102PressureReadout;
        [SerializeField] private TMP_Text _p102TemperatureReadout;
        [SerializeField] private TMP_Text _p102RunStopReadout;
        [SerializeField] private CanvasGroup _fadeCanvasGroup;
        [SerializeField, Min(0.1f)] private float _arrivalContextSeconds = 3f;

        [Header("Process tuning")]
        [SerializeField, Min(0f)] private float _initialPressureMpa = 4.12f;
        [SerializeField, Min(0f)] private float _reducedPressureMpa = 1.20f;
        [SerializeField, Min(0.1f)] private float _pressureDecreaseSeconds = 9f;
        [Header("STOP zone (scene editable)")]
        [Tooltip("Move this transform in the Scene view to place the STOP proximity sphere.")]
        [SerializeField] private Transform _stopZoneAnchor;
        [Tooltip("Used when no anchor is assigned, or when Use Anchor Scale As Radius is off.")]
        [SerializeField, Min(0.01f)] private float _stopZoneRadiusMeters = 0.32f;
        [Tooltip("If enabled, sphere radius = anchor lossyScale.x * 0.5.")]
        [SerializeField] private bool _useAnchorScaleAsRadius = false;
        [SerializeField] private float _stopZoneForwardOffsetMeters = -0.35f;
        [SerializeField, Min(0f)] private float _p102PressFaceOutwardOffsetMeters = 0.02f;
        [SerializeField, Min(0.01f)] private float _stopButtonReachMeters = 0.12f;

        private Vector3 _p102PressFallbackWorldPosition;
        private float _pressureMpa;
        private float _pressureElapsed;
        private bool _pumpStopped;
        private bool _pressureDecreaseStarted;
        private bool _hasInitialized;
        private bool _isArrivalContextActive;
        private bool _isRightHandInsideStopZone;
        private float _selectedRunningFontSize;
        private bool _hasSelectedRunningFontSize;
        private GameObject _stopProximityVisual;
        private Renderer _stopProximityRenderer;
        private Material _stopProximityMaterial;
        private EntrySafetyStep _lastStep;
        private float _stepElapsed;

        public bool IsPressureDecreaseComplete => _pumpStopped && _pressureElapsed >= _pressureDecreaseSeconds;
        public bool IsRightHandInsideStopZone => _isRightHandInsideStopZone;

        public Transform ActiveGazeTargetTransform
        {
            get
            {
                if (_isArrivalContextActive)
                    return null;

                return GetTarget(EntrySafetyGazeTargetMap.GetRequiredTarget(_lastStep))?.transform;
            }
        }

        private void Awake()
        {
            ResolveLiveXrRefs();
            ResolveP102References();
            _p102PressFallbackWorldPosition = _p102Press == null
                ? Vector3.zero
                : _p102Press.position;
            CacheSelectedRunningFontSize();
            ResetPresentation();
        }

        private void Update()
        {
            if (!_pumpStopped || !_pressureDecreaseStarted || IsPressureDecreaseComplete)
                return;

            _pressureElapsed = Mathf.Min(_pressureDecreaseSeconds, _pressureElapsed + Time.deltaTime);
            var progress = _pressureElapsed / _pressureDecreaseSeconds;
            _pressureMpa = Mathf.Lerp(_initialPressureMpa, _reducedPressureMpa, progress);
            RefreshReadouts();
        }

        private void ResolveLiveXrRefs()
        {
            _xrOrigin = EntrySafetyAppXrResolve.PreferLive(_xrOrigin, "Common XR Origin");
            if (_xrOrigin == null)
                _xrOrigin = EntrySafetyAppXrResolve.PreferLive(null, "XR Origin (VR)");

            _head = EntrySafetyAppXrResolve.PreferLive(_head, "Main Camera");
            _rightHand = EntrySafetyAppXrResolve.PreferLive(_rightHand, "Right Controller");
        }

        private void OnDestroy()
        {
            if (_stopProximityMaterial == null)
                return;

            if (Application.isPlaying)
                Destroy(_stopProximityMaterial);
            else
                DestroyImmediate(_stopProximityMaterial);
        }

        public void ResetPresentation()
        {
            ResolveLiveXrRefs();
            ResolveP102References();
            _pressureMpa = _initialPressureMpa;
            _pressureElapsed = 0f;
            _pumpStopped = false;
            _pressureDecreaseStarted = false;
            _isArrivalContextActive = false;
            _isRightHandInsideStopZone = false;
            SetStopProximityVisible(false);
            _lastStep = EntrySafetyStep.SceneIntro;
            RestorePressPositions();
            if (_fadeCanvasGroup != null)
            {
                _fadeCanvasGroup.alpha = 0f;
                _fadeCanvasGroup.blocksRaycasts = false;
            }

            SetTargets(EntrySafetyGazeTargetId.None);
            UpdateGazeHighlight(EntrySafetyGazeTargetId.None, 0f);
            RefreshReadouts();
            EnsurePanelLocomotionBlocker();
            _hasInitialized = true;
        }

        public IEnumerator MoveToPanelWithFade()
        {
            if (!_hasInitialized)
                ResetPresentation();

            yield return FadeTo(1f, 0.4f);
            MoveHeadToPanelFocus();
            yield return FadeTo(0f, 0.4f);
            yield return new WaitForSeconds(0.2f);
        }

        public IEnumerator ShowArrivalContext()
        {
            if (!_hasInitialized)
                ResetPresentation();

            _isArrivalContextActive = true;
            SetTargets(EntrySafetyGazeTargetId.None);
            UpdateGazeHighlight(EntrySafetyGazeTargetId.None, 0f);
            yield return new WaitForSeconds(_arrivalContextSeconds);
            _isArrivalContextActive = false;
        }

        public void UpdateForStep(EntrySafetyStep step, float deltaTime, float gazeProgress01, bool isGazing = false)
        {
            if (!_hasInitialized)
                ResetPresentation();

            _ = isGazing;
            if (step != _lastStep)
            {
                _lastStep = step;
                _stepElapsed = 0f;
            }
            else
            {
                _stepElapsed += deltaTime;
            }

            if (_isArrivalContextActive)
            {
                SetTargets(EntrySafetyGazeTargetId.None);
                UpdateGazeHighlight(EntrySafetyGazeTargetId.None, 0f);
                return;
            }

            var target = EntrySafetyGazeTargetMap.GetRequiredTarget(step);
            SetTargets(target);
            UpdateGazeHighlight(target, gazeProgress01);
            if (step != EntrySafetyStep.StopPump || _pumpStopped)
                SetStopProximityVisible(false);
        }

        public void UpdateStopProximity(bool isStopStep, Vector3 rightHandTrackingPosition, bool hasRightHandPose)
        {
            ResolveLiveXrRefs();
            ResolveP102References();
            if (!isStopStep || _pumpStopped || _xrOrigin == null || _p102Press == null)
            {
                _isRightHandInsideStopZone = false;
                SetStopProximityVisible(false);
                return;
            }

            _isRightHandInsideStopZone = TryGetRightHandWorldPosition(
                                             rightHandTrackingPosition,
                                             hasRightHandPose,
                                             out var rightHandWorldPosition) &&
                                         IsInsideP102Stop(rightHandWorldPosition);
            SetStopProximityVisible(true);
            SetStopProximityColor(_isRightHandInsideStopZone ? StopZoneInsideColor : StopZoneOutsideColor);
        }

        public EntrySafetyStopAttemptResult TryStopPump(Vector3 rightHandTrackingPosition, bool hasRightHandPose)
        {
            if (_pumpStopped)
                return EntrySafetyStopAttemptResult.Ignored;

            ResolveP102References();
            if (_p102Press == null ||
                !TryGetRightHandWorldPosition(rightHandTrackingPosition, hasRightHandPose, out var rightHandWorldPosition))
            {
                return EntrySafetyStopAttemptResult.Ignored;
            }

            if (!IsInsideP102Stop(rightHandWorldPosition))
                return EntrySafetyStopAttemptResult.Missed;

            _pumpStopped = true;
            _pressureElapsed = 0f;
            _pressureDecreaseStarted = true;
            _pressureMpa = _initialPressureMpa;
            _isRightHandInsideStopZone = false;
            SetStopProximityVisible(false);
            SharedIncidentState.Current.SetFacilityRunState(FacilityRunState.Stopped);
            StartCoroutine(AnimateStopPress());
            RefreshReadouts();
            return EntrySafetyStopAttemptResult.Stopped;
        }

        public void BeginPressureDecrease(float durationSeconds)
        {
            if (!_pumpStopped || _pressureDecreaseStarted)
                return;

            if (durationSeconds > 0.1f)
                _pressureDecreaseSeconds = durationSeconds;

            _pressureElapsed = 0f;
            _pressureMpa = _initialPressureMpa;
            _pressureDecreaseStarted = true;
            RefreshReadouts();
        }

        public void ShowWaitFeedback()
        {
        }

        public void ClearGazeFocus()
        {
            SetTargets(EntrySafetyGazeTargetId.None);
            UpdateGazeHighlight(EntrySafetyGazeTargetId.None, 0f);
        }

        public void ShowNextAreaReady()
        {
        }

        private void MoveHeadToPanelFocus()
        {
            ResolveLiveXrRefs();
            if (_xrOrigin == null || _head == null || _panelFocus == null)
                return;

            if (_playerAnchor == null)
                return;

            var positionDelta = _playerAnchor.position - _head.position;
            _xrOrigin.position += positionDelta;

            var flattenedDirection = Vector3.ProjectOnPlane(_panelFocus.position - _head.position, Vector3.up);
            if (flattenedDirection.sqrMagnitude < 0.0001f)
                return;

            var currentForward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (currentForward.sqrMagnitude < 0.0001f)
                currentForward = Vector3.forward;
            var yawDelta = Vector3.SignedAngle(currentForward, flattenedDirection, Vector3.up);
            _xrOrigin.RotateAround(_head.position, Vector3.up, yawDelta);
        }

        private IEnumerator FadeTo(float alpha, float seconds)
        {
            if (_fadeCanvasGroup == null)
                yield break;

            var start = _fadeCanvasGroup.alpha;
            var elapsed = 0f;
            _fadeCanvasGroup.blocksRaycasts = alpha > 0f;
            while (elapsed < seconds)
            {
                elapsed += SharedTrainingPause.UnscaledDeltaTime;
                _fadeCanvasGroup.alpha = Mathf.Lerp(start, alpha, elapsed / seconds);
                yield return null;
            }

            _fadeCanvasGroup.alpha = alpha;
            _fadeCanvasGroup.blocksRaycasts = alpha > 0f;
        }

        private void SetTargets(EntrySafetyGazeTargetId activeTarget)
        {
            SetTargetActive(_pressureTarget, activeTarget == EntrySafetyGazeTargetId.PressureDisplay);
            SetTargetActive(_temperatureTarget, activeTarget == EntrySafetyGazeTargetId.TemperatureDisplay);
            SetTargetActive(_runTarget, activeTarget == EntrySafetyGazeTargetId.RunIndicator);
            SetTargetActive(_stopTarget, activeTarget == EntrySafetyGazeTargetId.StopIndicator && _pumpStopped);
            SetTargetActive(_reducedPressureTarget, activeTarget == EntrySafetyGazeTargetId.ReducedPressureDisplay && IsPressureDecreaseComplete);
        }

        private void UpdateGazeHighlight(EntrySafetyGazeTargetId activeTarget, float gazeProgress01)
        {
            if (_gazeHighlight == null)
                return;

            if (activeTarget == EntrySafetyGazeTargetId.None &&
                _lastStep != EntrySafetyStep.StopPump)
            {
                _gazeHighlight.ClearFollow();
                return;
            }

            var anchor = GetHighlightAnchor(activeTarget);
            if (anchor == null)
            {
                _gazeHighlight.ClearFollow();
                return;
            }

            var faceOutward = -GetPanelForward();
            _gazeHighlight.Follow(anchor, faceOutward, GetHighlightSize(activeTarget, anchor));
            _gazeHighlight.SetProgress(_lastStep == EntrySafetyStep.StopPump ? 1f : gazeProgress01);
        }

        private Transform GetHighlightAnchor(EntrySafetyGazeTargetId targetId)
        {
            switch (targetId)
            {
                case EntrySafetyGazeTargetId.PressureDisplay:
                case EntrySafetyGazeTargetId.ReducedPressureDisplay:
                    return _p102PressureReadout != null ? _p102PressureReadout.transform : GetTarget(targetId)?.transform;
                case EntrySafetyGazeTargetId.TemperatureDisplay:
                    return _p102TemperatureReadout != null ? _p102TemperatureReadout.transform : _temperatureTarget?.transform;
                case EntrySafetyGazeTargetId.RunIndicator:
                case EntrySafetyGazeTargetId.StopIndicator:
                    return _p102RunStopReadout != null ? _p102RunStopReadout.transform : GetTarget(targetId)?.transform;
                default:
                    return GetTarget(targetId)?.transform;
            }
        }

        private Vector2 GetHighlightSize(EntrySafetyGazeTargetId targetId, Transform anchor)
        {
            var fallback = targetId == EntrySafetyGazeTargetId.RunIndicator ||
                           targetId == EntrySafetyGazeTargetId.StopIndicator ||
                           _lastStep == EntrySafetyStep.StopPump
                ? _statusHighlightSizeMeters
                : _readoutHighlightSizeMeters;

            if (TryMeasureReadoutSize(anchor, out var measured))
                return measured;

            return fallback;
        }

        private bool TryMeasureReadoutSize(Transform anchor, out Vector2 size)
        {
            size = default;
            if (anchor == null)
                return false;

            var readout = anchor.GetComponent<TMP_Text>();
            if (readout == null)
                readout = anchor.GetComponentInChildren<TMP_Text>();
            if (readout == null)
                return false;

            var corners = new Vector3[4];
            readout.rectTransform.GetWorldCorners(corners);
            var width = Vector3.Distance(corners[0], corners[3]);
            var height = Vector3.Distance(corners[0], corners[1]);
            if (width < 0.01f || height < 0.01f)
                return false;

            size = new Vector2(
                width * _readoutHighlightWidthScale + _readoutHighlightPaddingMeters.x,
                height + _readoutHighlightPaddingMeters.y);
            return true;
        }

        private EntrySafetyGazeTargetBehaviour GetTarget(EntrySafetyGazeTargetId targetId)
        {
            switch (targetId)
            {
                case EntrySafetyGazeTargetId.PressureDisplay:
                    return _pressureTarget;
                case EntrySafetyGazeTargetId.TemperatureDisplay:
                    return _temperatureTarget;
                case EntrySafetyGazeTargetId.RunIndicator:
                    return _runTarget;
                case EntrySafetyGazeTargetId.StopIndicator:
                    return _stopTarget;
                case EntrySafetyGazeTargetId.ReducedPressureDisplay:
                    return _reducedPressureTarget;
                default:
                    return null;
            }
        }

        private bool IsInsideP102Stop(Vector3 rightHandWorldPosition)
        {
            if (_p102Press == null)
                return false;

            EnsureStopCollider(_p102Press);
            var collider = _p102Press.GetComponent<Collider>();
            if (collider != null)
            {
                var closest = collider.ClosestPoint(rightHandWorldPosition);
                if (Vector3.Distance(closest, rightHandWorldPosition) <= _stopButtonReachMeters)
                    return true;
            }

            return Vector3.Distance(rightHandWorldPosition, GetStopZoneCenter()) <= GetStopZoneRadius();
        }

        private bool TryGetRightHandWorldPosition(
            Vector3 rightHandTrackingPosition,
            bool hasRightHandPose,
            out Vector3 rightHandWorldPosition)
        {
            if (_rightHand != null)
            {
                rightHandWorldPosition = _rightHand.position;
                return true;
            }

            if (hasRightHandPose && _xrOrigin != null)
            {
                rightHandWorldPosition = _xrOrigin.TransformPoint(rightHandTrackingPosition);
                return true;
            }

            rightHandWorldPosition = Vector3.zero;
            return false;
        }

        private IEnumerator AnimateStopPress()
        {
            if (_p102Press == null)
                yield break;

            var releasedPosition = GetReleasedPressPosition();
            var pressedPosition = releasedPosition + GetPanelForward() * 0.012f;
            yield return MovePress(_p102Press, releasedPosition, pressedPosition, 0.12f);
            yield return new WaitForSeconds(0.08f);
            yield return MovePress(_p102Press, pressedPosition, releasedPosition, 0.12f);
        }

        private Vector3 GetStopZoneCenter()
        {
            if (_stopZoneAnchor != null)
                return _stopZoneAnchor.position;

            if (_p102Press != null)
                return _p102Press.position + GetPanelForward() * _stopZoneForwardOffsetMeters;

            return transform.position;
        }

        private float GetStopZoneRadius()
        {
            var radius = _stopZoneRadiusMeters;
            if (_useAnchorScaleAsRadius && _stopZoneAnchor != null)
                radius = _stopZoneAnchor.lossyScale.x * 0.5f;

            return Mathf.Clamp(radius, MinStopZoneRadiusMeters, MaxStopZoneRadiusMeters);
        }

        private Vector3 GetReleasedPressPosition()
        {
            var press = _p102Press;
            if (press == null)
                return _p102PressFallbackWorldPosition;
            if (press.parent == null)
                return _p102PressFallbackWorldPosition;

            return press.parent.position - GetPanelForward() * _p102PressFaceOutwardOffsetMeters;
        }

        private Vector3 GetPanelForward()
        {
            return _panelFocus == null ? Vector3.forward : _panelFocus.forward;
        }

        private IEnumerator MovePress(Transform press, Vector3 from, Vector3 to, float duration)
        {
            if (press == null)
                yield break;

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                press.position = Vector3.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            press.position = to;
        }

        private void SetStopProximityVisible(bool visible)
        {
            if (!visible)
            {
                if (_stopProximityVisual != null)
                    _stopProximityVisual.SetActive(false);
                return;
            }

            EnsureStopProximityVisual();
            if (_stopProximityVisual == null)
                return;

            var center = GetStopZoneCenter();
            var diameter = GetStopZoneRadius() * 2f;
            _stopProximityVisual.transform.position = center;
            _stopProximityVisual.transform.localScale = Vector3.one * diameter;
            _stopProximityVisual.SetActive(true);
        }

        private void EnsureStopProximityVisual()
        {
            if (_stopProximityVisual != null || _p102Press == null)
                return;

            _stopProximityVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _stopProximityVisual.name = "SelectedStopProximitySphere";
            _stopProximityVisual.transform.SetParent(transform, true);

            var collider = _stopProximityVisual.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                    Destroy(collider);
                else
                    DestroyImmediate(collider);
            }

            _stopProximityRenderer = _stopProximityVisual.GetComponent<Renderer>();
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null && _stopProximityRenderer != null)
            {
                _stopProximityMaterial = new Material(shader);
                ApplyStopProximityMaterialColor(StopZoneOutsideColor);
                _stopProximityRenderer.sharedMaterial = _stopProximityMaterial;
                _stopProximityRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _stopProximityRenderer.receiveShadows = false;
            }

            _stopProximityVisual.transform.localScale = Vector3.one * (GetStopZoneRadius() * 2f);
            _stopProximityVisual.SetActive(false);
        }

        private void SetStopProximityColor(Color color)
        {
            ApplyStopProximityMaterialColor(color);
        }

        private void ApplyStopProximityMaterialColor(Color color)
        {
            if (_stopProximityMaterial == null)
                return;

            _stopProximityMaterial.color = color;
            if (_stopProximityMaterial.HasProperty("_BaseColor"))
                _stopProximityMaterial.SetColor("_BaseColor", color);
            if (_stopProximityMaterial.HasProperty("_Color"))
                _stopProximityMaterial.SetColor("_Color", color);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            ResolveP102References();
            var center = GetStopZoneCenter();
            var radius = GetStopZoneRadius();
            Gizmos.color = new Color(0.20f, 0.50f, 1.00f, 0.35f);
            Gizmos.DrawWireSphere(center, radius);
            Gizmos.color = new Color(0.20f, 0.50f, 1.00f, 0.08f);
            Gizmos.DrawSphere(center, radius);
        }
#endif

        private void RefreshReadouts()
        {
            ResolveP102References();
            SetText(_p102PressureReadout, $"{_pressureMpa:0.00} bar");
            SetText(_p102TemperatureReadout, P102Temperature);
            SetText(_p102RunStopReadout, _pumpStopped ? "STOP" : "RUN");
            if (_p102PressureReadout != null)
                _p102PressureReadout.color = Color.white;
            if (_p102TemperatureReadout != null)
                _p102TemperatureReadout.color = Color.white;
            if (_p102RunStopReadout == null)
                return;

            CacheSelectedRunningFontSize();
            _p102RunStopReadout.color = _pumpStopped ? StopNeonColor : RunNeonColor;
            if (_hasSelectedRunningFontSize)
            {
                _p102RunStopReadout.fontSize = _pumpStopped
                    ? Mathf.Max(1f, _selectedRunningFontSize - 4f)
                    : _selectedRunningFontSize;
            }
        }

        private void CacheSelectedRunningFontSize()
        {
            if (_hasSelectedRunningFontSize || _p102RunStopReadout == null)
                return;

            _selectedRunningFontSize = _p102RunStopReadout.fontSize;
            _hasSelectedRunningFontSize = true;
        }

        private void ResolveP102References()
        {
            if (_p102PressureReadout == null)
                _p102PressureReadout = FindNamed<TMP_Text>(P102PressureName);
            if (_p102TemperatureReadout == null)
                _p102TemperatureReadout = FindNamed<TMP_Text>(P102TemperatureName);
            if (_p102RunStopReadout == null)
                _p102RunStopReadout = FindNamed<TMP_Text>(P102StatusName);
            if (_p102Press == null)
                _p102Press = FindNamedTransform(P102StopPressName);
            EnsureStopCollider(_p102Press);
        }

        private void RestorePressPositions()
        {
            if (_p102Press != null)
                _p102Press.position = GetReleasedPressPosition();
        }

        private static void EnsureStopCollider(Transform press)
        {
            if (press == null)
                return;

            var box = press.GetComponent<BoxCollider>();
            if (box == null)
                box = press.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;

            var worldScale = press.lossyScale;
            box.size = new Vector3(
                StopButtonWorldSizeMeters / Mathf.Max(0.0001f, worldScale.x),
                StopButtonWorldSizeMeters / Mathf.Max(0.0001f, worldScale.y),
                StopButtonWorldSizeMeters / Mathf.Max(0.0001f, worldScale.z));
        }

        private void EnsurePanelLocomotionBlocker()
        {
            var panel = FindPanelRoot(_p102Press);
            if (panel == null)
                panel = FindNamedTransform("PT_Panel");
            if (panel == null)
                return;

            EnsureStopCollider(_p102Press);
            EntrySafetyPtPanelLocomotionBlocker.Ensure(panel, GetPanelForward());
        }

        private static Transform FindPanelRoot(Transform start)
        {
            var current = start;
            while (current != null)
            {
                if (current.name == "PT_Panel")
                    return current;
                current = current.parent;
            }

            return null;
        }

        private static T FindNamed<T>(string objectName) where T : Component
        {
            var host = FindNamedTransform(objectName);
            return host != null ? host.GetComponent<T>() : null;
        }

        private static Transform FindNamedTransform(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return null;

            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (var i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (candidate != null && candidate.name == objectName)
                    return candidate;
            }

            return null;
        }

        private static void SetTargetActive(EntrySafetyGazeTargetBehaviour target, bool active)
        {
            if (target != null && target.gameObject.activeSelf != active)
                target.gameObject.SetActive(active);
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
                text.text = value;
        }
    }
}
