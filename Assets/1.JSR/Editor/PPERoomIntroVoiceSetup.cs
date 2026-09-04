using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ensures a PPE_RoomIntroVoice player exists under PPE_System.
/// </summary>
public static class PPERoomIntroVoiceSetup
{
    const string ObjectName = "PPE_RoomIntroVoice";
    const string ClipPath = "Assets/1.JSR/Resources/PPE_RoomIntroVoice.mp3";

    public static void SetupRoomIntroVoice()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE Intro Voice] No active loaded scene.");
            return;
        }

        var existing = Object.FindAnyObjectByType<PPERoomIntroVoice>(FindObjectsInactive.Include);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
            go.name = ObjectName;
            Debug.Log("[PPE Intro Voice] Reused existing player.");
        }
        else
        {
            var system = GameObject.Find("PPE_System");
            go = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Create PPE_RoomIntroVoice");
            if (system != null)
                Undo.SetTransformParent(go.transform, system.transform, "Parent intro voice");
            Undo.AddComponent<AudioSource>(go);
            Undo.AddComponent<PPERoomIntroVoice>(go);
            Debug.Log("[PPE Intro Voice] Created under PPE_System.");
        }

        var source = go.GetComponent<AudioSource>();
        if (source == null)
            source = Undo.AddComponent<AudioSource>(go);
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;

        var voice = go.GetComponent<PPERoomIntroVoice>();
        if (voice == null)
            voice = Undo.AddComponent<PPERoomIntroVoice>(go);

        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        if (clip == null)
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/1.JSR/Audio/PPE/PPE_RoomIntroVoice.mp3");

        var so = new SerializedObject(voice);
        if (clip != null)
            so.FindProperty("introClip").objectReferenceValue = clip;
        so.FindProperty("playOnStart").boolValue = true;
        so.FindProperty("startDelay").floatValue = 0f;
        so.FindProperty("volume").floatValue = 1f;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(scene);
        if (scene.isDirty && !string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Selection.activeObject = go;
        if (clip == null)
            Debug.LogWarning($"[PPE Intro Voice] Clip not found yet at {ClipPath}. Unity will import on refresh; Resources load still works at runtime.");
        else
            Debug.Log($"[PPE Intro Voice] Wired clip '{clip.name}'. Plays once on scene start.");
    }
}
