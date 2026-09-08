using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace HClTraining.SharedXR
{
    /// <summary>
    /// Stage-independent pause signal owned by the Shared XR layer. Stage assemblies
    /// can use this without depending on App code, while head/controller tracking and
    /// App UI input remain alive.
    /// </summary>
    public static class SharedTrainingPause
    {
        static float inactiveStartedAt;
        static float inactiveDuration;
        static int externalInactiveMask;

        public static bool IsPaused { get; private set; }
        public static bool IsClockInactive => IsPaused || externalInactiveMask != 0;
        public static float UnscaledTime => UnityEngine.Time.unscaledTime - inactiveDuration -
                                            (IsClockInactive ? UnityEngine.Time.unscaledTime - inactiveStartedAt : 0f);
        public static float UnscaledDeltaTime => IsClockInactive ? 0f : UnityEngine.Time.unscaledDeltaTime;
        public static event Action<bool> Changed;

        public static void SetPaused(bool paused)
        {
            if (IsPaused == paused)
                return;

            var wasInactive = IsClockInactive;
            IsPaused = paused;
            ApplyClockTransition(wasInactive);
            Changed?.Invoke(paused);
        }

        /// <summary>
        /// Excludes overlapping OS/XR inactive periods from the shared training clock
        /// without opening or closing the in-app pause menu.
        /// </summary>
        public static void SetExternalInactive(int reasonBit, bool inactive)
        {
            if (reasonBit <= 0)
                return;

            var wasInactive = IsClockInactive;
            if (inactive)
                externalInactiveMask |= reasonBit;
            else
                externalInactiveMask &= ~reasonBit;
            ApplyClockTransition(wasInactive);
        }

        static void ApplyClockTransition(bool wasInactive)
        {
            var isInactive = IsClockInactive;
            if (wasInactive == isInactive)
                return;
            if (isInactive)
                inactiveStartedAt = UnityEngine.Time.unscaledTime;
            else
                inactiveDuration += Mathf.Max(0f, UnityEngine.Time.unscaledTime - inactiveStartedAt);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeState()
        {
            IsPaused = false;
            inactiveStartedAt = 0f;
            inactiveDuration = 0f;
            externalInactiveMask = 0;
            Changed = null;
        }
    }

    public enum SharedXRHand
    {
        Left,
        Right,
    }

    public enum SharedXRControl
    {
        Trigger,
        Grip,
        PrimaryButton,
        SecondaryButton,
        Thumbstick,
        ThumbstickClick,
        Menu,
        Select,
        Activate,
        UIPress,
        Position,
        Rotation,
    }

    /// <summary>
    /// Stable low-level XR input API for Stage-owned handlers. Existing XRI actions
    /// are borrowed from the App InputActionManager. Only Quest face buttons, which
    /// are absent from the XRI Starter asset, use contract-owned actions.
    /// </summary>
    [DefaultExecutionOrder(-9900)]
    [DisallowMultipleComponent]
    public sealed class SharedXRInputContract : MonoBehaviour
    {
        readonly Dictionary<(SharedXRHand, SharedXRControl), InputAction> actions =
            new Dictionary<(SharedXRHand, SharedXRControl), InputAction>();
        readonly List<InputAction> ownedActions = new List<InputAction>();

        public static SharedXRInputContract Instance { get; private set; }
        public bool IsReady { get; private set; }
        public bool GameplayInputSuppressed { get; private set; }
        public event Action<SharedXRHand, SharedXRControl> Performed;
        public event Action<SharedXRHand, SharedXRControl> Canceled;

        public void Initialize(GameObject commonXrOrigin)
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Shared XR Input] Duplicate contract instance.", this);
                enabled = false;
                return;
            }

            Instance = this;
            IsReady = false;
            GameplayInputSuppressed = false;
            actions.Clear();
            DisposeOwnedActions();

            var asset = FindSharedAsset(commonXrOrigin);
            if (asset == null)
            {
                Debug.LogError("[Shared XR Input] App InputActionManager has no action asset.", this);
                return;
            }

            RegisterXriActions(asset, SharedXRHand.Left, "XRI Left");
            RegisterXriActions(asset, SharedXRHand.Right, "XRI Right");
            RegisterFaceButtons(SharedXRHand.Left);
            RegisterFaceButtons(SharedXRHand.Right);
            if (!ValidateRequiredActions())
                return;
            WireEvents(true);
            EnsureActionsEnabled();
            IsReady = true;
        }

        public bool TryGetAction(SharedXRHand hand, SharedXRControl control, out InputAction action) =>
            actions.TryGetValue((hand, control), out action) && action != null;

        public float ReadValue(SharedXRHand hand, SharedXRControl control)
        {
            return !GameplayInputSuppressed && TryGetAction(hand, control, out var action)
                ? action.ReadValue<float>()
                : 0f;
        }

        public Vector2 ReadThumbstick(SharedXRHand hand)
        {
            return !GameplayInputSuppressed &&
                   TryGetAction(hand, SharedXRControl.Thumbstick, out var action)
                ? action.ReadValue<Vector2>()
                : Vector2.zero;
        }

        public bool SendHaptic(SharedXRHand hand, float amplitude, float duration)
        {
            if (GameplayInputSuppressed)
                return false;

            var node = hand == SharedXRHand.Left ? XRNode.LeftHand : XRNode.RightHand;
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid || !device.TryGetHapticCapabilities(out var capabilities) ||
                !capabilities.supportsImpulse)
            {
                return false;
            }

            return device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), Mathf.Max(0f, duration));
        }

        /// <summary>
        /// Temporarily suppresses Stage-owned semantic input while keeping the underlying
        /// XRI action asset enabled for App UI rays and tracked controller poses.
        /// </summary>
        public void SetGameplayInputSuppressed(bool suppressed)
        {
            GameplayInputSuppressed = suppressed;
        }

        /// <summary>
        /// Stage development rigs can reference the same XRI action asset as the App rig.
        /// Disabling such a legacy InputActionManager disables the shared asset globally,
        /// even though the App manager is still active. Restore every borrowed/owned action
        /// without creating a second action map or changing any binding.
        /// </summary>
        public void EnsureActionsEnabled()
        {
            var restored = 0;
            foreach (var pair in actions)
            {
                var action = pair.Value;
                if (action != null && !action.enabled)
                {
                    action.Enable();
                    restored++;
                }
            }

            if (restored > 0)
                Debug.LogWarning($"[Shared XR Input] Restored {restored} disabled shared XRI action(s).", this);
        }

        static InputActionAsset FindSharedAsset(GameObject commonXrOrigin)
        {
            if (commonXrOrigin == null)
                return null;

            var managers = commonXrOrigin.GetComponentsInChildren<InputActionManager>(true);
            for (var i = 0; i < managers.Length; i++)
            {
                var assets = managers[i].actionAssets;
                for (var j = 0; j < assets.Count; j++)
                {
                    var asset = assets[j];
                    if (asset != null && asset.FindActionMap("XRI Left", false) != null &&
                        asset.FindActionMap("XRI Right", false) != null)
                    {
                        return asset;
                    }
                }
            }
            return null;
        }

        void RegisterXriActions(InputActionAsset asset, SharedXRHand hand, string poseMapName)
        {
            var interactionMap = poseMapName + " Interaction";
            Register(hand, SharedXRControl.Position, asset.FindAction(poseMapName + "/Position", false));
            Register(hand, SharedXRControl.Rotation, asset.FindAction(poseMapName + "/Rotation", false));
            Register(hand, SharedXRControl.Thumbstick, asset.FindAction(poseMapName + "/Thumbstick", false));
            Register(hand, SharedXRControl.Grip, asset.FindAction(interactionMap + "/Select Value", false));
            Register(hand, SharedXRControl.Select, asset.FindAction(interactionMap + "/Select", false));
            Register(hand, SharedXRControl.Trigger, asset.FindAction(interactionMap + "/Activate Value", false));
            Register(hand, SharedXRControl.Activate, asset.FindAction(interactionMap + "/Activate", false));
            Register(hand, SharedXRControl.UIPress, asset.FindAction(interactionMap + "/UI Press", false));

        }

        void RegisterFaceButtons(SharedXRHand hand)
        {
            var usage = hand == SharedXRHand.Left ? "LeftHand" : "RightHand";
            RegisterOwned(hand, SharedXRControl.PrimaryButton,
                $"<XRController>{{{usage}}}/{{PrimaryButton}}",
                $"<OculusTouchController>{{{usage}}}/primaryButton");
            RegisterOwned(hand, SharedXRControl.SecondaryButton,
                $"<XRController>{{{usage}}}/{{SecondaryButton}}",
                $"<OculusTouchController>{{{usage}}}/secondaryButton");
            RegisterOwned(hand, SharedXRControl.ThumbstickClick,
                $"<XRController>{{{usage}}}/{{Primary2DAxisClick}}",
                $"<OculusTouchController>{{{usage}}}/thumbstickClicked");
            if (hand == SharedXRHand.Left)
            {
                RegisterOwned(hand, SharedXRControl.Menu,
                    "<XRController>{LeftHand}/{MenuButton}",
                    "<OculusTouchController>{LeftHand}/menu");
            }
        }

        void RegisterOwned(
            SharedXRHand hand,
            SharedXRControl control,
            string binding,
            string fallbackBinding)
        {
            var action = new InputAction($"Shared XR {hand} {control}", InputActionType.Button);
            action.AddBinding(binding);
            action.AddBinding(fallbackBinding);
            ownedActions.Add(action);
            Register(hand, control, action);
            action.Enable();
        }

        void Register(SharedXRHand hand, SharedXRControl control, InputAction action)
        {
            if (action != null)
                actions[(hand, control)] = action;
        }

        bool ValidateRequiredActions()
        {
            var required = new[]
            {
                SharedXRControl.Trigger,
                SharedXRControl.Grip,
                SharedXRControl.PrimaryButton,
                SharedXRControl.SecondaryButton,
                SharedXRControl.Thumbstick,
                SharedXRControl.ThumbstickClick,
                SharedXRControl.Select,
                SharedXRControl.Activate,
                SharedXRControl.UIPress,
                SharedXRControl.Position,
                SharedXRControl.Rotation,
            };
            var valid = true;
            foreach (SharedXRHand hand in Enum.GetValues(typeof(SharedXRHand)))
            {
                for (var i = 0; i < required.Length; i++)
                {
                    if (TryGetAction(hand, required[i], out _))
                        continue;
                    valid = false;
                    Debug.LogError($"[Shared XR Input] Missing contract action: {hand}/{required[i]}.", this);
                }
            }
            return valid;
        }

        void WireEvents(bool subscribe)
        {
            foreach (var pair in actions)
            {
                if (subscribe)
                {
                    pair.Value.performed += OnPerformed;
                    pair.Value.canceled += OnCanceled;
                }
                else
                {
                    pair.Value.performed -= OnPerformed;
                    pair.Value.canceled -= OnCanceled;
                }
            }
        }

        void OnPerformed(InputAction.CallbackContext context) => Dispatch(context.action, Performed);
        void OnCanceled(InputAction.CallbackContext context) => Dispatch(context.action, Canceled);

        void Dispatch(InputAction action, Action<SharedXRHand, SharedXRControl> callback)
        {
            if (callback == null || action == null)
                return;
            foreach (var pair in actions)
            {
                if (pair.Value == action)
                {
                    // The in-app menu button must remain available while gameplay input
                    // is paused so the same button can close the pause menu again.
                    if (GameplayInputSuppressed && pair.Key.Item2 != SharedXRControl.Menu)
                        return;
                    callback(pair.Key.Item1, pair.Key.Item2);
                    return;
                }
            }
        }

        void DisposeOwnedActions()
        {
            for (var i = 0; i < ownedActions.Count; i++)
                ownedActions[i]?.Dispose();
            ownedActions.Clear();
        }

        void OnDestroy()
        {
            WireEvents(false);
            DisposeOwnedActions();
            actions.Clear();
            IsReady = false;
            GameplayInputSuppressed = false;
            if (Instance == this)
                Instance = null;
        }
    }
}
