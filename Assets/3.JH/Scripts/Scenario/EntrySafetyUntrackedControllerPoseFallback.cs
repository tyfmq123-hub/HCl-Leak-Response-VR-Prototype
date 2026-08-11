using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>
    /// Keeps controller visuals out of the camera when the PC simulator has no controller pose.
    /// Real tracked poses always take precedence.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class EntrySafetyUntrackedControllerPoseFallback : MonoBehaviour
    {
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _leftController;
        [SerializeField] private Transform _rightController;
        [SerializeField] private Vector3 _recordedHeadLocalPosition = new Vector3(-0.149f, 1.765f, 0.190f);
        [SerializeField] private Vector3 _leftControllerFromHead = new Vector3(-0.250f, -0.909f, -0.133f);
        [SerializeField] private Vector3 _rightControllerFromHead = new Vector3(0.252f, -0.921f, -0.086f);
        [SerializeField] private Vector3 _leftLocalEulerAngles = new Vector3(68.02f, 270.52f, 283.06f);
        [SerializeField] private Vector3 _rightLocalEulerAngles = new Vector3(71.07f, 88.21f, 85.99f);

        private void LateUpdate()
        {
            ResolveLiveXrRefs();
            ApplyFallback();
        }

        public bool ApplyFallback()
        {
            ResolveLiveXrRefs();
            if (_head == null)
                return false;

            var applied = ApplyFallbackToHead();
            applied |= ApplyFallbackToController(_leftController, _leftControllerFromHead, _leftLocalEulerAngles);
            applied |= ApplyFallbackToController(_rightController, _rightControllerFromHead, _rightLocalEulerAngles);
            return applied;
        }

        private void ResolveLiveXrRefs()
        {
            _head = EntrySafetyAppXrResolve.PreferLive(_head, "Main Camera");
            _leftController = EntrySafetyAppXrResolve.PreferLive(_leftController, "Left Controller");
            _rightController = EntrySafetyAppXrResolve.PreferLive(_rightController, "Right Controller");
        }

        private bool ApplyFallbackToHead()
        {
            if (_head.localPosition.sqrMagnitude > 0.0001f)
                return false;

            _head.localPosition = _recordedHeadLocalPosition;
            return true;
        }

        private bool ApplyFallbackToController(Transform controller, Vector3 fromHead, Vector3 localEulerAngles)
        {
            if (controller == null || controller.localPosition.sqrMagnitude > 0.0001f)
                return false;

            controller.localPosition = _head.localPosition + fromHead;
            controller.localRotation = Quaternion.Euler(localEulerAngles);
            return true;
        }
    }
}
