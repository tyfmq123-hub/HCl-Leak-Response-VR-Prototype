using UnityEngine;

/// <summary>
/// Solid cabinet physics for PPE_StorageRoot. The wardrobe FBX is visual-only
/// (no generated colliders), so the Stage owns both shelf support colliders and
/// one visual-bounds-fitted player blocker.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-300)]
public sealed class PPECabinetPhysics : MonoBehaviour
{
    public const string PhysicsRootName = "Physics";

    static readonly string[] ShelfNames =
    {
        "Shelf_01_Collider",
        "Shelf_02_Collider",
        "Shelf_03_Collider",
        "Shelf_04_Collider",
        "Shelf_05_Collider"
    };

    static readonly string[] RightShelfNames =
    {
        null,
        "Shelf_02_Right_Collider",
        "Shelf_03_Right_Collider",
        "Shelf_04_Right_Collider",
        "Shelf_05_Right_Collider"
    };

    const float ShelfColumnCenterX = 0.55f;
    const float ShelfColumnWidth = 0.58f;
    const float AuthoredSupportPadding = 0.08f;
    const float AuthoredSupportSurfaceGap = 0.004f;

    [SerializeField] Vector3 cabinetSize = new Vector3(1.70f, 2.05f, 0.55f);
    [SerializeField, Range(0.03f, 0.08f)] float wallThickness = 0.04f;
    [SerializeField, Range(0.04f, 0.10f)] float shelfThickness = 0.06f;
    [SerializeField]
    [Tooltip("BoxCollider center Y in StorageRoot local space. Tops sit below authored PPE slot poses.")]
    float[] shelfLocalY = { 0.05f, 0.61f, 0.86f, 1.13f, 1.43f };
    [SerializeField] float catchLocalY = 0f;
    [SerializeField, Range(0.04f, 0.12f)] float catchThickness = 0.08f;
    [SerializeField, Range(0.12f, 0.40f)] float releasedItemShelfCatchDistance = 0.28f;
    [SerializeField, Range(0.002f, 0.02f)] float releasedItemSurfaceGap = 0.006f;
    [SerializeField] bool logShelfPhysicsOnAwake = true;

    bool loggedShelfPhysics;

    public static PPECabinetPhysics EnsureOn(GameObject storageRoot)
    {
        if (storageRoot == null)
            return null;

        var physics = storageRoot.GetComponent<PPECabinetPhysics>();
        if (physics == null)
            physics = storageRoot.AddComponent<PPECabinetPhysics>();
        physics.EnsurePhysicsColliders();
        return physics;
    }

    void Awake()
    {
        EnsurePhysicsColliders();
        if (logShelfPhysicsOnAwake)
            LogShelfPhysics();
    }

    public void EnsurePhysicsColliders()
    {
        var physics = FindOrCreateChild(transform, PhysicsRootName);
        physics.localPosition = Vector3.zero;
        physics.localRotation = Quaternion.identity;
        physics.localScale = Vector3.one;

        var width = Mathf.Max(0.2f, cabinetSize.x);
        var height = Mathf.Max(0.2f, cabinetSize.y);
        var depth = Mathf.Max(0.2f, cabinetSize.z);
        var wall = Mathf.Clamp(wallThickness, 0.02f, 0.12f);

        var innerWidth = Mathf.Max(0.1f, width - wall * 2f);
        var shelfDepth = depth + 0.03f;
        var shelfH = Mathf.Max(0.10f, Mathf.Clamp(shelfThickness, 0.04f, 0.12f));

        EnsureBox(
            physics,
            "LeftWallCollider",
            new Vector3(-(width * 0.5f - wall * 0.5f), height * 0.5f, 0f),
            new Vector3(wall, height, depth));
        EnsureBox(
            physics,
            "RightWallCollider",
            new Vector3(width * 0.5f - wall * 0.5f, height * 0.5f, 0f),
            new Vector3(wall, height, depth));
        EnsureBox(
            physics,
            "BackWallCollider",
            new Vector3(0f, height * 0.5f, -depth * 0.5f + wall * 0.5f),
            new Vector3(width, height, wall));

        var count = shelfLocalY != null ? Mathf.Min(ShelfNames.Length, shelfLocalY.Length) : 0;
        for (var i = 0; i < count; i++)
        {
            if (i == 0)
            {
                EnsureBox(
                    physics,
                    ShelfNames[i],
                    new Vector3(0f, shelfLocalY[i], 0f),
                    new Vector3(innerWidth, shelfH, shelfDepth));
                continue;
            }

            EnsureBox(
                physics,
                ShelfNames[i],
                new Vector3(ShelfColumnCenterX, shelfLocalY[i], 0f),
                new Vector3(ShelfColumnWidth, shelfH, shelfDepth));
            EnsureBox(
                physics,
                RightShelfNames[i],
                new Vector3(-ShelfColumnCenterX, shelfLocalY[i], 0f),
                new Vector3(ShelfColumnWidth, shelfH, shelfDepth));
        }

        EnsureBox(
            physics,
            "CatchCollider",
            new Vector3(0f, catchLocalY, 0f),
            new Vector3(width, Mathf.Clamp(catchThickness, 0.04f, 0.12f), depth));

        EnsureAuthoredItemSupports(physics, shelfH);

        ConfigurePlayerBlocker(width, height, depth);
    }

    void EnsureAuthoredItemSupports(Transform physics, float supportHeight)
    {
        var storedItems = GetComponentsInChildren<PPEStoredDisplayState>(true);
        for (var i = 0; i < storedItems.Length; i++)
        {
            var storedItem = storedItems[i];
            if (storedItem == null || !TryGetItemWorldBounds(storedItem.transform, out var worldBounds))
                continue;

            var localBounds = WorldBoundsToLocal(worldBounds);
            var supportTop = localBounds.min.y - AuthoredSupportSurfaceGap;
            var supportCenter = new Vector3(
                localBounds.center.x,
                supportTop - supportHeight * 0.5f,
                localBounds.center.z);
            var supportSize = new Vector3(
                Mathf.Max(0.18f, localBounds.size.x + AuthoredSupportPadding),
                supportHeight,
                Mathf.Max(0.18f, localBounds.size.z + AuthoredSupportPadding));

            EnsureBox(
                physics,
                "Shelf_Item_" + i.ToString("D2") + "_" + storedItem.name + "_Collider",
                supportCenter,
                supportSize);
        }
    }

    /// <summary>
    /// XR grabs move cabinet PPE kinematically, so a hand can place an item a few
    /// centimetres inside a thin shelf before release. Put that item back on the
    /// nearest authored shelf surface and explicitly restore every shelf/item
    /// collision pair before dynamic gravity resumes.
    /// </summary>
    public bool TrySupportReleasedItem(Transform itemRoot, Rigidbody itemBody)
    {
        if (itemRoot == null || !TryGetItemWorldBounds(itemRoot, out var worldBounds))
            return false;

        var physicsRoot = transform.Find(PhysicsRootName);
        if (physicsRoot == null)
            return false;

        var nearestTop = 0f;
        var nearestDistance = float.PositiveInfinity;
        var shelfColliders = physicsRoot.GetComponentsInChildren<Collider>(true);
        const float horizontalPadding = 0.04f;
        for (var i = 0; i < shelfColliders.Length; i++)
        {
            var shelf = shelfColliders[i];
            if (shelf == null || !shelf.enabled || shelf.isTrigger ||
                shelf.name.IndexOf("Shelf_", System.StringComparison.Ordinal) < 0)
            {
                continue;
            }

            var shelfBounds = shelf.bounds;
            var overlapsX = worldBounds.max.x >= shelfBounds.min.x - horizontalPadding &&
                            worldBounds.min.x <= shelfBounds.max.x + horizontalPadding;
            var overlapsZ = worldBounds.max.z >= shelfBounds.min.z - horizontalPadding &&
                            worldBounds.min.z <= shelfBounds.max.z + horizontalPadding;
            if (!overlapsX || !overlapsZ)
                continue;

            var shelfTop = shelfBounds.max.y;
            var distance = Mathf.Abs(worldBounds.min.y - shelfTop);
            if (distance >= nearestDistance)
                continue;

            nearestTop = shelfTop;
            nearestDistance = distance;
        }

        if (nearestDistance > Mathf.Max(0.12f, releasedItemShelfCatchDistance))
            return false;

        RestoreShelfCollisionPairs(itemRoot);

        var requiredLift = nearestTop + releasedItemSurfaceGap - worldBounds.min.y;
        if (requiredLift > 0f)
        {
            var worldOffset = Vector3.up * requiredLift;
            if (itemBody != null)
                itemBody.position += worldOffset;
            else
                itemRoot.position += worldOffset;
            Physics.SyncTransforms();
        }

        if (itemBody != null && !itemBody.isKinematic && itemBody.linearVelocity.y < 0f)
        {
            var velocity = itemBody.linearVelocity;
            velocity.y = 0f;
            itemBody.linearVelocity = velocity;
        }

        return true;
    }

    void RestoreShelfCollisionPairs(Transform itemRoot)
    {
        var physicsRoot = transform.Find(PhysicsRootName);
        if (physicsRoot == null)
            return;

        var itemColliders = itemRoot.GetComponentsInChildren<Collider>(true);
        var shelfColliders = physicsRoot.GetComponentsInChildren<Collider>(true);
        for (var shelfIndex = 0; shelfIndex < shelfColliders.Length; shelfIndex++)
        {
            var shelfCollider = shelfColliders[shelfIndex];
            if (shelfCollider == null || !shelfCollider.enabled || shelfCollider.isTrigger)
                continue;
            if (shelfCollider.name.IndexOf("Shelf_", System.StringComparison.Ordinal) < 0)
                continue;

            for (var itemIndex = 0; itemIndex < itemColliders.Length; itemIndex++)
            {
                var itemCollider = itemColliders[itemIndex];
                if (itemCollider == null || !itemCollider.enabled || itemCollider.isTrigger)
                    continue;
                Physics.IgnoreCollision(shelfCollider, itemCollider, false);
            }
        }
    }

    static bool TryGetItemWorldBounds(Transform itemRoot, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        var colliders = itemRoot.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (found)
            return true;

        var renderers = itemRoot.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    Bounds WorldBoundsToLocal(Bounds worldBounds)
    {
        var localBounds = new Bounds();
        var found = false;
        var min = worldBounds.min;
        var max = worldBounds.max;
        for (var cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            var worldCorner = new Vector3(
                (cornerIndex & 1) == 0 ? min.x : max.x,
                (cornerIndex & 2) == 0 ? min.y : max.y,
                (cornerIndex & 4) == 0 ? min.z : max.z);
            var localCorner = transform.InverseTransformPoint(worldCorner);
            if (!found)
            {
                localBounds = new Bounds(localCorner, Vector3.zero);
                found = true;
            }
            else
            {
                localBounds.Encapsulate(localCorner);
            }
        }

        return localBounds;
    }

    void ConfigurePlayerBlocker(float fallbackWidth, float fallbackHeight, float fallbackDepth)
    {
        var rootBox = GetComponent<BoxCollider>();
        if (rootBox == null)
            rootBox = gameObject.AddComponent<BoxCollider>();

        if (TryGetStorageVisualLocalBounds(out var visualBounds))
        {
            rootBox.center = visualBounds.center;
            rootBox.size = visualBounds.size;
        }
        else
        {
            rootBox.center = new Vector3(0f, fallbackHeight * 0.5f, 0f);
            rootBox.size = new Vector3(fallbackWidth, fallbackHeight, fallbackDepth);
        }

        rootBox.isTrigger = false;
        rootBox.enabled = true;

        // The solid root box blocks only the trainee. Held/released PPE uses the
        // Ignore Raycast layer, so exclude that layer from this outer blocker.
        // Shelf colliders remain on Default and still collide with every PPE item.
        var ppePhysicsLayer = LayerMask.NameToLayer("Ignore Raycast");
        rootBox.excludeLayers = ppePhysicsLayer >= 0
            ? 1 << ppePhysicsLayer
            : 0;
    }

    bool TryGetStorageVisualLocalBounds(out Bounds localBounds)
    {
        localBounds = default;
        Transform visualRoot = null;
        for (var i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child != null && child.name == "PPE_Storage")
            {
                visualRoot = child;
                break;
            }
        }

        if (visualRoot == null)
            return false;

        var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        var found = false;
        for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            var renderer = renderers[rendererIndex];
            if (renderer == null)
                continue;

            var bounds = renderer.bounds;
            var min = bounds.min;
            var max = bounds.max;
            for (var cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                var worldCorner = new Vector3(
                    (cornerIndex & 1) == 0 ? min.x : max.x,
                    (cornerIndex & 2) == 0 ? min.y : max.y,
                    (cornerIndex & 4) == 0 ? min.z : max.z);
                var localCorner = transform.InverseTransformPoint(worldCorner);
                if (!found)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    found = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        return found && localBounds.size.sqrMagnitude > 0.0001f;
    }

    public void LogShelfPhysics()
    {
        if (loggedShelfPhysics)
            return;
        loggedShelfPhysics = true;

        var physics = transform.Find(PhysicsRootName);
        if (physics == null)
            return;

        var colliders = physics.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var col = colliders[i];
            if (col == null)
                continue;

            Debug.Log(
                "[Cabinet Shelf Physics]\n" +
                "Shelf=" + col.name + "\n" +
                "ColliderType=" + col.GetType().Name + "\n" +
                "Enabled=" + col.enabled + "\n" +
                "IsTrigger=" + col.isTrigger + "\n" +
                "Layer=" + LayerMask.LayerToName(col.gameObject.layer) + "\n" +
                "Bounds=" + col.bounds,
                col);
        }
    }

    static Transform FindOrCreateChild(Transform parent, string childName)
    {
        var existing = parent.Find(childName);
        if (existing != null)
            return existing;

        var go = new GameObject(childName);
        go.layer = parent.gameObject.layer;
        var child = go.transform;
        child.SetParent(parent, false);
        return child;
    }

    static void EnsureBox(Transform parent, string childName, Vector3 localPosition, Vector3 size)
    {
        var child = FindOrCreateChild(parent, childName);
        child.localPosition = localPosition;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        child.gameObject.layer = parent.gameObject.layer;

        var box = child.GetComponent<BoxCollider>();
        if (box == null)
            box = child.gameObject.AddComponent<BoxCollider>();

        box.center = Vector3.zero;
        box.size = size;
        box.isTrigger = false;
        box.enabled = true;
    }
}
