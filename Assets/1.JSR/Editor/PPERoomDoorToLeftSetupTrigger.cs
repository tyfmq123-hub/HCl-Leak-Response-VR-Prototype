using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PPERoomDoorToLeftSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunPPERoomDoorToLeftSetup.trigger.txt";

    static PPERoomDoorToLeftSetupTrigger()
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

        Debug.Log("[PPE Door] Trigger — moving doorway to left wall.");
        PPERoomDoorToLeftSetup.MoveDoorwayToLeftWall();
    }
}
