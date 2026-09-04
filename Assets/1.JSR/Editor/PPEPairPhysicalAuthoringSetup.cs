using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Authors left/right physical pair members in 04_PPE before Play Mode.
/// Uses persistent generated left/right meshes. Never copies a combined pair mesh.
/// </summary>
public static class PPEPairPhysicalAuthoringSetup
{
    const string ScenePath = "Assets/Scenes/04_PPE.unity";
    const float GloveGrabDistance = PPEDirectGrabProximityFilter.NearGrabHoverSelectDistance;
    const float BootGrabDistance = 0.10f;

    public static void AuthorPairPieces()
    {
        var scene = EnsurePpeSceneOpen();
        if (!scene.IsValid())
            return;

        var report = new StringBuilder();
        var changed = AuthorScene(scene, report);
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log(report.ToString());
    }

    public static void SwapInnerGloveSides()
    {
        SwapSides(PPEEquipmentManager.IdInnerGloves);
    }

    public static void SwapBootSides()
    {
        SwapSides(PPEEquipmentManager.IdChemicalBoots);
    }

    public static bool IsAuthoringRequired()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            return false;

        return !IsFullyAuthored(scene, PPEEquipmentManager.IdInnerGloves) ||
               !IsFullyAuthored(scene, PPEEquipmentManager.IdChemicalGloves) ||
               !IsFullyAuthored(scene, PPEEquipmentManager.IdChemicalBoots);
    }

    public static bool AuthorScene(Scene scene, StringBuilder report)
    {
        report.AppendLine("=== PPE Left/Right Pair Authoring ===");
        var changed = false;
        changed |= AuthorEquipable(scene, PPEEquipmentManager.IdInnerGloves, report);
        changed |= AuthorEquipable(scene, PPEEquipmentManager.IdChemicalGloves, report);
        changed |= AuthorEquipable(scene, PPEEquipmentManager.IdChemicalBoots, report);
        return changed;
    }

    static Scene EnsurePpeSceneOpen()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isLoaded && scene.path == ScenePath)
            return scene;

        scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid() || !scene.isLoaded)
            Debug.LogError("[PPE Pair Authoring] Could not open " + ScenePath);
        return scene;
    }

    static bool AuthorEquipable(Scene scene, string itemId, StringBuilder report)
    {
        var equipable = FindEquipable(scene, itemId);
        if (equipable == null)
        {
            report.AppendLine("MISSING " + itemId);
            return false;
        }

        if (IsFullyAuthored(equipable.gameObject))
        {
            report.AppendLine("  " + itemId + ": already authored, no changes");
            return false;
        }

        var root = equipable.gameObject;
        var coordinator = root.GetComponent<PPEGlovePairCoordinator>();
        if (coordinator == null)
            coordinator = Undo.AddComponent<PPEGlovePairCoordinator>(root);

        Undo.RecordObject(coordinator, "Author PPE pair coordinator");
        Undo.RecordObject(equipable, "Author PPE pair deactivateOnEquip");
        equipable.EditorSetDeactivateOnEquip(false);

        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalBoots, System.StringComparison.Ordinal))
            coordinator.EditorApplyPairSettings(PPEPairTargetKind.BodyLowerLeg, false, 0.18f, 40f, 0.38f, false);
        else
            coordinator.EditorApplyPairSettings(PPEPairTargetKind.TrackedHands, true, 0.12f, 40f, 0.38f, false);

        var left = FindOrCreateMemberRoot(root.transform, LeftMemberName(itemId));
        var right = FindOrCreateMemberRoot(root.transform, RightMemberName(itemId));
        var assigned = false;
        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalGloves, System.StringComparison.Ordinal))
        {
            AssignExistingSideVisuals(root.transform, left, right, report);
            assigned = HasIndividualVisual(left.transform) && HasIndividualVisual(right.transform);
        }
        else if (string.Equals(itemId, PPEEquipmentManager.IdInnerGloves, System.StringComparison.Ordinal))
        {
            assigned = AssignGeneratedPairVisuals(
                root.transform,
                left,
                right,
                PPEPairMeshSeparator.InnerGloveLeft,
                PPEPairMeshSeparator.InnerGloveRight,
                false,
                report);
        }
        else
        {
            assigned = AssignGeneratedPairVisuals(
                root.transform,
                left,
                right,
                PPEPairMeshSeparator.ChemicalBootLeft,
                PPEPairMeshSeparator.ChemicalBootRight,
                true,
                report);
        }

        if (!assigned)
        {
            report.AppendLine("STOP " + itemId + ": true left/right geometry was not authored.");
            return false;
        }

        var leftMember = ConfigureMember(coordinator, left, PPEGrabHand.Left, GrabDistance(itemId));
        var rightMember = ConfigureMember(coordinator, right, PPEGrabHand.Right, GrabDistance(itemId));
        coordinator.EditorAssignMembers(leftMember, rightMember);
        EditorUtility.SetDirty(coordinator);
        ConvertLogicalRoot(root, report);
        DisableLogicalRootColliders(root);
        report.AppendLine("  " + itemId + ": " + left.name + " + " + right.name);
        return true;
    }

    static void AssignExistingSideVisuals(
        Transform logical,
        GameObject leftRoot,
        GameObject rightRoot,
        StringBuilder report)
    {
        var leftVisual = FindNamedVisual(logical, "Left", leftRoot.transform, rightRoot.transform);
        var rightVisual = FindNamedVisual(logical, "Right", leftRoot.transform, rightRoot.transform);
        if (leftVisual != null)
            ReparentPreserveWorld(leftVisual, leftRoot.transform);
        if (rightVisual != null)
            ReparentPreserveWorld(rightVisual, rightRoot.transform);
        if (leftVisual == null || rightVisual == null)
            report.AppendLine("  ProtectiveGloves: missing named Left/Right visuals.");
        else
            report.AppendLine("  ProtectiveGloves: using existing VrHand Left/Right meshes.");
    }

    static bool AssignGeneratedPairVisuals(
        Transform logical,
        GameObject leftRoot,
        GameObject rightRoot,
        string leftPath,
        string rightPath,
        bool bootsShelfYawMinus90,
        StringBuilder report)
    {
        if (!PPEPairMeshSeparator.TryLoadPair(leftPath, rightPath, out var leftMesh, out var rightMesh, out var error))
        {
            var source = FirstSourceMeshFilter(logical, leftRoot.transform, rightRoot.transform);
            if (source == null || source.sharedMesh == null)
            {
                report.AppendLine("  " + error);
                report.AppendLine("  Source asset requires manual Blender/asset separation.");
                return false;
            }

            if (!PPEPairMeshSeparator.TryExtractConnectedPair(
                    source.sharedMesh,
                    bootsShelfYawMinus90,
                    out leftMesh,
                    out rightMesh,
                    out error))
            {
                report.AppendLine("  " + error);
                return false;
            }

            report.AppendLine("  Extracted connected islands from readable source mesh (editor-time).");
        }
        else
        {
            report.AppendLine("  Using persistent meshes " + leftPath + " / " + rightPath);
        }

        var sourceFilter = FirstSourceMeshFilter(logical, leftRoot.transform, rightRoot.transform);
        CreateOrUpdateVisual(leftRoot.transform, leftMesh, sourceFilter);
        CreateOrUpdateVisual(rightRoot.transform, rightMesh, sourceFilter);
        DisableSourceRenderers(logical, leftRoot.transform, rightRoot.transform);
        leftRoot.transform.localPosition = Vector3.zero;
        leftRoot.transform.localRotation = Quaternion.identity;
        leftRoot.transform.localScale = Vector3.one;
        rightRoot.transform.localPosition = Vector3.zero;
        rightRoot.transform.localRotation = Quaternion.identity;
        rightRoot.transform.localScale = Vector3.one;
        return leftMesh != rightMesh;
    }

    static void CreateOrUpdateVisual(Transform physical, Mesh mesh, MeshFilter source)
    {
        var visual = physical.Find("Visual");
        if (visual == null)
        {
            var go = new GameObject("Visual");
            Undo.RegisterCreatedObjectUndo(go, "Create pair visual");
            visual = go.transform;
            visual.SetParent(physical, false);
        }

        if (source != null)
        {
            visual.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            var parentScale = physical.lossyScale;
            var worldScale = source.transform.lossyScale;
            visual.localScale = new Vector3(
                SafeDivide(worldScale.x, parentScale.x),
                SafeDivide(worldScale.y, parentScale.y),
                SafeDivide(worldScale.z, parentScale.z));
        }
        else
        {
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
        }

        var filter = visual.GetComponent<MeshFilter>();
        if (filter == null)
            filter = Undo.AddComponent<MeshFilter>(visual.gameObject);
        Undo.RecordObject(filter, "Assign pair visual mesh");
        filter.sharedMesh = mesh;

        var renderer = visual.GetComponent<MeshRenderer>();
        if (renderer == null)
            renderer = Undo.AddComponent<MeshRenderer>(visual.gameObject);
        if (source != null)
        {
            var sourceRenderer = source.GetComponent<MeshRenderer>();
            if (sourceRenderer != null)
            {
                Undo.RecordObject(renderer, "Assign pair visual materials");
                renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            }
        }
    }

    static PPEGlovePairMember ConfigureMember(
        PPEGlovePairCoordinator coordinator,
        GameObject root,
        PPEGrabHand side,
        float grabDistance)
    {
        var visual = FindFirstVisual(root.transform);
        var member = root.GetComponent<PPEGlovePairMember>();
        if (member == null)
            member = Undo.AddComponent<PPEGlovePairMember>(root);

        Undo.RecordObject(member, "Configure pair member");
        member.Configure(coordinator, side, visual, null);
        member.EditorSetVisualRoot(visual);

        var body = root.GetComponent<Rigidbody>();
        if (body == null)
            body = Undo.AddComponent<Rigidbody>(root);
        Undo.RecordObject(body, "Configure pair rigidbody");
        body.mass = 0.35f;
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        var grab = root.GetComponent<PPEHandGrabbable>();
        if (grab == null)
            grab = Undo.AddComponent<PPEHandGrabbable>(root);
        Undo.RecordObject(grab, "Configure pair grab");
        grab.selectMode = InteractableSelectMode.Single;
        grab.distanceCalculationMode = XRBaseInteractable.DistanceCalculationMode.ColliderPosition;
        grab.movementType = XRGrabInteractable.MovementType.Kinematic;
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.smoothPosition = true;
        grab.smoothRotation = true;
        grab.attachEaseInTime = 0f;
        grab.useDynamicAttach = true;
        grab.matchAttachPosition = true;
        grab.matchAttachRotation = true;
        grab.throwOnDetach = false;
        grab.forceGravityOnDetach = false;

        var stored = root.GetComponent<PPEStoredDisplayState>();
        if (stored == null)
            stored = Undo.AddComponent<PPEStoredDisplayState>(root);
        Undo.RecordObject(stored, "Configure pair stored");
        stored.EditorApplyPairMemberDefaults();

        var filter = root.GetComponent<PPEDirectGrabProximityFilter>();
        if (filter == null)
            filter = Undo.AddComponent<PPEDirectGrabProximityFilter>(root);
        Undo.RecordObject(filter, "Configure pair grab filter");
        filter.EditorApplyDefaults(grabDistance);
        filter.RegisterWithInteractable();

        member.EnsurePrimitiveInteractionVolumes();
        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(member);
        EditorUtility.SetDirty(grab);
        EditorUtility.SetDirty(stored);
        EditorUtility.SetDirty(filter);
        return member;
    }

    static void ConvertLogicalRoot(GameObject root, StringBuilder report)
    {
        var grab = root.GetComponent<PPEHandGrabbable>();
        var simple = root.GetComponent<XRSimpleInteractable>();
        if (simple == null)
            simple = Undo.AddComponent<XRSimpleInteractable>(root);

        Undo.RecordObject(simple, "Configure pair hover interactable");
        if (grab != null)
            simple.interactionLayers = grab.interactionLayers;
        simple.selectMode = InteractableSelectMode.Multiple;
        simple.colliders.Clear();
        EditorUtility.SetDirty(simple);

        if (grab != null)
            Undo.DestroyObjectImmediate(grab);

        var filter = root.GetComponent<PPEDirectGrabProximityFilter>();
        if (filter != null)
            Undo.DestroyObjectImmediate(filter);

        var stored = root.GetComponent<PPEStoredDisplayState>();
        if (stored != null)
            Undo.DestroyObjectImmediate(stored);

        var volumes = root.GetComponent<PPEPrimitiveGrabVolumes>();
        if (volumes != null)
            Undo.DestroyObjectImmediate(volumes);

        var body = root.GetComponent<Rigidbody>();
        if (body != null)
            Undo.DestroyObjectImmediate(body);

        report.AppendLine("  " + root.name + ": logical hover-only root");
    }

    static void DisableLogicalRootColliders(GameObject root)
    {
        var colliders = root.GetComponents<Collider>();
        for (var i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;
            Undo.RecordObject(colliders[i], "Disable logical pair collider");
            colliders[i].enabled = false;
        }
    }

    static GameObject FindOrCreateMemberRoot(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null)
            return existing.gameObject;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create pair physical member");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    static Transform FindNamedVisual(Transform logical, string token, Transform left, Transform right)
    {
        var transforms = logical.GetComponentsInChildren<Transform>(true);
        Transform fallback = null;
        for (var i = 0; i < transforms.Length; i++)
        {
            var child = transforms[i];
            if (child == null || child == logical || child == left || child == right)
                continue;
            if (child.IsChildOf(left) || child.IsChildOf(right))
                continue;
            if (child.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (child.GetComponent<MeshFilter>() != null || child.GetComponentInChildren<MeshFilter>(true) != null)
            {
                if (child.parent == logical || (child.parent != null && child.parent.name == "Visual"))
                    return child;
                if (fallback == null)
                    fallback = child;
            }
        }

        return fallback;
    }

    static Transform FindFirstVisual(Transform root)
    {
        var named = root.Find("Visual");
        if (named != null)
            return named;
        var filter = root.GetComponentInChildren<MeshFilter>(true);
        return filter != null ? filter.transform : root;
    }

    static MeshFilter FirstSourceMeshFilter(Transform sourceRoot, Transform leftMember, Transform rightMember)
    {
        var filters = sourceRoot.GetComponentsInChildren<MeshFilter>(true);
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;
            if (filter.transform.IsChildOf(leftMember) || filter.transform.IsChildOf(rightMember))
                continue;
            return filter;
        }

        return null;
    }

    static void DisableSourceRenderers(Transform sourceRoot, Transform leftMember, Transform rightMember)
    {
        var renderers = sourceRoot.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;
            if (renderer.transform.IsChildOf(leftMember) || renderer.transform.IsChildOf(rightMember))
                continue;
            Undo.RecordObject(renderer, "Hide combined pair source renderer");
            renderer.enabled = false;
        }
    }

    static void ReparentPreserveWorld(Transform child, Transform newParent)
    {
        if (child == null || child.parent == newParent)
            return;
        Undo.SetTransformParent(child, newParent, "Reparent pair visual");
        EditorUtility.SetDirty(child);
    }

    static void SwapSides(string itemId)
    {
        var scene = EnsurePpeSceneOpen();
        var equipable = FindEquipable(scene, itemId);
        if (equipable == null)
        {
            Debug.LogError("[PPE Pair Authoring] Item not found: " + itemId);
            return;
        }

        var left = equipable.transform.Find(LeftMemberName(itemId));
        var right = equipable.transform.Find(RightMemberName(itemId));
        var leftFilter = left != null ? left.GetComponentInChildren<MeshFilter>(true) : null;
        var rightFilter = right != null ? right.GetComponentInChildren<MeshFilter>(true) : null;
        if (leftFilter == null || rightFilter == null)
        {
            Debug.LogError("[PPE Pair Authoring] Cannot swap sides; missing visuals on " + itemId);
            return;
        }

        Undo.RecordObject(leftFilter, "Swap pair sides");
        Undo.RecordObject(rightFilter, "Swap pair sides");
        var mesh = leftFilter.sharedMesh;
        leftFilter.sharedMesh = rightFilter.sharedMesh;
        rightFilter.sharedMesh = mesh;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PPE Pair Authoring] Swapped visual meshes on " + itemId);
    }

    static bool IsFullyAuthored(Scene scene, string itemId)
    {
        var equipable = FindEquipable(scene, itemId);
        return equipable != null && IsFullyAuthored(equipable.gameObject);
    }

    static bool IsFullyAuthored(GameObject root)
    {
        if (root.GetComponent<PPEGlovePairCoordinator>() == null)
            return false;
        if (root.GetComponent<PPEHandGrabbable>() != null)
            return false;

        var left = root.transform.Find(LeftMemberName(root.GetComponent<PPEEquipable>().ItemId));
        var right = root.transform.Find(RightMemberName(root.GetComponent<PPEEquipable>().ItemId));
        if (left == null || right == null)
            return false;
        if (left.GetComponent<PPEGlovePairMember>() == null || right.GetComponent<PPEGlovePairMember>() == null)
            return false;
        if (left.GetComponent<PPEHandGrabbable>() == null || right.GetComponent<PPEHandGrabbable>() == null)
            return false;
        if (left.GetComponent<PPEStoredDisplayState>() == null || right.GetComponent<PPEStoredDisplayState>() == null)
            return false;

        var leftMesh = MeshOf(left);
        var rightMesh = MeshOf(right);
        if (leftMesh == null || rightMesh == null || leftMesh == rightMesh)
            return false;

        var source = FirstSourceMeshFilter(root.transform, left, right);
        if (source != null && source.sharedMesh != null &&
            (leftMesh == source.sharedMesh || rightMesh == source.sharedMesh))
            return false;

        return true;
    }

    static bool HasIndividualVisual(Transform member)
    {
        return MeshOf(member) != null;
    }

    static Mesh MeshOf(Transform member)
    {
        var filter = member.GetComponentInChildren<MeshFilter>(true);
        return filter != null ? filter.sharedMesh : null;
    }

    static PPEEquipable FindEquipable(Scene scene, string itemId)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var equipable in root.GetComponentsInChildren<PPEEquipable>(true))
            {
                if (string.Equals(equipable.ItemId, itemId, System.StringComparison.Ordinal))
                    return equipable;
            }
        }

        return null;
    }

    static string LeftMemberName(string itemId)
    {
        if (string.Equals(itemId, PPEEquipmentManager.IdInnerGloves, System.StringComparison.Ordinal))
            return "LeftInnerGlovePhysical";
        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalBoots, System.StringComparison.Ordinal))
            return "LeftBootPhysical";
        return "LeftProtectiveGlovePhysical";
    }

    static string RightMemberName(string itemId)
    {
        if (string.Equals(itemId, PPEEquipmentManager.IdInnerGloves, System.StringComparison.Ordinal))
            return "RightInnerGlovePhysical";
        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalBoots, System.StringComparison.Ordinal))
            return "RightBootPhysical";
        return "RightProtectiveGlovePhysical";
    }

    static float GrabDistance(string itemId)
    {
        return string.Equals(itemId, PPEEquipmentManager.IdChemicalBoots, System.StringComparison.Ordinal)
            ? BootGrabDistance
            : GloveGrabDistance;
    }

    static float SafeDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) > 0.0001f ? value / divisor : value;
    }
}

[InitializeOnLoad]
static class PPEPairPhysicalAuthoringSetupTrigger
{
    const string SessionKey = "PPE.PairPhysicalAuthoring.v2";
    const string TargetScenePath = "Assets/Scenes/04_PPE.unity";

    static PPEPairPhysicalAuthoringSetupTrigger()
    {
        EditorApplication.delayCall += TryApply;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == TargetScenePath)
            EditorApplication.delayCall += TryApply;
    }

    static void TryApply()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != TargetScenePath)
            return;

        if (!PPEPairPhysicalAuthoringSetup.IsAuthoringRequired())
        {
            SessionState.SetBool(SessionKey, true);
            return;
        }

        PPEPairPhysicalAuthoringSetup.AuthorPairPieces();
        SessionState.SetBool(SessionKey, true);
    }
}
