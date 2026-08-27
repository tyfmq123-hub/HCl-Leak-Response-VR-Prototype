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
            ResolveLiveGripAnimator();
            var shouldLock = _detectorController != null && _detectorController.IsEquipped;
            if (_leftHandGripAnimator == null)
                return;
            if (_wasLocked == shouldLock)
                return;

            _leftHandGripAnimator.enabled = !shouldLock;

            if (shouldLock)
            {
                var handAnimator = _leftHandGripAnimator as Animator;
                if (handAnimator == null)
                    handAnimator = _leftHandGripAnimator.GetComponent<Animator>();
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

        private void ResolveLiveGripAnimator()
        {
            if (_leftHandGripAnimator != null && _leftHandGripAnimator.gameObject.activeInHierarchy)
                return;

            var left = EntrySafetyAppXrResolve.FindActiveByName("Left Controller");
            if (left == null)
                return;

            var animators = left.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null || !animator.gameObject.activeInHierarchy)
                    continue;

                _leftHandGripAnimator = animator;
                return;
            }
        }
    }
}
