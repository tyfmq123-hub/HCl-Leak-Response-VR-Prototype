using System.Collections;
using HClTraining.VRInput;
using UnityEngine;
using TMPro;

namespace HClTraining.App
{
    /// <summary>
    /// Presents stationary, visual-only controller guides without connecting them to XR tracking.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XRControllerGuide : MonoBehaviour
    {
        public enum ControllerButton
        {
            None, LeftTrigger, LeftGrip, LeftStick, X, Y,
            RightTrigger, RightGrip, RightStick, A, B
        }

        [System.Serializable]
        public sealed class GuideItem
        {
            public ControllerButton button;
            public GameObject label;
            public Renderer marker;
        }

        [Header("Controller Visuals")]
        [SerializeField] GameObject leftControllerGuide;
        [SerializeField] GameObject rightControllerGuide;
        [SerializeField] Transform leftPresentationRoot;
        [SerializeField] Transform rightPresentationRoot;
        [Tooltip("Pose-controlled VisualPivot. Kept under the legacy serialized field name for scene compatibility.")]
        [SerializeField] Transform leftControllerVisualRoot;
        [Tooltip("Pose-controlled VisualPivot. Kept under the legacy serialized field name for scene compatibility.")]
        [SerializeField] Transform rightControllerVisualRoot;
        [SerializeField] Transform leftModelOffset;
        [SerializeField] Transform rightModelOffset;
        [Header("Display Points")]
        [SerializeField] Transform leftDisplayPoint;
        [SerializeField] Transform rightDisplayPoint;

        [Header("Visibility")]
        [SerializeField] bool showLeftController = true;
        [SerializeField] bool showRightController = true;

        [Header("Rotation Offsets")]
        [SerializeField] Vector3 triggerRotationOffset = new Vector3(0f, 180f, 0f);
        [SerializeField] Vector3 gripRotationOffset = new Vector3(0f, -90f, 0f);

        [Header("Global VR Input Theme")]
        [SerializeField] VRInputVisualTheme inputVisualTheme;

        [Header("Left Controller Guide Points")]
        [SerializeField] Transform leftTriggerPoint;
        [SerializeField] Transform leftGripPoint;
        [SerializeField] Transform leftStickPoint;
        [SerializeField] Transform leftPrimaryButtonPoint;
        [SerializeField] Transform leftSecondaryButtonPoint;

        [Header("Right Controller Guide Points")]
        [SerializeField] Transform rightTriggerPoint;
        [SerializeField] Transform rightGripPoint;
        [SerializeField] Transform rightStickPoint;
        [SerializeField] Transform rightPrimaryButtonPoint;
        [SerializeField] Transform rightSecondaryButtonPoint;

        [Header("Face Button Highlights")]
        [SerializeField] Renderer xHighlight;
        [SerializeField] Renderer yHighlight;
        [SerializeField] Renderer aHighlight;
        [SerializeField] Renderer bHighlight;

        [Header("Tutorial Presentation")]
        [SerializeField] TMP_Text stepText;
        [SerializeField] GuideItem[] guideItems;
        [SerializeField, Min(2f)] float secondsPerStep = 7f;
        [SerializeField] bool playAutomatically = true;

        static readonly string[] StepMessages =
        {
            "VR 컨트롤러 사용법을 확인해 보세요.",
            "잡기와 조작에 사용하는 버튼입니다.",
            "이동과 방향 전환에 사용하는 스틱입니다.",
            "훈련 안내와 장비 확인에 사용하는 버튼입니다.",
            "컨트롤러 사용법을 확인했습니다."
        };

        int currentStep;
        float nextStepTime;
        MaterialPropertyBlock markerProperties;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly Color NormalMarkerColor = new Color(0.1f, 0.75f, 1f, 0.85f);
        Quaternion leftBaseRotation = Quaternion.identity;
        Quaternion rightBaseRotation = Quaternion.identity;
        Coroutine leftPoseTransition;
        Coroutine rightPoseTransition;
        [SerializeField, Min(0f)] float poseTransitionSeconds = 0.35f;
        ControllerButton[] highlightedButtons = System.Array.Empty<ControllerButton>();

        public Transform LeftTriggerPoint => leftTriggerPoint;
        public Transform LeftGripPoint => leftGripPoint;
        public Transform LeftStickPoint => leftStickPoint;
        public Transform LeftPrimaryButtonPoint => leftPrimaryButtonPoint;
        public Transform LeftSecondaryButtonPoint => leftSecondaryButtonPoint;
        public Transform RightTriggerPoint => rightTriggerPoint;
        public Transform RightGripPoint => rightGripPoint;
        public Transform RightStickPoint => rightStickPoint;
        public Transform RightPrimaryButtonPoint => rightPrimaryButtonPoint;
        public Transform RightSecondaryButtonPoint => rightSecondaryButtonPoint;
        public VRInputVisualTheme InputVisualTheme =>
            inputVisualTheme != null ? inputVisualTheme : VRInputTextFormatter.Theme;

        public Color GetButtonColor(ControllerButton button)
        {
            return TryGetInputType(button, out var input)
                ? InputVisualTheme.GetColor(input)
                : NormalMarkerColor;
        }

        public string ColorizeButtonText(ControllerButton button, string sourceText)
        {
            return TryGetInputType(button, out var input)
                ? VRInputTextFormatter.FormatButtonName(sourceText, input, InputVisualTheme)
                : VRInputTextFormatter.Format(sourceText, InputVisualTheme);
        }

        public static string GetCalloutText(ControllerButton button)
        {
            switch (button)
            {
                case ControllerButton.LeftTrigger:
                case ControllerButton.RightTrigger: return "TRIGGER / 선택";
                case ControllerButton.LeftGrip:
                case ControllerButton.RightGrip: return "GRIP / 잡기";
                case ControllerButton.LeftStick: return "왼쪽 스틱 / 이동";
                case ControllerButton.RightStick: return "오른쪽 스틱 / 방향 전환";
                case ControllerButton.X: return "X 버튼";
                case ControllerButton.Y: return "Y 버튼";
                case ControllerButton.A: return "A 버튼";
                case ControllerButton.B: return "B 버튼";
                default: return button.ToString();
            }
        }

        static bool TryGetInputType(ControllerButton button, out VRInputType input)
        {
            switch (button)
            {
                case ControllerButton.A: input = VRInputType.A; return true;
                case ControllerButton.B: input = VRInputType.B; return true;
                case ControllerButton.X: input = VRInputType.X; return true;
                case ControllerButton.Y: input = VRInputType.Y; return true;
                case ControllerButton.LeftTrigger:
                case ControllerButton.RightTrigger: input = VRInputType.Trigger; return true;
                case ControllerButton.LeftGrip:
                case ControllerButton.RightGrip: input = VRInputType.Grip; return true;
                default:
                    input = default;
                    return false;
            }
        }

        public void PresentSingleControl(ControllerButton button)
        {
            PresentControls(button);
        }

        public void PresentControls(params ControllerButton[] buttons)
        {
            if (ContainsTriggerControl(buttons))
                PresentOffsetControls(triggerRotationOffset, buttons);
            else if (ContainsGripControl(buttons))
                PresentOffsetControls(gripRotationOffset, buttons);
            else
                PresentNaturalControls(buttons);
        }

        void PresentOffsetControls(Vector3 localEulerOffset, params ControllerButton[] buttons)
        {
            var left = HasLeftControl(buttons);
            var right = HasRightControl(buttons);
            SetControllerVisibility(left, right);
            SetLabelsFor(buttons);

            var offset = Quaternion.Euler(localEulerOffset);
            SetTargetRotation(true, left ? leftBaseRotation * offset : leftBaseRotation);
            SetTargetRotation(false, right ? rightBaseRotation * offset : rightBaseRotation);
            HighlightButtons(buttons);

            var visualRoot = left ? leftControllerVisualRoot : rightControllerVisualRoot;
            Debug.Log(
                $"[VRGuide] Step: {string.Join("/", buttons)} | " +
                $"VisualRoot: {(visualRoot != null ? visualRoot.name : "Missing")} | " +
                $"Local Rotation Offset: {localEulerOffset}",
                this);
        }

        public void PresentNaturalControls(params ControllerButton[] buttons)
        {
            var left = HasLeftControl(buttons);
            var right = HasRightControl(buttons);
            SetControllerVisibility(left, right);
            SetLabelsFor(buttons);
            RestoreBaseRotations();
            HighlightButtons(buttons);
        }

        static bool HasLeftControl(ControllerButton[] buttons) =>
            buttons != null && System.Array.Exists(
                buttons, button => button >= ControllerButton.LeftTrigger && button <= ControllerButton.Y);

        static bool HasRightControl(ControllerButton[] buttons) =>
            buttons != null && System.Array.Exists(
                buttons, button => button >= ControllerButton.RightTrigger && button <= ControllerButton.B);

        static bool ContainsTriggerControl(ControllerButton[] buttons) =>
            buttons != null && System.Array.Exists(
                buttons, button => button == ControllerButton.LeftTrigger ||
                                   button == ControllerButton.RightTrigger);

        static bool ContainsGripControl(ControllerButton[] buttons) =>
            buttons != null && System.Array.Exists(
                buttons, button => button == ControllerButton.LeftGrip ||
                                   button == ControllerButton.RightGrip);

        void SetControllerVisibility(bool left, bool right)
        {
            if (leftControllerGuide != null) leftControllerGuide.SetActive(left);
            if (rightControllerGuide != null) rightControllerGuide.SetActive(right);
        }

        void SetLabelsFor(ControllerButton[] buttons)
        {
            if (guideItems == null)
                return;
            for (var i = 0; i < guideItems.Length; i++)
            {
                var item = guideItems[i];
                if (item?.label != null)
                    item.label.SetActive(false);
            }
        }

        void SetTargetRotation(bool left, Quaternion rotation)
        {
            if (left)
            {
                StartRotationTransition(true, leftControllerVisualRoot, rotation);
            }
            else
            {
                StartRotationTransition(false, rightControllerVisualRoot, rotation);
            }
        }

        void StartRotationTransition(bool left, Transform visualPivot, Quaternion rotation)
        {
            if (visualPivot == null)
                return;
            if (!Application.isPlaying || poseTransitionSeconds <= 0f)
            {
                visualPivot.localRotation = rotation;
                return;
            }

            if (left && leftPoseTransition != null) StopCoroutine(leftPoseTransition);
            if (!left && rightPoseTransition != null) StopCoroutine(rightPoseTransition);
            var transition = StartCoroutine(TransitionRotation(visualPivot, rotation, left));
            if (left) leftPoseTransition = transition;
            else rightPoseTransition = transition;
        }

        IEnumerator TransitionRotation(Transform visualPivot, Quaternion rotation, bool left)
        {
            var startRotation = visualPivot.localRotation;
            var elapsed = 0f;
            while (elapsed < poseTransitionSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / poseTransitionSeconds);
                visualPivot.localRotation = Quaternion.Slerp(startRotation, rotation, progress);
                yield return null;
            }
            visualPivot.localRotation = rotation;
            if (left) leftPoseTransition = null;
            else rightPoseTransition = null;
        }

        public void HideControlLabels()
        {
            if (guideItems != null)
                for (var i = 0; i < guideItems.Length; i++)
                    if (guideItems[i]?.label != null)
                        guideItems[i].label.SetActive(false);
            HighlightButtons();
        }

        public void ShowBothControllers()
        {
            HideControlLabels();
            if (leftControllerGuide != null) leftControllerGuide.SetActive(true);
            if (rightControllerGuide != null) rightControllerGuide.SetActive(true);
            RestoreBaseRotations();
        }

        public void PresentBothHands(ControllerButton leftButton, ControllerButton rightButton)
        {
            PresentControls(leftButton, rightButton);
        }

        void Awake()
        {
            LogInitializationPose("LEFT BEFORE INIT", leftControllerVisualRoot);
            LogInitializationPose("RIGHT BEFORE INIT", rightControllerVisualRoot);
            ApplyGuideState();
            EnsurePresentationStructure(
                leftControllerGuide, ref leftPresentationRoot,
                ref leftControllerVisualRoot, ref leftModelOffset);
            EnsurePresentationStructure(
                rightControllerGuide, ref rightPresentationRoot,
                ref rightControllerVisualRoot, ref rightModelOffset);
            CaptureBaseRotations();
            ValidateFaceButtonGuideItems();
            ShowStep(0);
            nextStepTime = Time.unscaledTime + secondsPerStep;
            LogInitializationPose("LEFT AFTER INIT", leftControllerVisualRoot);
            LogInitializationPose("RIGHT AFTER INIT", rightControllerVisualRoot);
        }

        void CaptureBaseRotations()
        {
            if (leftControllerVisualRoot != null)
                leftBaseRotation = leftControllerVisualRoot.localRotation;
            if (rightControllerVisualRoot != null)
                rightBaseRotation = rightControllerVisualRoot.localRotation;
        }

        public void RestoreBaseRotations()
        {
            SetTargetRotation(true, leftBaseRotation);
            SetTargetRotation(false, rightBaseRotation);
        }

        void LogInitializationPose(string label, Transform target)
        {
            if (target == null)
            {
                Debug.Log($"[VRGuide] {label} | Missing", this);
                return;
            }
            Debug.Log(
                $"[VRGuide] {label} | Path={GetHierarchyPath(target)} | " +
                $"LocalPosition={target.localPosition} | LocalRotation={target.localRotation} | " +
                $"LocalScale={target.localScale}",
                this);
        }

        static string GetHierarchyPath(Transform target)
        {
            var path = target.name;
            while (target.parent != null)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }

        void Update()
        {
            PulseHighlightedMarkers();
            if (!playAutomatically || Time.unscaledTime < nextStepTime)
                return;

            ShowStep(Mathf.Min(currentStep + 1, StepMessages.Length - 1));
            nextStepTime = Time.unscaledTime + secondsPerStep;
            if (currentStep == StepMessages.Length - 1)
                playAutomatically = false;
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
                ApplyGuideState();
        }

        public void ApplyGuideState()
        {
            PlaceVisual(leftControllerGuide, leftDisplayPoint, showLeftController);
            PlaceVisual(rightControllerGuide, rightDisplayPoint, showRightController);
        }

        public void ShowStep(int step)
        {
            currentStep = Mathf.Clamp(step, 0, StepMessages.Length - 1);
            if (stepText != null)
                stepText.text = StepMessages[currentStep];

            switch (currentStep)
            {
                case 1:
                    HighlightButtons(ControllerButton.LeftTrigger, ControllerButton.LeftGrip,
                        ControllerButton.RightTrigger, ControllerButton.RightGrip);
                    break;
                case 2:
                    HighlightButtons(ControllerButton.LeftStick, ControllerButton.RightStick);
                    break;
                case 3:
                    HighlightButtons(ControllerButton.X, ControllerButton.Y,
                        ControllerButton.A, ControllerButton.B);
                    break;
                default:
                    HighlightButtons();
                    break;
            }
        }

        public void HighlightButton(ControllerButton button)
        {
            HighlightButtons(button);
        }

        public void HighlightButtons(params ControllerButton[] buttons)
        {
            if (guideItems == null)
                return;

            highlightedButtons = buttons ?? System.Array.Empty<ControllerButton>();
            markerProperties ??= new MaterialPropertyBlock();
            for (var i = 0; i < guideItems.Length; i++)
            {
                var item = guideItems[i];
                if (item == null)
                    continue;

                var highlighted = System.Array.IndexOf(highlightedButtons, item.button) >= 0;
                var buttonColor = GetButtonColor(item.button);
                if (item.label != null)
                    item.label.SetActive(false);
                if (item.marker == null)
                    continue;

                item.marker.gameObject.SetActive(highlighted);
                item.marker.enabled = highlighted;
                item.marker.GetPropertyBlock(markerProperties);
                markerProperties.SetColor(BaseColorId, highlighted ? buttonColor : NormalMarkerColor);
                markerProperties.SetColor(EmissionColorId, highlighted ? buttonColor * 1.35f : NormalMarkerColor);
                item.marker.SetPropertyBlock(markerProperties);
                item.marker.transform.localScale = Vector3.one * (highlighted ? 0.024f : 0.018f);
                if (highlighted)
                    Debug.Log(
                        $"[VRGuide] Step={item.button}, Highlight={item.marker.name}, " +
                        $"IsNull=false, Activated={item.marker.gameObject.activeInHierarchy}",
                        this);
            }
        }

        void PulseHighlightedMarkers()
        {
            if (guideItems == null || highlightedButtons == null)
                return;
            var pulse = 0.021f + Mathf.Sin(Time.unscaledTime * 3f) * 0.0025f;
            for (var i = 0; i < guideItems.Length; i++)
            {
                var item = guideItems[i];
                if (item?.marker != null && System.Array.IndexOf(highlightedButtons, item.button) >= 0)
                    item.marker.transform.localScale = Vector3.one * pulse;
            }
        }

        static void PlaceVisual(GameObject visual, Transform displayPoint, bool visible)
        {
            if (visual == null)
                return;

            visual.SetActive(visible);
            // The complete presentation hierarchy is authored by the editor Builder.
            // Runtime must not reparent or reset its serialized transform values.
            DisableInteractionAndTracking(visual);
        }

        static void EnsurePresentationStructure(
            GameObject controllerGuide,
            ref Transform presentationRoot,
            ref Transform visualPivot,
            ref Transform modelOffset)
        {
            if (controllerGuide == null)
                return;

            presentationRoot ??= controllerGuide.transform.Find("PresentationRoot");
            visualPivot ??= presentationRoot != null ? presentationRoot.Find("VisualPivot") : null;
            modelOffset ??= visualPivot != null ? visualPivot.Find("ModelOffset") : null;

            if (presentationRoot == null || visualPivot == null || modelOffset == null)
                Debug.LogError(
                    $"[VRGuide] Saved presentation hierarchy is incomplete on {controllerGuide.name}. " +
                    "Run the editor Builder; runtime reconstruction is disabled.",
                    controllerGuide);
        }

        void ValidateFaceButtonGuideItems()
        {
            ValidateFaceButtonGuideItem(ControllerButton.X, xHighlight);
            ValidateFaceButtonGuideItem(ControllerButton.Y, yHighlight);
            ValidateFaceButtonGuideItem(ControllerButton.A, aHighlight);
            ValidateFaceButtonGuideItem(ControllerButton.B, bHighlight);
        }

        void ValidateFaceButtonGuideItem(ControllerButton button, Renderer highlightReference)
        {
            GuideItem item = null;
            if (guideItems != null)
                item = System.Array.Find(guideItems, candidate => candidate != null && candidate.button == button);

            if (item == null)
            {
                Debug.LogWarning($"[VRGuide] {button} GuideItem is not assigned. Run the Loading Controller Guide Builder.", this);
                return;
            }

            if (item.marker == null)
                Debug.LogWarning($"[VRGuide] {button} Highlight marker is not assigned.", this);
            if (highlightReference == null)
                Debug.LogWarning($"[VRGuide] {button} serialized highlight reference is not assigned.", this);
            else if (item.marker != null && item.marker != highlightReference)
                Debug.LogWarning($"[VRGuide] {button} GuideItem marker and serialized highlight reference do not match.", this);
        }

        static void DisableInteractionAndTracking(GameObject visual)
        {
            var behaviours = visual.GetComponentsInChildren<Behaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null || behaviour is TMP_Text)
                    continue;

                var typeName = behaviour.GetType().Name;
                if (typeName.Contains("Interactor") ||
                    typeName.Contains("TrackedPose") ||
                    typeName.Contains("ActionBasedController") ||
                    typeName.Contains("ControllerInputAction") ||
                    typeName.Contains("ControllerModel"))
                    behaviour.enabled = false;
            }

            var colliders = visual.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            var rigidbodies = visual.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].isKinematic = true;
                rigidbodies[i].detectCollisions = false;
            }
        }
    }
}
