using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Places VR hand Left/Right under PPE_Storage/GlovesSlot as the PPE_Gloves visual,
/// then ensures ray-select + ChemicalGloves equip wiring (no physics grab).
/// </summary>
public static class PPEChemicalGlovesStorageSetup
{
    const string LeftAssetPath = "Assets/Common/VRHands/vr hand_Left.prefab";
    const string RightAssetPath = "Assets/Common/VRHands/vr hand_Right.prefab";

    const string StorageName = "PPE_Storage";
    const string SlotName = "GlovesSlot";
    const string GlovesRootName = "PPE_Gloves";
    const string VisualChildName = "Visual";
    const string LeftInstanceName = "VrHand_Left";
    const string RightInstanceName = "VrHand_Right";

    /// <summary>Target size for the paired hands on the left 3rd shelf.</summary>
    const float TargetWorldHeight = 0.22f;
    const float TargetWorldWidth = 0.45f;
    /// <summary>World-space distance between left/right hand centers after fit.</summary>
    const float PairSpacingWorld = 0.14f;

    const string PreferredScenePath = "Assets/Scenes/04_PPE.unity";

    public static void InstallChemicalGlovesIntoStorage()
    {
        InstallVrHandsIntoStorage();
    }

    public static void InstallVrHandsIntoStorage()
    {
        EnsurePreferredSceneLoaded();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Gloves] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Install VR Hands Into PPE Storage ===");
        report.AppendLine($"Scene: {scene.name}");

        CleanupOrphanDuplicates(scene, report);

        var leftModel = LoadModel(LeftAssetPath, report);
        var rightModel = LoadModel(RightAssetPath, report);
        if (leftModel == null || rightModel == null)
        {
            Debug.LogError(report.ToString());
            return;
        }

        var slot = FindDeep(scene, SlotName);
        if (slot == null)
        {
            var storage = FindDeep(scene, StorageName);
            if (storage == null)
            {
                report.AppendLine("ERROR: PPE_Storage not found.");
                Debug.LogError(report.ToString());
                return;
            }

            var slotGo = new GameObject(SlotName);
            Undo.RegisterCreatedObjectUndo(slotGo, "Create GlovesSlot");
            Undo.SetTransformParent(slotGo.transform, storage, "Parent GlovesSlot");
            slotGo.transform.localPosition = Vector3.zero;
            slotGo.transform.localRotation = Quaternion.identity;
            slotGo.transform.localScale = Vector3.one;
            slot = slotGo.transform;
            report.AppendLine("Created GlovesSlot under PPE_Storage.");
        }
        else
        {
            report.AppendLine($"GlovesSlot: {GetHierarchyPath(slot)}");
        }

        AlignGlovesSlotToLeftThirdShelf(scene, slot, report);

        var glovesRoot = EnsureGlovesRoot(slot, report);
        var visual = EnsureVisualChild(glovesRoot.transform, report);

        ClearVisualContents(visual, report);
        var left = SpawnGlove(leftModel, LeftInstanceName, visual, report);
        var right = SpawnGlove(rightModel, RightInstanceName, visual, report);

        // Strip VRGloveHand / colliders from shelf display copies.
        StripRuntimeHandComponents(left, report);
        StripRuntimeHandComponents(right, report);

        BakeSkinnedMeshes(left, report);
        BakeSkinnedMeshes(right, report);

        // Pose first (shared), fit scale on Visual, then separate in world units.
        PoseGlove(left.transform);
        PoseGlove(right.transform);
        left.transform.localPosition = Vector3.zero;
        right.transform.localPosition = Vector3.zero;
        FitPairToShelf(visual, TargetWorldHeight, TargetWorldWidth, report);
        SeparatePairInWorld(left.transform, right.transform, PairSpacingWorld, report);

        // Keep interactable root at identity under the slot; scale lives on Visual.
        Undo.RecordObject(glovesRoot.transform, "Reset PPE_Gloves local xform");
        glovesRoot.transform.localPosition = Vector3.zero;
        glovesRoot.transform.localRotation = Quaternion.identity;
        glovesRoot.transform.localScale = Vector3.one;

        // Remove grab/hang behavior — shelf PPE is ray-select + equip only.
        // XRGrabInteractable was causing the gloves to snap/fly to the controller on select.
        RemoveGrabInteractable(glovesRoot, report);

        EnsureEquipable(glovesRoot, report);
        EnsureRayInteractableBasics(glovesRoot, report);
        RefitRootCollider(glovesRoot, report);
        UpdateHighlightVisual(glovesRoot, report);

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.SetDirty(glovesRoot);
            EditorUtility.SetDirty(visual.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);
        }

        Selection.activeGameObject = glovesRoot;
        report.AppendLine("Done. Shelf shows VR hands. Ray-select remains; wearable PPE equips by Grip placement only.");
        Debug.Log(report.ToString(), glovesRoot);
    }

    static void EnsurePreferredSceneLoaded()
    {
        var active = SceneManager.GetActiveScene();
        if (active.IsValid() && active.path == PreferredScenePath)
            return;

        if (!System.IO.File.Exists(PreferredScenePath))
            return;

        // Don't interrupt an unrelated dirty scene silently — only auto-open if current isn't the PPE room.
        if (active.IsValid() && !string.IsNullOrEmpty(active.path) &&
            active.path.IndexOf("04_PPE", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        EditorSceneManager.OpenScene(PreferredScenePath, OpenSceneMode.Single);
    }

    static GameObject LoadModel(string assetPath, StringBuilder report)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (model == null)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }

        if (model == null)
            report.AppendLine($"ERROR: missing model {assetPath}");
        else
            report.AppendLine($"Loaded {assetPath}");

        return model;
    }

    static GameObject EnsureGlovesRoot(Transform slot, StringBuilder report)
    {
        Transform existing = null;
        for (int i = 0; i < slot.childCount; i++)
        {
            var child = slot.GetChild(i);
            if (child.name == GlovesRootName ||
                child.name.IndexOf("PPE_ChemicalGloves", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                child.name.IndexOf("PPE_Gloves", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                existing = child;
                break;
            }
        }

        GameObject root;
        if (existing != null)
        {
            root = existing.gameObject;
            if (root.name != GlovesRootName)
            {
                Undo.RecordObject(root, "Rename gloves root");
                root.name = GlovesRootName;
            }

            report.AppendLine($"Reused gloves root: {GetHierarchyPath(root.transform)}");
        }
        else
        {
            root = new GameObject(GlovesRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create PPE_Gloves");
            Undo.SetTransformParent(root.transform, slot, "Parent PPE_Gloves");
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            report.AppendLine("Created PPE_Gloves under GlovesSlot.");
        }

        return root;
    }

    static Transform EnsureVisualChild(Transform glovesRoot, StringBuilder report)
    {
        var visual = glovesRoot.Find(VisualChildName);
        if (visual != null)
        {
            report.AppendLine("Reused Visual child.");
            return visual;
        }

        var go = new GameObject(VisualChildName);
        Undo.RegisterCreatedObjectUndo(go, "Create Gloves Visual");
        Undo.SetTransformParent(go.transform, glovesRoot, "Parent Gloves Visual");
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        report.AppendLine("Created Visual child.");
        return go.transform;
    }

    static void ClearVisualContents(Transform visual, StringBuilder report)
    {
        // Destroy children back-to-front (includes prefab instances).
        for (int i = visual.childCount - 1; i >= 0; i--)
        {
            var child = visual.GetChild(i).gameObject;
            report.AppendLine($"Removed old visual: {child.name}");
            Undo.DestroyObjectImmediate(child);
        }
    }

    static GameObject SpawnGlove(GameObject model, string name, Transform parent, StringBuilder report)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(instance, "Spawn " + name);
        instance.name = name;
        Undo.SetTransformParent(instance.transform, parent, "Parent " + name);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        // Strip animators — storage gloves are static props for ray-select equip.
        foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
        {
            if (animator != null)
                Undo.DestroyObjectImmediate(animator);
        }

        report.AppendLine($"Spawned {name}");
        return instance;
    }

    /// <summary>
    /// Snap GlovesSlot to the left bay 3rd shelf — same X/Z as face shield / respirator / boots,
    /// Y halfway between respirator (2nd) and boots (4th).
    /// </summary>
    static void AlignGlovesSlotToLeftThirdShelf(Scene scene, Transform slot, StringBuilder report)
    {
        var face = FindDeep(scene, "FaceShieldSlot");
        var respirator = FindDeep(scene, "RespiratorSlot");
        var boots = FindDeep(scene, "BootsSlot");

        Undo.RecordObject(slot, "Align GlovesSlot to left 3rd shelf");

        // Prefer sibling left-bay slots for X/Z.
        Transform axisSource = respirator != null ? respirator : (face != null ? face : boots);
        if (axisSource != null)
        {
            var p = axisSource.localPosition;
            float y;
            if (respirator != null && boots != null)
            {
                // 3rd shelf: between 2nd (respirator) and 4th (boots).
                y = Mathf.Lerp(respirator.localPosition.y, boots.localPosition.y, 0.42f);
            }
            else if (respirator != null)
            {
                y = respirator.localPosition.y - 0.22f;
            }
            else
            {
                y = p.y;
            }

            slot.localPosition = new Vector3(p.x, y, p.z);
            slot.localRotation = Quaternion.identity;
            slot.localScale = Vector3.one;
            report.AppendLine(
                $"Aligned GlovesSlot to left 3rd shelf local=({slot.localPosition.x:F3}, {slot.localPosition.y:F3}, {slot.localPosition.z:F3})");
        }
        else
        {
            // Fallback matching PPEPreparationRoomLayoutTool DefaultLayouts gloves normalizedY≈0.40
            slot.localPosition = new Vector3(0.324f, 0.390f, -0.036f);
            slot.localRotation = Quaternion.identity;
            slot.localScale = Vector3.one;
            report.AppendLine("Aligned GlovesSlot via fallback left-bay local position.");
        }

        // Keep under PPE_Storage if somehow unparented.
        var storage = FindDeep(scene, StorageName);
        if (storage != null && slot.parent != storage)
        {
            Undo.SetTransformParent(slot, storage, "Reparent GlovesSlot");
            report.AppendLine("Reparented GlovesSlot under PPE_Storage.");
        }
    }

    static void BakeSkinnedMeshes(GameObject root, StringBuilder report)
    {
        var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var baked = 0;
        foreach (var smr in skinned)
        {
            if (smr == null || smr.sharedMesh == null)
                continue;

            var go = smr.gameObject;
            var mesh = new Mesh { name = smr.sharedMesh.name + "_Baked" };
            smr.BakeMesh(mesh, true);

            var materials = smr.sharedMaterials;
            Undo.DestroyObjectImmediate(smr);

            var filter = go.GetComponent<MeshFilter>();
            if (filter == null)
                filter = Undo.AddComponent<MeshFilter>(go);
            filter.sharedMesh = mesh;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
                renderer = Undo.AddComponent<MeshRenderer>(go);
            renderer.sharedMaterials = materials;
            baked++;
        }

        // Armature bones are unused after bake — hide empty bone hierarchy clutter.
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root.transform)
                continue;
            if (t.GetComponent<MeshFilter>() != null || t.GetComponent<MeshRenderer>() != null)
                continue;
            if (t.GetComponentsInChildren<MeshRenderer>(true).Length > 0)
                continue;
            // Likely a bone / empty — leave it; destroying bones can break hierarchy unexpectedly.
        }

        if (baked > 0)
            report.AppendLine($"Baked {baked} skinned mesh(es) on {root.name}.");
    }

    static void PoseGlove(Transform glove)
    {
        Undo.RecordObject(glove, "Pose glove on shelf");
        // Fingers forward out of locker (standing glove pose — reliable mesh bounds).
        glove.localRotation = Quaternion.Euler(0f, 180f, 0f);
    }

    static void SeparatePairInWorld(Transform left, Transform right, float worldSpacing, StringBuilder report)
    {
        Undo.RecordObject(left, "Separate left glove");
        Undo.RecordObject(right, "Separate right glove");

        var parent = left.parent;
        var half = worldSpacing * 0.5f;
        if (parent != null)
        {
            // Convert world offset along parent right into local space.
            var worldOffset = parent.right * half;
            var localOffset = parent.InverseTransformVector(worldOffset);
            left.localPosition = -localOffset;
            right.localPosition = localOffset;
        }
        else
        {
            left.localPosition = new Vector3(-half, 0f, 0f);
            right.localPosition = new Vector3(half, 0f, 0f);
        }

        report.AppendLine($"Separated pair worldSpacing={worldSpacing:F2}m.");
    }

    static void FitPairToShelf(Transform visual, float targetHeight, float targetWidth, StringBuilder report)
    {
        Undo.RecordObject(visual, "Fit gloves visual scale");
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        visual.localScale = Vector3.one;

        // Skinned meshes often have stale edit-time bounds — update from mesh when possible.
        foreach (var smr in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr.sharedMesh == null)
                continue;
            smr.updateWhenOffscreen = true;
        }

        var bounds = GetRendererWorldBounds(visual.gameObject);
        if (!bounds.HasValue)
        {
            report.AppendLine("WARN: no renderers to fit.");
            return;
        }

        var height = Mathf.Max(bounds.Value.size.y, 0.001f);
        var width = Mathf.Max(Mathf.Max(bounds.Value.size.x, bounds.Value.size.z), 0.001f);
        var fit = Mathf.Min(targetHeight / height, targetWidth / width);
        fit = Mathf.Clamp(fit, 0.8f, 8f);
        visual.localScale = Vector3.one * fit;

        // Sit on the shelf plane (slot origin ≈ shelf surface).
        bounds = GetRendererWorldBounds(visual.gameObject);
        if (bounds.HasValue)
        {
            var dy = visual.position.y - bounds.Value.min.y + 0.015f;
            visual.position += new Vector3(0f, dy, 0f);
            var dxz = visual.position - bounds.Value.center;
            visual.position += new Vector3(dxz.x, 0f, dxz.z);
        }

        report.AppendLine(
            $"Fitted visual scale x{fit:F3} → target H={targetHeight:F2}m W={targetWidth:F2}m.");
    }

    static void EnsureEquipable(GameObject glovesRoot, StringBuilder report)
    {
        var equipable = glovesRoot.GetComponent<PPEEquipable>();
        if (equipable == null)
        {
            equipable = Undo.AddComponent<PPEEquipable>(glovesRoot);
            report.AppendLine("Added PPEEquipable.");
        }

        Undo.RecordObject(equipable, "Configure ChemicalGloves");
        equipable.EditorApplyIdentity(PPEEquipmentManager.IdChemicalGloves, "내화학 보호장갑");
        EditorUtility.SetDirty(equipable);
        report.AppendLine("PPEEquipable → ChemicalGloves / 내화학 보호장갑");

        var item = glovesRoot.GetComponent<PPEItem>();
        if (item == null)
        {
            item = Undo.AddComponent<PPEItem>(glovesRoot);
            report.AppendLine("Added PPEItem.");
        }
    }

    static void EnsureRayInteractableBasics(GameObject glovesRoot, StringBuilder report)
    {
        if (glovesRoot.GetComponent<XRSimpleInteractable>() == null &&
            glovesRoot.GetComponent<XRBaseInteractable>() == null)
        {
            Undo.AddComponent<XRSimpleInteractable>(glovesRoot);
            report.AppendLine("Added XRSimpleInteractable.");
        }

        if (glovesRoot.GetComponent<PPEOutlineHighlight>() == null)
        {
            var outline = Undo.AddComponent<PPEOutlineHighlight>(glovesRoot);
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/1.JSR/Materials/PPE/PPE_Outline.mat");
            if (mat == null)
                mat = Resources.Load<Material>("PPE_Outline");
            if (mat != null)
            {
                var so = new SerializedObject(outline);
                var prop = so.FindProperty("outlineMaterialSource");
                if (prop != null)
                {
                    prop.objectReferenceValue = mat;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            report.AppendLine("Added PPEOutlineHighlight.");
        }

        if (glovesRoot.GetComponent<PPERayInteractionDebug>() == null)
        {
            Undo.AddComponent<PPERayInteractionDebug>(glovesRoot);
            report.AppendLine("Added PPERayInteractionDebug.");
        }
    }

    static void RemoveGrabInteractable(GameObject glovesRoot, StringBuilder report)
    {
        // STEP 2 hand grabbing supersedes the old shelf-only workaround. Keep the
        // common PPE grab component and its physics setup when this legacy tool is rerun.
        if (glovesRoot.GetComponent<PPEHandGrabbable>() != null)
        {
            report.AppendLine("Kept PPEHandGrabbable (STEP 2 hand/ray grab is active).");
            return;
        }

        // Add Simple FIRST so RequireComponent(XRBaseInteractable) on PPEItem /
        // PPEOutlineHighlight stays satisfied while GloveGrabPose is removed.
        if (glovesRoot.GetComponent<XRSimpleInteractable>() == null)
        {
            Undo.AddComponent<XRSimpleInteractable>(glovesRoot);
            report.AppendLine("Added XRSimpleInteractable before grab removal.");
        }

        // GloveGrabPose / XRGrabInteractable snaps the shelf item to the controller —
        // that looks like the gloves "flying away" when selecting/equipping.
        foreach (var grab in glovesRoot.GetComponents<XRGrabInteractable>())
        {
            Undo.DestroyObjectImmediate(grab);
            report.AppendLine(
                glovesRoot.GetComponent<XRGrabInteractable>() == null
                    ? "Removed XRGrabInteractable/GloveGrabPose (shelf PPE is not physics-grabbable)."
                    : "WARNING: could not remove XRGrabInteractable — check RequireComponent.");
        }

        var grip = glovesRoot.transform.Find("GripPoint");
        if (grip != null)
        {
            Undo.DestroyObjectImmediate(grip.gameObject);
            report.AppendLine("Removed GripPoint.");
        }

        var rb = glovesRoot.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Undo.DestroyObjectImmediate(rb);
            report.AppendLine("Removed Rigidbody.");
        }
    }

    static void StripRuntimeHandComponents(GameObject root, StringBuilder report)
    {
        if (root == null)
            return;

        foreach (var hand in root.GetComponentsInChildren<VRGloveHand>(true))
        {
            Undo.DestroyObjectImmediate(hand);
            report.AppendLine($"Removed VRGloveHand from shelf copy '{root.name}'.");
        }

        foreach (var col in root.GetComponentsInChildren<Collider>(true))
            Undo.DestroyObjectImmediate(col);
    }

    static void RefitRootCollider(GameObject glovesRoot, StringBuilder report)
    {
        DisableCubeHighlight(glovesRoot, report);
        EnsureMeshCollidersOnGloveMeshes(glovesRoot, report);
    }

    /// <summary>
    /// Gloves used a cube HighlightVisual via PPEHighlight; other PPE use mesh outline.
    /// Kill the cube so hover matches the glove silhouette (PPEOutlineHighlight).
    /// </summary>
    static void DisableCubeHighlight(GameObject glovesRoot, StringBuilder report)
    {
        var hv = glovesRoot.transform.Find("HighlightVisual");
        if (hv != null)
        {
            Undo.DestroyObjectImmediate(hv.gameObject);
            report.AppendLine("Removed HighlightVisual cube.");
        }

        var legacy = glovesRoot.GetComponent<PPEHighlight>();
        if (legacy != null)
        {
            Undo.RecordObject(legacy, "Disable gloves cube PPEHighlight");
            legacy.enabled = false;
            var so = new SerializedObject(legacy);
            var prop = so.FindProperty("highlightObject");
            if (prop != null)
            {
                prop.objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(legacy);
            report.AppendLine("Disabled PPEHighlight (cube hover) — outline uses PPEOutlineHighlight.");
        }
    }

    /// <summary>
    /// Replace the root BoxCollider with MeshColliders on baked glove meshes so
    /// ray hits follow the hand shape instead of a rectangular volume.
    /// </summary>
    static void EnsureMeshCollidersOnGloveMeshes(GameObject glovesRoot, StringBuilder report)
    {
        var visual = glovesRoot.transform.Find(VisualChildName);
        var searchRoot = visual != null ? visual.gameObject : glovesRoot;

        // Shelf copies may still be skinned — bake so MeshCollider can use a static mesh.
        BakeSkinnedMeshes(searchRoot, report);

        foreach (var box in glovesRoot.GetComponents<BoxCollider>())
        {
            Undo.DestroyObjectImmediate(box);
            report.AppendLine("Removed root BoxCollider.");
        }

        // Drop any leftover colliders under Visual before adding mesh ones.
        foreach (var col in searchRoot.GetComponentsInChildren<Collider>(true))
        {
            if (col == null)
                continue;
            Undo.DestroyObjectImmediate(col);
        }

        var filters = searchRoot.GetComponentsInChildren<MeshFilter>(true);
        var added = 0;
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            if (filter.gameObject.name == "HighlightVisual")
                continue;

            var mc = Undo.AddComponent<MeshCollider>(filter.gameObject);
            mc.sharedMesh = filter.sharedMesh;
            // Convex is required for non-kinematic Rigidbodies; shelf gloves have none,
            // but convex also avoids cook failures on dense hand meshes.
            mc.convex = true;
            mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                | MeshColliderCookingOptions.EnableMeshCleaning
                | MeshColliderCookingOptions.WeldColocatedVertices;
            added++;
        }

        if (added == 0)
        {
            // Fallback compact box if meshes are missing.
            var box = Undo.AddComponent<BoxCollider>(glovesRoot);
            box.center = new Vector3(0f, 0.12f, 0f);
            box.size = new Vector3(0.22f, 0.24f, 0.14f);
            report.AppendLine("WARNING: no glove MeshFilters — fell back to compact BoxCollider.");
            return;
        }

        // Empty collider list → XRI auto-collects child colliders (including MeshColliders).
        var simple = glovesRoot.GetComponent<XRSimpleInteractable>();
        if (simple != null)
        {
            Undo.RecordObject(simple, "Clear gloves interactable collider list");
            var so = new SerializedObject(simple);
            var collidersProp = so.FindProperty("m_Colliders");
            if (collidersProp != null && collidersProp.isArray)
            {
                collidersProp.ClearArray();
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(simple);
        }

        report.AppendLine($"Added {added} convex MeshCollider(s) matching glove meshes.");
    }

    public static void FixGlovesMeshColliderAndOutline()
    {
        EnsurePreferredSceneLoaded();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Gloves] No active loaded scene.");
            return;
        }

        var gloves = FindDeep(scene, GlovesRootName);
        if (gloves == null)
        {
            Debug.LogError("[PPE Gloves] PPE_Gloves not found.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Fix Gloves Mesh Collider And Outline ===");
        RefitRootCollider(gloves.gameObject, report);
        EnsureRayInteractableBasics(gloves.gameObject, report);

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.SetDirty(gloves.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);
        }

        Selection.activeGameObject = gloves.gameObject;
        Debug.Log(report.ToString(), gloves);
    }

    /// <summary>
    /// Old chemical-glove setup left a giant cube HighlightVisual (~1m deep) that
    /// lights up on ray hover and steals aim from neighboring PPE.
    /// </summary>
    static void RefitHighlightVisual(GameObject glovesRoot, BoxCollider box, StringBuilder report)
    {
        // Kept for callers; cube highlight is removed entirely now.
        DisableCubeHighlight(glovesRoot, report);
    }

    static void UpdateHighlightVisual(GameObject glovesRoot, StringBuilder report)
    {
        DisableCubeHighlight(glovesRoot, report);
    }

    static void CleanupOrphanDuplicates(Scene scene, StringBuilder report)
    {
        // Remove accidental "PPE_ChemicalGlb 1" style roots if someone dragged the folder into the scene.
        var toDestroy = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name.StartsWith("PPE_ChemicalGlb") ||
                root.name.StartsWith("ChemicalGlove_"))
                toDestroy.Add(root);
        }

        foreach (var go in toDestroy)
        {
            report.AppendLine($"Removed orphan scene root: {go.name}");
            Undo.DestroyObjectImmediate(go);
        }
    }

    static Transform FindDeep(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                return root.transform;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                    return t;
            }
        }

        return null;
    }

    static Bounds? GetRendererWorldBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds? bounds = null;
        foreach (var r in renderers)
        {
            if (r == null || !r.enabled)
                continue;
            if (r.gameObject.name == "HighlightVisual")
                continue;
            if (!bounds.HasValue)
                bounds = r.bounds;
            else
            {
                var b = bounds.Value;
                b.Encapsulate(r.bounds);
                bounds = b;
            }
        }

        return bounds;
    }

    static Vector3[] GetWorldCorners(Bounds bounds)
    {
        var c = bounds.center;
        var e = bounds.extents;
        return new[]
        {
            c + new Vector3(-e.x, -e.y, -e.z),
            c + new Vector3(-e.x, -e.y, e.z),
            c + new Vector3(-e.x, e.y, -e.z),
            c + new Vector3(-e.x, e.y, e.z),
            c + new Vector3(e.x, -e.y, -e.z),
            c + new Vector3(e.x, -e.y, e.z),
            c + new Vector3(e.x, e.y, -e.z),
            c + new Vector3(e.x, e.y, e.z),
        };
    }

    static string GetHierarchyPath(Transform t)
    {
        if (t == null)
            return "<null>";

        var stack = new Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }

        return string.Join("/", stack);
    }
}
