using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Rotates the windsock pivot once after the initial HCl measurement.</summary>
    public sealed class EntrySafetyWindChangeController : MonoBehaviour
    {
        [SerializeField] private Transform _windPivot;
        [SerializeField, Min(0.01f)] private float _durationSeconds = 3f;
        [SerializeField, Range(100f, 120f)] private float _localYawDeltaDegrees = 110f;

        private Transform _rotationPivot;
        private Quaternion _initialLocalRotation;
        private Quaternion _targetLocalRotation;
        private float _elapsedSeconds;
        private bool _isRunning;

        public bool IsComplete { get; private set; }

        private void Awake()
        {
            ResetWind();
        }

        public void ResetWind()
        {
            _rotationPivot = ResolveRotationPivot();
            if (_rotationPivot == null)
                return;

            _initialLocalRotation = _rotationPivot.localRotation;
            _targetLocalRotation = _initialLocalRotation * Quaternion.Euler(0f, _localYawDeltaDegrees, 0f);
            _elapsedSeconds = 0f;
            _isRunning = false;
            IsComplete = false;
        }

        public void Begin()
        {
            if (_rotationPivot == null || _isRunning || IsComplete)
                return;

            _elapsedSeconds = 0f;
            _isRunning = true;
        }

        public bool UpdateRotation(float deltaTime)
        {
            if (_rotationPivot == null || IsComplete)
                return IsComplete;

            if (!_isRunning)
                return false;

            _elapsedSeconds = Mathf.Min(_elapsedSeconds + Mathf.Max(0f, deltaTime), _durationSeconds);
            var progress01 = Mathf.Clamp01(_elapsedSeconds / _durationSeconds);
            var easedProgress = Mathf.SmoothStep(0f, 1f, progress01);
            _rotationPivot.localRotation = Quaternion.Slerp(_initialLocalRotation, _targetLocalRotation, easedProgress);

            if (progress01 < 1f)
                return false;

            _rotationPivot.localRotation = _targetLocalRotation;
            IsComplete = true;
            _isRunning = false;
            return true;
        }

        private Transform ResolveRotationPivot()
        {
            // WindPivot is authored at the pole connection and its local Y axis is
            // world-up. Rotating an imported descendant instead uses the FBX's wheel
            // axis, which lifts the entire vane through a vertical arc. Always rotate
            // this stable parent so ship+wheel+3d+model turns as one horizontal unit.
            return _windPivot;
        }
    }
}
