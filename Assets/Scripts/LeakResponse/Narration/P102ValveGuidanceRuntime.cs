using UnityEngine;
using VRProject.Valves;

namespace VRProject.LeakResponse.Narration
{
    [DisallowMultipleComponent]
    public sealed class P102ValveGuidanceRuntime : MonoBehaviour
    {
        public enum GuidanceStage
        {
            None,
            SuctionValve,
            DischargeValve,
        }

        static readonly Color GuidanceColor = new Color(0.72f, 0.98f, 1f, 0.92f);
        const float WrongDirectionDisplaySeconds = 0.65f;

        sealed class ValveGuidanceVisual
        {
            public ValveWheelClosureController Valve;
            public GameObject Root;
            public GameObject WrongDirectionRoot;
            public LineRenderer[] MarkerLines;
            public LineRenderer[] DirectionLines;
            public Vector3 PlaneNormal;
            public float WrongDirectionUntil;
        }

        ValveWheelClosureController suctionValve;
        ValveWheelClosureController dischargeValve;
        P102LeakNarrationController narration;
        ValveProgressUI_Runtime suctionProgress;
        ValveProgressUI_Runtime dischargeProgress;
        ValveGuidanceVisual suctionVisual;
        ValveGuidanceVisual dischargeVisual;
        GuidanceStage currentStage;

        public GuidanceStage CurrentStage => currentStage;
        public bool IsSuctionGuidanceVisible => currentStage == GuidanceStage.SuctionValve;
        public bool IsDischargeGuidanceVisible => currentStage == GuidanceStage.DischargeValve;
        public ValveProgressUI_Runtime SuctionProgress => suctionProgress;
        public ValveProgressUI_Runtime DischargeProgress => dischargeProgress;

        public void Configure(ValveWheelClosureController suction, ValveWheelClosureController discharge,
            P102LeakNarrationController narrationController, Transform cameraTransform,
            Transform suctionFixedAnchor, Transform dischargeFixedAnchor)
        {
            Unsubscribe();
            DestroyVisuals();

            suctionValve = suction;
            dischargeValve = discharge;
            narration = narrationController;

            if (suctionValve != null)
            {
                suctionVisual = CreateGuidanceVisual(suctionValve, "SUC");
                suctionProgress = CreateProgress(suctionValve, "흡입측 밸브 폐쇄", cameraTransform,
                    suctionFixedAnchor, "SUC");
                suctionValve.OnFullyClosed.AddListener(OnSuctionClosed);
            }

            if (dischargeValve != null)
            {
                dischargeVisual = CreateGuidanceVisual(dischargeValve, "DIS");
                dischargeProgress = CreateProgress(dischargeValve, "토출측 밸브 폐쇄", cameraTransform,
                    dischargeFixedAnchor, "DIS");
                dischargeValve.OnFullyClosed.AddListener(OnDischargeClosed);
            }

            if (narration != null)
                narration.CueStarted += OnCueStarted;

            SetStage(GuidanceStage.None);
        }

        void Update()
        {
            UpdateVisual(suctionVisual, currentStage == GuidanceStage.SuctionValve);
            UpdateVisual(dischargeVisual, currentStage == GuidanceStage.DischargeValve);
        }

        void OnDestroy()
        {
            Unsubscribe();
            DestroyVisuals();
        }

        void OnCueStarted(P102LeakNarrationCue cue)
        {
            switch (cue)
            {
                case P102LeakNarrationCue.LeakSceneArrival:
                case P102LeakNarrationCue.SuctionValveClosed:
                case P102LeakNarrationCue.BothValvesClosed:
                    SetStage(GuidanceStage.None);
                    break;
                case P102LeakNarrationCue.SuctionValveActivated:
                    SetStage(GuidanceStage.SuctionValve);
                    Debug.Log("[P102 Flow] SUC guidance started", this);
                    break;
                case P102LeakNarrationCue.DischargeValveActivated:
                    SetStage(GuidanceStage.DischargeValve);
                    Debug.Log("[P102 Flow] DIS guidance started", this);
                    break;
            }
        }

        public void ApplyCue(P102LeakNarrationCue cue)
        {
            OnCueStarted(cue);
        }

        void OnSuctionClosed()
        {
            if (currentStage == GuidanceStage.SuctionValve)
                SetStage(GuidanceStage.None);
            Debug.Log("[P102 Flow] SUC fully closed; valve guidance hidden", this);
        }

        void OnDischargeClosed()
        {
            if (currentStage == GuidanceStage.DischargeValve)
                SetStage(GuidanceStage.None);
            Debug.Log("[P102 Flow] DIS fully closed; valve guidance hidden", this);
        }

        void SetStage(GuidanceStage requestedStage)
        {
            GuidanceStage resolvedStage = requestedStage;
            if (resolvedStage == GuidanceStage.SuctionValve &&
                (suctionValve == null || suctionValve.IsFullyClosed))
            {
                resolvedStage = GuidanceStage.None;
            }
            else if (resolvedStage == GuidanceStage.DischargeValve &&
                     (dischargeValve == null || dischargeValve.IsFullyClosed))
            {
                resolvedStage = GuidanceStage.None;
            }

            currentStage = resolvedStage;
            bool showSuction = resolvedStage == GuidanceStage.SuctionValve;
            bool showDischarge = resolvedStage == GuidanceStage.DischargeValve;

            SetVisualVisible(suctionVisual, showSuction);
            SetVisualVisible(dischargeVisual, showDischarge);

            if (showSuction) suctionProgress?.Show(); else suctionProgress?.Hide();
            if (showDischarge) dischargeProgress?.Show(); else dischargeProgress?.Hide();
        }

        void Unsubscribe()
        {
            if (narration != null)
                narration.CueStarted -= OnCueStarted;
            if (suctionValve != null)
                suctionValve.OnFullyClosed.RemoveListener(OnSuctionClosed);
            if (dischargeValve != null)
                dischargeValve.OnFullyClosed.RemoveListener(OnDischargeClosed);
        }

        void DestroyVisuals()
        {
            DestroyRuntimeObject(suctionVisual != null ? suctionVisual.Root : null);
            DestroyRuntimeObject(dischargeVisual != null ? dischargeVisual.Root : null);
            DestroyRuntimeObject(suctionProgress != null ? suctionProgress.gameObject : null);
            DestroyRuntimeObject(dischargeProgress != null ? dischargeProgress.gameObject : null);
            suctionVisual = null;
            dischargeVisual = null;
            suctionProgress = null;
            dischargeProgress = null;
        }

        static void DestroyRuntimeObject(GameObject runtimeObject)
        {
            if (runtimeObject == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(runtimeObject);
            else
                Object.DestroyImmediate(runtimeObject);
        }

        static ValveProgressUI_Runtime CreateProgress(ValveWheelClosureController valve, string title,
            Transform cameraTransform, Transform fixedAnchor, string suffix)
        {
            if (fixedAnchor == null)
            {
                Debug.LogError(
                    $"[P102 UI] {suffix} fixed Valve Root is missing. The progress UI was not created.", valve);
                return null;
            }

            string objectName = $"P102_{suffix}_ProgressUI_Runtime";
            GameObject root = P102RuntimeUtility.FindSceneObject(objectName) ?? new GameObject(objectName);
            P102RuntimeUtility.SetIgnoreRaycastRecursively(root);
            ValveProgressUI_Runtime progress = P102RuntimeUtility.GetOrAdd<ValveProgressUI_Runtime>(root);
            progress.Configure(valve, title, fixedAnchor, cameraTransform);
            return progress;
        }

        static ValveGuidanceVisual CreateGuidanceVisual(ValveWheelClosureController valve, string suffix)
        {
            var visual = new ValveGuidanceVisual
            {
                Valve = valve,
                Root = new GameObject($"P102_{suffix}_ValveGuidance_Runtime"),
                MarkerLines = new LineRenderer[5],
                DirectionLines = new LineRenderer[3],
            };

            ResolvePlane(valve, out visual.PlaneNormal, out Vector3 planeUp);
            visual.Root.transform.rotation = Quaternion.LookRotation(visual.PlaneNormal, planeUp);

            float halfSize = GetMarkerHalfSize(valve);
            float cornerLength = Mathf.Clamp(halfSize * 0.28f, 0.045f, 0.11f);
            for (int i = 0; i < 4; ++i)
            {
                float xSign = (i == 0 || i == 3) ? -1f : 1f;
                float ySign = i < 2 ? 1f : -1f;
                LineRenderer corner = CreateLine($"CornerMarker_{i + 1:00}", visual.Root.transform, 3, 0.009f);
                corner.SetPosition(0, new Vector3(xSign * (halfSize - cornerLength), ySign * halfSize, 0f));
                corner.SetPosition(1, new Vector3(xSign * halfSize, ySign * halfSize, 0f));
                corner.SetPosition(2, new Vector3(xSign * halfSize, ySign * (halfSize - cornerLength), 0f));
                visual.MarkerLines[i] = corner;
            }

            LineRenderer pointer = CreateLine("TopPointer", visual.Root.transform, 4, 0.011f);
            float pointerY = halfSize + 0.075f;
            pointer.SetPositions(new[]
            {
                new Vector3(-0.04f, pointerY + 0.045f, 0f),
                new Vector3(0f, pointerY, 0f),
                new Vector3(0.04f, pointerY + 0.045f, 0f),
                new Vector3(0f, pointerY, 0f),
            });
            visual.MarkerLines[4] = pointer;

            visual.WrongDirectionRoot = new GameObject("CorrectClockwiseDirection_Transient");
            visual.WrongDirectionRoot.transform.SetParent(visual.Root.transform, false);
            LineRenderer arc = CreateLine("ClockwiseArc", visual.WrongDirectionRoot.transform, 17, 0.012f);
            float arrowRadius = halfSize + 0.035f;
            for (int i = 0; i < arc.positionCount; ++i)
            {
                float degrees = Mathf.Lerp(155f, -55f, i / (float)(arc.positionCount - 1));
                float radians = degrees * Mathf.Deg2Rad;
                arc.SetPosition(i, new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * arrowRadius);
            }
            visual.DirectionLines[0] = arc;

            Vector3 tip = arc.GetPosition(arc.positionCount - 1);
            LineRenderer headA = CreateLine("ArrowHeadA", visual.WrongDirectionRoot.transform, 2, 0.014f);
            LineRenderer headB = CreateLine("ArrowHeadB", visual.WrongDirectionRoot.transform, 2, 0.014f);
            headA.SetPositions(new[] { tip + new Vector3(-0.015f, 0.065f, 0f), tip });
            headB.SetPositions(new[] { tip + new Vector3(-0.07f, 0.005f, 0f), tip });
            visual.DirectionLines[1] = headA;
            visual.DirectionLines[2] = headB;

            P102RuntimeUtility.SetIgnoreRaycastRecursively(visual.Root);
            UpdateVisualAnchor(visual);
            SetVisualVisible(visual, false);
            return visual;
        }

        static LineRenderer CreateLine(string objectName, Transform parent, int pointCount, float width)
        {
            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = pointCount;
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = P102RuntimeUtility.CreateCyanMaterial(objectName + "_Material", GuidanceColor);
            line.startColor = line.endColor = GuidanceColor;
            return line;
        }

        static void ResolvePlane(ValveWheelClosureController valve, out Vector3 normal, out Vector3 up)
        {
            HingeJoint hinge = valve != null ? valve.GetComponent<HingeJoint>() : null;
            normal = hinge != null
                ? valve.transform.TransformDirection(hinge.axis).normalized
                : valve != null ? valve.transform.forward : Vector3.forward;
            up = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            if (up.sqrMagnitude < 0.001f)
                up = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
            if (up.sqrMagnitude < 0.001f)
                up = Vector3.right;
        }

        static float GetMarkerHalfSize(ValveWheelClosureController valve)
        {
            if (valve != null && P102RuntimeUtility.TryGetRendererBounds(valve.gameObject, out Bounds bounds))
                return Mathf.Clamp(bounds.extents.magnitude * 0.78f, 0.18f, 0.48f);
            return 0.3f;
        }

        static void UpdateVisual(ValveGuidanceVisual visual, bool active)
        {
            if (visual == null || !active)
                return;

            UpdateVisualAnchor(visual);
            float pulse = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 1.4f) + 1f) * 0.5f;
            Color pulseColor = GuidanceColor;
            pulseColor.a = Mathf.Lerp(0.42f, 0.95f, pulse);
            for (int i = 0; i < visual.MarkerLines.Length; ++i)
            {
                LineRenderer marker = visual.MarkerLines[i];
                if (marker == null)
                    continue;
                marker.startColor = marker.endColor = pulseColor;
                marker.widthMultiplier = Mathf.Lerp(0.007f, 0.012f, pulse);
            }

            if (visual.Valve != null && visual.Valve.IsGrabbed &&
                visual.Valve.CurrentRotationDirection == ValveWheelClosureController.RotationDirection.Opening)
            {
                visual.WrongDirectionUntil = Time.unscaledTime + WrongDirectionDisplaySeconds;
            }

            bool showDirection = Time.unscaledTime < visual.WrongDirectionUntil;
            if (visual.WrongDirectionRoot != null &&
                visual.WrongDirectionRoot.activeSelf != showDirection)
            {
                visual.WrongDirectionRoot.SetActive(showDirection);
            }
        }

        static void UpdateVisualAnchor(ValveGuidanceVisual visual)
        {
            if (visual == null || visual.Root == null || visual.Valve == null)
                return;

            Vector3 center = P102RuntimeUtility.TryGetRendererBounds(visual.Valve.gameObject, out Bounds bounds)
                ? bounds.center
                : visual.Valve.transform.position;
            visual.Root.transform.position = center + visual.PlaneNormal * 0.035f;
        }

        static void SetVisualVisible(ValveGuidanceVisual visual, bool visible)
        {
            if (visual == null || visual.Root == null)
                return;

            visual.WrongDirectionUntil = 0f;
            visual.Root.SetActive(visible);
            if (visual.WrongDirectionRoot != null)
                visual.WrongDirectionRoot.SetActive(false);
        }
    }
}
