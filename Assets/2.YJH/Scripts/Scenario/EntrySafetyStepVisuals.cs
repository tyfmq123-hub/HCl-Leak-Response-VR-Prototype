using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>Shows only the physical marker needed for the active initial training step.</summary>
    public sealed class EntrySafetyStepVisuals : MonoBehaviour
    {
        private const string WindsockObjectName = "Windsock_UnityReady";

        [SerializeField] private GameObject _windsockMarker;
        [SerializeField] private GameObject _equipmentZoneMarker;

        private Transform _windsock;
        private Camera _viewerCamera;
        private LineRenderer _arrowStem;
        private LineRenderer _arrowHead;
        private Material _arrowMaterial;

        private void Awake()
        {
            ResolveWindsock();
            BuildWindsockArrow();
        }

        private void LateUpdate()
        {
            if (_windsockMarker == null || !_windsockMarker.activeInHierarchy)
                return;

            if (_windsock == null)
                ResolveWindsock();
            if (_windsock == null || !TryGetRendererBounds(_windsock, out var bounds))
                return;

            _viewerCamera = EntrySafetyAppXrResolve.PreferLiveMainCamera(_viewerCamera);
            var right = _viewerCamera != null
                ? Vector3.ProjectOnPlane(_viewerCamera.transform.right, Vector3.up).normalized
                : Vector3.right;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;

            var tip = bounds.center + Vector3.up * (bounds.extents.y + 0.22f);
            var tail = tip + Vector3.up * 0.82f;
            _windsockMarker.transform.position = tip;

            if (_arrowStem != null)
            {
                _arrowStem.SetPosition(0, tail);
                _arrowStem.SetPosition(1, tip);
            }
            if (_arrowHead != null)
            {
                _arrowHead.SetPosition(0, tip + Vector3.up * 0.28f - right * 0.24f);
                _arrowHead.SetPosition(1, tip);
                _arrowHead.SetPosition(2, tip + Vector3.up * 0.28f + right * 0.24f);
            }
        }

        public void SetStep(EntrySafetyStep step)
        {
            SetActive(_windsockMarker, step == EntrySafetyStep.CheckInitialWind || step == EntrySafetyStep.CheckChangedWind);
            SetActive(_equipmentZoneMarker, false);
        }

        private static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value)
                target.SetActive(value);
        }

        private void ResolveWindsock()
        {
            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == WindsockObjectName)
                {
                    _windsock = transforms[i];
                    return;
                }
            }
        }

        private void BuildWindsockArrow()
        {
            if (_windsockMarker == null)
                return;

            // Replace the old fixed beacon with a simple non-pulsing downward arrow.
            // The marker follows the authored windsock at runtime, so moving the windsock
            // in the Scene does not require changing another world-space coordinate.
            var oldRenderers = _windsockMarker.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < oldRenderers.Length; i++)
                oldRenderers[i].enabled = false;
            var oldLights = _windsockMarker.GetComponentsInChildren<Light>(true);
            for (var i = 0; i < oldLights.Length; i++)
                oldLights[i].enabled = false;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null)
                return;
            _arrowMaterial = new Material(shader) { color = new Color32(255, 196, 37, 255) };
            _arrowStem = CreateArrowLine("WindsockArrowStem", 2);
            _arrowHead = CreateArrowLine("WindsockArrowHead", 3);
        }

        private LineRenderer CreateArrowLine(string objectName, int pointCount)
        {
            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(_windsockMarker.transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = pointCount;
            line.widthMultiplier = 0.07f;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = _arrowMaterial;
            line.startColor = _arrowMaterial.color;
            line.endColor = _arrowMaterial.color;
            return line;
        }

        private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
                return false;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var found = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
            return found;
        }

        private void OnDestroy()
        {
            if (_arrowMaterial != null)
                Destroy(_arrowMaterial);
        }
    }
}
