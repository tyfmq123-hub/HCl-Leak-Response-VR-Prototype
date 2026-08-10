using System.Collections;
using TMPro;
using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Owns the P-102-only runtime presentation and interaction surface on the existing four-row P/T panel.</summary>
    public sealed class EntrySafetyGoal2PtPanelController : MonoBehaviour
    {
        private static readonly Color P102RunNeonColor = new Color(0.15f, 1f, 0.35f, 1f);
        private static readonly Color P102StopNeonColor = new Color(1f, 0.12f, 0.25f, 1f);
        private static readonly Color StopZoneOutsideColor = new Color(0.20f, 0.50f, 1.00f, 0.22f);
        private static readonly Color StopZoneInsideColor = new Color(0.35f, 0.70f, 1.00f, 0.32f);

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

        [Header("Physical P-102 readouts")]
        [SerializeField] private TMP_Text _p102PressureReadout;
        [SerializeField] private TMP_Text _p102TemperatureReadout;
        [SerializeField] private TMP_Text _p102RunStopReadout;
        [SerializeField] private CanvasGroup _fadeCanvasGroup;
        [SerializeField, Min(0.1f)] private float _arrivalContextSeconds = 3f;

        [Header("Temporary process tuning")]
        [SerializeField, Min(0f)] private float _initialPressureMpa = 4.12f;
        [SerializeField, Min(0f)] private float _reducedPressureMpa = 1.20f;
        [SerializeField, Min(0.1f)] private float _pressureDecreaseSeconds = 5f;
        [Header("P-102 STOP zone (scene editable)")]
        [Tooltip("Move this transform in the Scene view to place the STOP proximity sphere.")]
        [SerializeField] private Transform _stopZoneAnchor;
        [Tooltip("Used when no anchor is assigned, or when Use Anchor Scale As Radius is off.")]
        [SerializeField, Min(0.01f)] private float _stopZoneRadiusMeters = 0.18f;
        [Tooltip("If enabled, sphere radius = anchor lossyScale.x * 0.5.")]
        [SerializeField] private bool _useAnchorScaleAsRadius = true;
        // Fallback placement when _stopZoneAnchor is null. Negative is outward toward the player.
        [SerializeField] private float _stopZoneForwardOffsetMeters = -0.55f;
        [SerializeField, Min(0f)] private float _p102PressFaceOutwardOffsetMeters = 0.02f;

        private Vector3 _p102PressFallbackWorldPosition;
        private float _pressureMpa;
        private float _pressureElapsed;
        private bool _pumpStopped;
        private bool _pressureDecreaseStarted;
        private bool _hasInitialized;
        private bool _isArrivalContextActive;
        private bool _isRightHandInsideStopZone;
        private float _p102RunningFontSize;
        private bool _hasP102RunningFontSize;
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
            _p102PressFallbackWorldPosition = _p102Press == null ? Vector3.zero : _p102Press.position;
            CacheP102RunningFontSize();
            ResetPresentation();
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
            _pressureMpa = _initialPressureMpa;
            _pressureElapsed = 0f;
            _pumpStopped = false;
            _pressureDecreaseStarted = false;
            _isArrivalContextActive = false;
            _isRightHandInsideStopZone = false;
            SetStopProximityVisible(false);
            _lastStep = EntrySafetyStep.SceneIntro;
            if (_p102Press != null)
                _p102Press.position = GetReleasedPressPosition();
            if (_fadeCanvasGroup != null)
            {
                _fadeCanvasGroup.alpha = 0f;
                _fadeCanvasGroup.blocksRaycasts = false;
            }

            SetTargets(EntrySafetyGazeTargetId.None);
            UpdateGazeHighlight(EntrySafetyGazeTargetId.None, false);
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
            UpdateGazeHighlight(EntrySafetyGazeTargetId.None, false);
            yield return new WaitForSeconds(_arrivalContextSeconds);
            _isArrivalContextActive = false;
        }

        public void UpdateForStep(EntrySafetyStep step, float deltaTime, float gazeProgress01, bool isGazing = false)
        {
            if (!_hasInitialized)
                ResetPresentation();

            _ = gazeProgress01;

            if (step != _lastStep)
            {
                _lastStep = step;
                _stepElapsed = 0f;
            }
            else
            {
                _stepElapsed += deltaTime;
            }

            if (_pumpStopped &&
                _pressureDecreaseStarted &&
                !IsPressureDecreaseComplete &&
                step == EntrySafetyStep.WaitPressureDecrease)
            {
                _pressureElapsed = Mathf.Min(_pressureDecreaseSeconds, _pressureElapsed + deltaTime);
                var progress = _pressureElapsed / _pressureDecreaseSeconds;
                _pressureMpa = Mathf.Lerp(_initialPressureMpa, _reducedPressureMpa, progress);
                RefreshReadouts();
            }

            if (_isArrivalContextActive)
            {
                SetTargets(EntrySafetyGazeTargetId.None);
                UpdateGazeHighlight(EntrySafetyGazeTargetId.None, false);
                return;
            }

            var target = EntrySafetyGazeTargetMap.GetRequiredTarget(step);
            SetTargets(target);
            UpdateGazeHighlight(target, isGazing);
            if (step != EntrySafetyStep.StopPump || _pumpStopped)
                SetStopProximityVisible(false);
        }

        public void UpdateStopProximity(bool isStopStep, Vector3 rightHandTrackingPosition, bool hasRightHandPose)
        {
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
                                         IsInsideStopZone(rightHandWorldPosition);
            SetStopProximityVisible(true);
            SetStopProximityColor(_isRightHandInsideStopZone ? StopZoneInsideColor : StopZoneOutsideColor);
        }

        public bool TryStopPump(Vector3 rightHandTrackingPosition, bool hasRightHandPose)
        {
            if (_pumpStopped || _p102Press == null ||
                !TryGetRightHandWorldPosition(rightHandTrackingPosition, hasRightHandPose, out var rightHandWorldPosition))
            {
                return false;
            }

            if (!IsInsideStopZone(rightHandWorldPosition))
            {
                return false;
            }

            _pumpStopped = true;
            _pressureElapsed = 0f;
            _pressureDecreaseStarted = false;
            _pressureMpa = _initialPressureMpa;
            _isRightHandInsideStopZone = false;
            SetStopProximityVisible(false);
            StartCoroutine(AnimateStopPress());
            RefreshReadouts();
            return true;
        }

        /// <summary>
        /// Starts the P-102 pressure drop during WaitPressureDecrease.
        /// Prefer matching <paramref name="durationSeconds"/> to the wait-step TTS length.
        /// </summary>
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
            // Guidance text UI removed; pressure wait continues without panel copy.
        }

        /// <summary>Keeps Goal 2 complete without restoring the removed world-card guidance texts.</summary>
        public void ShowNextAreaReady()
        {
        }

        private void MoveHeadToPanelFocus()
        {
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
                elapsed += Time.unscaledDeltaTime;
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

        private void UpdateGazeHighlight(EntrySafetyGazeTargetId activeTarget, bool isGazing)
        {
            if (_gazeHighlight == null)
                return;

            if (activeTarget == EntrySafetyGazeTargetId.None)
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
            _gazeHighlight.SetGazing(isGazing);
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
                           targetId == EntrySafetyGazeTargetId.StopIndicator
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

        private bool IsInsideStopZone(Vector3 rightHandWorldPosition)
        {
            var stopZoneCenter = GetStopZoneCenter();
            return Vector3.Distance(rightHandWorldPosition, stopZoneCenter) <= GetStopZoneRadius();
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
            yield return MovePress(releasedPosition, pressedPosition, 0.15f);
            yield return new WaitForSeconds(0.08f);
            yield return MovePress(pressedPosition, releasedPosition, 0.15f);
        }

        private Vector3 GetStopZoneCenter()
        {
            if (_stopZoneAnchor != null)
                return _stopZoneAnchor.position;

            if (_p102Press == null)
                return transform.position;

            return GetReleasedPressPosition() + GetPanelForward() * _stopZoneForwardOffsetMeters;
        }

        private float GetStopZoneRadius()
        {
            if (_stopZoneAnchor != null && _useAnchorScaleAsRadius)
                return Mathf.Max(0.01f, _stopZoneAnchor.lossyScale.x * 0.5f);

            return Mathf.Max(0.01f, _stopZoneRadiusMeters);
        }

        private Vector3 GetReleasedPressPosition()
        {
            if (_p102Press.parent == null)
                return _p102PressFallbackWorldPosition;

            return _p102Press.parent.position - GetPanelForward() * _p102PressFaceOutwardOffsetMeters;
        }

        private Vector3 GetPanelForward()
        {
            return _panelFocus == null ? Vector3.forward : _panelFocus.forward;
        }

        private IEnumerator MovePress(Vector3 from, Vector3 to, float duration)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _p102Press.position = Vector3.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            _p102Press.position = to;
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
            _stopProximityVisual.name = "P102StopProximitySphere";
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
            var center = GetStopZoneCenter();
            var radius = GetStopZoneRadius();
            Gizmos.color = new Color(0.20f, 0.50f, 1.00f, 0.35f);
            Gizmos.DrawWireSphere(center, radius);
            Gizmos.color = new Color(0.20f, 0.50f, 1.00f, 0.08f);
            Gizmos.DrawSphere(center, radius);
        }

        private void OnDrawGizmosSelected()
        {
            var center = GetStopZoneCenter();
            var radius = GetStopZoneRadius();
            Gizmos.color = new Color(0.35f, 0.70f, 1.00f, 0.9f);
            Gizmos.DrawWireSphere(center, radius);
        }
#endif

        private void RefreshReadouts()
        {
            SetText(_p102PressureReadout, $"{_pressureMpa:0.00} bar");
            SetText(_p102TemperatureReadout, "72.3 °C");
            SetText(_p102RunStopReadout, _pumpStopped ? "STOP" : "RUN");
            SetP102RunStopColor(_pumpStopped);
        }

        private void SetP102RunStopColor(bool isStopped)
        {
            if (_p102RunStopReadout == null)
                return;

            CacheP102RunningFontSize();
            var statusColor = isStopped ? P102StopNeonColor : P102RunNeonColor;
            _p102RunStopReadout.color = statusColor;
            _p102RunStopReadout.fontSize = isStopped
                ? Mathf.Max(1f, _p102RunningFontSize - 4f)
                : _p102RunningFontSize;
        }

        private void CacheP102RunningFontSize()
        {
            if (_hasP102RunningFontSize || _p102RunStopReadout == null)
                return;

            _p102RunningFontSize = _p102RunStopReadout.fontSize;
            _hasP102RunningFontSize = true;
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
