using UnityEngine;

namespace VRProject.LeakResponse.Narration
{
    /// <summary>Physical setup needed when the scene is loaded, not when its training starts.</summary>
    public static class P102WasteContainerPhysics
    {
        public const string CollisionRootName = "P102_WasteBodyCollision_Runtime";
        public const int WallCount = 16;

        public static void PrepareBeforeSimulation(GameObject container)
        {
            if (container == null) return;
            foreach (var child in container.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != "HazWasteContainer_Lid") continue;
                var body = child.GetComponent<Rigidbody>();
                if (body == null) continue;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                // A dormant additive Leak scene may spend minutes behind PreEntry.
                // Its authored closed pose must survive BEFORE Waste.Configure caches it.
                body.useGravity = false;
                body.isKinematic = true;
            }
            EnsureBodyCollision(container);
        }

        public static Transform EnsureBodyCollision(GameObject container)
        {
            if (container == null) return null;
            var existing = container.transform.Find(CollisionRootName);
            if (existing != null) return existing;
            Transform body = null;
            foreach (var child in container.GetComponentsInChildren<Transform>(true))
                if (child.name == "HazWasteContainer_Body") { body = child; break; }
            if (body == null) return null;
            // Preserve any explicit solid collision authored by a scene owner.
            foreach (var collider in body.GetComponentsInChildren<Collider>(true))
                if (collider.enabled && !collider.isTrigger) return null;
            var mesh = body.GetComponent<MeshFilter>();
            if (mesh == null || mesh.sharedMesh == null) return null;

            var bounds = BoundsInContainer(mesh, container.transform);
            if (bounds.size.x <= 0.001f || bounds.size.y <= 0.001f || bounds.size.z <= 0.001f)
                return null;
            var root = new GameObject(CollisionRootName).transform;
            root.SetParent(container.transform, false);
            root.gameObject.layer = body.gameObject.layer;
            float thickness = Mathf.Min(0.02f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.1f);
            float radiusX = bounds.extents.x - thickness * 0.5f;
            float radiusZ = bounds.extents.z - thickness * 0.5f;
            // Thin polygonal walls + bottom. No roof and no full-height box filling the intake.
            for (int i = 0; i < WallCount; i++)
            {
                float a = i * Mathf.PI * 2f / WallCount;
                float b = (i + 1) * Mathf.PI * 2f / WallCount;
                var p = new Vector3(Mathf.Cos(a) * radiusX, 0, Mathf.Sin(a) * radiusZ);
                var q = new Vector3(Mathf.Cos(b) * radiusX, 0, Mathf.Sin(b) * radiusZ);
                var wall = AddBox(root, "Wall_" + i.ToString("00"),
                    bounds.center + (p + q) * 0.5f,
                    new Vector3(thickness, bounds.size.y, (q - p).magnitude + thickness));
                wall.transform.localRotation = Quaternion.LookRotation(q - p, Vector3.up);
            }
            AddBox(root, "Bottom", new Vector3(bounds.center.x, bounds.min.y + thickness * 0.5f, bounds.center.z),
                new Vector3(bounds.size.x - thickness * 2f, thickness, bounds.size.z - thickness * 2f));
            return root;
        }

        static Bounds BoundsInContainer(MeshFilter mesh, Transform container)
        {
            var local = mesh.sharedMesh.bounds;
            var matrix = container.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
            var bounds = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z)));
            return bounds;
        }

        static BoxCollider AddBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var child = new GameObject(name);
            child.layer = parent.gameObject.layer;
            child.transform.SetParent(parent, false);
            child.transform.localPosition = center;
            var collider = child.AddComponent<BoxCollider>();
            collider.size = size;
            collider.isTrigger = false;
            return collider;
        }
    }
}
