using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Places PPE_glrove as the first-sequence Inner Gloves in the locker right bay
/// (viewer-right of the center chemical suit hangers).
/// </summary>
public static class PPEInnerGlovesStorageSetup
{
    const string PreferredScenePath = "Assets/Scenes/04_PPE.unity";
    const string StorageName = "PPE_Storage";
    const string SlotName = "InnerGlovesSlot";
    const string RootName = "PPE_InnerGloves";
    const string VisualChildName = "Visual";

    const string ModelPath =
        "Assets/1.JSR/Models/PPE_glrove/tripo_convert_b2144694-05cd-4817-9f7f-96da96b80e66.fbx";

    const string OutlineMatPath = "Assets/1.JSR/Materials/PPE/PPE_Outline.mat";

    // Mirror of left-bay GlovesSlot (~+0.36 X): right bay beside SuitSlot, forward like gloves.
    static readonly Vector3 SlotLocalPos = new Vector3(-0.324f, 0.42f, 0.12f);
    static readonly Vector3 SlotLocalEuler = new Vector3(0f, 180f, 0f);

    const float TargetWorldHeight = 0.28f;
    const float TargetWorldWidth = 0.45f;

    public static void InstallInnerGlovesIntoRightBay()
    {
        EnsurePreferredSceneLoaded();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE InnerGloves] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== Install Inner Gloves (PPE_glrove) ===");
        report.AppendLine($"Scene: {scene.name}");

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            report.AppendLine($"ERROR: model missing at {ModelPath}");
            Debug.LogError(report.ToString());
            return;
        }

        var storage = FindDeep(scene, StorageName);
        if (storage == null)
        {
            report.AppendLine("ERROR: PPE_Storage not found.");
            Debug.LogError(report.ToString());
            return;
        }

        var slot = EnsureSlot(storage, report);
        var root = EnsureRoot(slot, report);
        EnsureVisual(root, model, report);
        FitVisualToShelf(root.transform.Find(VisualChildName), report);
        EnsureCollider(root, report);
        EnsureEquipable(root, report);
        EnsureRayInteractableBasics(root, report);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(report.ToString(), root);
    }

    static void EnsurePreferredSceneLoaded()
    {
        var active = SceneManager.GetActiveScene();
        if (active.path == PreferredScenePath)
            return;
        if (File.Exists(PreferredScenePath))
            EditorSceneManager.OpenScene(PreferredScenePath, OpenSceneMode.Single);
    }

    static Transform FindDeep(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var t = FindDeep(root.transform, name);
            if (t != null)
                return t;
        }

        return null;
    }

    static Transform FindDeep(Transform parent, string name)
    {
        if (parent == null)
            return null;
        if (parent.name == name)
            return parent;
        for (var i = 0; i < parent.childCount; i++)
        {
            var found = FindDeep(parent.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    static Transform EnsureSlot(Transform storage, StringBuilder report)
    {
        var slot = storage.Find(SlotName);
        if (slot == null)
        {
            var go = new GameObject(SlotName);
            Undo.RegisterCreatedObjectUndo(go, "Create InnerGlovesSlot");
            Undo.SetTransformParent(go.transform, storage, "Parent InnerGlovesSlot");
            slot = go.transform;
            report.AppendLine($"Created {SlotName} under PPE_Storage.");
        }

        Undo.RecordObject(slot, "Place InnerGlovesSlot");
        slot.localPosition = SlotLocalPos;
        slot.localRotation = Quaternion.Euler(SlotLocalEuler);
        slot.localScale = Vector3.one;
        report.AppendLine($"Slot local pos={SlotLocalPos} (right bay beside suit).");
        return slot;
    }

    static GameObject EnsureRoot(Transform slot, StringBuilder report)
    {
        var existing = slot.Find(RootName);
        if (existing != null)
        {
            report.AppendLine($"Reusing {RootName}.");
            return existing.gameObject;
        }

        // Remove prior attempts with other names.
        for (var i = slot.childCount - 1; i >= 0; i--)
        {
            var child = slot.GetChild(i);
            if (child.name.IndexOf("Inner", System.StringComparison.OrdinalIgnoreCase) >= 0
                || child.name.IndexOf("glrove", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create PPE_InnerGloves");
        Undo.SetTransformParent(root.transform, slot, "Parent PPE_InnerGloves");
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        report.AppendLine($"Created {RootName}.");
        return root;
    }

    static void EnsureVisual(GameObject root, GameObject model, StringBuilder report)
    {
        var visualTf = root.transform.Find(VisualChildName);
        GameObject visual;
        if (visualTf == null)
        {
            visual = new GameObject(VisualChildName);
            Undo.RegisterCreatedObjectUndo(visual, "Create InnerGloves Visual");
            Undo.SetTransformParent(visual.transform, root.transform, "Parent Visual");
        }
        else
        {
            visual = visualTf.gameObject;
        }

        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        // Clear old mesh children.
        for (var i = visual.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(visual.transform.GetChild(i).gameObject);

        var instance = PrefabUtility.InstantiatePrefab(model, visual.transform) as GameObject;
        if (instance == null)
            instance = Object.Instantiate(model, visual.transform);

        Undo.RegisterCreatedObjectUndo(instance, "Instantiate PPE_glrove");
        instance.name = "PPE_glrove";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        report.AppendLine($"Instanced model from {ModelPath}.");
    }

    static void FitVisualToShelf(Transform visual, StringBuilder report)
    {
        if (visual == null)
            return;

        Undo.RecordObject(visual, "Fit InnerGloves visual");
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        visual.localScale = Vector3.one;

        var bounds = GetRendererWorldBounds(visual.gameObject);
        if (!bounds.HasValue)
        {
            report.AppendLine("WARN: no renderers to fit — leaving unit scale.");
            return;
        }

        var height = Mathf.Max(bounds.Value.size.y, 0.001f);
        var width = Mathf.Max(Mathf.Max(bounds.Value.size.x, bounds.Value.size.z), 0.001f);

        // Prefer readable height inside the locker; width is a soft clamp only.
        var fit = TargetWorldHeight / height;
        var maxWidth = TargetWorldWidth * 1.35f;
        if (width * fit > maxWidth)
            fit = maxWidth / width;

        fit = Mathf.Clamp(fit, 0.05f, 12f);
        visual.localScale = Vector3.one * fit;

        bounds = GetRendererWorldBounds(visual.gameObject);
        if (bounds.HasValue)
        {
            var dy = visual.position.y - bounds.Value.min.y + 0.01f;
            visual.position += new Vector3(0f, dy, 0f);
            var dxz = visual.position - bounds.Value.center;
            visual.position += new Vector3(dxz.x, 0f, dxz.z);
        }

        report.AppendLine($"Fitted visual scale x{fit:F3}.");
    }

    static Bounds? GetRendererWorldBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        Bounds? combined = null;
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled)
                continue;
            if (combined == null)
                combined = r.bounds;
            else
            {
                var b = combined.Value;
                b.Encapsulate(r.bounds);
                combined = b;
            }
        }

        return combined;
    }

    static void EnsureCollider(GameObject root, StringBuilder report)
    {
        var box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = Undo.AddComponent<BoxCollider>(root);

        var bounds = GetRendererWorldBounds(root);
        if (!bounds.HasValue)
        {
            box.size = new Vector3(0.25f, 0.2f, 0.15f);
            box.center = new Vector3(0f, 0.1f, 0f);
            report.AppendLine("BoxCollider with fallback size.");
            return;
        }

        var localCenter = root.transform.InverseTransformPoint(bounds.Value.center);
        var lossy = root.transform.lossyScale;
        var size = new Vector3(
            SafeDiv(bounds.Value.size.x, Mathf.Abs(lossy.x)),
            SafeDiv(bounds.Value.size.y, Mathf.Abs(lossy.y)),
            SafeDiv(bounds.Value.size.z, Mathf.Abs(lossy.z)));

        Undo.RecordObject(box, "Size InnerGloves collider");
        box.center = localCenter;
        box.size = size;
        box.isTrigger = false;
        report.AppendLine($"BoxCollider sized from mesh bounds ({size.x:F3},{size.y:F3},{size.z:F3}).");
    }

    static float SafeDiv(float a, float b) => Mathf.Abs(b) < 0.0001f ? a : a / b;

    static void EnsureEquipable(GameObject root, StringBuilder report)
    {
        var equipable = root.GetComponent<PPEEquipable>();
        if (equipable == null)
            equipable = Undo.AddComponent<PPEEquipable>(root);

        Undo.RecordObject(equipable, "Configure InnerGloves");
        equipable.EditorApplyIdentity(PPEEquipmentManager.IdInnerGloves, "\uB0B4\uD654\uD559 \uBCF4\uD638 \uC7A5\uAC11");
        EditorUtility.SetDirty(equipable);
        report.AppendLine("PPEEquipable → InnerGloves / 내화학 보호 장갑");

        if (root.GetComponent<PPEItem>() == null)
            Undo.AddComponent<PPEItem>(root);
    }

    static void EnsureRayInteractableBasics(GameObject root, StringBuilder report)
    {
        if (root.GetComponent<XRSimpleInteractable>() == null
            && root.GetComponent<XRBaseInteractable>() == null)
        {
            Undo.AddComponent<XRSimpleInteractable>(root);
            report.AppendLine("Added XRSimpleInteractable.");
        }

        if (root.GetComponent<PPEOutlineHighlight>() == null)
        {
            var outline = Undo.AddComponent<PPEOutlineHighlight>(root);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMatPath);
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

        if (root.GetComponent<PPERayInteractionDebug>() == null)
        {
            Undo.AddComponent<PPERayInteractionDebug>(root);
            report.AppendLine("Added PPERayInteractionDebug.");
        }
    }
}
