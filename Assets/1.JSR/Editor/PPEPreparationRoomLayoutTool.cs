using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places existing PPE models into the real PPE locker cabinet mesh
/// (ENV_PPECabinet_03 / tripo_convert_75ff6923…), not the empty organizer.
/// </summary>
public static class PPEPreparationRoomLayoutTool
{
    const string StorageName = "PPE_Storage";
    const string CabinetVisualName = "PPE_Cabinet";
    const string LayoutRootName = "PPE_LayoutRoot";
    const float FloorY = 0f;
    const string AutoRunTriggerPath = "Assets/1.JSR/Editor/RunPPELayoutArrange.trigger.txt";
    const string CabinetAssetGuid = "3b50e42ff173f744db220f2f4702b3d9";
    const string OrganizerFbxGuid = "f8158ea3aafb9264a8e59d4506ba8384"; // Assets/1.JSR/Models/PPE.fbm/PPE.fbx
    const string OrganizerFbxPath = "Assets/1.JSR/Models/PPE.fbm/PPE.fbx";

    static readonly HashSet<string> SlotNames = new HashSet<string>
    {
        "SuitSlot", "BootsSlot", "RespiratorSlot", "FaceShieldSlot", "GlovesSlot", "InnerGlovesSlot"
    };

    [Serializable]
    public class SlotLayout
    {
        public string objectName;
        public string slotName;
        public float yawCorrectionDegrees;
        public bool snapBottomToFloor;
        public float normalizedX;
        public float normalizedY;
        public float normalizedZ;
        public float fitHeightFraction; // of cabinet height
        public float fitWidthFraction;  // of cabinet width
    }

    // Reference locker: LEFT shelves (shield / respirator / gloves / boots),
    // CENTER hanging suit, RIGHT closed storage (empty).
    static SlotLayout[] DefaultLayouts => new[]
    {
        new SlotLayout
        {
            objectName = "PPE_ChemicalFaceShield",
            slotName = "FaceShieldSlot",
            yawCorrectionDegrees = 180f,
            snapBottomToFloor = false,
            normalizedX = 0.83f,
            normalizedY = 0.88f,
            normalizedZ = 0.40f,
            fitHeightFraction = 0.18f,
            fitWidthFraction = 0.22f,
        },
        new SlotLayout
        {
            objectName = "PPE_FaceRespirator.fbm",
            slotName = "RespiratorSlot",
            yawCorrectionDegrees = 180f,
            snapBottomToFloor = false,
            normalizedX = 0.83f,
            normalizedY = 0.66f,
            normalizedZ = 0.40f,
            fitHeightFraction = 0.18f,
            fitWidthFraction = 0.22f,
        },
        new SlotLayout
        {
            objectName = "PPE_Gloves",
            slotName = "GlovesSlot",
            yawCorrectionDegrees = 180f,
            snapBottomToFloor = false,
            normalizedX = 0.83f,
            normalizedY = 0.40f,
            normalizedZ = 0.40f,
            fitHeightFraction = 0.20f,
            fitWidthFraction = 0.22f,
        },
        new SlotLayout
        {
            objectName = "PPE_ChemicalSafetyBoots_01",
            slotName = "BootsSlot",
            yawCorrectionDegrees = 180f,
            snapBottomToFloor = true,
            normalizedX = 0.83f,
            normalizedY = 0.06f,
            normalizedZ = 0.40f,
            fitHeightFraction = 0.22f,
            fitWidthFraction = 0.24f,
        },
        new SlotLayout
        {
            objectName = "PPE_ChemicalSuit_01",
            slotName = "SuitSlot",
            yawCorrectionDegrees = 180f,
            snapBottomToFloor = true,
            normalizedX = 0.50f,
            normalizedY = 0.08f,
            normalizedZ = 0.42f,
            fitHeightFraction = 0.90f,
            fitWidthFraction = 0.32f,
        },
    };

    [InitializeOnLoadMethod]
    static void AutoRunIfTriggered()
    {
        if (!File.Exists(AutoRunTriggerPath))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(AutoRunTriggerPath))
                return;

            var mode = "run";
            try { mode = File.ReadAllText(AutoRunTriggerPath).Trim(); }
            catch { /* ignored */ }

            try
            {
                File.Delete(AutoRunTriggerPath);
                if (File.Exists(AutoRunTriggerPath + ".meta"))
                    File.Delete(AutoRunTriggerPath + ".meta");
            }
            catch { /* ignored */ }

            RepairBrokenRenames();
            DumpSceneCabinetCandidates();
            if (!mode.StartsWith("dump", StringComparison.OrdinalIgnoreCase))
                Arrange(null);
        };
    }

    public static void ArrangePreparationRoomEquipment()
    {
        RepairBrokenRenames();
        DumpSceneCabinetCandidates();
        Arrange(null);
    }

    public static void ArrangeIntoSelectedCabinet()
    {
        var sel = Selection.activeGameObject;
        if (sel == null)
        {
            Debug.LogError("Select the locker cabinet GameObject in the Hierarchy, then run this command.");
            return;
        }

        RepairBrokenRenames();
        Arrange(ResolveCabinetFromSelection(sel));
    }

    public static void DumpSceneCabinetCandidatesMenu() => DumpSceneCabinetCandidates();

    static void DumpSceneCabinetCandidates()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PPE cabinet candidates ===");
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var b = GetObjectWorldBounds(root, excludeEquipment: true);
            var bAll = GetObjectWorldBounds(root, excludeEquipment: false);
            var meshHits = CountCabinetAssetMeshes(root.transform);
            sb.AppendLine(
                $"ROOT {root.name} meshHits={meshHits} " +
                $"boundsExEq={(b.HasValue ? b.Value.size.ToString("F2") : "none")} " +
                $"boundsAll={(bAll.HasValue ? bAll.Value.size.ToString("F2") : "none")} " +
                $"pos={root.transform.position}");
        }

        sb.AppendLine("--- MeshFilters (world size y>=0.8 or x>=1.2) ---");
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;
                var worldSize = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale);
                var sx = Mathf.Abs(worldSize.x);
                var sy = Mathf.Abs(worldSize.y);
                var sz = Mathf.Abs(worldSize.z);
                if (sy < 0.8f && sx < 1.2f)
                    continue;
                var path = AssetDatabase.GetAssetPath(mf.sharedMesh);
                var prefab = PrefabUtility.GetCorrespondingObjectFromSource(mf.gameObject);
                var prefabPath = prefab != null ? AssetDatabase.GetAssetPath(prefab) : "";
                sb.AppendLine(
                    $"MF {GetPath(mf.transform)} size=({sx:F2},{sy:F2},{sz:F2}) " +
                    $"meshPath='{path}' prefab='{prefabPath}'");
            }
        }

        sb.AppendLine("--- Prefab instances (PPE/cabinet/tripo) ---");
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                    continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                var path = src != null ? AssetDatabase.GetAssetPath(src) : "";
                if (string.IsNullOrEmpty(path))
                    continue;
                if (path.IndexOf("/PPE/", StringComparison.OrdinalIgnoreCase) < 0
                    && path.IndexOf("Cabinet", StringComparison.OrdinalIgnoreCase) < 0
                    && path.IndexOf("tripo", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var b = GetObjectWorldBounds(t.gameObject, excludeEquipment: false);
                sb.AppendLine(
                    $"PREFAB {GetPath(t)} path='{path}' size={(b.HasValue ? b.Value.size.ToString("F2") : "none")}");
            }
        }

        foreach (var go in FindAllByName(StorageName))
            sb.AppendLine($"NAMED PPE_Storage path={GetPath(go.transform)} meshHits={CountCabinetAssetMeshes(go.transform)}");
        foreach (var go in FindAllByName(LayoutRootName))
            sb.AppendLine($"NAMED PPE_LayoutRoot path={GetPath(go.transform)} meshHits={CountCabinetAssetMeshes(go.transform)}");

        Debug.Log(sb.ToString());
    }

    static int CountCabinetAssetMeshes(Transform root)
    {
        var n = 0;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (IsCabinetMeshAsset(mf.sharedMesh) || IsCabinetPrefabObject(mf.gameObject))
                n++;
        }

        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (IsCabinetMeshAsset(smr.sharedMesh) || IsCabinetPrefabObject(smr.gameObject))
                n++;
        }

        if (n == 0 && IsCabinetPrefabObject(root.gameObject))
            n = 1;

        return n;
    }

    static bool IsCabinetMeshAsset(UnityEngine.Object meshOrAsset)
    {
        if (meshOrAsset == null)
            return false;

        // Prefab / model root path via mesh asset.
        var path = AssetDatabase.GetAssetPath(meshOrAsset);
        if (!string.IsNullOrEmpty(path))
        {
            if (path.IndexOf("ENV_PPECabinet", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (path.IndexOf("75ff6923", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (guid == CabinetAssetGuid)
                return true;
        }

        // Some imported submeshes report empty mesh paths — climb via PrefabUtility on the mesh name.
        var meshName = meshOrAsset.name ?? "";
        if (meshName.IndexOf("75ff6923", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    static bool IsCabinetPrefabObject(GameObject go)
    {
        if (go == null)
            return false;
        var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (src == null)
            return false;
        var path = AssetDatabase.GetAssetPath(src);
        if (string.IsNullOrEmpty(path))
            return false;
        if (path.IndexOf("ENV_PPECabinet", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (path.IndexOf("75ff6923", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return AssetDatabase.AssetPathToGUID(path) == CabinetAssetGuid;
    }

    static bool IsEnvironmentProp(Transform t)
    {
        while (t != null)
        {
            var n = t.name;
            if (n.StartsWith("Wall_", StringComparison.Ordinal)) return true;
            if (n is "Floor" or "Ceiling" or "Directional Light") return true;
            t = t.parent;
        }

        return false;
    }

    static void RepairBrokenRenames()
    {
        foreach (var storage in FindAllByName(StorageName).ToArray())
        {
            var mf = storage.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var size = Vector3.Scale(mf.sharedMesh.bounds.size, storage.transform.lossyScale);
                if (Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.z)) < 0.3f && Mathf.Abs(size.y) > 2f)
                {
                    Undo.RecordObject(storage, "Restore wall name");
                    var p = storage.transform.position;
                    string wallName = "Wall_Back";
                    if (Mathf.Abs(p.x) > Mathf.Abs(p.z) && p.x < 0) wallName = "Wall_Left";
                    else if (Mathf.Abs(p.x) > Mathf.Abs(p.z) && p.x > 0) wallName = "Wall_Right";
                    else if (p.z < 0) wallName = "Wall_Front_Left";
                    storage.name = wallName;
                    Debug.LogWarning($"Restored mis-named wall back to {wallName}");
                }
            }
        }

        foreach (var storage in FindAllByName(StorageName).ToArray())
        {
            if (!IsUnderEquipment(storage.transform))
                continue;
            Undo.RecordObject(storage, "Repair PPE_Storage name");
            storage.name = "tripo_part_recovered";
            Debug.LogWarning($"Repaired mis-named PPE part: {GetPath(storage.transform)}");
        }

        // Pull slots off walls if they stuck there.
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!root.name.StartsWith("Wall_", StringComparison.Ordinal))
                continue;
            foreach (var slotName in SlotNames.ToArray())
            {
                var slot = root.transform.Find(slotName);
                if (slot == null)
                    continue;
                EnsureLayoutRoot();
                var layoutRoot = FindSceneObject(LayoutRootName);
                Undo.SetTransformParent(slot, layoutRoot.transform, "Move slot off wall");
                Debug.LogWarning($"Moved {slotName} off {root.name} onto {LayoutRootName}");
            }
        }
    }

    static bool IsUnderEquipment(Transform t)
    {
        while (t != null)
        {
            if (SlotNames.Contains(t.name))
                return true;
            if (t.name.StartsWith("PPE_Chemical", StringComparison.Ordinal)
                || t.name.StartsWith("PPE_Face", StringComparison.Ordinal)
                || t.name == "PPE_Gloves")
                return true;
            t = t.parent;
        }

        return false;
    }

    static GameObject ResolveCabinetFromSelection(GameObject sel)
    {
        if (sel == null)
            return null;

        // If selection (or ancestor) owns cabinet meshes, use that owner.
        var t = sel.transform;
        while (t != null)
        {
            if (CountCabinetAssetMeshes(t) > 0 && !IsUnderEquipment(t))
                return FindCabinetOwner(t);
            t = t.parent;
        }

        // Selection might be empty organizer sitting next to the real locker — search by asset.
        var byAsset = FindCabinetByAsset();
        if (byAsset != null)
            return byAsset;

        // Fall back to selection if it has any sizable mesh (non-equipment).
        var b = GetObjectWorldBounds(sel, excludeEquipment: true);
        if (b.HasValue && b.Value.size.y >= 1.5f && b.Value.size.x >= 1.2f && !IsEnvironmentProp(sel.transform))
            return sel;

        return sel;
    }

    static void Arrange(GameObject forcedCabinet)
    {
        var notes = new List<string>();

        // Remove the wrongly auto-created locker mesh object(s).
        RemoveAutoCreatedCabinetVisuals(notes);

        var organizer = EnsureOrganizerStorage(notes);
        if (organizer == null)
        {
            Debug.LogError("PPE layout: PPE_Storage (PPE.fbx) missing.");
            return;
        }

        // Scale PPE_Storage to room-sized locker, park against back wall.
        FitStorageIntoRoom(organizer, notes);

        var visual = organizer;
        var localBounds = GetLocalMeshBounds(visual);
        if (!localBounds.HasValue || localBounds.Value.size.sqrMagnitude < 0.01f)
        {
            var wb = GetObjectWorldBounds(visual, excludeEquipment: true)
                     ?? GetObjectWorldBounds(visual, excludeEquipment: false);
            if (wb.HasValue)
                localBounds = WorldToLocalBounds(visual.transform, wb.Value);
        }

        if (!localBounds.HasValue || localBounds.Value.size.y < 0.3f)
        {
            localBounds = new Bounds(new Vector3(0f, 1.1f, 0.35f), new Vector3(2.8f, 2.2f, 0.85f));
            notes.Add("Using fallback compartment box on PPE_Storage.");
        }

        var roomCenter = EstimateRoomCenter(organizer.transform);
        var placed = new List<string>();
        var missing = new List<string>();

        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Arrange Preparation Room Equipment");

        foreach (var layout in DefaultLayouts)
        {
            var target = FindEquipment(layout.objectName);
            if (target == null)
            {
                missing.Add(layout.objectName);
                continue;
            }

            if (target.transform == organizer.transform)
                continue;

            if (layout.objectName == "PPE_Gloves" && IsOrganizerFbxInstance(organizer))
            {
                notes.Add("Skipped PPE_Gloves — PPE.fbx is already PPE_Storage.");
                continue;
            }

            var slot = EnsureSlot(organizer.transform, layout.slotName);
            PlaceLocalIntoVisual(
                target, slot, layout, organizer.transform, localBounds.Value, roomCenter, notes);
            DeduplicateSlotChildren(slot, notes);
            placed.Add($"{layout.objectName} → {StorageName}/{layout.slotName} local={slot.localPosition}");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = organizer;

        var finalWorld = GetObjectWorldBounds(organizer, excludeEquipment: true)
                         ?? GetObjectWorldBounds(organizer, excludeEquipment: false);
        var sb = new StringBuilder();
        sb.AppendLine("PPE layout complete.");
        sb.AppendLine($"PPE_Storage: {GetPath(organizer.transform)} scale={organizer.transform.localScale} pos={organizer.transform.position}");
        sb.AppendLine($"World size={(finalWorld.HasValue ? finalWorld.Value.size.ToString("F2") : "none")}");
        sb.AppendLine($"Placement bounds center={localBounds.Value.center} size={localBounds.Value.size}");
        foreach (var p in placed) sb.AppendLine("  • " + p);
        foreach (var m in missing) sb.AppendLine("  ✗ missing " + m);
        foreach (var n in notes) sb.AppendLine("  • " + n);
        DumpSlots(organizer.transform, sb);
        Debug.Log(sb.ToString(), organizer);
    }

    /// <summary>
    /// Scale PPE_Storage to a human-scale locker and place it against Wall_Back facing the room.
    /// </summary>
    static void FitStorageIntoRoom(GameObject storage, List<string> notes)
    {
        const float targetHeight = 2.35f;
        const float targetWidth = 3.10f;
        const float targetDepth = 0.95f;

        // Measure mesh only (ignore already-placed equipment children) when possible.
        var world = GetObjectWorldBounds(storage, excludeEquipment: true);
        if (!world.HasValue || world.Value.size.y < 0.05f)
            world = GetObjectWorldBounds(storage, excludeEquipment: false);
        if (!world.HasValue || world.Value.size.y < 0.05f)
            return;

        // Reset rotation upright, face room center (back wall is +Z).
        Undo.RecordObject(storage.transform, "Fit PPE_Storage to room");
        storage.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        storage.transform.localScale = Vector3.one;

        world = GetObjectWorldBounds(storage, excludeEquipment: true)
                ?? GetObjectWorldBounds(storage, excludeEquipment: false);
        if (!world.HasValue)
            return;

        var raw = AbsVec(world.Value.size);
        var sx = raw.x > 0.01f ? targetWidth / raw.x : 1f;
        var sy = raw.y > 0.01f ? targetHeight / raw.y : 1f;
        var sz = raw.z > 0.01f ? targetDepth / raw.z : 1f;
        // Prefer uniform-ish scale so the locker doesn't shear; bias toward height match then average.
        var uniform = (sx + sy + sz) / 3f;
        // Keep closer to height match for standing lockers.
        var scale = Mathf.Lerp(uniform, sy, 0.55f);
        storage.transform.localScale = Vector3.one * scale;

        world = GetObjectWorldBounds(storage, excludeEquipment: true)
                ?? GetObjectWorldBounds(storage, excludeEquipment: false);
        if (!world.HasValue)
            return;

        // If still too narrow/wide vs target, apply mild non-uniform correction.
        var s = AbsVec(world.Value.size);
        var corr = new Vector3(
            s.x > 0.01f ? targetWidth / s.x : 1f,
            s.y > 0.01f ? targetHeight / s.y : 1f,
            s.z > 0.01f ? targetDepth / s.z : 1f);
        storage.transform.localScale = Vector3.Scale(storage.transform.localScale, corr);

        world = GetObjectWorldBounds(storage, excludeEquipment: true)
                ?? GetObjectWorldBounds(storage, excludeEquipment: false);
        if (!world.HasValue)
            return;

        // Flush the back of the locker to the room-facing face of Wall_Back.
        var wallBack = GameObject.Find("Wall_Back");
        float targetBackZ = 2.85f; // fallback near back wall
        if (wallBack != null)
        {
            var wallBounds = GetObjectWorldBounds(wallBack, excludeEquipment: false);
            if (wallBounds.HasValue)
            {
                // Interior face of back wall (room is toward -Z from Wall_Back).
                targetBackZ = wallBounds.Value.min.z - 0.01f;
            }
            else
            {
                targetBackZ = wallBack.transform.position.z
                              - Mathf.Abs(wallBack.transform.lossyScale.z) * 0.5f
                              - 0.01f;
            }
        }

        // With yaw 180, locker opens toward -Z; its back is world max.z.
        storage.transform.position += new Vector3(
            0f - world.Value.center.x,
            FloorY - world.Value.min.y,
            targetBackZ - world.Value.max.z);

        var final = GetObjectWorldBounds(storage, excludeEquipment: true)
                    ?? GetObjectWorldBounds(storage, excludeEquipment: false);
        notes.Add(
            $"Fit PPE_Storage scale={storage.transform.localScale} " +
            $"worldSize={(final.HasValue ? final.Value.size.ToString("F2") : "?")} " +
            $"pos={storage.transform.position} backZ→{targetBackZ:F2}");
    }

    /// <summary>
    /// Deletes PPE_Cabinet / ENV_PPECabinet instances that this tool previously auto-created.
    /// Rescue any slots onto PPE_Storage first.
    /// </summary>
    static void RemoveAutoCreatedCabinetVisuals(List<string> notes)
    {
        var toDelete = new List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var isCabinetName = root.name == CabinetVisualName
                                || root.name.StartsWith(CabinetVisualName, StringComparison.Ordinal);
            var isEnvCabinet = CountCabinetAssetMeshes(root.transform) > 0;
            if (!isCabinetName && !isEnvCabinet)
                continue;
            // Never delete PPE_Storage even if somehow tagged.
            if (root.name == StorageName || IsOrganizerFbxInstance(root))
                continue;
            toDelete.Add(root);
        }

        if (toDelete.Count == 0)
            return;

        var organizer = FindAllByName(StorageName).FirstOrDefault(g => CountCabinetAssetMeshes(g.transform) == 0)
                        ?? FindAllByName(StorageName).FirstOrDefault();

        foreach (var go in toDelete)
        {
            if (organizer != null)
            {
                foreach (var slotName in SlotNames.ToArray())
                {
                    var slot = go.transform.Find(slotName);
                    if (slot == null)
                        continue;
                    var existing = organizer.transform.Find(slotName);
                    if (existing == null)
                        Undo.SetTransformParent(slot, organizer.transform, $"Rescue {slotName}");
                    else
                    {
                        while (slot.childCount > 0)
                            Undo.SetTransformParent(slot.GetChild(0), existing, "Rescue equipment");
                        Undo.DestroyObjectImmediate(slot.gameObject);
                    }
                }
            }

            notes.Add($"Deleted auto-created '{go.name}'");
            Undo.DestroyObjectImmediate(go);
        }
    }

    static Bounds WorldToLocalBounds(Transform root, Bounds world)
    {
        Bounds local = new Bounds(root.InverseTransformPoint(world.center), Vector3.zero);
        foreach (var c in GetCorners(world))
            local.Encapsulate(root.InverseTransformPoint(c));
        return local;
    }

    /// <summary>
    /// Ensures ENV locker mesh is NOT named PPE_Storage. PPE_Storage stays an empty organizer.
    /// </summary>
    static GameObject SeparateOrganizerFromCabinetVisual(GameObject preferred, List<string> notes)
    {
        // If PPE_Storage currently IS the locker mesh, rename mesh away and recreate empty organizer.
        foreach (var named in FindAllByNamePrefix(StorageName).ToArray())
        {
            if (CountCabinetAssetMeshes(named.transform) == 0)
                continue;

            var oldName = named.name;
            Undo.RecordObject(named, "Separate locker mesh from PPE_Storage");
            named.name = CabinetVisualName;
            notes.Add($"Locker mesh was wrongly named '{oldName}' → '{CabinetVisualName}'");
        }

        // Delete duplicate locker meshes; keep one PPE_Cabinet.
        var visuals = new List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (CountCabinetAssetMeshes(root.transform) > 0 && !IsUnderEquipment(root.transform))
                visuals.Add(root);
        }

        GameObject keeper = null;
        if (preferred != null && CountCabinetAssetMeshes(preferred.transform) > 0)
            keeper = preferred;
        else
        {
            foreach (var v in visuals)
            {
                if (v.name == CabinetVisualName || v.name.StartsWith("tripo_convert", StringComparison.OrdinalIgnoreCase))
                {
                    keeper = v;
                    break;
                }
            }

            if (keeper == null && visuals.Count > 0)
                keeper = visuals[0];
        }

        foreach (var dup in visuals)
        {
            if (dup == keeper)
                continue;
            // Steal slots off duplicates onto organizer later; destroy extra meshes.
            foreach (var slotName in SlotNames.ToArray())
            {
                var slot = dup.transform.Find(slotName);
                if (slot == null)
                    continue;
                var org = EnsureOrganizerStorage(notes);
                var existing = org.transform.Find(slotName);
                if (existing == null)
                    Undo.SetTransformParent(slot, org.transform, $"Rescue {slotName}");
                else
                {
                    while (slot.childCount > 0)
                        Undo.SetTransformParent(slot.GetChild(0), existing, "Rescue equipment");
                    Undo.DestroyObjectImmediate(slot.gameObject);
                }
            }

            notes.Add($"Removed duplicate locker mesh '{dup.name}'");
            Undo.DestroyObjectImmediate(dup);
        }

        if (keeper != null && keeper.name != CabinetVisualName)
        {
            Undo.RecordObject(keeper, "Name PPE_Cabinet");
            keeper.name = CabinetVisualName;
        }

        return keeper ?? FindCabinetByAsset();
    }

    static GameObject EnsureOrganizerStorage(List<string> notes)
    {
        // Already a PPE.fbx instance named PPE_Storage?
        foreach (var go in FindAllByName(StorageName))
        {
            if (CountCabinetAssetMeshes(go.transform) > 0)
                continue;
            if (IsOrganizerFbxInstance(go))
                return go;
        }

        // Reuse an existing PPE.fbx in the scene (often named PPE / PPE_Gloves).
        GameObject fbxInstance = null;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (CountCabinetAssetMeshes(root.transform) > 0)
                continue;
            if (!IsOrganizerFbxInstance(root))
                continue;
            fbxInstance = root;
            break;
        }

        if (fbxInstance == null)
        {
            var path = AssetDatabase.GUIDToAssetPath(OrganizerFbxGuid);
            if (string.IsNullOrEmpty(path))
                path = OrganizerFbxPath;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogError($"PPE.fbx not found at {path}");
                // Fallback empty organizer so layout can still run.
                var empty = new GameObject(StorageName);
                Undo.RegisterCreatedObjectUndo(empty, "Create PPE_Storage");
                return empty;
            }

            fbxInstance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (fbxInstance == null)
                fbxInstance = UnityEngine.Object.Instantiate(asset);
            Undo.RegisterCreatedObjectUndo(fbxInstance, "Instantiate PPE.fbx as PPE_Storage");
            fbxInstance.transform.position = Vector3.zero;
            notes.Add($"Instantiated organizer from {path}");
        }

        // Move slots/children off any empty PPE_Storage onto the FBX instance, then remove empties.
        foreach (var old in FindAllByName(StorageName).ToArray())
        {
            if (old == fbxInstance)
                continue;
            if (CountCabinetAssetMeshes(old.transform) > 0)
                continue;
            if (IsOrganizerFbxInstance(old))
                continue;

            TransferSlotsAndEquipment(old.transform, fbxInstance.transform, notes);
            notes.Add($"Replaced empty '{old.name}' with PPE.fbx organizer");
            Undo.DestroyObjectImmediate(old);
        }

        // Also absorb PPE_LayoutRoot slots if present.
        var layout = FindSceneObject(LayoutRootName);
        if (layout != null && layout != fbxInstance && CountCabinetAssetMeshes(layout.transform) == 0
            && !IsOrganizerFbxInstance(layout))
        {
            TransferSlotsAndEquipment(layout.transform, fbxInstance.transform, notes);
            Undo.DestroyObjectImmediate(layout);
            notes.Add($"Merged '{LayoutRootName}' into PPE.fbx organizer");
        }

        if (fbxInstance.name != StorageName)
        {
            Undo.RecordObject(fbxInstance, "Name PPE_Storage");
            notes.Add($"Renamed '{fbxInstance.name}' → '{StorageName}' (PPE.fbx)");
            fbxInstance.name = StorageName;
        }

        return fbxInstance;
    }

    static bool IsOrganizerFbxInstance(GameObject go)
    {
        if (go == null)
            return false;
        var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
        var path = src != null ? AssetDatabase.GetAssetPath(src) : "";
        if (string.IsNullOrEmpty(path))
            return false;
        if (path.Replace('\\', '/').EndsWith("PPE.fbm/PPE.fbx", StringComparison.OrdinalIgnoreCase))
            return true;
        return AssetDatabase.AssetPathToGUID(path) == OrganizerFbxGuid;
    }

    static void TransferSlotsAndEquipment(Transform from, Transform to, List<string> notes)
    {
        foreach (var slotName in SlotNames.ToArray())
        {
            var slot = from.Find(slotName);
            if (slot == null)
                continue;
            var existing = to.Find(slotName);
            if (existing == null)
            {
                Undo.SetTransformParent(slot, to, $"Move {slotName}");
                notes.Add($"Moved {slotName} → PPE.fbx organizer");
            }
            else
            {
                while (slot.childCount > 0)
                    Undo.SetTransformParent(slot.GetChild(0), existing, "Move equipment");
                Undo.DestroyObjectImmediate(slot.gameObject);
            }
        }

        // Move any leftover equipment roots that were direct children.
        for (var i = from.childCount - 1; i >= 0; i--)
        {
            var child = from.GetChild(i);
            if (SlotNames.Contains(child.name))
                continue;
            if (child.name.StartsWith("PPE_Chemical", StringComparison.Ordinal)
                || child.name.StartsWith("PPE_Face", StringComparison.Ordinal)
                || child.name == "PPE_Gloves")
            {
                Undo.SetTransformParent(child, to, $"Move {child.name}");
            }
        }
    }

    static void RestoreMisnamedOrganizers(List<string> notes)
    {
        // Do NOT rename PPE_Storage when it is the PPE.fbx organizer.
        // Only rename stray PPE.fbx instances that are still called PPE / PPE_Gloves outside storage.
    }

    static bool IsStorageNameFamily(string name) =>
        name == StorageName || name.StartsWith(StorageName, StringComparison.Ordinal);

    /// <summary>
    /// Keep a single ENV_PPECabinet as PPE_Storage. Merge slots from PPE_Storage1 duplicates, delete extras.
    /// Gloves wrongly named PPE_Storage are renamed to PPE_Gloves.
    /// </summary>
    static GameObject ConsolidateCabinets(GameObject preferred, List<string> notes)
    {
        var cabinets = new List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.parent != null && t.parent.gameObject != root && CountCabinetAssetMeshes(t) > 0)
                {
                    // Only collect cabinet owners (roots of cabinet prefabs), not every mesh child.
                }
            }

            if (CountCabinetAssetMeshes(root.transform) > 0 && !IsUnderEquipment(root.transform))
                cabinets.Add(root);
        }

        // Also catch nested/renamed roots named PPE_Storage*.
        foreach (var go in FindAllByNamePrefix(StorageName))
        {
            if (CountCabinetAssetMeshes(go.transform) > 0 && !cabinets.Contains(go))
                cabinets.Add(go);
        }

        // Rename non-cabinet PPE_Storage* (usually gloves) away first.
        foreach (var go in FindAllByNamePrefix(StorageName))
        {
            if (CountCabinetAssetMeshes(go.transform) > 0)
                continue;
            var oldName = go.name;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            var path = src != null ? AssetDatabase.GetAssetPath(src) : "";
            Undo.RecordObject(go, "Rename non-cabinet Storage");
            if (path.IndexOf("PPE.fbm", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("PPE.fbx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                go.name = "PPE_Gloves";
                notes.Add($"Renamed gloves '{oldName}' → PPE_Gloves");
            }
            else
            {
                go.name = LayoutRootName;
                notes.Add($"Renamed non-cabinet '{oldName}' → {LayoutRootName}");
            }
        }

        if (cabinets.Count == 0)
            return FindCabinetByAsset();

        // Prefer: preferred arg, then object that already has slots, then largest mesh hit count.
        GameObject keeper = null;
        if (preferred != null && cabinets.Contains(preferred))
            keeper = preferred;
        else
        {
            int bestScore = -1;
            foreach (var c in cabinets)
            {
                var score = CountCabinetAssetMeshes(c.transform) * 10;
                foreach (var slot in SlotNames)
                    if (c.transform.Find(slot) != null) score += 5;
                if (c.name == StorageName) score += 3;
                if (score > bestScore)
                {
                    bestScore = score;
                    keeper = c;
                }
            }
        }

        if (keeper == null)
            keeper = cabinets[0];

        foreach (var dup in cabinets)
        {
            if (dup == keeper)
                continue;

            // Move slot children onto keeper, then delete duplicate locker.
            foreach (var slotName in SlotNames.ToArray())
            {
                var slot = dup.transform.Find(slotName);
                if (slot == null)
                    continue;
                var existing = keeper.transform.Find(slotName);
                if (existing == null)
                {
                    Undo.SetTransformParent(slot, keeper.transform, $"Move {slotName} to keeper");
                    notes.Add($"Moved {slotName} from {dup.name} → {keeper.name}");
                }
                else
                {
                    // Move equipment children then destroy duplicate slot.
                    while (slot.childCount > 0)
                    {
                        var child = slot.GetChild(0);
                        Undo.SetTransformParent(child, existing, $"Move {child.name} to keeper slot");
                    }

                    Undo.DestroyObjectImmediate(slot.gameObject);
                }
            }

            notes.Add($"Removed duplicate cabinet '{dup.name}'");
            Undo.DestroyObjectImmediate(dup);
        }

        ClaimStorageName(keeper, notes);
        return keeper;
    }

    static void ClaimStorageName(GameObject cabinet, List<string> notes)
    {
        foreach (var other in FindAllByNamePrefix(StorageName))
        {
            if (other == cabinet)
                continue;
            if (CountCabinetAssetMeshes(other.transform) > 0)
                continue;

            Undo.RecordObject(other, "Free PPE_Storage name");
            var src = PrefabUtility.GetCorrespondingObjectFromSource(other);
            var path = src != null ? AssetDatabase.GetAssetPath(src) : "";
            other.name = (path.IndexOf("PPE.fbm", StringComparison.OrdinalIgnoreCase) >= 0
                          || path.IndexOf("PPE.fbx", StringComparison.OrdinalIgnoreCase) >= 0)
                ? "PPE_Gloves"
                : LayoutRootName;
            notes.Add($"Freed name: {other.name}");
        }

        if (cabinet.name != CabinetVisualName)
        {
            Undo.RecordObject(cabinet, "Name cabinet visual PPE_Cabinet");
            notes.Add($"Renamed '{cabinet.name}' → '{CabinetVisualName}'");
            cabinet.name = CabinetVisualName;
        }
    }

    static List<GameObject> FindAllByNamePrefix(string prefix)
    {
        var list = new List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == prefix || t.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    // Avoid matching PPE_StorageFoo that isn't Storage family — require exact or Storage+digit
                    if (t.name == prefix || (t.name.Length > prefix.Length && char.IsDigit(t.name[prefix.Length])))
                        list.Add(t.gameObject);
                }
            }
        }

        return list;
    }

    static GameObject EnsureCabinetInScene(List<string> notes)
    {
        var existing = FindCabinetByAsset();
        if (existing != null)
        {
            NormalizeCabinetScale(existing, notes);
            return existing;
        }

        var path = AssetDatabase.GUIDToAssetPath(CabinetAssetGuid);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError($"Cabinet asset GUID not found: {CabinetAssetGuid}");
            return null;
        }

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            Debug.LogError($"Failed to load cabinet asset at {path}");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        if (instance == null)
            instance = UnityEngine.Object.Instantiate(asset);

        Undo.RegisterCreatedObjectUndo(instance, "Instantiate PPE Cabinet");
        instance.name = CabinetVisualName;
        instance.transform.SetPositionAndRotation(new Vector3(0f, 0f, 2.0f), Quaternion.Euler(0f, 180f, 0f));
        instance.transform.localScale = Vector3.one;

        NormalizeCabinetScale(instance, notes);
        notes.Add($"Instantiated locker mesh as '{CabinetVisualName}' from {path}");
        return instance;
    }

    static void NormalizeCabinetScale(GameObject cabinet, List<string> notes)
    {
        Bounds? MeasureWorld()
        {
            // Prefer cabinet meshes only so equipment children don't inflate bounds.
            Bounds? localMesh = null;
            foreach (var mf in cabinet.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;
                if (IsUnderSlotOrEquipment(mf.transform, cabinet.transform))
                    continue;
                if (!IsCabinetMeshAsset(mf.sharedMesh) && !IsCabinetPrefabObject(mf.gameObject))
                {
                    // Still allow if this is the only mesh under a cabinet prefab root.
                    if (CountCabinetAssetMeshes(cabinet.transform) > 0)
                        continue;
                }

                foreach (var c in GetCorners(mf.sharedMesh.bounds))
                {
                    var world = mf.transform.TransformPoint(c);
                    var inRoot = cabinet.transform.InverseTransformPoint(world);
                    if (!localMesh.HasValue) localMesh = new Bounds(inRoot, Vector3.zero);
                    else
                    {
                        var x = localMesh.Value;
                        x.Encapsulate(inRoot);
                        localMesh = x;
                    }
                }
            }

            if (localMesh.HasValue)
                return EncapsulateCorners(cabinet, localMesh.Value);

            return GetObjectWorldBounds(cabinet, excludeEquipment: true)
                   ?? GetObjectWorldBounds(cabinet, excludeEquipment: false);
        }

        // Target a standing 3-bay locker matching the reference photo proportions.
        const float targetWidth = 3.0f;
        const float targetHeight = 2.2f;
        const float targetDepth = 0.90f;

        var candidates = new[]
        {
            Quaternion.Euler(0f, 180f, 0f),
            Quaternion.Euler(0f, 0f, 0f),
            Quaternion.Euler(-90f, 180f, 0f),
            Quaternion.Euler(-90f, 0f, 0f),
            Quaternion.Euler(0f, 90f, 0f),
            Quaternion.Euler(0f, -90f, 0f),
        };

        float bestScore = float.NegativeInfinity;
        Quaternion bestRot = Quaternion.Euler(0f, 180f, 0f);
        Vector3 bestScale = Vector3.one;
        Bounds bestBounds = default;
        var found = false;
        var originalPos = cabinet.transform.position;

        foreach (var rot in candidates)
        {
            cabinet.transform.SetPositionAndRotation(originalPos, rot);
            cabinet.transform.localScale = Vector3.one;
            var raw = MeasureWorld();
            if (!raw.HasValue)
                continue;

            var r = AbsVec(raw.Value.size);
            if (r.x < 0.01f || r.y < 0.01f || r.z < 0.01f)
                continue;

            // Non-uniform scale in local axes ≈ world axes for yaw-only rotations.
            // For pitched rotations this is approximate but still usable.
            var scale = new Vector3(targetWidth / r.x, targetHeight / r.y, targetDepth / r.z);
            cabinet.transform.localScale = scale;
            var wb = MeasureWorld();
            if (!wb.HasValue)
                continue;

            var s = wb.Value.size;
            var score = 0f;
            score += 10f - Mathf.Abs(s.x - targetWidth);
            score += 10f - Mathf.Abs(s.y - targetHeight);
            score += 10f - Mathf.Abs(s.z - targetDepth);
            // Strongly prefer shallow depth (standing locker, not a long slab).
            if (s.z > 1.6f) score -= 20f;
            if (s.x < 2.2f) score -= 8f;
            if (s.y < 1.8f) score -= 8f;

            if (score > bestScore)
            {
                bestScore = score;
                bestRot = rot;
                bestScale = scale;
                bestBounds = wb.Value;
                found = true;
            }
        }

        if (!found)
            return;

        Undo.RecordObject(cabinet.transform, "Normalize PPE cabinet");
        cabinet.transform.rotation = bestRot;
        cabinet.transform.localScale = bestScale;
        notes.Add($"Cabinet orient={bestRot.eulerAngles} scale={bestScale} score={bestScore:F1} size={bestBounds.size}");

        var sized = MeasureWorld();
        if (!sized.HasValue)
            return;

        // Back wall at z=3; park locker on floor, open face toward room center (0,0,0).
        cabinet.transform.position += new Vector3(
            0f - sized.Value.center.x,
            FloorY - sized.Value.min.y,
            2.15f - sized.Value.center.z);
        notes.Add($"Cabinet placed center→(0,*,2.15) pos={cabinet.transform.position}");
    }

    static Bounds EncapsulateCorners(GameObject root, Bounds localBounds)
    {
        Bounds? b = null;
        foreach (var c in GetCorners(localBounds))
        {
            var w = root.transform.TransformPoint(c);
            if (!b.HasValue) b = new Bounds(w, Vector3.zero);
            else
            {
                var x = b.Value;
                x.Encapsulate(w);
                b = x;
            }
        }

        return b ?? new Bounds(root.transform.position, Vector3.one);
    }

    static Vector3 AbsVec(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static Vector3 Invert(Vector3 s) => new Vector3(
        Mathf.Abs(s.x) < 1e-5f ? 1f : 1f / s.x,
        Mathf.Abs(s.y) < 1e-5f ? 1f : 1f / s.y,
        Mathf.Abs(s.z) < 1e-5f ? 1f : 1f / s.z);

    static GameObject ResolveCabinet(List<string> notes)
    {
        var byAsset = FindCabinetByAsset();
        if (byAsset != null)
        {
            notes.Add($"Using cabinet asset match: {GetPath(byAsset.transform)} meshHits={CountCabinetAssetMeshes(byAsset.transform)}");
            return byAsset;
        }

        var sel = Selection.activeGameObject;
        if (sel != null)
        {
            var fromSel = ResolveCabinetFromSelection(sel);
            if (fromSel != null && CountCabinetAssetMeshes(fromSel.transform) > 0)
            {
                notes.Add($"Using selection-resolved cabinet: {GetPath(fromSel.transform)}");
                return fromSel;
            }
        }

        // Prefer named PPE_Storage that actually has cabinet meshes.
        foreach (var go in FindAllByName(StorageName))
        {
            if (IsUnderEquipment(go.transform) || IsEnvironmentProp(go.transform))
                continue;
            if (CountCabinetAssetMeshes(go.transform) > 0)
            {
                notes.Add($"Using named PPE_Storage with cabinet meshes: {GetPath(go.transform)}");
                return go;
            }
        }

        // Large non-environment root with mesh (last resort).
        GameObject best = null;
        float bestVol = -1f;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (IsEnvironmentProp(root.transform) || IsUnderXrOrigin(root.transform))
                continue;
            if (root.name.StartsWith("PPE_Chemical", StringComparison.Ordinal)
                || root.name.StartsWith("PPE_Face", StringComparison.Ordinal))
                continue;

            var b = GetObjectWorldBounds(root, excludeEquipment: true);
            if (!b.HasValue)
                continue;
            var s = b.Value.size;
            if (s.y < 1.8f || s.x < 1.5f || s.z < 0.4f)
                continue;
            if (Mathf.Max(s.x, s.y, s.z) > 8f)
                continue;
            var vol = s.x * s.y * s.z;
            notes.Add($"Size candidate {root.name} size={s}");
            if (vol > bestVol)
            {
                bestVol = vol;
                best = root;
            }
        }

        if (best != null)
            notes.Add($"Chose size candidate {best.name}");
        return best;
    }

    static GameObject FindCabinetByAsset()
    {
        GameObject best = null;
        var bestHits = 0;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!IsCabinetMeshAsset(mf.sharedMesh))
                    continue;
                if (IsUnderEquipment(mf.transform))
                    continue;
                var owner = FindCabinetOwner(mf.transform);
                var hits = CountCabinetAssetMeshes(owner.transform);
                if (hits > bestHits)
                {
                    bestHits = hits;
                    best = owner;
                }
            }

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!IsCabinetMeshAsset(smr.sharedMesh))
                    continue;
                if (IsUnderEquipment(smr.transform))
                    continue;
                var owner = FindCabinetOwner(smr.transform);
                var hits = CountCabinetAssetMeshes(owner.transform);
                if (hits > bestHits)
                {
                    bestHits = hits;
                    best = owner;
                }
            }
        }

        return best;
    }

    static GameObject FindCabinetOwner(Transform meshTransform)
    {
        Transform t = meshTransform;
        Transform chosen = meshTransform;
        while (t != null)
        {
            var n = t.name;
            if (n == StorageName || n == LayoutRootName
                || n.IndexOf("Cabinet", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("tripo_convert", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ENV_PPE", StringComparison.OrdinalIgnoreCase) >= 0)
                chosen = t;
            if (t.parent == null)
                return chosen != null ? chosen.gameObject : t.gameObject;
            t = t.parent;
        }

        return meshTransform.gameObject;
    }

    static bool IsUnderXrOrigin(Transform t)
    {
        while (t != null)
        {
            if (t.name.Contains("XR Origin") || t.name.Contains("Controller"))
                return true;
            t = t.parent;
        }

        return false;
    }

    static void EnsureLayoutRoot()
    {
        if (FindSceneObject(LayoutRootName) != null)
            return;
        var go = new GameObject(LayoutRootName);
        Undo.RegisterCreatedObjectUndo(go, "Create PPE_LayoutRoot");
    }

    static void MoveNonCabinetChildrenOff(Transform cabinet, List<string> notes)
    {
        // Gloves belong in the left shelf bay (reference). Keep them available for placement.
    }

    static GameObject FindEquipment(string objectName)
    {
        if (objectName == "PPE_Gloves")
        {
            // Prefer an explicit gloves object that is NOT the PPE_Storage organizer.
            foreach (var candidateName in new[] { "PPE_Gloves", "PPE" })
            {
                var go = FindSceneObject(candidateName);
                if (go == null)
                    continue;
                if (go.name == StorageName || IsOrganizerFbxInstance(go) && go.name == StorageName)
                    continue;
                if (CountCabinetAssetMeshes(go.transform) > 0)
                    continue;
                // If this IS the organizer fbx but wrongly still named PPE_Gloves, skip — EnsureOrganizer will claim it.
                if (IsOrganizerFbxInstance(go))
                    continue;
                return go;
            }

            return null;
        }

        return FindSceneObject(objectName);
    }

    static void DeduplicateSlotChildren(Transform slot, List<string> notes)
    {
        var seen = new HashSet<string>();
        for (var i = slot.childCount - 1; i >= 0; i--)
        {
            var child = slot.GetChild(i);
            if (seen.Add(child.name))
                continue;
            notes.Add($"Removed duplicate '{child.name}' under {slot.name}");
            Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    /// <summary>
    /// Places equipment under organizer slots, but positions them using the visual locker mesh bounds.
    /// </summary>
    static void PlaceLocalIntoVisual(
        GameObject target,
        Transform slot,
        SlotLayout layout,
        Transform visual,
        Bounds visualLocalBounds,
        Vector3 roomCenter,
        List<string> notes)
    {
        if (target.transform.parent != null)
            Undo.SetTransformParent(target.transform, null, $"Unparent {target.name}");

        Undo.SetTransformParent(target.transform, slot, $"Parent {target.name}");
        Undo.RecordObject(slot, $"Move {slot.name}");
        Undo.RecordObject(target.transform, $"Place {target.name}");

        // Point inside the visual locker (visual-local), then express as organizer-local for the slot.
        var visualLocalPoint = new Vector3(
            Mathf.Lerp(visualLocalBounds.min.x, visualLocalBounds.max.x, layout.normalizedX),
            Mathf.Lerp(visualLocalBounds.min.y, visualLocalBounds.max.y, layout.normalizedY),
            Mathf.Lerp(visualLocalBounds.min.z, visualLocalBounds.max.z, layout.normalizedZ));
        var worldPoint = visual.TransformPoint(visualLocalPoint);
        var organizer = slot.parent;
        slot.position = worldPoint;
        slot.rotation = Quaternion.identity;
        slot.localScale = Vector3.one;
        if (organizer != null)
        {
            // Keep slot scale stable under organizer.
            slot.localScale = Vector3.one;
        }

        target.transform.localPosition = Vector3.zero;
        target.transform.localRotation = Quaternion.identity;

        var parentLossy = AbsVec(slot.lossyScale);
        target.transform.localScale = new Vector3(
            1f / Mathf.Max(parentLossy.x, 1e-4f),
            1f / Mathf.Max(parentLossy.y, 1e-4f),
            1f / Mathf.Max(parentLossy.z, 1e-4f));

        var toCenter = roomCenter - target.transform.position;
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude > 0.0001f)
        {
            target.transform.rotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up)
                                        * Quaternion.Euler(0f, layout.yawCorrectionDegrees, 0f);
        }

        var cabinetWorld = GetObjectWorldBounds(visual.gameObject, excludeEquipment: true);
        var itemBounds = GetObjectWorldBounds(target);
        var fitH = layout.fitHeightFraction > 0.01f ? layout.fitHeightFraction : (layout.snapBottomToFloor ? 0.85f : 0.22f);
        var fitW = layout.fitWidthFraction > 0.01f ? layout.fitWidthFraction : 0.24f;
        if (itemBounds.HasValue && cabinetWorld.HasValue)
        {
            var maxH = cabinetWorld.Value.size.y * fitH;
            var maxW = cabinetWorld.Value.size.x * fitW;
            var h = Mathf.Max(itemBounds.Value.size.y, 0.001f);
            var w = Mathf.Max(Mathf.Max(itemBounds.Value.size.x, itemBounds.Value.size.z), 0.001f);
            var fit = Mathf.Min(maxH / h, maxW / w);
            if (Mathf.Abs(fit - 1f) > 0.02f)
            {
                target.transform.localScale *= fit;
                notes.Add($"{target.name} scaled x{fit:F2} to fit compartment");
            }
        }

        if (layout.snapBottomToFloor)
        {
            var b = GetObjectWorldBounds(target);
            if (b.HasValue)
            {
                var cabinetFloorY = cabinetWorld?.min.y ?? FloorY;
                var dy = cabinetFloorY - b.Value.min.y + 0.02f;
                if (Mathf.Abs(dy) > 0.0001f)
                    target.transform.position += new Vector3(0f, dy, 0f);
            }
        }
        else
        {
            var b = GetObjectWorldBounds(target);
            if (b.HasValue)
            {
                var dy = slot.position.y - b.Value.center.y;
                target.transform.position += new Vector3(0f, dy, 0f);
            }
        }

        notes.Add($"{target.name} world={target.transform.position} under {StorageName}/{slot.name}");
    }

    static void PlaceLocal(
        GameObject target,
        Transform slot,
        SlotLayout layout,
        Bounds localBounds,
        Vector3 roomCenter,
        List<string> notes)
    {
        if (target.transform.parent != null)
            Undo.SetTransformParent(target.transform, null, $"Unparent {target.name}");

        Undo.SetTransformParent(target.transform, slot, $"Parent {target.name}");
        Undo.RecordObject(slot, $"Move {slot.name}");
        Undo.RecordObject(target.transform, $"Place {target.name}");

        slot.localPosition = new Vector3(
            Mathf.Lerp(localBounds.min.x, localBounds.max.x, layout.normalizedX),
            Mathf.Lerp(localBounds.min.y, localBounds.max.y, layout.normalizedY),
            Mathf.Lerp(localBounds.min.z, localBounds.max.z, layout.normalizedZ));
        slot.localRotation = Quaternion.identity;
        slot.localScale = Vector3.one;

        target.transform.localPosition = Vector3.zero;
        target.transform.localRotation = Quaternion.identity;

        var parentLossy = AbsVec(slot.lossyScale);
        target.transform.localScale = new Vector3(
            1f / Mathf.Max(parentLossy.x, 1e-4f),
            1f / Mathf.Max(parentLossy.y, 1e-4f),
            1f / Mathf.Max(parentLossy.z, 1e-4f));

        // Face out of the locker toward room center.
        var toCenter = roomCenter - target.transform.position;
        toCenter.y = 0f;
        if (toCenter.sqrMagnitude > 0.0001f)
        {
            target.transform.rotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up)
                                        * Quaternion.Euler(0f, layout.yawCorrectionDegrees, 0f);
        }

        var cabinetWorld = slot.parent != null
            ? GetObjectWorldBounds(slot.parent.gameObject, excludeEquipment: true)
            : null;
        var itemBounds = GetObjectWorldBounds(target);
        var fitH = layout.fitHeightFraction > 0.01f ? layout.fitHeightFraction : (layout.snapBottomToFloor ? 0.85f : 0.22f);
        var fitW = layout.fitWidthFraction > 0.01f ? layout.fitWidthFraction : 0.24f;
        if (itemBounds.HasValue && cabinetWorld.HasValue)
        {
            var maxH = cabinetWorld.Value.size.y * fitH;
            var maxW = cabinetWorld.Value.size.x * fitW;
            var h = Mathf.Max(itemBounds.Value.size.y, 0.001f);
            var w = Mathf.Max(Mathf.Max(itemBounds.Value.size.x, itemBounds.Value.size.z), 0.001f);
            var fit = Mathf.Min(maxH / h, maxW / w);
            if (Mathf.Abs(fit - 1f) > 0.02f)
            {
                target.transform.localScale *= fit;
                notes.Add($"{target.name} scaled x{fit:F2} to fit compartment");
            }
        }

        if (layout.snapBottomToFloor)
        {
            var b = GetObjectWorldBounds(target);
            if (b.HasValue)
            {
                var cabinetFloorY = cabinetWorld?.min.y ?? FloorY;
                var dy = cabinetFloorY - b.Value.min.y + 0.02f;
                if (Mathf.Abs(dy) > 0.0001f)
                    target.transform.position += new Vector3(0f, dy, 0f);
            }
        }
        else
        {
            // Keep shelf items visually resting on their shelf band (center of item ≈ slot Y).
            var b = GetObjectWorldBounds(target);
            if (b.HasValue)
            {
                var dy = slot.position.y - b.Value.center.y;
                target.transform.position += new Vector3(0f, dy, 0f);
            }
        }

        notes.Add($"{target.name} world={target.transform.position} lossy={target.transform.lossyScale}");
    }

    static Transform EnsureSlot(Transform cabinet, string slotName)
    {
        var existing = cabinet.Find(slotName);
        if (existing != null)
            return existing;

        var orphan = FindSceneObject(slotName);
        if (orphan != null)
        {
            Undo.SetTransformParent(orphan.transform, cabinet, $"Parent {slotName}");
            return orphan.transform;
        }

        var go = new GameObject(slotName);
        Undo.RegisterCreatedObjectUndo(go, $"Create {slotName}");
        Undo.SetTransformParent(go.transform, cabinet, $"Parent {slotName}");
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    static Bounds? GetLocalMeshBounds(GameObject root)
    {
        Bounds? local = null;
        var preferCabinetAsset = CountCabinetAssetMeshes(root.transform) > 0;

        void EncapsulateMesh(Transform meshTf, Bounds meshLocalBounds)
        {
            foreach (var c in GetCorners(meshLocalBounds))
            {
                var world = meshTf.TransformPoint(c);
                var inRoot = root.transform.InverseTransformPoint(world);
                if (!local.HasValue) local = new Bounds(inRoot, Vector3.zero);
                else
                {
                    var b = local.Value;
                    b.Encapsulate(inRoot);
                    local = b;
                }
            }
        }

        // Pass 1: cabinet-asset meshes only (when available).
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null)
                continue;
            if (IsUnderSlotOrEquipment(mf.transform, root.transform))
                continue;
            if (preferCabinetAsset && !IsCabinetMeshAsset(mf.sharedMesh))
                continue;
            EncapsulateMesh(mf.transform, mf.sharedMesh.bounds);
        }

        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null)
                continue;
            if (IsUnderSlotOrEquipment(smr.transform, root.transform))
                continue;
            if (preferCabinetAsset && !IsCabinetMeshAsset(smr.sharedMesh))
                continue;
            EncapsulateMesh(smr.transform, smr.sharedMesh.bounds);
        }

        // Pass 2: any non-equipment mesh under root (asset filter was too strict).
        if (!local.HasValue || local.Value.size.sqrMagnitude < 0.01f)
        {
            local = null;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;
                if (IsUnderSlotOrEquipment(mf.transform, root.transform))
                    continue;
                EncapsulateMesh(mf.transform, mf.sharedMesh.bounds);
            }
        }

        if (local.HasValue && local.Value.size.sqrMagnitude > 0.01f)
            return local;

        var worldB = GetObjectWorldBounds(root, excludeEquipment: true);
        if (!worldB.HasValue)
            worldB = GetObjectWorldBounds(root, excludeEquipment: false);
        if (!worldB.HasValue)
            return null;

        Bounds localB = new Bounds(root.transform.InverseTransformPoint(worldB.Value.center), Vector3.zero);
        foreach (var c in GetCorners(worldB.Value))
            localB.Encapsulate(root.transform.InverseTransformPoint(c));
        return localB;
    }

    static Bounds? GetFallbackLocalBounds(GameObject root)
    {
        // Last resort: author a locker-sized local box so placement can proceed.
        var world = GetObjectWorldBounds(root, excludeEquipment: false);
        if (world.HasValue && world.Value.size.sqrMagnitude > 0.01f)
        {
            Bounds localB = new Bounds(root.transform.InverseTransformPoint(world.Value.center), Vector3.zero);
            foreach (var c in GetCorners(world.Value))
                localB.Encapsulate(root.transform.InverseTransformPoint(c));
            if (localB.size.sqrMagnitude > 0.01f)
                return localB;
        }

        // ~2.2m tall locker in local space assuming identity-ish transform.
        var lossy = AbsVec(root.transform.lossyScale);
        var size = new Vector3(
            2.4f / Mathf.Max(lossy.x, 1e-4f),
            2.2f / Mathf.Max(lossy.y, 1e-4f),
            0.8f / Mathf.Max(lossy.z, 1e-4f));
        return new Bounds(new Vector3(0f, size.y * 0.5f, 0f), size);
    }

    static bool IsUnderSlotOrEquipment(Transform t, Transform root)
    {
        while (t != null && t != root)
        {
            if (SlotNames.Contains(t.name))
                return true;
            if (t.name.StartsWith("PPE_Chemical", StringComparison.Ordinal)
                || t.name.StartsWith("PPE_Face", StringComparison.Ordinal)
                || t.name == "PPE_Gloves")
                return true;
            t = t.parent;
        }

        return false;
    }

    static Bounds? GetObjectWorldBounds(GameObject go, bool excludeEquipment = true)
    {
        Bounds? b = null;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (excludeEquipment && IsUnderSlotOrEquipment(r.transform, go.transform))
                continue;
            if (!b.HasValue) b = r.bounds;
            else
            {
                var x = b.Value;
                x.Encapsulate(r.bounds);
                b = x;
            }
        }

        return b;
    }

    static Vector3[] GetCorners(Bounds b) => new[]
    {
        new Vector3(b.min.x, b.min.y, b.min.z),
        new Vector3(b.min.x, b.min.y, b.max.z),
        new Vector3(b.min.x, b.max.y, b.min.z),
        new Vector3(b.min.x, b.max.y, b.max.z),
        new Vector3(b.max.x, b.min.y, b.min.z),
        new Vector3(b.max.x, b.min.y, b.max.z),
        new Vector3(b.max.x, b.max.y, b.min.z),
        new Vector3(b.max.x, b.max.y, b.max.z),
    };

    static void DumpSlots(Transform cabinet, StringBuilder sb)
    {
        sb.AppendLine("Slots:");
        foreach (var name in SlotNames)
        {
            var slot = cabinet.Find(name);
            if (slot == null)
            {
                sb.AppendLine($"  • {name}: MISSING");
                continue;
            }

            var kids = new List<string>();
            for (var i = 0; i < slot.childCount; i++)
                kids.Add(slot.GetChild(i).name);
            sb.AppendLine($"  • {name}: local={slot.localPosition} world={slot.position} kids=[{string.Join(", ", kids)}]");
        }
    }

    static List<GameObject> FindAllByName(string objectName)
    {
        var list = new List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == objectName)
                    list.Add(t.gameObject);
            }
        }

        return list;
    }

    static GameObject FindSceneObject(string objectName)
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == objectName)
                    return t.gameObject;
            }
        }

        return null;
    }

    static Vector3 EstimateRoomCenter(Transform storage)
    {
        var floor = GameObject.Find("Floor");
        if (floor != null)
            return new Vector3(floor.transform.position.x, storage.position.y + 1.2f, floor.transform.position.z);
        return new Vector3(0f, storage.position.y + 1.2f, 0f);
    }

    static string GetPath(Transform t)
    {
        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }

    static bool AlmostOne(Vector3 s) =>
        Mathf.Abs(s.x - 1f) < 0.001f && Mathf.Abs(s.y - 1f) < 0.001f && Mathf.Abs(s.z - 1f) < 0.001f;
}
