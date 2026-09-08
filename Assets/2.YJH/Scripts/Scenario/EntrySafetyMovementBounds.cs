using UnityEngine;

namespace JH.EntrySafety
{
    /// <summary>
    /// Adds PreEntry-local solid colliders so the existing CharacterController /
    /// shared locomotion cannot leave the authored playable area. Does not add an
    /// XR Origin, camera, or movement provider.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntrySafetyMovementBounds : MonoBehaviour
    {
        [SerializeField] private EntrySafetyNoAccessOverlayController _noAccessOverlay;
        [SerializeField] private Vector3 _areaCenter = new Vector3(2.4f, 1.5f, -13.2f);
        [SerializeField] private Vector3 _areaSize = new Vector3(16f, 3.2f, 9.2f);
        [SerializeField, Min(0.1f)] private float _wallThickness = 0.4f;

        private Transform _wallsRoot;

        private void Awake()
        {
            if (_noAccessOverlay == null)
                _noAccessOverlay = FindAnyObjectByType<EntrySafetyNoAccessOverlayController>();

            EnsurePerimeterWalls();
            EnsureNoAccessIsSolid();
        }

        private void EnsureNoAccessIsSolid()
        {
            var boundary = _noAccessOverlay != null ? _noAccessOverlay.PhysicalBoundaryCollider : null;
            if (boundary == null)
                return;

            boundary.isTrigger = false;
            if (!boundary.enabled)
                boundary.enabled = true;
        }

        private void EnsurePerimeterWalls()
        {
            if (_wallsRoot != null)
                return;

            var existing = transform.Find("PreEntryPlayBounds");
            _wallsRoot = existing != null ? existing : new GameObject("PreEntryPlayBounds").transform;
            _wallsRoot.SetParent(transform, false);
            _wallsRoot.position = _areaCenter;

            var size = new Vector3(
                Mathf.Max(4f, _areaSize.x),
                Mathf.Max(2f, _areaSize.y),
                Mathf.Max(4f, _areaSize.z));
            var thickness = Mathf.Max(0.1f, _wallThickness);
            var half = size * 0.5f;

            CreateWall("SouthWall", new Vector3(0f, 0f, -half.z), new Vector3(size.x + thickness, size.y, thickness));
            CreateWall("WestWall", new Vector3(-half.x, 0f, 0f), new Vector3(thickness, size.y, size.z));
            CreateWall("EastWall", new Vector3(half.x, 0f, 0f), new Vector3(thickness, size.y, size.z));
            // Keep the north edge closed even after the no-access overlay fades.
            // Full-mode A still teleports into LeakResponse; this wall is the return boundary.
            CreateWall("NorthWall", new Vector3(0f, 0f, half.z), new Vector3(size.x + thickness, size.y, thickness));
        }

        private void CreateWall(string wallName, Vector3 localPosition, Vector3 size)
        {
            var existing = _wallsRoot.Find(wallName);
            var wall = existing != null ? existing.gameObject : new GameObject(wallName);
            wall.transform.SetParent(_wallsRoot, false);
            wall.transform.localPosition = localPosition;
            wall.transform.localRotation = Quaternion.identity;
            wall.layer = 0;

            var box = wall.GetComponent<BoxCollider>();
            if (box == null)
                box = wall.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.center = Vector3.zero;
            box.size = size;
        }
    }
}
