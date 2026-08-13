using System;
using HClTraining.SharedXR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR.Haptics;
using UnityEngine.InputSystem.Utilities;

namespace JH.EntrySafety
{
    /// <summary>Reads semantic Entry Safety actions supplied by the project-owned Input Action Asset.</summary>
    public sealed class EntrySafetyInputAdapter : MonoBehaviour
    {
        [SerializeField] private InputActionReference _equipDetectorAction;
        [SerializeField] private InputActionReference _leftTriggerAction;
        [SerializeField] private InputActionReference _simulatorEquipDetectorAction;
        [SerializeField] private InputActionReference _simulatorTriggerAction;
        [SerializeField] private InputActionReference _simulatorGazeConfirmAction;
        [SerializeField, Range(0f, 1f)] private float _triggerThreshold = 0.75f;

        public event Action EquipDetectorPressed;
        public event Action StoreDetectorPressed;
        public event Action GuidanceRepeatPressed;
        public event Action SimulationGazeConfirmPressed;
        public event Action MoveToPtPressed;
        public event Action RightTriggerPressed;
        public float LeftTriggerValue { get; private set; }
        public bool IsLeftTriggerPressed => LeftTriggerValue >= _triggerThreshold;
        public Vector3 RightHandPosition { get; private set; }
        public bool HasRightHandPose { get; private set; }

        private SharedXRInputContract _sharedInput;
#if UNITY_EDITOR
        private bool _enabledSimulatorEquipAction;
        private bool _enabledSimulatorGazeAction;
#endif

        public void PulseRightHand(float amplitude, float durationSeconds)
        {
            var clampedAmplitude = Mathf.Clamp01(amplitude);
            var clampedDuration = Mathf.Max(0.01f, durationSeconds);

            if (_sharedInput != null &&
                _sharedInput.SendHaptic(SharedXRHand.Right, clampedAmplitude, clampedDuration))
            {
                return;
            }

            // OpenXR/Quest exposes haptics through the XR device even when an Input System control is absent.
            var xrDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (xrDevice.isValid &&
                xrDevice.TryGetHapticCapabilities(out var capabilities) &&
                capabilities.supportsImpulse)
            {
                xrDevice.SendHapticImpulse(0u, clampedAmplitude, clampedDuration);
                return;
            }

            foreach (var device in InputSystem.devices)
            {
                if (!HasUsage(device, CommonUsages.RightHand))
                    continue;

                var command = SendHapticImpulseCommand.Create(0, clampedAmplitude, clampedDuration);
                device.ExecuteCommand(ref command);
                return;
            }
        }

        private bool _equipDetectorButtonWasPressed;
        private bool _storeDetectorButtonWasPressed;
        private bool _guidanceRepeatButtonWasPressed;
        private bool _moveToPtButtonWasPressed;
        private bool _rightTriggerWasPressed;

        private void OnEnable()
        {
            _sharedInput = SharedXRInputContract.Instance;
            if (_sharedInput != null)
                _sharedInput.Performed += OnSharedInputPerformed;
            else if (_equipDetectorAction != null && _equipDetectorAction.action != null)
                _equipDetectorAction.action.performed += OnEquipDetectorPerformed;
#if UNITY_EDITOR
            if (_simulatorEquipDetectorAction != null && _simulatorEquipDetectorAction.action != null)
            {
                if (!_simulatorEquipDetectorAction.action.enabled)
                {
                    _simulatorEquipDetectorAction.action.Enable();
                    _enabledSimulatorEquipAction = true;
                }
                _simulatorEquipDetectorAction.action.performed += OnEquipDetectorPerformed;
            }
            if (_simulatorGazeConfirmAction != null && _simulatorGazeConfirmAction.action != null)
            {
                if (!_simulatorGazeConfirmAction.action.enabled)
                {
                    _simulatorGazeConfirmAction.action.Enable();
                    _enabledSimulatorGazeAction = true;
                }
                _simulatorGazeConfirmAction.action.performed += OnSimulationGazeConfirmPerformed;
            }
#endif
        }

        private void OnDisable()
        {
            if (_sharedInput != null)
                _sharedInput.Performed -= OnSharedInputPerformed;
            else if (_equipDetectorAction != null && _equipDetectorAction.action != null)
                _equipDetectorAction.action.performed -= OnEquipDetectorPerformed;
            _sharedInput = null;
#if UNITY_EDITOR
            if (_simulatorEquipDetectorAction != null && _simulatorEquipDetectorAction.action != null)
            {
                _simulatorEquipDetectorAction.action.performed -= OnEquipDetectorPerformed;
                if (_enabledSimulatorEquipAction)
                    _simulatorEquipDetectorAction.action.Disable();
            }
            if (_simulatorGazeConfirmAction != null && _simulatorGazeConfirmAction.action != null)
            {
                _simulatorGazeConfirmAction.action.performed -= OnSimulationGazeConfirmPerformed;
                if (_enabledSimulatorGazeAction)
                    _simulatorGazeConfirmAction.action.Disable();
            }
            _enabledSimulatorEquipAction = false;
            _enabledSimulatorGazeAction = false;
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR
            // The additive-stage sanitizer disables the legacy Stage InputActionManager
            // after this component is enabled. Keep only these explicit simulator
            // actions alive so integrated Editor tests still exercise the Stage event.
            EnsureSimulatorActionsEnabled();
#endif
            var leftTriggerValue = _sharedInput != null
                ? _sharedInput.ReadValue(SharedXRHand.Left, SharedXRControl.Trigger)
                : (_leftTriggerAction?.action == null ? 0f : _leftTriggerAction.action.ReadValue<float>());
            var simulatorTriggerValue = 0f;
#if UNITY_EDITOR
            simulatorTriggerValue = _simulatorTriggerAction?.action == null ? 0f : _simulatorTriggerAction.action.ReadValue<float>();
#endif
            LeftTriggerValue = Mathf.Max(leftTriggerValue, simulatorTriggerValue);
            UpdateRightHandPose();

            // Keep a raw Quest/OpenXR fallback even when the shared semantic contract
            // exists. Some runtimes expose face-button features but do not resolve the
            // generic Input System SecondaryButton binding used by the contract.
            UpdatePhysicalButton(
                isRightHand: false,
                controlName: "secondaryButton",
                ref _equipDetectorButtonWasPressed,
                EquipDetectorPressed);
            UpdatePhysicalButton(
                isRightHand: true,
                controlName: "secondaryButton",
                ref _storeDetectorButtonWasPressed,
                StoreDetectorPressed);
            UpdatePhysicalButton(
                isRightHand: false,
                controlName: "primaryButton",
                ref _guidanceRepeatButtonWasPressed,
                GuidanceRepeatPressed);
            UpdatePhysicalButton(
                isRightHand: true,
                controlName: "primaryButton",
                ref _moveToPtButtonWasPressed,
                MoveToPtPressed);
            UpdatePhysicalButton(
                isRightHand: true,
                controlName: "triggerPressed",
                ref _rightTriggerWasPressed,
                RightTriggerPressed,
                _triggerThreshold);
        }

#if UNITY_EDITOR
        private void EnsureSimulatorActionsEnabled()
        {
            if (_simulatorEquipDetectorAction != null &&
                _simulatorEquipDetectorAction.action != null &&
                !_simulatorEquipDetectorAction.action.enabled)
            {
                _simulatorEquipDetectorAction.action.Enable();
                _enabledSimulatorEquipAction = true;
            }

            if (_simulatorGazeConfirmAction != null &&
                _simulatorGazeConfirmAction.action != null &&
                !_simulatorGazeConfirmAction.action.enabled)
            {
                _simulatorGazeConfirmAction.action.Enable();
                _enabledSimulatorGazeAction = true;
            }
        }
#endif

        private void OnSharedInputPerformed(SharedXRHand hand, SharedXRControl control)
        {
            if (hand == SharedXRHand.Left && control == SharedXRControl.SecondaryButton)
                EquipDetectorPressed?.Invoke();
            else if (hand == SharedXRHand.Right && control == SharedXRControl.SecondaryButton)
                StoreDetectorPressed?.Invoke();
            else if (hand == SharedXRHand.Left && control == SharedXRControl.PrimaryButton)
                GuidanceRepeatPressed?.Invoke();
            else if (hand == SharedXRHand.Right && control == SharedXRControl.PrimaryButton)
                MoveToPtPressed?.Invoke();
            else if (hand == SharedXRHand.Right && control == SharedXRControl.Activate)
                RightTriggerPressed?.Invoke();
        }

        private void OnEquipDetectorPerformed(InputAction.CallbackContext context)
        {
            EquipDetectorPressed?.Invoke();
        }

        private void OnSimulationGazeConfirmPerformed(InputAction.CallbackContext context)
        {
            SimulationGazeConfirmPressed?.Invoke();
        }

        private static void UpdatePhysicalButton(
            bool isRightHand,
            string controlName,
            ref bool wasPressed,
            Action pressed,
            float axisThreshold = 0.5f)
        {
            var requiredUsage = isRightHand ? CommonUsages.RightHand : CommonUsages.LeftHand;
            var isPressed = ReadXrFeatureButton(isRightHand, controlName, axisThreshold);
            foreach (var device in InputSystem.devices)
            {
                if (isPressed)
                    break;
                if (!HasUsage(device, requiredUsage))
                    continue;

                var button = device.TryGetChildControl<ButtonControl>(controlName);
                if (button != null)
                {
                    isPressed = button.isPressed;
                    break;
                }

                if (controlName == "triggerPressed")
                {
                    var trigger = device.TryGetChildControl<AxisControl>("trigger");
                    if (trigger != null)
                    {
                        isPressed = trigger.ReadValue() >= axisThreshold;
                        break;
                    }
                }
            }

            if (isPressed && !wasPressed)
                pressed?.Invoke();
            wasPressed = isPressed;
        }

        private static bool ReadXrFeatureButton(bool isRightHand, string controlName, float axisThreshold)
        {
            var node = isRightHand ? UnityEngine.XR.XRNode.RightHand : UnityEngine.XR.XRNode.LeftHand;
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return false;

            bool pressed;
            switch (controlName)
            {
                case "primaryButton":
                    return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out pressed) && pressed;
                case "secondaryButton":
                    return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out pressed) && pressed;
                case "triggerPressed":
                    if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out pressed) && pressed)
                        return true;
                    return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out var trigger) &&
                           trigger >= axisThreshold;
                default:
                    return false;
            }
        }

        private static bool HasUsage(UnityEngine.InputSystem.InputDevice device, InternedString requiredUsage)
        {
            foreach (var usage in device.usages)
            {
                if (usage == requiredUsage)
                    return true;
            }

            return false;
        }

        private void UpdateRightHandPose()
        {
            HasRightHandPose = false;
            foreach (var device in InputSystem.devices)
            {
                if (!HasUsage(device, CommonUsages.RightHand))
                    continue;

                var position = device.TryGetChildControl<Vector3Control>("devicePosition");
                if (position == null)
                    continue;

                RightHandPosition = position.ReadValue();
                HasRightHandPose = true;
                return;
            }
        }
    }
}
