using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds <see cref="PPEEquipable"/> to top-level PPE roots and ensures
/// <c>PPE_EquipmentManager</c> exists under <c>PPE_System</c>.
/// </summary>
public static class PPEEquipmentSetup
{
    struct EquipIdentity
    {
        public readonly string NameContains;
        public readonly string ItemId;
        public readonly string DisplayName;

        public EquipIdentity(string nameContains, string itemId, string displayName)
        {
            NameContains = nameContains;
            ItemId = itemId;
            DisplayName = displayName;
        }
    }

    // More specific patterns first (PPE_ChemicalGloves before PPE_Gloves).
    static readonly EquipIdentity[] Identities =
    {
        new EquipIdentity("PPE_Helmet", PPEEquipmentManager.IdHelmet, "안전모"),
        new EquipIdentity("PPE_ChemicalSuit", "ChemicalSuit", "내산성 화학보호복"),
        new EquipIdentity("PPE_ChemicalSafetyBoots", "ChemicalSafetyBoots", "내산성 화학보호 장화"),
        new EquipIdentity("PPE_FaceRespirator", "FaceRespirator", "전면형 호흡보호구"),
        new EquipIdentity("PPE_ChemicalFaceShield", "ChemicalFaceShield", "안면보호구"),
        new EquipIdentity("PPE_InnerGloves", "InnerGloves", "내화학 보호 장갑"),
        new EquipIdentity("PPE_ChemicalGloves", "ChemicalGloves", "내화학 보호장갑"),
        new EquipIdentity("PPE_Gloves", "ChemicalGloves", "내화학 보호장갑"),
    };

    public static void SetupEquipmentSystem()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Equipment] No active loaded scene.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("=== PPE Equipment System Setup ===");
        report.AppendLine($"Scene: {scene.name}");

        EnsureEquipmentManager(report);

        var roots = FindTopLevelPpeRoots();
        report.AppendLine($"Matched PPE roots: {roots.Count}");

        foreach (var root in roots)
        {
            if (!TryResolveIdentity(root.name, out var identity))
            {
                report.AppendLine($"  SKIP (no identity): {GetHierarchyPath(root.transform)}");
                continue;
            }

            var equipable = root.GetComponent<PPEEquipable>();
            if (equipable == null)
            {
                equipable = Undo.AddComponent<PPEEquipable>(root);
                report.AppendLine($"  ADD PPEEquipable: {root.name} → {identity.ItemId} / {identity.DisplayName}");
            }
            else
            {
                report.AppendLine($"  UPDATE PPEEquipable: {root.name} → {identity.ItemId} / {identity.DisplayName}");
            }

            Undo.RecordObject(equipable, "Configure PPEEquipable");
            equipable.EditorApplyIdentity(identity.ItemId, identity.DisplayName);
            if (identity.ItemId == PPEEquipmentManager.IdHelmet)
                equipable.EditorSetDeactivateOnEquip(false);
            EditorUtility.SetDirty(equipable);
            EditorUtility.SetDirty(root);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Debug.Log(report.ToString());
    }

    static PPEEquipmentManager EnsureEquipmentManager(StringBuilder report)
    {
        var existing = Object.FindAnyObjectByType<PPEEquipmentManager>(FindObjectsInactive.Include);
        if (existing != null)
        {
            if (existing.gameObject.name != "PPE_EquipmentManager")
                existing.gameObject.name = "PPE_EquipmentManager";
            report.AppendLine($"Manager: reuse existing on '{GetHierarchyPath(existing.transform)}'");
            return existing;
        }

        var system = GameObject.Find("PPE_System");
        GameObject go;
        if (system != null)
        {
            go = new GameObject("PPE_EquipmentManager");
            Undo.RegisterCreatedObjectUndo(go, "Create PPE_EquipmentManager");
            Undo.SetTransformParent(go.transform, system.transform, "Parent PPE_EquipmentManager");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            report.AppendLine("Manager: created under PPE_System");
        }
        else
        {
            go = new GameObject("PPE_EquipmentManager");
            Undo.RegisterCreatedObjectUndo(go, "Create PPE_EquipmentManager");
            report.AppendLine("Manager: created at scene root (PPE_System not found)");
        }

        return Undo.AddComponent<PPEEquipmentManager>(go);
    }

    static List<GameObject> FindTopLevelPpeRoots()
    {
        var results = new List<GameObject>();
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsTopLevelPpeRoot(t))
                    continue;
                results.Add(t.gameObject);
            }
        }

        return results;
    }

    static bool IsTopLevelPpeRoot(Transform t)
    {
        if (t == null)
            return false;

        if (PPERayInteractableCatalog.IsExcludedEnvironmentName(t.name))
            return false;

        if (!TryResolveIdentity(t.name, out _))
            return false;

        // Skip child meshes when a parent already matches an equipable PPE name.
        var p = t.parent;
        while (p != null)
        {
            if (!PPERayInteractableCatalog.IsExcludedEnvironmentName(p.name) &&
                TryResolveIdentity(p.name, out _))
                return false;
            p = p.parent;
        }

        return true;
    }

    static bool TryResolveIdentity(string objectName, out EquipIdentity identity)
    {
        identity = default;
        if (string.IsNullOrEmpty(objectName))
            return false;

        for (int i = 0; i < Identities.Length; i++)
        {
            if (objectName.IndexOf(Identities[i].NameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                identity = Identities[i];
                return true;
            }
        }

        return false;
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
