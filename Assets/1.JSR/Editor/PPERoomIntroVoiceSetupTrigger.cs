using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PPERoomIntroVoiceSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunPPERoomIntroVoiceSetup.trigger.txt";

    static PPERoomIntroVoiceSetupTrigger()
    {
        EditorApplication.delayCall += TryRun;
    }

    static void TryRun()
    {
        if (!File.Exists(TriggerPath))
            return;

        try
        {
            File.Delete(TriggerPath);
            if (File.Exists(TriggerPath + ".meta"))
                File.Delete(TriggerPath + ".meta");
        }
        catch
        {
            // ignored
        }

        Debug.Log("[PPE Intro Voice] Trigger detected — running setup.");
        PPERoomIntroVoiceSetup.SetupRoomIntroVoice();
    }
}
