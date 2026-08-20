using System;
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
        WrongFacility = 2,
        Stopped = 3,
    }

    /// <summary>
    /// Owns the four-row P/T panel. The active row follows SharedIncidentState's
    /// selected FacilityId instead of a P-102-only path.
    /// </summary>
    public sealed class EntrySafetyGoal2PtPanelController : MonoBehaviour
    {
        [Serializable]
        public sealed class FacilityRow
        {
            public FacilityId Facility = FacilityId.P102;
            public TMP_Text PressureReadout;
            public TMP_Text TemperatureReadout;
            public TMP_Text RunStopReadout;
            public Transform StopPress;
            public string AuthoredTemperature;
            public float InitialPressureBar;
        }

        private static readonly Color RunNeonColor = new Color(0.15f, 1f, 0.35f, 1f);
        private static readonly Color StopNeonColor = new Color(1f, 0.12f, 0.25f, 1f);
        private static readonly Color InactiveReadoutColor = new Color(0.55f, 0.62f, 0.7f, 0.55f);
        private static readonly Color StopZoneOutsideColor = new Color(0.20f, 0.50f, 1.00f, 0.28f);
        private static readonly Color StopZoneInsideColor = new Color(0.35f, 0.70f, 1.00f, 0.42f);
        private static readonly FacilityId[] AllFacilities =
        {
            FacilityId.P101, FacilityId.P102, FacilityId.P103, FacilityId.P104,
        };

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
        [SerializeField] private FacilityRow[] _facilityRows;

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
        private FacilityId _boundFacility = FacilityId.P102;

        public bool IsPressureDecreaseComplete => _pumpStopped && _pressureElapsed >= _pressureDecreaseSeconds;
        public bool IsRightHandInsideStopZone => _isRightHandInsideStopZone;
        public FacilityId BoundFacility => _boundFacility;

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
            EnsureFacilityRows();
            BindSelectedFacility();
            var selected = GetSelectedRow();
            _p102PressFallbackWorldPosition = selected?.StopPress == null
                ? Vector3.zero
                : selected.StopPress.position;
            CacheSelectedRunningFontSize();
            ResetPresentation();
        }

        private void Update()
        {
            if (!_pumpStopped || !_pressureDecreaseStarted || IsPressureDecreaseComplete)
                return;

            _pressureElapsed = Mathf.Min(_pressureDecreaseSeconds, _pressureElapsed + Time.deltaTime);
            var progress = _pressureElapsed / _pressureDecreaseSeconds;
            var row = GetSelectedRow();
            var startPressure = row != null ? row.InitialPressureBar : _initialPressureMpa;
            _pressureMpa = Mathf.Lerp(startPressure, _reducedPressureMpa, progress);
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
            EnsureFacilityRows();
            BindSelectedFacility();
            var row = GetSelectedRow();
            _pressureMpa = row != null ? row.InitialPressureBar : _initialPressureMpa;
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
            BindSelectedFacility();

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
            var row = GetSelectedRow();
            if (!isStopStep || _pumpStopped || _xrOrigin == null || row == null || row.StopPress == null)
            {
                _isRightHandInsideStopZone = false;
                SetStopProximityVisible(false);
                return;
            }

            _isRightHandInsideStopZone = TryGetRightHandWorldPosition(
                                             rightHandTrackingPosition,
                                             hasRightHandPose,
                                             out var rightHandWorldPosition) &&
                                         IsInsideSelectedStop(rightHandWorldPosition, row);
            SetStopProximityVisible(true);
            SetStopProximityColor(_isRightHandInsideStopZone ? StopZoneInsideColor : StopZoneOutsideColor);
        }

        public EntrySafetyStopAttemptResult TryStopPump(Vector3 rightHandTrackingPosition, bool hasRightHandPose)
        {
            if (_pumpStopped)
                return EntrySafetyStopAttemptResult.Ignored;

            BindSelectedFacility();
            var selected = GetSelectedRow();
            if (selected == null || selected.StopPress == null ||
                !TryGetRightHandWorldPosition(rightHandTrackingPosition, hasRightHandPose, out var rightHandWorldPosition))
            {
                return EntrySafetyStopAttemptResult.Ignored;
            }

            var overlapping = FindOverlappingRow(rightHandWorldPosition);
            if (overlapping != null && overlapping.Facility != selected.Facility)
                return EntrySafetyStopAttemptResult.WrongFacility;

            if (!IsInsideSelectedStop(rightHandWorldPosition, selected))
                return EntrySafetyStopAttemptResult.Missed;

            _pumpStopped = true;
            _pressureElapsed = 0f;
            _pressureDecreaseStarted = true;
            _pressureMpa = selected.InitialPressureBar;
            _isRightHandInsideStopZone = false;
            SetStopProximityVisible(false);
            SharedIncidentState.Current.SetFacilityRunState(FacilityRunState.Stopped);
            StartCoroutine(AnimateStopPress(selected));
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
            var row = GetSelectedRow();
            _pressureMpa = row != null ? row.InitialPressureBar : _initialPressureMpa;
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

            var row = GetSelectedRow();
            if (activeTarget == EntrySafetyGazeTargetId.None &&
                _lastStep != EntrySafetyStep.StopPump)
            {
                _gazeHighlight.ClearFollow();
                return;
            }

            var anchor = GetHighlightAnchor(activeTarget, row);
            if (anchor == null)
            {
                _gazeHighlight.ClearFollow();
                return;
            }

            var faceOutward = -GetPanelForward();
            _gazeHighlight.Follow(anchor, faceOutward, GetHighlightSize(activeTarget, anchor));
            _gazeHighlight.SetProgress(_lastStep == EntrySafetyStep.StopPump ? 1f : gazeProgress01);
        }

        private Transform GetHighlightAnchor(EntrySafetyGazeTargetId targetId, FacilityRow row)
        {
            if (row != null)
            {
                if (_lastStep == EntrySafetyStep.StopPump ||
                    targetId == EntrySafetyGazeTargetId.RunIndicator ||
                    targetId == EntrySafetyGazeTargetId.StopIndicator)
                    return row.RunStopReadout != null ? row.RunStopReadout.transform : row.StopPress;
                if (targetId == EntrySafetyGazeTargetId.PressureDisplay ||
                    targetId == EntrySafetyGazeTargetId.ReducedPressureDisplay)
                    return row.PressureReadout != null ? row.PressureReadout.transform : null;
                if (targetId == EntrySafetyGazeTargetId.TemperatureDisplay)
                    return row.TemperatureReadout != null ? row.TemperatureReadout.transform : null;
            }

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

        private bool IsInsideSelectedStop(Vector3 rightHandWorldPosition, FacilityRow row)
        {
            if (row == null || row.StopPress == null)
                return false;

            EnsureStopCollider(row.StopPress);
            var collider = row.StopPress.GetComponent<Collider>();
            if (collider != null)
            {
                var closest = collider.ClosestPoint(rightHandWorldPosition);
                if (Vector3.Distance(closest, rightHandWorldPosition) <= _stopButtonReachMeters)
                    return true;
            }

            return Vector3.Distance(rightHandWorldPosition, GetStopZoneCenter(row)) <= GetStopZoneRadius(row);
        }

        private FacilityRow FindOverlappingRow(Vector3 rightHandWorldPosition)
        {
            if (_facilityRows == null)
                return null;

            for (var i = 0; i < _facilityRows.Length; i++)
            {
                var row = _facilityRows[i];
                if (row == null || row.StopPress == null)
                    continue;
                if (IsInsideSelectedStop(rightHandWorldPosition, row))
                    return row;
            }

            return null;
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

        private IEnumerator AnimateStopPress(FacilityRow row)
        {
            if (row == null || row.StopPress == null)
                yield break;

            var releasedPosition = GetReleasedPressPosition(row);
            var pressedPosition = releasedPosition + GetPanelForward() * 0.012f;
            yield return MovePress(row.StopPress, releasedPosition, pressedPosition, 0.12f);
            yield return new WaitForSeconds(0.08f);
            yield return MovePress(row.StopPress, pressedPosition, releasedPosition, 0.12f);
        }

        private Vector3 GetStopZoneCenter(FacilityRow row)
        {
            if (row != null && row.StopPress != null)
                return row.StopPress.position + GetPanelForward() * _stopZoneForwardOffsetMeters;

            if (_stopZoneAnchor != null)
                return _stopZoneAnchor.position;

            if (_p102Press == null)
                return transform.position;

            return GetReleasedPressPosition(GetSelectedRow()) + GetPanelForward() * _stopZoneForwardOffsetMeters;
        }

        private float GetStopZoneRadius(FacilityRow row)
        {
            if (_useAnchorScaleAsRadius && row != null && row.StopPress != null)
                return Mathf.Max(0.12f, row.StopPress.lossyScale.x * 0.5f);

            return Mathf.Max(0.12f, _stopZoneRadiusMeters);
        }

        private Vector3 GetReleasedPressPosition(FacilityRow row)
        {
            var press = row != null ? row.StopPress : _p102Press;
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

            var row = GetSelectedRow();
            var center = GetStopZoneCenter(row);
            var diameter = GetStopZoneRadius(row) * 2f;
            _stopProximityVisual.transform.position = center;
            _stopProximityVisual.transform.localScale = Vector3.one * diameter;
            _stopProximityVisual.SetActive(true);
        }

        private void EnsureStopProximityVisual()
        {
            var row = GetSelectedRow();
            if (_stopProximityVisual != null || row == null || row.StopPress == null)
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

            _stopProximityVisual.transform.localScale = Vector3.one * (GetStopZoneRadius(row) * 2f);
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
            EnsureFacilityRows();
            var row = GetSelectedRow();
            var center = GetStopZoneCenter(row);
            var radius = GetStopZoneRadius(row);
            Gizmos.color = new Color(0.20f, 0.50f, 1.00f, 0.35f);
            Gizmos.DrawWireSphere(center, radius);
            Gizmos.color = new Color(0.20f, 0.50f, 1.00f, 0.08f);
            Gizmos.DrawSphere(center, radius);
        }
#endif

        private void RefreshReadouts()
        {
            EnsureFacilityRows();
            if (_facilityRows == null)
                return;

            for (var i = 0; i < _facilityRows.Length; i++)
            {
                var row = _facilityRows[i];
                if (row == null)
                    continue;

                var isSelected = row.Facility == _boundFacility;
                var pressure = isSelected ? $"{_pressureMpa:0.00} bar" : $"{row.InitialPressureBar:0.00} bar";
                SetText(row.PressureReadout, pressure);
                SetText(row.TemperatureReadout, row.AuthoredTemperature);
                SetText(row.RunStopReadout, isSelected && _pumpStopped ? "STOP" : "RUN");
                ApplyRowStyle(row, isSelected);
            }
        }

        private void ApplyRowStyle(FacilityRow row, bool isSelected)
        {
            if (row == null)
                return;

            var pressureColor = isSelected ? Color.white : InactiveReadoutColor;
            var temperatureColor = isSelected ? Color.white : InactiveReadoutColor;
            if (row.PressureReadout != null)
                row.PressureReadout.color = pressureColor;
            if (row.TemperatureReadout != null)
                row.TemperatureReadout.color = temperatureColor;
            if (row.RunStopReadout == null)
                return;

            if (!isSelected)
            {
                row.RunStopReadout.color = InactiveReadoutColor;
                return;
            }

            CacheSelectedRunningFontSize();
            row.RunStopReadout.color = _pumpStopped ? StopNeonColor : RunNeonColor;
            if (_hasSelectedRunningFontSize)
            {
                row.RunStopReadout.fontSize = _pumpStopped
                    ? Mathf.Max(1f, _selectedRunningFontSize - 4f)
                    : _selectedRunningFontSize;
            }
        }

        private void CacheSelectedRunningFontSize()
        {
            var row = GetSelectedRow();
            if (_hasSelectedRunningFontSize || row == null || row.RunStopReadout == null)
                return;

            _selectedRunningFontSize = row.RunStopReadout.fontSize;
            _hasSelectedRunningFontSize = true;
        }

        private void BindSelectedFacility()
        {
            _boundFacility = EntrySafetyFacilityCatalog.ResolveSelectedFacility();
        }

        private FacilityRow GetSelectedRow()
        {
            EnsureFacilityRows();
            if (_facilityRows == null)
                return null;

            for (var i = 0; i < _facilityRows.Length; i++)
            {
                var row = _facilityRows[i];
                if (row != null && row.Facility == _boundFacility)
                    return row;
            }

            return _facilityRows.Length > 0 ? _facilityRows[0] : null;
        }

        private void EnsureFacilityRows()
        {
            if (_facilityRows == null || _facilityRows.Length != AllFacilities.Length)
                _facilityRows = new FacilityRow[AllFacilities.Length];

            for (var i = 0; i < AllFacilities.Length; i++)
            {
                var facility = AllFacilities[i];
                var row = _facilityRows[i] ?? new FacilityRow();
                row.Facility = facility;
                if (row.PressureReadout == null)
                    row.PressureReadout = FindNamed<TMP_Text>(EntrySafetyFacilityCatalog.PressureMeshName(facility));
                if (row.TemperatureReadout == null)
                    row.TemperatureReadout = FindNamed<TMP_Text>(EntrySafetyFacilityCatalog.TemperatureMeshName(facility));
                if (row.RunStopReadout == null)
                    row.RunStopReadout = FindNamed<TMP_Text>(EntrySafetyFacilityCatalog.StatusMeshName(facility));
                if (row.StopPress == null)
                    row.StopPress = FindNamedTransform(EntrySafetyFacilityCatalog.StopPressName(facility));

                if (facility == FacilityId.P102)
                {
                    if (row.PressureReadout == null)
                        row.PressureReadout = _p102PressureReadout;
                    if (row.TemperatureReadout == null)
                        row.TemperatureReadout = _p102TemperatureReadout;
                    if (row.RunStopReadout == null)
                        row.RunStopReadout = _p102RunStopReadout;
                    if (row.StopPress == null)
                        row.StopPress = _p102Press;
                    if (row.InitialPressureBar <= 0.01f)
                        row.InitialPressureBar = _initialPressureMpa;
                }

                ApplyAuthoredDefaults(row);
                if (row.StopPress != null)
                    EnsureStopCollider(row.StopPress);
                _facilityRows[i] = row;
            }
        }

        private static void ApplyAuthoredDefaults(FacilityRow row)
        {
            if (row == null)
                return;

            if (string.IsNullOrEmpty(row.AuthoredTemperature) || row.InitialPressureBar <= 0.01f)
            {
                switch (row.Facility)
                {
                    case FacilityId.P101:
                        row.AuthoredTemperature = "68.7 °C";
                        row.InitialPressureBar = 3.45f;
                        break;
                    case FacilityId.P103:
                        row.AuthoredTemperature = "61.2 °C";
                        row.InitialPressureBar = 2.87f;
                        break;
                    case FacilityId.P104:
                        row.AuthoredTemperature = "59.8 °C";
                        row.InitialPressureBar = 3.02f;
                        break;
                    default:
                        if (string.IsNullOrEmpty(row.AuthoredTemperature))
                            row.AuthoredTemperature = "72.3 °C";
                        if (row.InitialPressureBar <= 0.01f)
                            row.InitialPressureBar = 4.12f;
                        break;
                }
            }
        }

        private void RestorePressPositions()
        {
            if (_facilityRows == null)
                return;

            for (var i = 0; i < _facilityRows.Length; i++)
            {
                var row = _facilityRows[i];
                if (row?.StopPress == null)
                    continue;
                row.StopPress.position = GetReleasedPressPosition(row);
            }
        }

        private static void EnsureStopCollider(Transform press)
        {
            if (press == null)
                return;

            var box = press.GetComponent<BoxCollider>();
            if (box == null)
                box = press.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (box.size.sqrMagnitude < 0.0001f)
                box.size = Vector3.one;
            var size = box.size;
            size.x = Mathf.Max(size.x, 0.18f);
            size.y = Mathf.Max(size.y, 0.12f);
            size.z = Mathf.Max(size.z, 0.18f);
            box.size = size;
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

            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
