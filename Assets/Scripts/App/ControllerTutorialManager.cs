using System.Collections;
using HClTraining.VRInput;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace HClTraining.App
{
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class ControllerTutorialManager : MonoBehaviour
    {
        [Header("Guide")]
        [SerializeField] bool showGuideOnLoading = true;
        [SerializeField] XRControllerGuide visualizer;
        [SerializeField] VRGuideStep[] steps;

        [Header("UI")]
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text progressText;
        [SerializeField] TMP_Text instructionText;
        [SerializeField] TMP_Text descriptionText;
        [SerializeField] TMP_Text usageText;
        [SerializeField] TMP_Text statusText;
        [SerializeField] Button continueButton;
        [SerializeField] Camera standalonePreviewCamera;
        [SerializeField] UnityEvent onOrientationCompleted = new UnityEvent();
        [SerializeField, Min(0.5f)] float locationDisplaySeconds = 1.5f;

        Behaviour[] locomotionBehaviours;
        bool[] locomotionPreviousStates;
        bool locomotionLocked;
        bool acceptingInput;
        int currentStep = -1;
        readonly System.Collections.Generic.List<XRInteractorLineVisual> loadingRayVisuals = new();
        readonly System.Collections.Generic.List<bool> loadingRayVisualStates = new();
        readonly System.Collections.Generic.List<LineRenderer> loadingRayLines = new();
        readonly System.Collections.Generic.List<bool> loadingRayLineStates = new();
        readonly System.Collections.Generic.Dictionary<InputAction, bool> inputActionInitialStates = new();

        public static ControllerTutorialManager Active { get; private set; }
        public bool OrientationCompleted { get; private set; }
        public bool ContinueRequested { get; private set; }
        public UnityEvent OnOrientationCompleted => onOrientationCompleted;

        void Awake()
        {
            ConfigureCamera();
            ResolveAndLockLocomotion();
            HideLoadingRayVisuals();
            if (continueButton != null)
            {
                continueButton.gameObject.SetActive(false);
                continueButton.onClick.AddListener(RequestContinue);
            }
            Active = this;
        }

        void ConfigureCamera()
        {
            var appCamera = AppBootstrap.Instance != null ? AppBootstrap.Instance.MainCamera : null;
            if (standalonePreviewCamera == null)
                return;
            standalonePreviewCamera.gameObject.SetActive(appCamera == null);
        }

        IEnumerator Start()
        {
            if (!ValidateReferences())
            {
                RestoreLocomotion();
                RestoreLoadingRayVisuals();
                yield break;
            }
            EnsureRichTextEnabled();

            if (loadingRayVisuals.Count == 0)
                HideLoadingRayVisuals();
            PlaceRelativeToHeadset();
            for (var i = 0; i < steps.Length; i++)
                EnableStepActions(steps[i]);
            visualizer.ShowBothControllers();
            SetText("VR 조작 가이드", "", "아무 컨트롤러 버튼이나 눌러 시작하십시오.",
                "훈련에서 사용하는 기본 조작을 간단히 연습합니다.", "", "입력 대기 중");

            // XR/controller objects can finish enabling one frame after this scene's
            // Start. Reassert the authored two-controller Intro presentation then.
            yield return null;
            visualizer.ShowBothControllers();

            if (!showGuideOnLoading)
            {
                CompleteGuide();
                yield break;
            }

            while (!AnyIntroActionPressed())
                yield return null;
            yield return new WaitForSecondsRealtime(0.25f);
            BeginStep(0);
        }

        void Update()
        {
            if (!acceptingInput || currentStep < 0 || currentStep >= steps.Length)
                return;
            if (IsStepInputDetected(steps[currentStep]))
                StartCoroutine(ConfirmCurrentStep());
        }

        void BeginStep(int index)
        {
            currentStep = index;
            var step = steps[index];
            var button = ButtonFor(step);
            EnableStepActions(step);
            if (step.controllerSide == ControllerSide.Both)
            {
                if (step.inputType == VRGuideInputType.Grip)
                    visualizer.PresentBothHands(XRControllerGuide.ControllerButton.LeftGrip, XRControllerGuide.ControllerButton.RightGrip);
                else
                    visualizer.PresentBothHands(XRControllerGuide.ControllerButton.LeftTrigger, XRControllerGuide.ControllerButton.RightTrigger);
            }
            else
            {
                PresentStepControls(step, button);
            }
            SetText(visualizer.ColorizeButtonText(button, step.title), $"{index + 1} / {steps.Length}", "강조된 조작 위치를 확인하십시오.",
                step.description, step.usageDescription, "위치 확인 중");
            StartCoroutine(WaitThenAcceptInput(step, button));
        }

        void PresentStepControls(VRGuideStep step, params XRControllerGuide.ControllerButton[] buttons)
        {
            visualizer.PresentControls(buttons);
        }

        IEnumerator WaitThenAcceptInput(VRGuideStep step, XRControllerGuide.ControllerButton button)
        {
            acceptingInput = false;
            yield return new WaitForSecondsRealtime(locationDisplaySeconds);
            instructionText.text = visualizer.ColorizeButtonText(button, step.instructionText);
            statusText.text = "입력 대기 중";
            acceptingInput = true;
        }

        IEnumerator ConfirmCurrentStep()
        {
            acceptingInput = false;
            var step = steps[currentStep];
            statusText.text = "✓  입력이 확인되었습니다.";
            statusText.color = new Color(0.38f, 0.86f, 1f);
            if (step.inputType == VRGuideInputType.Trigger || step.inputType == VRGuideInputType.Grip)
                visualizer.RestoreBaseRotations();
            yield return new WaitForSecondsRealtime(step.completionDelay);
            if (currentStep + 1 < steps.Length)
                BeginStep(currentStep + 1);
            else
                CompleteGuide();
        }

        void CompleteGuide()
        {
            OrientationCompleted = true;
            RestoreInputActionStates();
            visualizer.ShowBothControllers();
            SetText("조작 연습 완료", $"{steps.Length} / {steps.Length}", "기본적인 VR 조작 방법을 익혔습니다.",
                "이제 훈련을 시작할 준비가 되었습니다.", "", "확인 완료");
            if (continueButton != null)
                continueButton.gameObject.SetActive(true);
            onOrientationCompleted.Invoke();
        }

        public void RequestContinue()
        {
            if (!OrientationCompleted)
                return;
            ContinueRequested = true;
            RestoreLocomotion();
        }

        bool AnyIntroActionPressed()
        {
            foreach (var device in InputSystem.devices)
            {
                if (!(device is XRController))
                    continue;
                foreach (var control in device.allControls)
                    if (control is ButtonControl button && button.wasPressedThisFrame)
                        return true;
            }
            return false;
        }

        static bool IsStepInputDetected(VRGuideStep step)
        {
            if (step.inputType == VRGuideInputType.XButton)
                return ControllerFaceButtonPressed(true, true);
            if (step.inputType == VRGuideInputType.YButton)
                return ControllerFaceButtonPressed(true, false);
            if (step.inputType == VRGuideInputType.AButton)
                return ControllerFaceButtonPressed(false, true);
            if (step.inputType == VRGuideInputType.BButton)
                return ControllerFaceButtonPressed(false, false);
            return ButtonPressed(step.primaryAction) || ButtonPressed(step.alternateAction);
        }

        static bool ControllerFaceButtonPressed(bool leftHand, bool primaryButton)
        {
            foreach (var device in InputSystem.devices)
            {
                var handUsage = leftHand ? CommonUsages.LeftHand : CommonUsages.RightHand;
                var matchesHand = false;
                for (var i = 0; i < device.usages.Count; i++)
                    if (device.usages[i] == handUsage) matchesHand = true;
                if (!(device is XRController) || !matchesHand)
                    continue;
                var primary = device.TryGetChildControl<ButtonControl>("primaryButton");
                var secondary = device.TryGetChildControl<ButtonControl>("secondaryButton");
                var target = primaryButton ? primary : secondary;
                if (target != null && target.wasPressedThisFrame) return true;
            }
            return false;
        }

        static bool ButtonPressed(InputActionReference reference) =>
            reference != null && reference.action != null && reference.action.WasPressedThisFrame();

        void EnableStepActions(VRGuideStep step)
        {
            SetActionEnabled(step.primaryAction);
            SetActionEnabled(step.alternateAction);
        }

        void SetActionEnabled(InputActionReference reference)
        {
            var action = reference?.action;
            if (action == null)
                return;

            if (!inputActionInitialStates.ContainsKey(action))
                inputActionInitialStates.Add(action, action.enabled);
            if (!action.enabled)
                action.Enable();
        }

        void RestoreInputActionStates()
        {
            foreach (var state in inputActionInitialStates)
                if (state.Key != null && !state.Value && state.Key.enabled)
                    state.Key.Disable();
            inputActionInitialStates.Clear();
        }

        static XRControllerGuide.ControllerButton ButtonFor(VRGuideStep step)
        {
            switch (step.inputType)
            {
                case VRGuideInputType.Grip: return step.controllerSide == ControllerSide.Left
                    ? XRControllerGuide.ControllerButton.LeftGrip : XRControllerGuide.ControllerButton.RightGrip;
                case VRGuideInputType.Trigger: return step.controllerSide == ControllerSide.Left
                    ? XRControllerGuide.ControllerButton.LeftTrigger : XRControllerGuide.ControllerButton.RightTrigger;
                case VRGuideInputType.XButton: return XRControllerGuide.ControllerButton.X;
                case VRGuideInputType.YButton: return XRControllerGuide.ControllerButton.Y;
                case VRGuideInputType.AButton: return XRControllerGuide.ControllerButton.A;
                default: return XRControllerGuide.ControllerButton.B;
            }
        }

        void PlaceRelativeToHeadset()
        {
            var camera = AppBootstrap.Instance != null ? AppBootstrap.Instance.MainCamera : Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("Controller Guide: HMD camera was not found; authored placement will be used.", this);
                return;
            }
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = camera.transform.forward;
            transform.position = camera.transform.position + forward * 1.5f;
            transform.position = new Vector3(transform.position.x, camera.transform.position.y, transform.position.z);
            // A world-space Canvas is mirrored when the HMD sees its back face.
            // Turn the complete authored presentation toward the headset while
            // preserving all controller poses relative to the guide root.
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        void ResolveAndLockLocomotion()
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
            {
                Debug.LogWarning("Controller Guide: Existing XR Origin was not found.", this);
                return;
            }
            var found = origin.GetComponentsInChildren<Behaviour>(true);
            var list = new System.Collections.Generic.List<Behaviour>();
            foreach (var behaviour in found)
            {
                if (behaviour == null)
                    continue;

                var name = behaviour.GetType().Name;
                if (name.Contains("MoveProvider") || name.Contains("TurnProvider") ||
                    name.Contains("TeleportationProvider") || name.Contains("CharacterControllerDriver"))
                    list.Add(behaviour);
            }
            locomotionBehaviours = list.ToArray();
            locomotionPreviousStates = new bool[locomotionBehaviours.Length];
            for (var i = 0; i < locomotionBehaviours.Length; i++)
            {
                locomotionPreviousStates[i] = locomotionBehaviours[i].enabled;
                locomotionBehaviours[i].enabled = false;
            }
            locomotionLocked = true;
        }

        void RestoreLocomotion()
        {
            if (!locomotionLocked) return;
            for (var i = 0; i < locomotionBehaviours.Length; i++)
                if (locomotionBehaviours[i] != null) locomotionBehaviours[i].enabled = locomotionPreviousStates[i];
            locomotionLocked = false;
        }

        void HideLoadingRayVisuals()
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
                return;

            var seenLines = new System.Collections.Generic.HashSet<LineRenderer>();
            foreach (var visual in origin.GetComponentsInChildren<XRInteractorLineVisual>(true))
            {
                loadingRayVisuals.Add(visual);
                loadingRayVisualStates.Add(visual.enabled);
                visual.enabled = false;

                foreach (var line in visual.GetComponentsInChildren<LineRenderer>(true))
                {
                    if (!seenLines.Add(line)) continue;
                    loadingRayLines.Add(line);
                    loadingRayLineStates.Add(line.enabled);
                    line.enabled = false;
                }
            }
        }

        void RestoreLoadingRayVisuals()
        {
            for (var i = 0; i < loadingRayVisuals.Count; i++)
                if (loadingRayVisuals[i] != null)
                    loadingRayVisuals[i].enabled = loadingRayVisualStates[i];
            for (var i = 0; i < loadingRayLines.Count; i++)
                if (loadingRayLines[i] != null)
                    loadingRayLines[i].enabled = loadingRayLineStates[i];
            loadingRayVisuals.Clear();
            loadingRayVisualStates.Clear();
            loadingRayLines.Clear();
            loadingRayLineStates.Clear();
        }

        bool ValidateReferences()
        {
            var valid = true;
            valid &= ValidateRequiredReference(visualizer, nameof(visualizer));
            valid &= ValidateRequiredReference(titleText, nameof(titleText));
            valid &= ValidateRequiredReference(progressText, nameof(progressText));
            valid &= ValidateRequiredReference(instructionText, nameof(instructionText));
            valid &= ValidateRequiredReference(descriptionText, nameof(descriptionText));
            valid &= ValidateRequiredReference(usageText, nameof(usageText));
            valid &= ValidateRequiredReference(statusText, nameof(statusText));

            if (steps == null || steps.Length == 0)
            {
                Debug.LogError("[ControllerTutorialManager] steps are not assigned.", this);
                valid = false;
            }
            else
            {
                for (var i = 0; i < steps.Length; i++)
                    if (steps[i] == null)
                    {
                        Debug.LogError($"[ControllerTutorialManager] steps[{i}] is not assigned.", this);
                        valid = false;
                    }
            }

            return valid;
        }

        bool ValidateRequiredReference(Object reference, string fieldName)
        {
            if (reference != null)
                return true;
            Debug.LogError($"[ControllerTutorialManager] {fieldName} is not assigned.", this);
            return false;
        }

        void SetText(string title, string progress, string instruction, string description, string usage, string status)
        {
            var theme = visualizer != null ? visualizer.InputVisualTheme : null;
            if (titleText != null)
            {
                titleText.text = VRInputTextFormatter.Format(title, theme);
            }
            if (progressText != null) progressText.text = VRInputTextFormatter.Format(progress, theme);
            if (instructionText != null)
            {
                instructionText.text = VRInputTextFormatter.Format(instruction, theme);
            }
            if (descriptionText != null) descriptionText.text = VRInputTextFormatter.Format(description, theme);
            if (usageText != null) usageText.text = VRInputTextFormatter.Format(usage, theme);
            if (statusText != null)
            {
                statusText.text = VRInputTextFormatter.Format(status, theme);
                statusText.color = new Color(0.72f, 0.78f, 0.84f);
            }
        }

        void EnsureRichTextEnabled()
        {
            var textFields = new[]
            {
                titleText, progressText, instructionText,
                descriptionText, usageText, statusText,
            };
            for (var i = 0; i < textFields.Length; i++)
                if (textFields[i] != null && !textFields[i].richText)
                    textFields[i].richText = true;
        }

        void OnDisable()
        {
            RestoreInputActionStates();
            RestoreLocomotion();
            RestoreLoadingRayVisuals();
        }

        void OnDestroy()
        {
            RestoreInputActionStates();
            RestoreLocomotion();
            RestoreLoadingRayVisuals();
            if (Active == this) Active = null;
        }
    }
}
