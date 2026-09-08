using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Adds PPEInteractableOutline to matching PPE roots without changing transforms or physics.
/// </summary>
public static class PPEInteractableOutlineSetup
{
    const string OutlineMaterialPath = "Assets/1.JSR/Materials/PPE/PPE_Outline.mat";
    const string OutlineResourcesPath = "Assets/Resources/PPE_Outline.mat";
    const string OutlineShaderName = "PPE/Outline";

    static readonly string[] NamePatterns =
    {
        "PPE_ChemicalSuit",
        "PPE_ChemicalSafetyBoots",
        "PPE_FaceRespirator",
        "PPE_ChemicalFaceShield",
        "PPE_ChemicalGloves",
        "PPE_Gloves",
    };

    static readonly HashSet<string> ExcludeExact = new HashSet<string>
    {
        "PPE_Storage",
        "PPE_Cabinet",
        "PPE_System",
        "PPEManager",
        "PPE_LayoutRoot",
        "SuitSlot",
        "BootsSlot",
        "RespiratorSlot",
        "FaceShieldSlot",
        "GlovesSlot",
    };

    public static void SetupInteractableOutlines()
    {
        EnsureOutlineMaterialAssets();
        var material = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath)
                       ?? AssetDatabase.LoadAssetAtPath<Material>(OutlineResourcesPath);

        var added = new List<string>();
        var already = new List<string>();
        var warnings = new List<string>();

        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsTargetPpeRoot(t))
                    continue;

                var go = t.gameObject;
                var path = GetPath(t);

                var existing = go.GetComponent<PPEInteractableOutline>();
                if (existing != null)
                {
                    already.Add(path);
                    if (material != null)
                        AssignSourceMaterial(existing, material);
                    continue;
                }

                var interactable = go.GetComponent<XRBaseInteractable>();
                if (interactable == null)
                {
                    warnings.Add(
                        $"{path}: no XRBaseInteractable / XRGrabInteractable / XRSimpleInteractable. " +
                        "Outline was NOT added. Add an interactable + collider manually.");
                    continue;
                }

                var hasCollider = go.GetComponentInChildren<Collider>(true) != null;
                if (!hasCollider)
                {
                    warnings.Add(
                        $"{path}: has interactable but no Collider in children. " +
                        "Ray/Direct hover may not work until a collider is added.");
                }

                var outline = Undo.AddComponent<PPEInteractableOutline>(go);
                AssignSourceMaterial(outline, material);
                added.Add(path + (hasCollider ? "" : " (no collider)"));
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        var sb = new StringBuilder();
        sb.AppendLine("PPE Interactable Outline setup complete.");
        sb.AppendLine($"Added: {added.Count}");
        foreach (var a in added) sb.AppendLine("  + " + a);
        sb.AppendLine($"Already present: {already.Count}");
        foreach (var a in already) sb.AppendLine("  · " + a);
        sb.AppendLine($"Warnings: {warnings.Count}");
        foreach (var w in warnings) sb.AppendLine("  ! " + w);
        Debug.Log(sb.ToString());
    }

    static bool IsTargetPpeRoot(Transform t)
    {
        if (t == null)
            return false;
        if (ExcludeExact.Contains(t.name))
            return false;
        if (t.name.StartsWith("Wall_", System.StringComparison.Ordinal))
            return false;
        if (t.name is "Floor" or "Ceiling" or "Directional Light")
            return false;

        if (!MatchesPpeName(t.name))
            return false;

        // Prefer the named equipment root: skip if a parent already matches a PPE pattern
        // (avoids attaching to every tripo_part child).
        var p = t.parent;
        while (p != null)
        {
            if (MatchesPpeName(p.name) && !ExcludeExact.Contains(p.name))
                return false;
            p = p.parent;
        }

        return true;
    }

    static bool MatchesPpeName(string objectName)
    {
        foreach (var pattern in NamePatterns)
        {
            if (objectName.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    static void AssignSourceMaterial(PPEInteractableOutline outline, Material material)
    {
        if (outline == null || material == null)
            return;

        var so = new SerializedObject(outline);
        var prop = so.FindProperty("outlineMaterialSource");
        if (prop != null && prop.objectReferenceValue == null)
        {
            prop.objectReferenceValue = material;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void EnsureOutlineMaterialAssets()
    {
        var shader = Shader.Find(OutlineShaderName);
        if (shader == null)
        {
            Debug.LogError($"Shader '{OutlineShaderName}' not found. Wait for import, then re-run setup.");
            return;
        }

        Directory.CreateDirectory("Assets/1.JSR/Materials/PPE");
        Directory.CreateDirectory("Assets/Resources");

        CreateMaterialIfMissing(OutlineMaterialPath, shader);
        CreateMaterialIfMissing(OutlineResourcesPath, shader);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void CreateMaterialIfMissing(string path, Shader shader)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return;

        var mat = new Material(shader)
        {
            name = Path.GetFileNameWithoutExtension(path),
            color = new Color(1f, 0.92f, 0.016f, 1f),
        };
        mat.SetColor("_OutlineColor", new Color(1f, 0.92f, 0.016f, 1f));
        mat.SetFloat("_OutlineWidth", 2.5f);
        AssetDatabase.CreateAsset(mat, path);
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
}
