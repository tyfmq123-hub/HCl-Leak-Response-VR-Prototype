using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PPERoomAtmosphereSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunPPERoomAtmosphereSetup.trigger.txt";

    static PPERoomAtmosphereSetupTrigger()
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

        Debug.Log("[PPE Atmosphere] Trigger detected — brightening room.");
        PPERoomAtmosphereSetup.BrightenRoomAtmosphere();
    }
}
