using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PPEWearableResponseBagSetupTrigger
{
    const string TriggerPath = "Assets/Scripts/Editor/RunPPEWearableResponseBagSetup.trigger.txt";

    static PPEWearableResponseBagSetupTrigger()
    {
        EditorApplication.delayCall += CleanupStaleTriggerOnly;
    }

    static void CleanupStaleTriggerOnly()
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

        Debug.LogWarning(
            "[Wearable Bag] Auto-setup on domain reload is disabled. " +
            "Run Tools > PPE > Setup Wearable Response Bag manually.");
    }
}
