using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only world-dimension printer for Donning PPE.
/// Uses mesh AABB transformed to world meters, not Transform.localScale alone.
/// </summary>
public static class PPEWorldDimensionAudit
{
    const string ScenePath = "Assets/Scenes/04_PPE.unity";

    public static void Audit()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var report = new StringBuilder();
        report.AppendLine("[PPE Scale Audit]");
        report.AppendLine("World AABB from MeshFilter / SkinnedMeshRenderer (meters).");
        report.AppendLine();

        AuditEquipable(scene, "InnerGloves", report);
        AuditEquipable(scene, "ChemicalSuit", report);
        AuditEquipable(scene, "ChemicalSafetyBoots", report);
        AuditEquipable(scene, "FaceRespirator", report);
        AuditEquipable(scene, "ChemicalFaceShield", report);
        AuditEquipable(scene, "ChemicalGloves", report);

        var wearable = Object.FindAnyObjectByType<ChemicalGlovesWearableVisual>(FindObjectsInactive.Include);
        if (wearable != null)
        {
            report.AppendLine("ChemicalGloves Equipped Visual (ChemicalGloves_WearableHost):");
            AppendNamed(wearable.transform, "WearableHost", report);
            AppendSerializedGlove(wearable, report);
        }
        else
        {
            report.AppendLine("ChemicalGloves Equipped Visual: missing host");
        }

        Debug.Log(report.ToString());
    }

    static void AuditEquipable(Scene scene, string itemId, StringBuilder report)
    {
        PPEEquipable match = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var equipable in root.GetComponentsInChildren<PPEEquipable>(true))
            {
                if (equipable != null && equipable.ItemId == itemId)
                {
                    match = equipable;
                    break;
                }
            }
            if (match != null)
                break;
        }

        report.AppendLine($"=== {itemId} ===");
        if (match == null)
        {
            report.AppendLine("  not found");
            report.AppendLine();
            return;
        }

        var equippedVisual = ReadEquippedVisual(match);
        report.AppendLine($"  root: {match.name}");
        report.AppendLine($"  deactivateOnEquip: {match.DeactivateOnEquip}");
        report.AppendLine($"  equippedVisual: {(equippedVisual != null ? equippedVisual.name : "Missing")}");
        report.AppendLine($"  guidePrefab: {(match.GuidePrefab != null ? match.GuidePrefab.name : "none — copies held mesh")}");

        var coordinator = match.GetComponent<PPEGlovePairCoordinator>();
        if (coordinator != null)
        {
            AuditMember("Left stored/held", coordinator.LeftMember, report);
            AuditMember("Right stored/held", coordinator.RightMember, report);
        }
        else
        {
            AppendNamed(match.transform, "Stored / Held / Guide source", report);
        }

        if (equippedVisual != null)
            AppendNamed(equippedVisual.transform, "Equipped Visual", report);
        else
            report.AppendLine("  Equipped Visual = Missing");

        report.AppendLine();
    }

    static GameObject ReadEquippedVisual(PPEEquipable equipable)
    {
        var so = new SerializedObject(equipable);
        var prop = so.FindProperty("equippedVisual");
        return prop != null ? prop.objectReferenceValue as GameObject : null;
    }

    static void AuditMember(string label, PPEGlovePairMember member, StringBuilder report)
    {
        if (member == null)
        {
            report.AppendLine($"  {label}: missing member");
            return;
        }

        var source = member.VisualRoot != null ? member.VisualRoot : member.transform;
        AppendNamed(source, label, report);
    }

    static void AppendSerializedGlove(ChemicalGlovesWearableVisual wearable, StringBuilder report)
    {
        var so = new SerializedObject(wearable);
        AppendRef(so, "leftGlove", "Worn Left", report);
        AppendRef(so, "rightGlove", "Worn Right", report);
    }

    static void AppendRef(SerializedObject so, string property, string label, StringBuilder report)
    {
        var prop = so.FindProperty(property);
        var go = prop != null ? prop.objectReferenceValue as GameObject : null;
        if (go == null)
        {
            report.AppendLine($"  {label}: missing");
            return;
        }

        AppendNamed(go.transform, label, report);
    }

    static void AppendNamed(Transform source, string label, StringBuilder report)
    {
        if (!TryWorldBounds(source, out var size, out var rendererSize, out var method))
        {
            report.AppendLine($"  {label}: no mesh");
            return;
        }

        report.AppendLine(
            $"  {label}: {size.x:0.000} × {size.y:0.000} × {size.z:0.000} m  ({method})");
        if (rendererSize.sqrMagnitude > 0f && (rendererSize - size).magnitude > 0.005f)
        {
            report.AppendLine(
                $"    Renderer.bounds: {rendererSize.x:0.000} × {rendererSize.y:0.000} × {rendererSize.z:0.000} m");
        }
    }

    static bool TryWorldBounds(Transform source, out Vector3 meshWorldSize, out Vector3 rendererSize, out string method)
    {
        meshWorldSize = Vector3.zero;
        rendererSize = Vector3.zero;
        method = "";

        var hasMesh = false;
        var min = Vector3.zero;
        var max = Vector3.zero;

        var filters = source.GetComponentsInChildren<MeshFilter>(true);
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
                continue;
            Encapsulate(filter.transform, filter.sharedMesh.bounds, ref hasMesh, ref min, ref max);
        }

        var skins = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (var i = 0; i < skins.Length; i++)
        {
            var skin = skins[i];
            if (skin == null || skin.sharedMesh == null)
                continue;
            Encapsulate(skin.transform, skin.sharedMesh.bounds, ref hasMesh, ref min, ref max);
        }

        if (hasMesh)
        {
            meshWorldSize = max - min;
            method = "mesh.bounds × transform";
        }

        var hasRenderer = false;
        var rMin = Vector3.zero;
        var rMax = Vector3.zero;
        var renderers = source.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;
            var b = renderer.bounds;
            if (!hasRenderer)
            {
                rMin = b.min;
                rMax = b.max;
                hasRenderer = true;
            }
            else
            {
                rMin = Vector3.Min(rMin, b.min);
                rMax = Vector3.Max(rMax, b.max);
            }
        }

        if (hasRenderer)
            rendererSize = rMax - rMin;

        if (!hasMesh && hasRenderer)
        {
            meshWorldSize = rendererSize;
            method = "Renderer.bounds";
            return true;
        }

        return hasMesh;
    }

    static void Encapsulate(Transform transform, Bounds localBounds, ref bool has, ref Vector3 min, ref Vector3 max)
    {
        var corners = new Vector3[8];
        var c = localBounds.center;
        var e = localBounds.extents;
        corners[0] = transform.TransformPoint(c + new Vector3(-e.x, -e.y, -e.z));
        corners[1] = transform.TransformPoint(c + new Vector3(-e.x, -e.y, e.z));
        corners[2] = transform.TransformPoint(c + new Vector3(-e.x, e.y, -e.z));
        corners[3] = transform.TransformPoint(c + new Vector3(-e.x, e.y, e.z));
        corners[4] = transform.TransformPoint(c + new Vector3(e.x, -e.y, -e.z));
        corners[5] = transform.TransformPoint(c + new Vector3(e.x, -e.y, e.z));
        corners[6] = transform.TransformPoint(c + new Vector3(e.x, e.y, -e.z));
        corners[7] = transform.TransformPoint(c + new Vector3(e.x, e.y, e.z));

        for (var i = 0; i < 8; i++)
        {
            if (!has)
            {
                min = max = corners[i];
                has = true;
            }
            else
            {
                min = Vector3.Min(min, corners[i]);
                max = Vector3.Max(max, corners[i]);
            }
        }
    }
}
