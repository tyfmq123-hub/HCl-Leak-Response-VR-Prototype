using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRProject.LeakResponse.Logic;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>Single-mesh clockwise annular gauge; no rotating child objects are used.</summary>
    [DisallowMultipleComponent]
    public sealed class P102RadialGaugeGraphic : MaskableGraphic
    {
        [SerializeField, Range(0f, 1f)] float fillAmount = 1f;
        [SerializeField, Min(1f)] float thickness = 22f;

        public float FillAmount
        {
            get => fillAmount;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(fillAmount, clamped))
                    return;
                fillAmount = clamped;
                SetVerticesDirty();
            }
        }

        public float Thickness
        {
            get => thickness;
            set
            {
                thickness = Mathf.Max(1f, value);
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (fillAmount <= 0f)
                return;

            Rect rect = GetPixelAdjustedRect();
            float outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
            float innerRadius = Mathf.Max(0f, outerRadius - thickness);
            Vector2 center = rect.center;
            int segmentCount = Mathf.Max(1, Mathf.CeilToInt(96f * fillAmount));
            float sweep = Mathf.PI * 2f * fillAmount;

            for (int segment = 0; segment < segmentCount; ++segment)
            {
                float t0 = segment / (float)segmentCount;
                float t1 = (segment + 1f) / segmentCount;
                // Top origin, clockwise decrease from the remaining arc's trailing edge.
                float angle0 = Mathf.PI * 0.5f - sweep * t0;
                float angle1 = Mathf.PI * 0.5f - sweep * t1;
                int firstVertex = vertexHelper.currentVertCount;
                AddVertex(vertexHelper, center, outerRadius, angle0);
                AddVertex(vertexHelper, center, innerRadius, angle0);
                AddVertex(vertexHelper, center, innerRadius, angle1);
                AddVertex(vertexHelper, center, outerRadius, angle1);
                vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
                vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
            }
        }

        void AddVertex(VertexHelper vertexHelper, Vector2 center, float radius, float angle)
        {
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            vertexHelper.AddVert(vertex);
        }
    }

    [DisallowMultipleComponent]
    public sealed class P102FlangeGazeRuntime : MonoBehaviour
    {
        const float RequiredGazeSeconds = 3f;
        const float MaximumGazeDistance = 8f;
        const float SurfaceClearanceMeters = 0.075f;
        const float VerticalClearanceMeters = 0.10f;
        const float CanvasScale = 0.001f;
        const float CanvasHeightUnits = 360f;
        const string CountdownObjectName = "P102_FlangeGazeCountdown_Runtime";

        readonly ContinuousGazeTimer timer = new ContinuousGazeTimer(RequiredGazeSeconds);
        readonly HashSet<Collider> targetColliderSet = new HashSet<Collider>();
        readonly RaycastHit[] raycastHits = new RaycastHit[32];

        P102LeakNarrationController narration;
        Transform flangeRoot;
        Transform leakPoint;
        Transform viewerCamera;
        Collider[] targetColliders = Array.Empty<Collider>();
        GameObject countdownRoot;
        P102RadialGaugeGraphic radialFill;
        TMP_Text countdownText;
        TMP_Text instructionText;
        Collider lastHitCollider;
        Collider leakPointCollider;
        bool gazeStageActive;
        bool flangeStageEntered;
        bool wasGazing;
        bool warnedMissingReference;
        int completionDispatchCount;

        public bool IsStageActive => gazeStageActive;
        public bool HasEnteredStage => flangeStageEntered;
        public float ElapsedGazeSeconds => timer.ElapsedSeconds;
        public bool IsComplete => timer.IsComplete;
        public Transform FixedAnchor => flangeRoot;
        public Transform CountdownTransform => countdownRoot != null ? countdownRoot.transform : null;
        public Collider LastHitCollider => lastHitCollider;
        public int CompletionDispatchCount => completionDispatchCount;
        public float GaugeFillAmount => radialFill != null ? radialFill.FillAmount : 0f;
        public float ConfiguredSurfaceClearanceMeters => SurfaceClearanceMeters;
        public float ConfiguredVerticalClearanceMeters => VerticalClearanceMeters;

        public void Configure(P102LeakNarrationController narrationController, Transform fixedFlangeRoot,
            Transform leakPointTransform, Transform cameraTransform)
        {
            Unsubscribe();
            narration = narrationController;
            flangeRoot = fixedFlangeRoot;
            leakPoint = leakPointTransform;
            viewerCamera = ResolveActiveHeadCamera(cameraTransform);
            timer.Reset();
            gazeStageActive = false;
            flangeStageEntered = false;
            wasGazing = false;
            warnedMissingReference = false;
            completionDispatchCount = 0;

            EnsureLeakPointCollider();
            ResolveTargetColliders();
            BuildOrReuseCountdown();
            SetCountdownVisible(false);

            if (narration != null)
            {
                narration.CueStarted += OnCueStarted;
                narration.CueCompleted += OnCueCompleted;
            }

            Debug.Log($"[P102 Gaze] Head Camera={P102RuntimeUtility.GetHierarchyPath(viewerCamera)}", this);
            Debug.Log($"[P102 Gaze] Flange Root={P102RuntimeUtility.GetHierarchyPath(flangeRoot)}", this);
        }

        void Update()
        {
            if (gazeStageActive)
                TickGaze(Time.deltaTime);
        }

        void LateUpdate()
        {
            if (countdownRoot != null && countdownRoot.activeSelf)
            {
                P102RuntimeUtility.ApplyYawOnlyBillboard(
                    countdownRoot.transform, viewerCamera, Time.unscaledDeltaTime);
            }
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        void OnCueStarted(P102LeakNarrationCue cue)
        {
            if (cue != P102LeakNarrationCue.LeakReductionStarted)
                return;

            flangeStageEntered = true;
            gazeStageActive = false;
            timer.Reset();
            SetCountdownVisible(true);
            UpdateListeningDisplay();
            Debug.Log("[P102 Gaze] Flange stage entered; waiting for TTS 06 to finish.", this);
        }

        void OnCueCompleted(P102LeakNarrationCue cue)
        {
            if (cue == P102LeakNarrationCue.LeakReductionStarted)
            {
                viewerCamera = ResolveActiveHeadCamera(viewerCamera);
                if (!HasRequiredReferences())
                {
                    ReportMissingReferencesOnce();
                    return;
                }

                timer.Reset();
                gazeStageActive = true;
                wasGazing = false;
                lastHitCollider = null;
                SetCountdownVisible(true);
                UpdateCountdownText(false);
                Debug.Log(
                    $"[P102 Gaze] Gaze activated after TTS 06. Head={P102RuntimeUtility.GetHierarchyPath(viewerCamera)}, " +
                    $"Flange={P102RuntimeUtility.GetHierarchyPath(flangeRoot)}", this);
            }
            else if (cue == P102LeakNarrationCue.LeakReductionCompleted)
            {
                SetCountdownVisible(false);
                bool enqueued = narration != null &&
                                narration.EnqueueOnce(P102LeakNarrationCue.AbsorbentSockPlacementActivated);
                Debug.Log(
                    $"[P102 Gaze] Completion forwarded to next Gate=" +
                    $"{P102LeakNarrationCue.AbsorbentSockPlacementActivated}, enqueued={enqueued}", this);
            }
        }

        void TickGaze(float deltaTime)
        {
            if (!HasRequiredReferences())
            {
                ReportMissingReferencesOnce();
                return;
            }

            bool hoveringTarget = IsHeadRayHittingFlange(out Collider hitCollider);
            if (hoveringTarget && !wasGazing)
            {
                Debug.Log(
                    $"[P102 Gaze] Entered flange collider=" +
                    P102RuntimeUtility.GetHierarchyPath(hitCollider.transform), this);
            }
            else if (!hoveringTarget && wasGazing)
            {
                Debug.Log(
                    $"[P102 Gaze] Exited flange; timer reset from {timer.ElapsedSeconds:F2}s.", this);
            }

            bool completedThisTick = timer.Tick(hoveringTarget, deltaTime);
            wasGazing = hoveringTarget;
            lastHitCollider = hitCollider;
            UpdateCountdownText(hoveringTarget);

            if (!completedThisTick)
                return;

            gazeStageActive = false;
            ++completionDispatchCount;
            if (countdownText != null)
                countdownText.text = "✓";
            if (instructionText != null)
                instructionText.text = "확인 완료";
            if (radialFill != null)
                radialFill.FillAmount = 0f;
            Debug.Log("[P102 Gaze] 3초 연속 Gaze 완료 (exactly once).", this);
            bool enqueued = narration != null &&
                            narration.EnqueueOnce(P102LeakNarrationCue.LeakReductionCompleted);
            Debug.Log(
                $"[P102 Flow] LeakReductionCompleted delivered to Gate=" +
                $"{P102LeakNarrationCue.LeakReductionCompleted}, enqueued={enqueued}", this);
        }

        void Unsubscribe()
        {
            if (narration != null)
            {
                narration.CueStarted -= OnCueStarted;
                narration.CueCompleted -= OnCueCompleted;
            }
        }

        bool IsHeadRayHittingFlange(out Collider hitCollider)
        {
            hitCollider = null;
            if (viewerCamera == null)
                return false;

            var ray = new Ray(viewerCamera.position, viewerCamera.forward);
            int hitCount = Physics.RaycastNonAlloc(ray, raycastHits, MaximumGazeDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            float nearestDistance = float.PositiveInfinity;
            Collider nearestCollider = null;

            for (int i = 0; i < hitCount; ++i)
            {
                Collider candidate = raycastHits[i].collider;
                if (candidate == null || raycastHits[i].distance >= nearestDistance)
                    continue;
                if (candidate.transform == viewerCamera || candidate.transform.IsChildOf(viewerCamera))
                    continue;

                nearestDistance = raycastHits[i].distance;
                nearestCollider = candidate;
            }

            hitCollider = nearestCollider;
            return nearestCollider != null && targetColliderSet.Contains(nearestCollider);
        }

        void ResolveTargetColliders()
        {
            targetColliderSet.Clear();
            if (flangeRoot == null)
            {
                targetColliders = Array.Empty<Collider>();
                return;
            }

            Collider[] candidates = flangeRoot.GetComponentsInChildren<Collider>(true);
            var resolved = new List<Collider>(candidates.Length);
            for (int i = 0; i < candidates.Length; ++i)
            {
                Collider candidate = candidates[i];
                if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy)
                    continue;
                if (candidate.GetComponentInParent<Canvas>() != null)
                    continue;
                if (IsLeakEffectCollider(candidate))
                    continue;

                resolved.Add(candidate);
                targetColliderSet.Add(candidate);
                Debug.Log(
                    $"[P102 Gaze] Registered flange collider=" +
                    P102RuntimeUtility.GetHierarchyPath(candidate.transform), candidate);
            }

            targetColliders = resolved.ToArray();
        }

        void EnsureLeakPointCollider()
        {
            leakPointCollider = null;
            if (leakPoint == null || flangeRoot == null || !leakPoint.IsChildOf(flangeRoot))
                return;

            SphereCollider sphere = leakPoint.GetComponent<SphereCollider>();
            if (sphere == null)
                sphere = leakPoint.gameObject.AddComponent<SphereCollider>();
            sphere.center = Vector3.zero;
            sphere.radius = 0.16f;
            sphere.isTrigger = true;
            sphere.enabled = true;
            leakPointCollider = sphere;
        }

        static bool IsLeakEffectCollider(Collider candidate)
        {
            Transform current = candidate != null ? candidate.transform : null;
            while (current != null)
            {
                if (current.name.IndexOf("Puddle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("FloorImpact", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("FallingDrop", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        void BuildOrReuseCountdown()
        {
            if (flangeRoot == null)
                return;

            if (countdownRoot == null)
            {
                Transform existing = FindChildByName(flangeRoot, CountdownObjectName);
                countdownRoot = existing != null ? existing.gameObject : null;
            }

            if (countdownRoot == null)
            {
                countdownRoot = new GameObject(CountdownObjectName, typeof(RectTransform), typeof(Canvas));
                Canvas canvas = countdownRoot.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                RectTransform rootRect = countdownRoot.GetComponent<RectTransform>();
                rootRect.sizeDelta = new Vector2(390f, CanvasHeightUnits);

                Image panel = countdownRoot.AddComponent<Image>();
                panel.color = new Color(0.015f, 0.035f, 0.045f, 0.90f);
                panel.raycastTarget = false;
                CanvasGroup group = countdownRoot.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;

                P102RadialGaugeGraphic track = CreateRadialGraphic(
                    "CountdownTrack", countdownRoot.transform,
                    new Color(0.03f, 0.09f, 0.11f, 0.98f), 1f);
                SetRect(track.rectTransform, new Vector2(0f, 18f), new Vector2(238f, 238f));
                radialFill = CreateRadialGraphic(
                    "CountdownFill", countdownRoot.transform,
                    new Color(0.1f, 0.95f, 1f, 1f), 1f);
                SetRect(radialFill.rectTransform, new Vector2(0f, 18f), new Vector2(238f, 238f));

                var textObject = new GameObject(
                    "CountdownText", typeof(RectTransform), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(countdownRoot.transform, false);
                countdownText = textObject.GetComponent<TextMeshProUGUI>();
                countdownText.font = P102RuntimeUtility.LoadRuntimeFont();
                countdownText.fontSize = 58f;
                countdownText.color = Color.white;
                countdownText.outlineColor = new Color32(0, 0, 0, 220);
                countdownText.outlineWidth = 0.16f;
                countdownText.alignment = TextAlignmentOptions.Center;
                SetRect(countdownText.rectTransform, new Vector2(0f, 18f), new Vector2(210f, 100f));

                var instructionObject = new GameObject(
                    "InstructionText", typeof(RectTransform), typeof(TextMeshProUGUI));
                instructionObject.transform.SetParent(countdownRoot.transform, false);
                instructionText = instructionObject.GetComponent<TextMeshProUGUI>();
                instructionText.font = P102RuntimeUtility.LoadRuntimeFont();
                instructionText.fontSize = 29f;
                instructionText.color = Color.white;
                instructionText.outlineColor = new Color32(0, 0, 0, 220);
                instructionText.outlineWidth = 0.14f;
                instructionText.alignment = TextAlignmentOptions.Center;
                instructionText.textWrappingMode = TextWrappingModes.Normal;
                SetRect(instructionText.rectTransform, new Vector2(0f, -135f), new Vector2(350f, 58f));
            }
            else
            {
                Transform countdown = FindChildByName(countdownRoot.transform, "CountdownText");
                Transform instruction = FindChildByName(countdownRoot.transform, "InstructionText");
                Transform fill = FindChildByName(countdownRoot.transform, "CountdownFill");
                countdownText = countdown != null ? countdown.GetComponent<TMP_Text>() : null;
                instructionText = instruction != null ? instruction.GetComponent<TMP_Text>() : null;
                radialFill = fill != null ? fill.GetComponent<P102RadialGaugeGraphic>() : null;
            }

            countdownRoot.transform.SetParent(flangeRoot, false);
            ConfigurePassiveUi();
            PlaceCountdownOutsideFlange();
        }

        static P102RadialGaugeGraphic CreateRadialGraphic(string objectName, Transform parent,
            Color color, float fillAmount)
        {
            var radialObject = new GameObject(
                objectName, typeof(RectTransform), typeof(P102RadialGaugeGraphic));
            radialObject.transform.SetParent(parent, false);
            P102RadialGaugeGraphic graphic = radialObject.GetComponent<P102RadialGaugeGraphic>();
            graphic.color = color;
            graphic.FillAmount = fillAmount;
            graphic.Thickness = 24f;
            graphic.raycastTarget = false;
            return graphic;
        }

        static void SetRect(RectTransform rectTransform, Vector2 anchoredPosition, Vector2 size)
        {
            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;
        }

        void ConfigurePassiveUi()
        {
            Graphic[] graphics = countdownRoot.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; ++i)
                graphics[i].raycastTarget = false;

            CanvasGroup[] groups = countdownRoot.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; ++i)
            {
                groups[i].interactable = false;
                groups[i].blocksRaycasts = false;
            }

            GraphicRaycaster[] raycasters = countdownRoot.GetComponentsInChildren<GraphicRaycaster>(true);
            for (int i = 0; i < raycasters.Length; ++i)
                raycasters[i].enabled = false;
        }

        void PlaceCountdownOutsideFlange()
        {
            if (countdownRoot == null || flangeRoot == null ||
                !P102RuntimeUtility.TryGetLocalColliderBounds(flangeRoot, targetColliders,
                    out Bounds localBounds))
            {
                return;
            }

            Vector3 faceAxis = localBounds.size.z <= localBounds.size.x
                ? Vector3.forward
                : Vector3.right;
            Vector3 observerLocalOffset = viewerCamera != null
                ? flangeRoot.InverseTransformPoint(viewerCamera.position) - localBounds.center
                : leakPoint != null
                    ? flangeRoot.InverseTransformPoint(leakPoint.position) - localBounds.center
                    : faceAxis;
            if (Vector3.Dot(observerLocalOffset, faceAxis) < 0f)
                faceAxis = -faceAxis;

            float rootUpScale = Mathf.Max(0.0001f, flangeRoot.TransformVector(Vector3.up).magnitude);
            float faceScale = Mathf.Max(0.0001f, flangeRoot.TransformVector(faceAxis).magnitude);
            float localVerticalGap = VerticalClearanceMeters / rootUpScale;
            float localSurfaceGap = SurfaceClearanceMeters / faceScale;
            float faceExtent = Mathf.Abs(faceAxis.z) > 0.5f
                ? localBounds.extents.z
                : localBounds.extents.x;
            float canvasHalfHeight = CanvasHeightUnits * CanvasScale * 0.5f;

            Vector3 localPosition = localBounds.center;
            localPosition += Vector3.up *
                             (localBounds.extents.y + localVerticalGap + canvasHalfHeight);
            localPosition += faceAxis * (faceExtent + localSurfaceGap);

            Transform countdownTransform = countdownRoot.transform;
            countdownTransform.localPosition = localPosition;
            countdownTransform.localScale = Vector3.one * CanvasScale;
            countdownTransform.rotation = P102RuntimeUtility.ResolveYawOnlyCanvasRotation(
                countdownTransform.position,
                viewerCamera != null
                    ? viewerCamera.position
                    : countdownTransform.position + flangeRoot.TransformDirection(faceAxis),
                flangeRoot.rotation);

            Debug.Log(
                $"[P102 UI] Flange fixed anchor={P102RuntimeUtility.GetHierarchyPath(flangeRoot)}, " +
                $"localPosition={countdownTransform.localPosition:F4}, " +
                $"localRotation={countdownTransform.localRotation.eulerAngles:F2}, " +
                $"surfaceGap={SurfaceClearanceMeters:F3}m, verticalGap={VerticalClearanceMeters:F3}m",
                countdownRoot);
        }

        void UpdateCountdownText(bool hovering)
        {
            if (countdownText == null)
                return;

            int remaining = Mathf.Max(1, Mathf.CeilToInt(RequiredGazeSeconds - timer.ElapsedSeconds));
            countdownText.text = $"{remaining}초";
            if (instructionText != null)
                instructionText.text = "플랜지를 3초간 바라보세요";
            if (radialFill != null)
            {
                radialFill.FillAmount = Mathf.Clamp01(
                    (RequiredGazeSeconds - timer.ElapsedSeconds) / RequiredGazeSeconds);
            }
        }

        void UpdateListeningDisplay()
        {
            if (countdownText != null)
                countdownText.text = "3초";
            if (instructionText != null)
                instructionText.text = "안내를 들어주세요";
            if (radialFill != null)
                radialFill.FillAmount = 1f;
        }

        void SetCountdownVisible(bool visible)
        {
            if (countdownRoot != null)
                countdownRoot.SetActive(visible);
        }

        bool HasRequiredReferences()
        {
            return viewerCamera != null && viewerCamera.gameObject.activeInHierarchy &&
                   flangeRoot != null && targetColliders.Length > 0;
        }

        void ReportMissingReferencesOnce()
        {
            if (warnedMissingReference)
                return;

            warnedMissingReference = true;
            string missing = viewerCamera == null
                ? "active HMD/Main Camera"
                : flangeRoot == null
                    ? "fixed Flange Root"
                    : targetColliders.Length == 0
                        ? "enabled Flange Collider"
                        : "active prerequisite";
            var candidatePaths = new List<string>();
            Collider[] allColliders = Resources.FindObjectsOfTypeAll<Collider>();
            for (int i = 0; i < allColliders.Length; ++i)
            {
                Collider candidate = allColliders[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid())
                    continue;
                string path = P102RuntimeUtility.GetHierarchyPath(candidate.transform);
                if (path.IndexOf("Flange", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("Leak", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    candidatePaths.Add(path);
                }
            }

            Debug.LogError(
                $"[P102 Gaze] Missing {missing}; automatic completion is disabled. " +
                $"Searched collider candidates: {string.Join(", ", candidatePaths)}", this);
        }

        static Transform ResolveActiveHeadCamera(Transform preferred)
        {
            if (IsActiveCamera(preferred))
                return preferred;

            Camera mainCamera = Camera.main;
            if (mainCamera != null && IsActiveCamera(mainCamera.transform))
                return mainCamera.transform;

            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < cameras.Length; ++i)
            {
                Camera camera = cameras[i];
                if (camera != null && camera.gameObject.scene.IsValid() &&
                    IsActiveCamera(camera.transform) &&
                    camera.stereoTargetEye != StereoTargetEyeMask.None)
                {
                    return camera.transform;
                }
            }

            return null;
        }

        static bool IsActiveCamera(Transform candidate)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
                return false;
            Camera camera = candidate.GetComponent<Camera>();
            return camera != null && camera.enabled;
        }

        static Transform FindChildByName(Transform root, string exactName)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; ++i)
            {
                if (children[i] != root &&
                    string.Equals(children[i].name, exactName, StringComparison.Ordinal))
                {
                    return children[i];
                }
            }
            return null;
        }

        public bool IsRegisteredTargetCollider(Collider candidate)
        {
            return candidate != null && targetColliderSet.Contains(candidate);
        }
    }
}
