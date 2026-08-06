using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PPECabinetRestoreSetupTrigger
{
    const string TriggerPath = "Assets/Scripts/Editor/RunPPECabinetRestoreSetup.trigger.txt";

    static PPECabinetRestoreSetupTrigger()
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

        Debug.Log("[PPE Cabinet] Trigger — restoring wardrobe + 3-door locker.");
        PPECabinetRestoreSetup.RestoreCabinet();
    }
}
