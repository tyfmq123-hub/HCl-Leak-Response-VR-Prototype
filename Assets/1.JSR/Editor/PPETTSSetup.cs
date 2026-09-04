using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates PPETTSManager under PPE_System, assigns English-named TTS clips, disables legacy intro voice.
/// </summary>
public static class PPETTSSetup
{
    const string PreferredScenePath = "Assets/Scenes/04_PPE.unity";
    const string ObjectName = "PPE_TTS";
    const string ClipFolder = "Assets/1.JSR/Audio/PPE/TTS";

    static readonly string[] ClipFiles =
    {
        "01_EmergencyIntro.wav",
        "02_WearEssentialPPE.wav",
        "03_WearInOrder.wav",
        "04_PointWithRay.wav",
        "05_OutlineShowsSelectable.wav",
        // Legacy numeric slot retained for stable cue indexing; runtime no longer enqueues it.
        "06_GripSelectThenEquip.wav",
        "07_BButtonCancel.wav",
        "08_WearInnerGloves.wav",
        "09_InnerGlovesEquipped.wav",
        "10_WearChemicalSuit.wav",
        "11_ChemicalSuitEquipped.wav",
        "12_WearChemicalBoots.wav",
        "13_ChemicalBootsEquipped.wav",
        "14_WearRespirator.wav",
        "15_RespiratorEquipped.wav",
        "16_WearFaceProtection.wav",
        "17_FaceProtectionEquipped.wav",
        "18_WearOuterGloves.wav",
        "19_OuterGlovesEquipped.wav",
        "20_EssentialPPEComplete.wav",
        "21_PrepareResponseBag.wav",
        "22_ResponseBagReady.wav",
        "23_PrepareWasteContainer.wav",
        "24_WasteContainerReady.wav",
        "25_CheckingPrepStatus.wav",
        "26_AllFieldPrepComplete.wav",
        "27_GoToFieldEntrance.wav",
        "28_MovingToFieldEntrance.wav",
        "29_WrongPPEOrder.wav",
        "30_WearGlovesFirst.wav",
        "31_OuterGlovesAfterRespirator.wav",
        "32_AlreadyEquipped.wav",
        "33_CompleteEssentialPPEFirst.wav",
        "34_MissingPPE.wav",
        "35_PrepareResponseBagFirst.wav",
        "36_CheckCurrentRequiredPPE.wav",
    };

    public static void SetupPpeTts()
    {
        EnsurePreferredSceneLoaded();
        AssetDatabase.Refresh();

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[PPE TTS] No active scene.");
            return;
        }

        var system = GameObject.Find("PPE_System");
        if (system == null)
        {
            Debug.LogError("[PPE TTS] PPE_System not found.");
            return;
        }

        var existing = Object.FindAnyObjectByType<PPETTSManager>(FindObjectsInactive.Include);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Create PPE_TTS");
            go.transform.SetParent(system.transform, false);
            Undo.AddComponent<AudioSource>(go);
            Undo.AddComponent<PPETTSManager>(go);
        }

        if (go.GetComponent<AudioSource>() == null)
            Undo.AddComponent<AudioSource>(go);
        var tts = go.GetComponent<PPETTSManager>();
        if (tts == null)
            tts = Undo.AddComponent<PPETTSManager>(go);

        var assigned = new List<PPETTSManager.CueClip>();
        var missing = 0;
        for (var i = 0; i < ClipFiles.Length; i++)
        {
            var path = ClipFolder + "/" + ClipFiles[i];
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                missing++;
                Debug.LogWarning($"[PPE TTS] Missing clip: {path}");
                continue;
            }

            assigned.Add(new PPETTSManager.CueClip
            {
                cue = (PPETTSCue)(i + 1),
                clip = clip,
            });
        }

        tts.EditorSetClips(assigned);
        EditorUtility.SetDirty(tts);

        // Ensure clips preload so Play() works on first frame in headset builds.
        for (var i = 0; i < ClipFiles.Length; i++)
        {
            var path = ClipFolder + "/" + ClipFiles[i];
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                continue;

            var settings = importer.defaultSampleSettings;
            var dirty = false;
            if (!settings.preloadAudioData)
            {
                settings.preloadAudioData = true;
                dirty = true;
            }

            if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                dirty = true;
            }

            if (settings.compressionFormat != AudioCompressionFormat.PCM)
            {
                settings.compressionFormat = AudioCompressionFormat.PCM;
                dirty = true;
            }

            if (!importer.forceToMono)
            {
                importer.forceToMono = true;
                dirty = true;
            }

            if (dirty)
            {
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        // Avoid double intro with legacy player.
        var intro = Object.FindAnyObjectByType<PPERoomIntroVoice>(FindObjectsInactive.Include);
        if (intro != null)
        {
            var so = new SerializedObject(intro);
            var play = so.FindProperty("playOnStart");
            if (play != null)
            {
                play.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(intro);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[PPE TTS] Setup complete. Assigned {assigned.Count}/36 clips. Missing={missing}", go);
    }

    static void EnsurePreferredSceneLoaded()
    {
        var active = SceneManager.GetActiveScene();
        if (active.path == PreferredScenePath)
            return;

        if (File.Exists(PreferredScenePath))
            EditorSceneManager.OpenScene(PreferredScenePath, OpenSceneMode.Single);
    }
}
