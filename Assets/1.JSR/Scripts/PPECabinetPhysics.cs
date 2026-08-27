using UnityEngine;

/// <summary>
/// Solid cabinet physics for PPE_StorageRoot. The wardrobe FBX is visual-only
/// (no generated colliders). FitCabinet only authored a thin back panel, so
/// released PPE fell through the visible shelves.
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

    [SerializeField] Vector3 cabinetSize = new Vector3(1.70f, 2.05f, 0.55f);
    [SerializeField, Range(0.03f, 0.08f)] float wallThickness = 0.04f;
    [SerializeField, Range(0.04f, 0.10f)] float shelfThickness = 0.06f;
    [SerializeField]
    [Tooltip("BoxCollider center Y in StorageRoot local space. Tops sit below authored PPE slot poses.")]
    float[] shelfLocalY = { 0.05f, 0.61f, 0.86f, 1.13f, 1.43f };
    [SerializeField] float catchLocalY = 0f;
    [SerializeField, Range(0.04f, 0.12f)] float catchThickness = 0.08f;
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
        var shelfH = Mathf.Clamp(shelfThickness, 0.04f, 0.12f);

        var innerWidth = Mathf.Max(0.1f, width - wall * 2f);
        var innerDepth = Mathf.Max(0.1f, depth - wall - 0.02f);
        var shelfZ = (-depth * 0.5f + wall + innerDepth * 0.5f);

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

        DisableDuplicateRootBackPanel();

        var count = shelfLocalY != null ? Mathf.Min(ShelfNames.Length, shelfLocalY.Length) : 0;
        for (var i = 0; i < count; i++)
        {
            EnsureBox(
                physics,
                ShelfNames[i],
                new Vector3(0f, shelfLocalY[i], shelfZ),
                new Vector3(innerWidth, shelfH, innerDepth));
        }

        EnsureBox(
            physics,
            "CatchCollider",
            new Vector3(0f, catchLocalY, 0f),
            new Vector3(width, Mathf.Clamp(catchThickness, 0.04f, 0.12f), depth));
    }

    void DisableDuplicateRootBackPanel()
    {
        var rootBox = GetComponent<BoxCollider>();
        if (rootBox == null)
            return;

        // FitCabinet put a 4 cm back panel on the root. Physics/BackWallCollider
        // replaces it so visual mesh and collision stay in separate children.
        rootBox.enabled = false;
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
