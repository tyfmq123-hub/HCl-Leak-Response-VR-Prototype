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
        static readonly Color DischargeArrowColor = new Color(0f, 0.42f, 1f, 1f);
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
            public Mesh DischargeArrowMesh;
            public Material DischargeArrowMaterial;
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
                suctionVisual = CreateGuidanceVisual(suctionValve, "SUC", false);
                suctionProgress = CreateProgress(suctionValve, "흡입측 밸브 폐쇄", cameraTransform,
                    suctionFixedAnchor, "SUC");
                suctionValve.OnFullyClosed.AddListener(OnSuctionClosed);
            }

            if (dischargeValve != null)
            {
                dischargeVisual = CreateGuidanceVisual(dischargeValve, "DIS", true);
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
            DestroyVisualAssets(suctionVisual);
            DestroyVisualAssets(dischargeVisual);
            DestroyRuntimeObject(suctionVisual != null ? suctionVisual.Root : null);
            DestroyRuntimeObject(dischargeVisual != null ? dischargeVisual.Root : null);
            DestroyRuntimeObject(suctionProgress != null ? suctionProgress.gameObject : null);
            DestroyRuntimeObject(dischargeProgress != null ? dischargeProgress.gameObject : null);
            suctionVisual = null;
            dischargeVisual = null;
            suctionProgress = null;
            dischargeProgress = null;
        }

        static void DestroyVisualAssets(ValveGuidanceVisual visual)
        {
            if (visual == null)
                return;

            DestroyRuntimeObject(visual.DischargeArrowMesh);
            DestroyRuntimeObject(visual.DischargeArrowMaterial);
        }

        static void DestroyRuntimeObject(Object runtimeObject)
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

        static ValveGuidanceVisual CreateGuidanceVisual(ValveWheelClosureController valve, string suffix,
            bool isDischarge)
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
            float cornerLength = Mathf.Clamp(halfSize * 0.32f, 0.08f, 0.16f);
            for (int i = 0; i < 4; ++i)
            {
                float xSign = (i == 0 || i == 3) ? -1f : 1f;
                float ySign = i < 2 ? 1f : -1f;
                LineRenderer corner = CreateLine($"CornerMarker_{i + 1:00}", visual.Root.transform, 3, 0.018f);
                corner.SetPosition(0, new Vector3(xSign * (halfSize - cornerLength), ySign * halfSize, 0f));
                corner.SetPosition(1, new Vector3(xSign * halfSize, ySign * halfSize, 0f));
                corner.SetPosition(2, new Vector3(xSign * halfSize, ySign * (halfSize - cornerLength), 0f));
                visual.MarkerLines[i] = corner;
            }

            // Keep a disabled LineRenderer for backwards-compatible runtime structure/tests.
            // The visible discharge arrow is a fixed planar mesh so its segments cannot
            // billboard independently or pulse wider when viewed from the side.
            LineRenderer pointer = CreateLine("DischargeValvePointer_Disabled", visual.Root.transform, 2, 0.01f);
            pointer.enabled = false;
            if (isDischarge)
                CreateStableDischargeArrow(visual, halfSize);
            visual.MarkerLines[4] = pointer;

            visual.WrongDirectionRoot = new GameObject("CorrectClockwiseDirection_Transient");
            visual.WrongDirectionRoot.transform.SetParent(visual.Root.transform, false);
            LineRenderer arc = CreateLine("ClockwiseArc", visual.WrongDirectionRoot.transform, 17, 0.026f);
            float arrowRadius = halfSize + 0.075f;
            for (int i = 0; i < arc.positionCount; ++i)
            {
                float degrees = Mathf.Lerp(35f, -145f, i / (float)(arc.positionCount - 1));
                float radians = degrees * Mathf.Deg2Rad;
                arc.SetPosition(i, new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * arrowRadius);
            }
            visual.DirectionLines[0] = arc;

            Vector3 tip = arc.GetPosition(arc.positionCount - 1);
            LineRenderer headA = CreateLine("ArrowHeadA", visual.WrongDirectionRoot.transform, 2, 0.03f);
            LineRenderer headB = CreateLine("ArrowHeadB", visual.WrongDirectionRoot.transform, 2, 0.03f);
            headA.SetPositions(new[] { tip + new Vector3(-0.025f, 0.11f, 0f), tip });
            headB.SetPositions(new[] { tip + new Vector3(-0.12f, 0.01f, 0f), tip });
            visual.DirectionLines[1] = headA;
            visual.DirectionLines[2] = headB;

            P102RuntimeUtility.SetIgnoreRaycastRecursively(visual.Root);
            UpdateVisualAnchor(visual);
            SetVisualVisible(visual, false);
            return visual;
        }

        static void CreateStableDischargeArrow(ValveGuidanceVisual visual, float halfSize)
        {
            var arrowObject = new GameObject("DischargeValveArrowMesh");
            arrowObject.transform.SetParent(visual.Root.transform, false);

            float tipX = halfSize + 0.12f;
            float neckX = tipX + 0.34f;
            float tailX = halfSize + 1.05f;
            const float bodyHalfHeight = 0.11f;
            const float headHalfHeight = 0.34f;

            var mesh = new Mesh { name = "P102 Discharge Valve Arrow Runtime" };
            mesh.vertices = new[]
            {
                new Vector3(tailX, bodyHalfHeight, 0f),
                new Vector3(neckX, bodyHalfHeight, 0f),
                new Vector3(neckX, -bodyHalfHeight, 0f),
                new Vector3(tailX, -bodyHalfHeight, 0f),
                new Vector3(neckX, headHalfHeight, 0f),
                new Vector3(tipX, 0f, 0f),
                new Vector3(neckX, -headHalfHeight, 0f),
            };
            mesh.triangles = new[]
            {
                0, 1, 2,
                0, 2, 3,
                4, 5, 6,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            MeshFilter filter = arrowObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = arrowObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Material material = P102RuntimeUtility.CreateCyanMaterial(
                "P102 Discharge Valve Arrow Material", DischargeArrowColor);
            if (material != null)
            {
                if (material.HasProperty("_Cull"))
                    material.SetFloat("_Cull", 0f);
                if (material.HasProperty("_CullMode"))
                    material.SetFloat("_CullMode", 0f);
                renderer.sharedMaterial = material;
            }

            visual.DischargeArrowMesh = mesh;
            visual.DischargeArrowMaterial = material;
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
                return Mathf.Clamp(bounds.extents.magnitude * 0.88f, 0.30f, 0.55f);
            return 0.38f;
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
                if (marker == null || !marker.enabled)
                    continue;
                marker.startColor = marker.endColor = pulseColor;
                marker.widthMultiplier = Mathf.Lerp(0.016f, 0.024f, pulse);
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
            visual.Root.transform.position = center - visual.PlaneNormal * 0.08f;
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
