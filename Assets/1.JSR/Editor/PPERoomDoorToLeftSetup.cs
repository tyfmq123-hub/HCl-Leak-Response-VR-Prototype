using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Moves the room doorway from the front wall to the left wall.
/// Only touches Wall_* and WallBand_* transforms. Does not modify PPE / cabinets / bag.
/// </summary>
public static class PPERoomDoorToLeftSetup
{
    public static void MoveDoorwayToLeftWall()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Door] No active loaded scene.");
            return;
        }

        // Left wall segments with ~1.2 m centered gap (same opening width as the old front door).
        SetWall("Wall_Front_Left", "Wall_Left_Front", new Vector3(-3.5f, 1.5f, -1.8f), new Vector3(0.15f, 3f, 2.4f));
        SetWall("Wall_Front_Right", "Wall_Left_Back", new Vector3(-3.5f, 1.5f, 1.8f), new Vector3(0.15f, 3f, 2.4f));
        SetWall("Wall_Front_Top", "Wall_Left_Top", new Vector3(-3.5f, 2.6f, 0f), new Vector3(0.15f, 0.8f, 1.2f));
        // Former left wall becomes solid front.
        SetWall("Wall_Left", "Wall_Front", new Vector3(0f, 1.5f, -3f), new Vector3(7f, 3f, 0.15f));

        // Lower bands (visual only), matching the new shell.
        SetBand("WallBand_Left", "WallBand_Front", new Vector3(0f, 0.475f, -2.9f), new Vector3(6.9f, 0.95f, 0.04f));
        SetBand("WallBand_Front_Left", "WallBand_Left_Front", new Vector3(-3.4f, 0.475f, -1.8f), new Vector3(0.04f, 0.95f, 2.35f));
        SetBand("WallBand_Front_Right", "WallBand_Left_Back", new Vector3(-3.4f, 0.475f, 1.8f), new Vector3(0.04f, 0.95f, 2.35f));

        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Debug.Log("[PPE Door] Doorway moved to left wall. PPE / cabinets / bag untouched.");
    }

    static void SetWall(string currentOrOldName, string newName, Vector3 pos, Vector3 scale)
    {
        var go = FindRootOrDeep(currentOrOldName) ?? FindRootOrDeep(newName);
        if (go == null)
        {
            Debug.LogWarning($"[PPE Door] Missing wall: {currentOrOldName}");
            return;
        }

        Undo.RecordObject(go.transform, "Move doorway wall");
        Undo.RecordObject(go, "Rename doorway wall");
        go.name = newName;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;
        EditorUtility.SetDirty(go);
        EditorUtility.SetDirty(go.transform);
    }

    static void SetBand(string currentOrOldName, string newName, Vector3 pos, Vector3 scale)
    {
        var go = FindRootOrDeep(currentOrOldName) ?? FindRootOrDeep(newName);
        if (go == null)
        {
            Debug.LogWarning($"[PPE Door] Missing wall band: {currentOrOldName}");
            return;
        }

        Undo.RecordObject(go.transform, "Move doorway band");
        Undo.RecordObject(go, "Rename doorway band");
        go.name = newName;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = scale;
        EditorUtility.SetDirty(go);
        EditorUtility.SetDirty(go.transform);
    }

    static GameObject FindRootOrDeep(string name)
    {
        var direct = GameObject.Find(name);
        if (direct != null)
            return direct;

        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name)
                return root;
            var t = root.GetComponentsInChildren<Transform>(true);
            foreach (var child in t)
            {
                if (child.name == name)
                    return child.gameObject;
            }
        }

        return null;
    }
}
