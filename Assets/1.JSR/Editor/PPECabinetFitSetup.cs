using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fits the PPE wardrobe around the calibrated human-scale items without
/// changing PPE world dimensions. Unparents slots from the storage mesh so the
/// cabinet can use a non-uniform furniture scale without shearing grab roots.
/// </summary>
public static class PPECabinetFitSetup
{
    const string ScenePath = "Assets/Scenes/04_PPE.unity";
    const string StorageName = "PPE_Storage";
    const string RootName = "PPE_StorageRoot";
    const float BakedSlotScale = 2.15f;

    static readonly Vector3 FurnitureScale = new Vector3(1.733f, 2.104f, 1.508f);
    static readonly Vector3 NativeCabinetSize = new Vector3(0.9811f, 0.9742f, 0.3647f);

    public static void EnsureCabinetShelfPhysics()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var root = FindNamed(scene, RootName);
        if (root == null)
        {
            Debug.LogError("[PPE Cabinet Physics] PPE_StorageRoot not found.");
            return;
        }

        PPECabinetPhysics.EnsureOn(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PPE Cabinet Physics] Ensured shelf/wall/catch BoxColliders under PPE_StorageRoot/Physics.");
    }

    public static void FitCabinet()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var report = new StringBuilder();
        report.AppendLine("=== PPE Cabinet Fit Around Calibrated PPE ===");

        var storage = FindNamed(scene, StorageName);
        if (storage == null)
        {
            Debug.LogError("[PPE Cabinet Fit] PPE_Storage not found.");
            return;
        }

        var root = EnsureStorageRootPlaced(scene, storage, report);
        BakeSlotsOffMesh(root, storage, report);
        ParentStorageMesh(root, storage, report);
        ApplyFurnitureScale(storage, report);
        PlaceSlots(root, report);
        EnsureBackPanelCollider(root, report);
        PPECabinetPhysics.EnsureOn(root);
        report.AppendLine("Ensured PPE_StorageRoot/Physics shelf, wall, and catch BoxColliders.");
        HideStrayRespiratorSpeck(scene, report);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(report.ToString());
    }

    static GameObject EnsureStorageRootPlaced(Scene scene, GameObject storage, StringBuilder report)
    {
        var root = FindNamed(scene, RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create PPE_StorageRoot");
            report.AppendLine("Created PPE_StorageRoot.");
        }

        Undo.RecordObject(root.transform, "Place PPE_StorageRoot");
        root.transform.SetParent(null, true);
        root.transform.position = storage.transform.position;
        root.transform.rotation = storage.transform.rotation;
        root.transform.localScale = Vector3.one;
        report.AppendLine(
            $"PPE_StorageRoot at {root.transform.position}, euler {root.transform.eulerAngles}, scale 1.");
        return root;
    }

    static void ParentStorageMesh(GameObject root, GameObject storage, StringBuilder report)
    {
        if (storage.transform.parent != root.transform)
        {
            Undo.SetTransformParent(storage.transform, root.transform, "Parent PPE_Storage under root");
            report.AppendLine("Parented PPE_Storage mesh under PPE_StorageRoot.");
        }

        Undo.RecordObject(storage.transform, "Zero PPE_Storage local pose");
        storage.transform.localPosition = Vector3.zero;
        storage.transform.localRotation = Quaternion.identity;
    }

    static void BakeSlotsOffMesh(GameObject root, GameObject storage, StringBuilder report)
    {
        var slotNames = new[]
        {
            "InnerGlovesSlot", "GlovesSlot", "SuitSlot",
            "BootsSlot", "RespiratorSlot", "FaceShieldSlot"
        };

        foreach (var name in slotNames)
        {
            var slot = FindNamed(root.scene, name);
            if (slot == null)
            {
                report.AppendLine("WARNING: missing " + name);
                continue;
            }

            if (slot.transform.parent == storage.transform)
            {
                Undo.SetTransformParent(slot.transform, root.transform, "Bake " + name + " off cabinet mesh");
                report.AppendLine("Reparented " + name + " to PPE_StorageRoot (world transform preserved).");
            }

            Undo.RecordObject(slot.transform, "Bake slot scale " + name);
            if (!ApproximatelyUniform(slot.transform.localScale, BakedSlotScale))
            {
                slot.transform.localScale = Vector3.one * BakedSlotScale;
                report.AppendLine(name + " localScale baked to " + BakedSlotScale + " to preserve PPE world size.");
            }
        }
    }

    static void ApplyFurnitureScale(GameObject storage, StringBuilder report)
    {
        Undo.RecordObject(storage.transform, "PPE_Storage furniture scale");
        var old = storage.transform.localScale;
        storage.transform.localScale = FurnitureScale;
        var world = Vector3.Scale(NativeCabinetSize, FurnitureScale);
        report.AppendLine(
            $"PPE_Storage furniture scale {old} → {FurnitureScale}");
        report.AppendLine(
            $"Cabinet world size ≈ {world.x:0.000} × {world.y:0.000} × {world.z:0.000} m");
    }

    static void PlaceSlots(GameObject root, StringBuilder report)
    {
        // StorageRoot local meters. Yaw 180: local +X is player's left.
        Place(root, "InnerGlovesSlot", new Vector3(-0.50f, 0.95f, 0.14f), report);
        Place(root, "GlovesSlot", new Vector3(0.50f, 0.70f, 0.14f), report);
        Place(root, "BootsSlot", new Vector3(0.50f, 0.13f, 0.10f), report);
        Place(root, "RespiratorSlot", new Vector3(0.50f, 1.22f, 0.14f), report);
        Place(root, "FaceShieldSlot", new Vector3(0.50f, 1.52f, 0.14f), report);
        Place(root, "SuitSlot", new Vector3(0f, 0.17f, -0.04f), report);
    }

    static void Place(GameObject root, string name, Vector3 localPos, StringBuilder report)
    {
        var slot = FindNamed(root.scene, name);
        if (slot == null)
            return;

        Undo.RecordObject(slot.transform, "Place " + name);
        var old = slot.transform.localPosition;
        slot.transform.localPosition = localPos;
        report.AppendLine($"{name} localPos {old} → {localPos}");
    }

    static void EnsureBackPanelCollider(GameObject root, StringBuilder report)
    {
        var world = Vector3.Scale(NativeCabinetSize, FurnitureScale);
        var col = root.GetComponent<BoxCollider>();
        if (col == null)
            col = Undo.AddComponent<BoxCollider>(root);

        Undo.RecordObject(col, "Cabinet back panel collider");
        col.size = new Vector3(world.x, world.y, 0.04f);
        col.center = new Vector3(0f, world.y * 0.5f, -world.z * 0.5f + 0.02f);
        col.isTrigger = false;
        report.AppendLine(
            $"Back-panel collider size {col.size} center {col.center} (does not fill the interior).");
    }

    static void HideStrayRespiratorSpeck(Scene scene, StringBuilder report)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;
                if (filter.sharedMesh.vertexCount > 12)
                    continue;
                var go = filter.gameObject;
                var path = go.name;
                var t = go.transform.parent;
                while (t != null)
                {
                    path = t.name + "/" + path;
                    t = t.parent;
                }

                if (!path.Contains("Respirator") && !path.Contains("FaceRespirator"))
                    continue;

                Undo.RecordObject(go, "Hide respirator tripo_part_5 speck");
                go.SetActive(false);
                report.AppendLine(
                    "Disabled " + path +
                    " (" + filter.sharedMesh.vertexCount +
                    " verts). Stray Tripo island, not a filter cartridge.");
            }
        }
    }

    static bool ApproximatelyUniform(Vector3 scale, float value)
    {
        return Mathf.Abs(scale.x - value) < 0.002f &&
               Mathf.Abs(scale.y - value) < 0.002f &&
               Mathf.Abs(scale.z - value) < 0.002f;
    }

    static GameObject FindNamed(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                return root;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == name)
                    return transforms[i].gameObject;
            }
        }

        return null;
    }
}
