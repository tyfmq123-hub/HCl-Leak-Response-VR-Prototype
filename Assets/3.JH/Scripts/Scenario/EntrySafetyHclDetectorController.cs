using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Controls the single, left-hand-attached HCl detector across both Goal 1 measurements.</summary>
    public sealed class EntrySafetyHclDetectorController : MonoBehaviour
    {
        [SerializeField] private GameObject _detectorInstance;
        [SerializeField] private EntrySafetyHclDetectorView _view;
        [SerializeField, Range(-1f, 1f)] private float _minimumDisplayGazeDot = 0.95f;
        [SerializeField, Min(0.01f)] private float _requiredHoldSeconds = 3f;
        [SerializeField, Min(0f)] private float _initialResultPpm = 3f;
        [SerializeField, Min(0f)] private float _reducedResultPpm = 0.8f;

        private EntrySafetyTriggerHoldTimer _initialTriggerTimer;
        private EntrySafetyTriggerHoldTimer _reducedTriggerTimer;

        public bool IsEquipped { get; private set; }
        public bool IsMeasuring => IsInitialMeasuring || IsReducedMeasuring;
        public bool IsInitialMeasuring => _initialTriggerTimer != null && _initialTriggerTimer.IsHolding && !_initialTriggerTimer.IsCompleted;
        public bool IsReducedMeasuring => _reducedTriggerTimer != null && _reducedTriggerTimer.IsHolding && !_reducedTriggerTimer.IsCompleted;
        public float MeasureProgress01 => _initialTriggerTimer?.Progress01 ?? 0f;
        public float ReducedMeasureProgress01 => _reducedTriggerTimer?.Progress01 ?? 0f;
        public bool HasInitialResult { get; private set; }
        public bool HasReducedResult { get; private set; }
        public float InitialResultPpm => _initialResultPpm;
        public float ReducedResultPpm => _reducedResultPpm;
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
                if (displayTransform == null || Camera.main == null)
                    return false;

                var directionToDisplay = displayTransform.position - Camera.main.transform.position;
                return directionToDisplay.sqrMagnitude > Mathf.Epsilon &&
                       Vector3.Dot(Camera.main.transform.forward, directionToDisplay.normalized) >= _minimumDisplayGazeDot;
            }
        }

        private void Awake()
        {
            EnsureTriggerTimers();
            if (_detectorInstance != null)
                _detectorInstance.SetActive(false);
        }

        public bool TryEquip()
        {
            if (IsEquipped || _detectorInstance == null)
                return false;

            _detectorInstance.SetActive(true);
            IsEquipped = true;
            _view?.ShowReady();
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

            _detectorInstance.SetActive(false);
            IsEquipped = false;
            return true;
        }

        public void ResetDetector()
        {
            IsEquipped = false;
            HasInitialResult = false;
            HasReducedResult = false;
            EnsureTriggerTimers();
            _initialTriggerTimer.Reset();
            _reducedTriggerTimer.Reset();
            if (_detectorInstance != null)
                _detectorInstance.SetActive(false);
        }

        private void EnsureTriggerTimers()
        {
            if (_initialTriggerTimer == null)
                _initialTriggerTimer = new EntrySafetyTriggerHoldTimer(_requiredHoldSeconds);
            if (_reducedTriggerTimer == null)
                _reducedTriggerTimer = new EntrySafetyTriggerHoldTimer(_requiredHoldSeconds);
        }

    }
}
