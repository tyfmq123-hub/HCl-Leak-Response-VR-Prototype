using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Disables the left-hand grip animation while the HCl detector is equipped.</summary>
    public sealed class EntrySafetyHclGripLock : MonoBehaviour
    {
        private static readonly int GripParameterId = Animator.StringToHash("Grip");

        [SerializeField] private EntrySafetyHclDetectorController _detectorController;
        [SerializeField] private Behaviour _leftHandGripAnimator;

        private bool _wasLocked;

        private void Update()
        {
            var shouldLock = _detectorController != null && _detectorController.IsEquipped;
            if (_wasLocked == shouldLock || _leftHandGripAnimator == null)
                return;

            _leftHandGripAnimator.enabled = !shouldLock;

            if (shouldLock)
            {
                var handAnimator = _leftHandGripAnimator.GetComponent<Animator>();
                if (handAnimator != null)
                    handAnimator.SetFloat(GripParameterId, 0f);
            }

            _wasLocked = shouldLock;
        }

        private void OnDisable()
        {
            if (_leftHandGripAnimator != null)
                _leftHandGripAnimator.enabled = true;

            _wasLocked = false;
        }
    }
}
